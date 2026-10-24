using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;

/// <summary>Presentation only: two exact screen-space pointers, shared shadows and a short cosmetic chain.</summary>
[DefaultExecutionOrder(10000), DisallowMultipleComponent, RequireComponent(typeof(Canvas))]
public sealed class GameplayPixelCursor : MonoBehaviour
{
    [Header("View (no raycasts or gameplay physics)")]
    [SerializeField] private RectTransform cursorRoot;
    [SerializeField] private UnityEngine.UI.Image arrowSprite;
    [SerializeField] private RectTransform chainRoot;
    [SerializeField] private UnityEngine.UI.Image[] chainLinks;
    [SerializeField] private Sprite menuSprite;
    [SerializeField] private RectTransform noiseRoot;
    [SerializeField] private UnityEngine.UI.Image[] noisePixels;

    [Header("Both cursor designs")]
    [SerializeField] private Vector2 menuHotspot = new Vector2(16f, 14f);
    [SerializeField] private Color shadowColor = new Color(0f, 0f, 0f, 0.72f);
    [SerializeField, Tooltip("Constant screen pixels; independent of cursor size.")]
    private Vector2 shadowOffset = new Vector2(3f, -3f);
    [SerializeField, Range(0.05f, 0.4f)] private float transitionDuration = 0.28f;
    [SerializeField, Range(0f, 1f)] private float transitionNoiseOpacity = 0.85f;

    [Header("Arrow (source image pixels, including transparent margins)")]
    [SerializeField, Min(0.25f), Tooltip("1 = original pixels; visible arrow is 16 x 20 px.")]
    private float cursorScale = 3f;
    [SerializeField, Tooltip("From the source image's top-left, NOT the opaque bounds.")]
    private Vector2 hotspot = new Vector2(16f, 14f);
    [SerializeField, Tooltip("Attachment point from the source image's top-left.")]
    private Vector2 connectionOffset = new Vector2(24f, 28.5f);

    [Header("Short chain (screen pixels at Cursor Scale = 1)")]
    [SerializeField, Min(2f)] private float chainLength = 18f;
    [SerializeField, Range(0f, 70f), Tooltip("Maximum angle of each joint from vertical, in degrees.")]
    private float swingAmplitude = 32f;
    [SerializeField, Range(0f, 2f)] private float inertia = 0.55f;
    [SerializeField, Min(0f), Tooltip("Downward acceleration in screen pixels / second squared.")]
    private float gravity = 180f;
    [SerializeField, Min(0f)] private float damping = 5.5f;
    [SerializeField, Min(0.1f), Tooltip("Spring response in inverse seconds.")]
    private float followSpeed = 7f;

    private static GameplayPixelCursor owner;
    private Canvas view;
    private Player player;
    private PauseMenuController pauseMenu;
    private GoalMenuController goalMenu;
    private CrystalAcquiredUI celebration;
    private CRTFilterController crt;
    private Sprite chainSprite;
    private UnityEngine.UI.Shadow[] shadows;
    private readonly float[] angles = new float[2];
    private readonly float[] angularVelocities = new float[2];
    private Vector2 previousAnchor, filteredVelocity, lastMousePosition;
    private Vector2Int lastResolution;
    private float previousScale;
    private bool pointerKnown, usingGamepad, ownsOSCursor, previousOSVisibility;
    private bool focused = true, applicationPaused;
    private bool styleKnown, chainStyle, targetChainStyle;
    private float styleChangedAt = float.NegativeInfinity;
    private float visibilityChangedAt = float.NegativeInfinity;
    private Gamepad activityGamepad;
    private Vector2 previousLeftStick, previousRightStick;
    public bool IsShowing => view != null && view.enabled;
    public bool IsChainStyle => styleKnown && chainStyle;
    public bool IsChangingDesign => styleKnown && Time.unscaledTime - styleChangedAt < transitionDuration;
    private bool IsChangingVisibility => Time.unscaledTime - visibilityChangedAt < transitionDuration;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOwner() => owner = null;

    private void Awake()
    {
        view = GetComponent<Canvas>();
        view.enabled = false;
        chainSprite = arrowSprite != null ? arrowSprite.sprite : null;
        shadows = GetComponentsInChildren<UnityEngine.UI.Shadow>(true);
        CacheSceneOwners();
    }

    private void CacheSceneOwners()
    {
        // Only scene changes refresh these references; no gameplay/input subscriptions.
        player = FindAnyObjectByType<Player>();
        pauseMenu = FindAnyObjectByType<PauseMenuController>();
        goalMenu = FindAnyObjectByType<GoalMenuController>();
        celebration = FindAnyObjectByType<CrystalAcquiredUI>();
        crt = FindAnyObjectByType<CRTFilterController>();
    }

