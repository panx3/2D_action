using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>Reusable frozen CRT screen, unscaled signal acquisition, and curved pointer mapping.</summary>
[DisallowMultipleComponent]
public sealed class CRTOverlayController : MonoBehaviour
{
    [Header("Presentation")]
    [SerializeField] private GameObject presentationRoot;
    [SerializeField] private CanvasGroup content;
    [SerializeField] private UnityEngine.UI.RawImage backdrop;
    [SerializeField] private UnityEngine.UI.RawImage softenedBackdrop;
    [SerializeField] private TMP_Text heading;
    [SerializeField] private TMP_Text subtitle;
    [SerializeField] private CRTMenuItem[] menuItems;
    [SerializeField] private RectTransform[] sparkles;

    [Header("CRT mode (unscaled)")]
    [SerializeField, Min(0.05f), Tooltip("Shared duration for entering and leaving CRT mode.")]
    private float acquisitionDuration = 0.22f;
    [SerializeField, Range(1f, 1.4f)] private float screenScale = 1.16f;
    [SerializeField, Range(0f, 4f), InspectorName("Transition Jitter (Render Pixels)")]
    private float transitionNoiseStrength = 2.4f;
    [SerializeField, Min(0f)] private float transitionJitterDuration = 0.16f;
    [SerializeField, Range(0f, 0.5f)] private float curvature = 0.22f;
    [SerializeField, Range(0f, 1f)] private float scanlines = 0.58f;
    [SerializeField, Range(0f, 0.2f)] private float noise = 0.045f;
    [SerializeField, Range(0f, 5f)] private float rgbOffsetPixels = 1.2f;
    [SerializeField, Range(0f, 1f)] private float vignette = 0.62f;
    [SerializeField, Range(0f, 0.5f)] private float glassReflection = 0.13f;
    [SerializeField, Range(0f, 6f)] private float backgroundBlur = 3f;
    [SerializeField, Range(0f, 1f)] private float backgroundBrightness = 0.28f;

    private CRTFilterController filter;
    private RenderTexture frozenFrame;
    private RenderTexture softenedFrame;
    private float shownAt;
    private float hiddenAt;
    private float hideStartProgress;
    private System.Action hidden;
    private float currentScale = 1f;
    private Vector2 lastPointer;
    private bool pointerKnown;
    private bool inputBlocked;
    private float signalInterference;
    public bool IsVisible { get; private set; }
    public bool IsClosing { get; private set; }
    public bool IsReady => IsVisible && !IsClosing && frozenFrame != null && Time.unscaledTime - shownAt >= acquisitionDuration;

    private void Awake()
    {
        presentationRoot.SetActive(false);
    }

    public bool Show()
    {
        if (IsVisible)
            return !IsClosing;
        CRTMenuNavigation.EnsureInput();
        if (filter == null)
            filter = FindAnyObjectByType<CRTFilterController>();
        if (filter == null || !filter.TryBeginOverlay(this, backgroundBlur, backgroundBrightness, ReceiveBackdrop))
            return false;

        IsVisible = true;
        pointerKnown = false;
        content.alpha = 0f;
        content.interactable = false;
        return true;
    }

    private void ReceiveBackdrop(RenderTexture frame, RenderTexture softened)
    {
        frozenFrame = frame;
        softenedFrame = softened;
        backdrop.texture = frame;
        softenedBackdrop.texture = softened;
        softenedBackdrop.color = new Color(1f, 1f, 1f, 0f);
        shownAt = Time.unscaledTime;
        presentationRoot.SetActive(true);
        Animate();
    }

    /// <summary>Reverse the current presentation before its owner resumes gameplay.</summary>
    public void HideAnimated(System.Action onHidden)
    {
        if (IsClosing)
            return;
        if (!IsVisible || frozenFrame == null)
        {
            Hide();
            onHidden?.Invoke();
            return;
        }
        hideStartProgress = Mathf.Clamp01((Time.unscaledTime - shownAt) / acquisitionDuration);
        hiddenAt = Time.unscaledTime;
        hidden = onHidden;
        IsClosing = true;
        content.interactable = false;
    }

    /// <summary>Immediate cleanup for scene changes, disable, or another screen taking priority.</summary>
    public void Hide()
    {
        inputBlocked = false;
        signalInterference = 0f;
        IsVisible = false;
        IsClosing = false;
        hidden = null;
        if (presentationRoot != null)
            presentationRoot.SetActive(false);
        if (filter != null)
            filter.EndOverlay(this);
        if (backdrop != null)
            backdrop.texture = null;
        if (softenedBackdrop != null)
            softenedBackdrop.texture = null;
        if (frozenFrame != null)
        {
            frozenFrame.Release();
            Destroy(frozenFrame);
            frozenFrame = null;
        }
        if (softenedFrame != null)
        {
            softenedFrame.Release();
            Destroy(softenedFrame);
            softenedFrame = null;
        }
    }

