using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>Presentation only: an exact screen-space pointer and two small simulated chain joints.</summary>
[DefaultExecutionOrder(10000), DisallowMultipleComponent, RequireComponent(typeof(Canvas))]
public sealed class GameplayPixelCursor : MonoBehaviour
{
    [Header("View (no raycasts or gameplay physics)")]
    [SerializeField] private RectTransform cursorRoot;
    [SerializeField] private UnityEngine.UI.Image arrowSprite;
    [SerializeField] private RectTransform chainRoot;
    [SerializeField] private UnityEngine.UI.Image[] chainLinks;

    [Header("Arrow (source image pixels, including transparent margins)")]
    [SerializeField, Min(0.25f), Tooltip("1 = original pixels; visible arrow is 16 x 20 px.")]
    private float cursorScale = 1f;
    [SerializeField, Tooltip("From the source image's top-left, NOT the opaque bounds.")]
    private Vector2 hotspot = new Vector2(16f, 14f);
    [SerializeField, Tooltip("Attachment point from the source image's top-left.")]
    private Vector2 connectionOffset = new Vector2(19.5f, 31.5f);

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
    private readonly float[] angles = new float[2];
    private readonly float[] angularVelocities = new float[2];
    private Vector2 previousAnchor, filteredVelocity, lastMousePosition;
    private Vector2Int lastResolution;
    private float previousScale;
    private bool pointerKnown, usingGamepad, ownsOSCursor, previousOSVisibility;
    private bool focused = true, applicationPaused;
    public bool IsShowing => view != null && view.enabled;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOwner() => owner = null;

    private void Awake()
    {
        view = GetComponent<Canvas>();
        view.enabled = false;
        // Discover existing owners once. No new input actions, subscriptions, or weapon references.
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
            return;
        }
        owner = this;
        focused = Application.isFocused;
        pointerKnown = false;
    }

    private void LateUpdate()
    {
        Mouse mouse = Mouse.current;
        if (owner != this || mouse == null || !HasView())
        {
            ReleaseCursor();
            return;
        }

        Vector2 point = mouse.position.ReadValue();
        // Device activity changes presentation only. Existing input readers retain authority.
        bool mouseMoved = pointerKnown && (point - lastMousePosition).sqrMagnitude > 0.01f;
        if (mouseMoved || mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)
            usingGamepad = false;
        else if (GamepadIsActive())
            usingGamepad = true;
        lastMousePosition = point;
        pointerKnown = true;

        bool inside = point.x >= 0f && point.y >= 0f && point.x < Screen.width && point.y < Screen.height;
        if (!HasGameViewFocus() || applicationPaused || !inside || usingGamepad
            || Cursor.lockState == CursorLockMode.Locked || !IsGameplay())
        {
            ReleaseCursor();
            return;
        }

        ShowCursor(point);
    }

    private void ShowCursor(Vector2 point)
    {
        bool reset = !IsShowing || lastResolution != new Vector2Int(Screen.width, Screen.height)
            || !Mathf.Approximately(previousScale, cursorScale);
        // CRTFilterController can convert all scene canvases. The cursor must always stay
        // outside that camera's low-resolution render and distortion, including on Resume.
        view.renderMode = RenderMode.ScreenSpaceOverlay;
        view.worldCamera = null;
        view.scaleFactor = 1f;
        view.pixelPerfect = false; // Do not round the arrow away from the actual mouse coordinate.
        view.sortingOrder = 32000;
        view.enabled = true;
        if (!ownsOSCursor)
        {
            previousOSVisibility = Cursor.visible;
            ownsOSCursor = true;
        }
        Cursor.visible = false;

        UpdateView(point, reset);
        lastResolution = new Vector2Int(Screen.width, Screen.height);
        previousScale = cursorScale;
    }

    private bool HasView() => cursorRoot != null && arrowSprite != null && arrowSprite.sprite != null
        && chainRoot != null && chainLinks != null && chainLinks.Length == 2
        && chainLinks[0] != null && chainLinks[0].sprite != null
        && chainLinks[1] != null && chainLinks[1].sprite != null;

    private bool IsGameplay() => gameObject.scene.name == "CompletScene"
        && Time.timeScale > 0f && !TitleStageTransition.IsTransitioning
        && player != null && player.isActiveAndEnabled && player.InputEnabled && !player.IsGoalInputBlocked
        && (pauseMenu == null || !pauseMenu.IsPaused)
        && (goalMenu == null || !goalMenu.IsGoalReached)
        && (celebration == null || !celebration.IsPlaying)
        && (crt == null || !crt.IsOverlayActive);

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

    private static bool GamepadIsActive()
    {
        Gamepad pad = Gamepad.current;
        if (pad == null || !pad.wasUpdatedThisFrame)
            return false;
        if (pad.leftStick.ReadValue().sqrMagnitude > 0.0625f
            || pad.rightStick.ReadValue().sqrMagnitude > 0.0625f)
            return true;
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
        arrow.pivot = Vector2.up;
        arrow.sizeDelta = arrowSprite.sprite.rect.size * cursorScale;
        arrow.anchoredPosition = new Vector2(-hotspot.x, hotspot.y) * cursorScale;
        Vector2 attachment = new Vector2(connectionOffset.x - hotspot.x, hotspot.y - connectionOffset.y) * cursorScale;
        chainRoot.anchoredPosition = attachment;
        Vector2 anchor = point + attachment;
        if (reset)
        {
            angles[0] = angles[1] = angularVelocities[0] = angularVelocities[1] = 0f;
            filteredVelocity = Vector2.zero;
            previousAnchor = anchor;
        }
        SimulateChain(anchor, Time.unscaledDeltaTime);
        DrawChain();
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
        if (view != null)
            view.enabled = false;
        if (!ownsOSCursor)
            return;
        Cursor.visible = previousOSVisibility;
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
        if (owner == this) owner = null;
    }

    private void OnApplicationQuit() => ReleaseCursor();
}