    private void OnEnable()
    {
        if (owner != null && owner != this)
        {
            enabled = false;
            Destroy(gameObject);
            return;
        }
        owner = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
        focused = Application.isFocused;
        pointerKnown = false;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => CacheSceneOwners();

    private void LateUpdate()
    {
        Mouse mouse = Mouse.current;
        if (owner != this || mouse == null || !HasView())
        {
            ReleaseCursor();
            return;
        }

        Vector2 point = mouse.position.ReadValue();
        UpdateInputDevice(mouse, point);

        bool inside = point.x >= 0f && point.y >= 0f && point.x < Screen.width && point.y < Screen.height;
        if (!HasGameViewFocus() || applicationPaused || !inside || Cursor.lockState == CursorLockMode.Locked)
        {
            ReleaseCursor();
            return;
        }

        ShowCursor(point);
    }

    private void UpdateInputDevice(Mouse mouse, Vector2 point)
    {
        // Sample both devices even when mouse activity wins this frame. A held stick
        // must not reclaim the pointer on the next unchanged gamepad report.
        bool gamepadActive = GamepadIsActive();
        bool mouseActive = pointerKnown && (point - lastMousePosition).sqrMagnitude > 0.01f;
        mouseActive |= mouse.scroll.ReadValue().sqrMagnitude > 0.01f;
        foreach (var control in mouse.allControls)
            if (control is ButtonControl button && (button.wasPressedThisFrame || button.wasReleasedThisFrame))
                mouseActive = true;

        bool nextUsingGamepad = mouseActive ? false : gamepadActive || usingGamepad;
        if (nextUsingGamepad != usingGamepad)
        {
            usingGamepad = nextUsingGamepad;
            visibilityChangedAt = Time.unscaledTime;
        }
        lastMousePosition = point;
        pointerKnown = true;
    }

    private void ShowCursor(Vector2 point)
    {
        bool styleChanged = UpdateStyle(IsGameplay());
        bool reset = !IsShowing || lastResolution != new Vector2Int(Screen.width, Screen.height)
            || !Mathf.Approximately(previousScale, cursorScale) || styleChanged;
        // CRTFilterController can convert all scene canvases. The cursor must always stay
        // outside that camera's low-resolution render and distortion, including on Resume.
        // These are native Canvas setters, not cursor movement. Repair state only
        // when another presentation owner changed it; keep the moving path small.
        if (view.renderMode != RenderMode.ScreenSpaceOverlay) view.renderMode = RenderMode.ScreenSpaceOverlay;
        if (view.worldCamera != null) view.worldCamera = null;
        if (view.scaleFactor != 1f) view.scaleFactor = 1f;
        if (view.pixelPerfect) view.pixelPerfect = false; // Keep the exact mouse coordinate.
        // The loading veil also uses the maximum order. Keeping this persistent root
        // last draws the pointer above it without changing the scene/transition owners.
        if (view.sortingOrder != short.MaxValue) view.sortingOrder = short.MaxValue;
        if (transform.GetSiblingIndex() < gameObject.scene.rootCount - 1) transform.SetAsLastSibling();
        bool showView = !usingGamepad || IsChangingVisibility;
        if (view.enabled != showView) view.enabled = showView;
        bool acquiringCursor = !ownsOSCursor;
        if (acquiringCursor)
        {
            previousOSVisibility = Cursor.visible;
            ownsOSCursor = true;
        }
#if UNITY_EDITOR
        // ESC revokes the Editor's hiding permission separately from Cursor.visible.
        // Re-arm it on acquisition / ESC, not on every moving frame. Reading this
        // key does not consume or change the pause owner's existing input.
        if (acquiringCursor || (Keyboard.current != null
            && (Keyboard.current.escapeKey.isPressed || Keyboard.current.escapeKey.wasPressedThisFrame)))
            UnityEditor.Unsupported.SetAllowCursorHide(true);
#endif
        if (Cursor.visible) Cursor.visible = false;

        if (!showView)
        {
            if (noiseRoot != null && noiseRoot.gameObject.activeSelf) noiseRoot.gameObject.SetActive(false);
            return;
        }

        bool showPointer = !usingGamepad || Time.unscaledTime - visibilityChangedAt < transitionDuration * 0.45f;
        arrowSprite.enabled = showPointer;
        chainRoot.gameObject.SetActive(chainStyle && showPointer);
        UpdateView(point, reset);
        UpdateNoise();
        lastResolution = new Vector2Int(Screen.width, Screen.height);
        previousScale = cursorScale;
    }

    private bool HasView() => cursorRoot != null && arrowSprite != null && chainSprite != null && menuSprite != null
        && chainRoot != null && chainLinks != null && chainLinks.Length == 2
        && chainLinks[0] != null && chainLinks[0].sprite != null
        && chainLinks[1] != null && chainLinks[1].sprite != null;

    // Combat and hook hit stops also set timeScale to zero. Use the player,
    // input and menu owners, so every playable stage uses the gameplay design.
    private bool IsGameplay() => !TitleStageTransition.IsTransitioning
        && player != null && player.isActiveAndEnabled && player.InputEnabled && !player.IsGoalInputBlocked
        && (pauseMenu == null || !pauseMenu.IsPaused)
        && (goalMenu == null || !goalMenu.IsGoalReached)
        && (celebration == null || !celebration.IsPlaying)
        && (crt == null || !crt.IsOverlayActive);

    private bool UpdateStyle(bool wantsChain)
    {
        bool changed = false;
        if (!styleKnown)
        {
            styleKnown = true;
            targetChainStyle = wantsChain;
            ApplyStyle(wantsChain);
            return true;
        }
        if (targetChainStyle != wantsChain)
        {
            targetChainStyle = wantsChain;
            styleChangedAt = Time.unscaledTime;
        }
        // Change the design in the middle of the short noise burst. Keep the actual
        // pointer and its shadow visible throughout, even while timeScale is zero.
        if (chainStyle != targetChainStyle && Time.unscaledTime - styleChangedAt >= transitionDuration * 0.45f)
        {
            ApplyStyle(targetChainStyle);
            changed = true;
        }
        return changed;
    }

    private void ApplyStyle(bool withChain)
    {
        chainStyle = withChain;
        arrowSprite.sprite = withChain ? chainSprite : menuSprite;
        chainRoot.gameObject.SetActive(withChain);
    }

    private void UpdateNoise()
    {
        if (noiseRoot == null || noisePixels == null)
            return;
        bool visible = IsShowing && (IsChangingDesign || IsChangingVisibility);
        noiseRoot.gameObject.SetActive(visible);
        if (!visible)
            return;
        float age = Time.unscaledTime - Mathf.Max(styleChangedAt, visibilityChangedAt);
        // The first frame must already show a faint signal: opening the CRT menu can
        // spend most of this short duration capturing its frozen background.
        float pulse = 0.35f + 0.65f * Mathf.Sin(Mathf.Clamp01(age / transitionDuration) * Mathf.PI);
        int tick = Mathf.FloorToInt(age * 50f);
        for (int i = 0; i < noisePixels.Length; i++)
        {
            // Dense horizontal tears around the shared arrowhead, in source pixels
            // measured from the tip. Only the fragments jitter; the click tip stays exact.
            int seed = i * 11 + tick * 7;
            float row = 3f + seed % 17;
            float x = i % 4 == 0 ? -2f : Mathf.Max(0f, Mathf.Min(row * 0.5f - 2f, seed % 10));
            float width = 5f + seed % 8;
            var pixel = noisePixels[i];
            pixel.rectTransform.anchoredPosition = new Vector2(x, -row) * cursorScale;
            pixel.rectTransform.sizeDelta = new Vector2(width, i % 3 == 0 ? 1.5f : 0.75f) * cursorScale;
            Color color = i % 3 == 0 ? new Color(0.95f, 0.66f, 0.82f) : i % 3 == 1 ? Color.white : new Color(0.18f, 0.2f, 0.28f);
            color.a = transitionNoiseOpacity * pulse;
            pixel.color = color;
        }
    }

    private bool HasGameViewFocus()
    {
        if (!focused || !Application.isFocused)
            return false;
#if UNITY_EDITOR
        // Application focus alone is insufficient when the pointer leaves Game View
        // for an Inspector/Scene tab inside the same Editor application.
        var window = UnityEditor.EditorWindow.mouseOverWindow;
        var selected = UnityEditor.EditorWindow.focusedWindow;
        return window != null && selected != null
            && window.GetType().Name == "GameView" && selected.GetType().Name == "GameView";
#else
        return true;
#endif
    }

    private bool GamepadIsActive()
    {
        Gamepad pad = Gamepad.current;
        if (pad != activityGamepad)
        {
            activityGamepad = pad;
            previousLeftStick = previousRightStick = Vector2.zero;
        }
        if (pad == null || !pad.wasUpdatedThisFrame)
            return false;
        Vector2 left = pad.leftStick.ReadValue(), right = pad.rightStick.ReadValue();
        bool stickActive = (left.sqrMagnitude > 0.0625f && (left - previousLeftStick).sqrMagnitude > 0.0001f)
            || (right.sqrMagnitude > 0.0625f && (right - previousRightStick).sqrMagnitude > 0.0001f);
        previousLeftStick = left;
        previousRightStick = right;
        if (stickActive) return true;
        foreach (var control in pad.allControls)
            if (control is ButtonControl button && button.wasPressedThisFrame)
                return true;
        return false;
    }

    private void UpdateView(Vector2 point, bool reset)
    {
        // Constant-pixel overlay coordinates are identical to Input System screen coordinates.
        cursorRoot.position = new Vector3(point.x, point.y, 0f);
        RectTransform arrow = arrowSprite.rectTransform;
        Vector2 displayHotspot = chainStyle ? hotspot : menuHotspot;
        arrow.pivot = Vector2.up;
        arrow.sizeDelta = arrowSprite.sprite.rect.size * cursorScale;
        arrow.anchoredPosition = new Vector2(-displayHotspot.x, displayHotspot.y) * cursorScale;
        foreach (var shadow in shadows)
        {
            if (shadow.effectColor != shadowColor) shadow.effectColor = shadowColor;
            if (shadow.effectDistance != shadowOffset) shadow.effectDistance = shadowOffset;
        }
        Vector2 attachment = new Vector2(connectionOffset.x - hotspot.x, hotspot.y - connectionOffset.y) * cursorScale;
        chainRoot.anchoredPosition = attachment;
        Vector2 anchor = point + attachment;
        if (reset)
        {
            angles[0] = angles[1] = angularVelocities[0] = angularVelocities[1] = 0f;
            filteredVelocity = Vector2.zero;
            previousAnchor = anchor;
        }
        if (chainStyle)
        {
            SimulateChain(anchor, Time.unscaledDeltaTime);
            DrawChain();
        }
    }

    private void SimulateChain(Vector2 anchor, float deltaTime)
    {
        float dt = Mathf.Clamp(deltaTime, 0.0001f, 0.05f);
        Vector2 velocity = (anchor - previousAnchor) / dt;
        Vector2 nextVelocity = Vector2.Lerp(filteredVelocity, velocity, 1f - Mathf.Exp(-20f * dt));
        Vector2 acceleration = (nextVelocity - filteredVelocity) / dt;
        previousAnchor = anchor;
        filteredVelocity = nextVelocity;
        float length = Mathf.Max(1f, chainLength * cursorScale * 0.5f);
        // Cap only cosmetic acceleration. Neither the pointer nor gameplay is clamped.
        Vector2 drive = Vector2.ClampMagnitude(-(acceleration + nextVelocity * 2f) * inertia,
            (gravity + followSpeed * followSpeed * length) * 4f);
        float limit = swingAmplitude * Mathf.Deg2Rad;
        int steps = Mathf.CeilToInt(dt * 120f);
        float h = dt / steps;
        for (int step = 0; step < steps; step++)
        {
            for (int i = 0; i < 2; i++)
            {
                float a = angles[i];
                Vector2 tangent = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                float rest = i == 0 ? 0f : angles[0] * 0.25f;
                float force = Vector2.Dot(drive, tangent) / length * (1f + i * 0.15f)
                    - gravity / length * Mathf.Sin(a) - followSpeed * followSpeed * (a - rest);
                angularVelocities[i] = (angularVelocities[i] + force * h) * Mathf.Exp(-damping * h);
                float candidate = a + angularVelocities[i] * h;
                angles[i] = Mathf.Clamp(candidate, -limit, limit);
                if (Mathf.Abs(candidate) > limit)
                    angularVelocities[i] = 0f;
            }
        }
    }

    private void DrawChain()
    {
        float length = Mathf.Max(1f, chainLength * cursorScale * 0.5f);
        Vector2 start = Vector2.zero;
        for (int i = 0; i < 2; i++)
        {
            RectTransform link = chainLinks[i].rectTransform;
            link.pivot = new Vector2(0f, 0.5f);
            // Each sprite is one unchanged half of the supplied horizontal strip.
            // A half-pixel overlap hides the seam while the two joints bend.
            Vector2 source = chainLinks[i].sprite.rect.size;
            link.sizeDelta = new Vector2(length + 0.5f * cursorScale, length * source.y / source.x);
            link.anchoredPosition = start;
            link.localRotation = Quaternion.Euler(0f, 0f, -90f + angles[i] * Mathf.Rad2Deg);
            start += new Vector2(Mathf.Sin(angles[i]), -Mathf.Cos(angles[i])) * length;
        }
    }

    private void ReleaseCursor()
    {
        if (view != null && view.enabled)
            view.enabled = false;
        if (noiseRoot != null && noiseRoot.gameObject.activeSelf)
            noiseRoot.gameObject.SetActive(false);
        if (!ownsOSCursor)
            return;
        if (Cursor.visible != previousOSVisibility) Cursor.visible = previousOSVisibility;
        ownsOSCursor = false;
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        focused = hasFocus;
        if (!hasFocus) ReleaseCursor();
    }

    private void OnApplicationPause(bool paused)
    {
        applicationPaused = paused;
        if (paused) ReleaseCursor();
    }

    private void OnDisable()
    {
        ReleaseCursor();
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (owner == this) owner = null;
    }

    private void OnApplicationQuit() => ReleaseCursor();
}
