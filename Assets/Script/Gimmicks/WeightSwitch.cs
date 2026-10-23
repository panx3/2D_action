using System.Collections;
using UnityEngine;
using UnityEngine.Events;

public partial class WeightSwitch : MonoBehaviour
{
    [Header("Detect Settings")]
    [SerializeField] private string targetTag = "morningstar";

    [Header("Visual Settings")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Transform visualRoot;
    [SerializeField] private Sprite offSprite;
    [SerializeField] private Sprite onSprite;
    [SerializeField] private Color offColor = Color.white;
    [SerializeField] private Color onColor = Color.white;
    [SerializeField] private Vector3 pressedLocalOffset = new Vector3(0f, -0.05f, 0f);
    [SerializeField, Min(0f)] private float visualMoveSpeed = 0.5f;

    [Header("Stone Pressure Mechanism")]
    [SerializeField] private Sprite restingBaseSprite;
    [SerializeField] private SpriteRenderer pressureCapRenderer;
    [SerializeField] private SpriteRenderer pistonRenderer;
    [SerializeField] private Vector3 artworkLocalPosition = new Vector3(0f, -0.5f, 0f);
    [SerializeField] private Vector3 artworkLocalScale = new Vector3(0.53f, 2.65f, 1f);
    [SerializeField, Min(0f)] private float pressTravel = 0.4875f;
    [SerializeField, Min(0f)] private float pressDuration = 0.16f;

    [Header("Press Sound")]
    [SerializeField] private AudioClip pressClip;
    [SerializeField, Range(0f, 1f)] private float pressVolume = 0.9f;
    [SerializeField] private AudioSource pressAudioSource;

    [Header("Events")]
    [Tooltip("一度だけ呼ばれます。扉のカメラ演出がある場合、到着してから発火します。")]
    [SerializeField] private UnityEvent onPressed;
    // Keep serialized legacy connections. A latched switch never invokes release.
    [SerializeField, HideInInspector] private UnityEvent onReleased;

    private bool isPressed;
    private Collider2D detectionCollider;
    private Vector3 visualRestLocalPosition;
    private Vector3 capRestPosition;
    private Vector3 pistonRestScale;
    private Coroutine pressRoutine;

    public bool IsPressed => isPressed;
    public event System.Action Pressed;

    private void Awake()
    {
        detectionCollider = GetComponent<Collider2D>();
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (visualRoot == null && spriteRenderer != null)
            visualRoot = spriteRenderer.transform;
        if (visualRoot != null)
        {
            if (restingBaseSprite != null)
            {
                visualRoot.localPosition = artworkLocalPosition;
                Vector3 scale = artworkLocalScale;
                scale.y = scale.x * Mathf.Abs(transform.lossyScale.x) / Mathf.Max(0.001f, Mathf.Abs(transform.lossyScale.y));
                visualRoot.localScale = scale;
                if (detectionCollider is BoxCollider2D trigger)
                {
                    trigger.size = Vector2.Scale(new Vector2(1.58f, 0.40f), scale);
                    trigger.offset = (Vector2)artworkLocalPosition + new Vector2(0f, 0.75f * scale.y);
                }
            }
            visualRestLocalPosition = visualRoot.localPosition;
        }
        if (pressureCapRenderer != null)
            capRestPosition = pressureCapRenderer.transform.localPosition;
        if (pistonRenderer != null)
            pistonRestScale = pistonRenderer.transform.localScale;
        if (pressAudioSource == null)
            pressAudioSource = OneShotAudioUtility.FindWorldImpactSource();
        ApplyRestingVisual();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (isActiveAndEnabled && !isPressed && other.CompareTag(targetTag))
            PressOnce();
    }

    private void PressOnce()
    {
        // Latch before invoking sound, animation or external listeners.
        if (isPressed)
            return;
        isPressed = true;
        if (detectionCollider != null)
            detectionCollider.enabled = false;

        OneShotAudioUtility.Play2D(pressAudioSource, pressClip, pressVolume, transform.position);
        if (isActiveAndEnabled && pressDuration > 0f)
            pressRoutine = StartCoroutine(AnimatePress());
        else
            ApplyPressedVisual();

        Pressed?.Invoke();
        if (!TryBeginDoorReveal())
            DispatchPressedConnection();
    }

    private IEnumerator AnimatePress()
    {
        float elapsed = 0f;
        while (elapsed < pressDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / pressDuration));
            if (pressureCapRenderer != null)
                pressureCapRenderer.transform.localPosition = capRestPosition + Vector3.down * (pressTravel * t);
            if (pistonRenderer != null)
            {
                Vector3 scale = pistonRestScale;
                scale.y *= 1f - t;
                pistonRenderer.transform.localScale = scale;
            }
            if (restingBaseSprite == null && visualRoot != null)
                visualRoot.localPosition = Vector3.MoveTowards(visualRoot.localPosition,
                    visualRestLocalPosition + pressedLocalOffset, Mathf.Max(0f, visualMoveSpeed) * Time.unscaledDeltaTime);
            yield return null;
        }
        ApplyPressedVisual();
        pressRoutine = null;
    }

    private void ApplyRestingVisual()
    {
        if (spriteRenderer == null)
            return;
        Sprite sprite = restingBaseSprite != null ? restingBaseSprite : offSprite;
        if (sprite != null) spriteRenderer.sprite = sprite;
        spriteRenderer.color = offColor;
    }

    private void ApplyPressedVisual()
    {
        if (spriteRenderer != null)
        {
            if (onSprite != null) spriteRenderer.sprite = onSprite;
            spriteRenderer.color = onColor;
        }
        if (pressureCapRenderer != null)
        {
            // The player support collider shares this transform and stays at the pressed height.
            pressureCapRenderer.transform.localPosition = capRestPosition + Vector3.down * pressTravel;
            pressureCapRenderer.enabled = false;
        }
        if (pistonRenderer != null) pistonRenderer.enabled = false;
        if (visualRoot != null)
            visualRoot.localPosition = visualRestLocalPosition + pressedLocalOffset;
    }

    private void OnDisable()
    {
        CancelDoorReveal();
        if (pressRoutine != null)
        {
            StopCoroutine(pressRoutine);
            pressRoutine = null;
        }
        if (isPressed)
            ApplyPressedVisual();
    }
}