    public void SetMessage(string title, string detail)
    {
        heading.text = title;
        subtitle.text = detail;
    }

    public void SetInputBlocked(bool blocked)
    {
        inputBlocked = blocked;
        content.interactable = IsReady && !blocked;
    }

    /// <summary>Brief whole-screen signal disturbance without acquiring another overlay.</summary>
    public void SetSignalInterference(float amount) => signalInterference = Mathf.Clamp01(amount);

    private void Update()
    {
        if (!IsVisible || frozenFrame == null)
            return;
        Animate();
        if (IsClosing && Time.unscaledTime - hiddenAt >= acquisitionDuration * hideStartProgress)
        {
            System.Action onHidden = hidden;
            Hide();
            onHidden?.Invoke();
            return;
        }
        if (IsReady && !inputBlocked)
        {
            EnsureMenuSelection();
            HandleCurvedPointer();
        }
    }

    private void EnsureMenuSelection()
    {
        var events = EventSystem.current;
        if (events == null) return;
        var selected = events.currentSelectedGameObject;
        foreach (CRTMenuItem item in menuItems)
        {
            var button = item.Button;
            if (button.gameObject == selected && button.isActiveAndEnabled && button.IsInteractable())
                return;
        }
        foreach (CRTMenuItem item in menuItems)
        {
            var button = item.Button;
            if (!button.isActiveAndEnabled || !button.IsInteractable()) continue;
            events.SetSelectedGameObject(button.gameObject);
            return;
        }
    }

    private void Animate()
    {
        float age = Time.unscaledTime - shownAt;
        float t = IsClosing
            ? Mathf.Clamp01(hideStartProgress - (Time.unscaledTime - hiddenAt) / acquisitionDuration)
            : Mathf.Clamp01(age / acquisitionDuration);
        float blend = Mathf.SmoothStep(0f, 1f, t);
        float jitterAge = IsClosing ? Time.unscaledTime - hiddenAt : age;
        float pulse = 0f;
        if (transitionJitterDuration > 0f && jitterAge < transitionJitterDuration)
        {
            float jitterProgress = Mathf.Clamp01(jitterAge / transitionJitterDuration);
            // Three short, diminishing row shifts instead of a full-screen static flash.
            float burst = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Repeat(jitterProgress * 3f, 1f));
            pulse = (1f - jitterProgress) * (0.25f + 0.75f * burst);
        }
        currentScale = Mathf.Lerp(1f, screenScale, blend);
        pulse = Mathf.Max(pulse, signalInterference * 0.45f);
        filter.SetOverlayFrame(this, curvature, scanlines, noise + signalInterference * 0.14f, rgbOffsetPixels,
            vignette, glassReflection, pulse, screenScale, transitionNoiseStrength, blend);
        softenedBackdrop.color = new Color(1f, 1f, 1f, blend);
        content.alpha = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.05f, 0.8f, t));
        content.interactable = IsReady && !inputBlocked;
        for (int i = 0; i < sparkles.Length; i++)
        {
            float wave = 0.65f + 0.35f * Mathf.Sin(age * 2.1f + i * 2.4f);
            sparkles[i].localScale = Vector3.one * wave;
        }
    }

    private void HandleCurvedPointer()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || EventSystem.current == null)
            return;
        Vector2 point = mouse.position.ReadValue();
        bool moved = pointerKnown && (point - lastPointer).sqrMagnitude > 0.25f;
        lastPointer = point;
        pointerKnown = true;
        if (!moved && !mouse.leftButton.wasPressedThisFrame)
            return;

        // Same destination -> source mapping as CRTFilter.shader. The UI raycaster sees
        // undistorted geometry, so CRT buttons use this mapping for mouse input only.
        Vector2 p = new Vector2(point.x / Screen.width, point.y / Screen.height) * 2f - Vector2.one;
        p /= currentScale;
        p *= 1f + curvature * p.sqrMagnitude;
        Vector2 sourcePoint = (p * 0.5f + Vector2.one * 0.5f);
        sourcePoint = Vector2.Scale(sourcePoint, new Vector2(Screen.width, Screen.height));
        Canvas canvas = backdrop.canvas;
        Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        foreach (CRTMenuItem item in menuItems)
        {
            UnityEngine.UI.Button button = item.Button;
            if (!button.isActiveAndEnabled || !button.interactable
                || !RectTransformUtility.RectangleContainsScreenPoint((RectTransform)button.transform, sourcePoint, camera))
                continue;
            EventSystem.current.SetSelectedGameObject(button.gameObject);
            if (mouse.leftButton.wasPressedThisFrame)
                button.onClick.Invoke();
            break;
        }
    }

    private void OnDisable() => Hide();
}
