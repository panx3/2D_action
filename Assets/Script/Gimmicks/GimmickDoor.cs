using System.Collections;
using UnityEngine;

public class GimmickDoor : MonoBehaviour
{
    [Header("Door State")]
    [SerializeField] private bool startsOpen = false;

    [Header("Linked Switch Hint")]
    [SerializeField, Tooltip("閉じたドアに初めて近づいたとき、接続されたスイッチをカメラで案内します。")]
    private bool showSwitchHint = true;
    [SerializeField, Min(0f), Tooltip("プレイヤーとドアのCollider間の距離（world units）。")]
    private float switchHintDistance = 1.5f;

    [Header("Visual Settings")]
    [SerializeField] private SpriteRenderer backRenderer;
    [SerializeField] private SpriteRenderer barrierRenderer;
    [SerializeField] private SpriteRenderer frontRenderer;
    [SerializeField] private Sprite closedSprite;
    [SerializeField] private Sprite openSprite;
    [SerializeField] private Color closedColor = Color.white;
    [SerializeField] private Color openColor = Color.white;
    [SerializeField, Min(0.01f)] private float transitionDuration = 0.22f;

    [Header("Open Sound")]
    [SerializeField] private AudioClip openClip;
    [SerializeField, Range(0f, 1f)] private float openVolume = 0.9f;

    private AudioSource openAudioSource;

    [Header("Layered Stone Artwork")]
    [SerializeField] private bool alignArtworkLayers;
    [SerializeField] private Vector3 artworkLocalPosition = new Vector3(0f, -0.55f, 0f);
    [SerializeField] private Vector3 artworkLocalScale = new Vector3(1.65f, 0.825f, 1f);
    [SerializeField, Min(0.01f), Tooltip("膜の中央に置く通行止め判定の厚み（world units）。")]
    private float barrierThickness = 0.12f;

    private Collider2D doorCollider;
    private bool isOpen;
    private bool initialized;
    private Coroutine transitionRoutine;
    private WeightSwitch linkedSwitch;
    private Player hintPlayer;
    private Collider2D hintPlayerCollider;
    private bool switchHintShown;

    public bool IsOpen => isOpen;
    public bool TransitionHadIntermediateFrame { get; private set; }
    public Vector3 RevealFocusPosition => frontRenderer != null ? frontRenderer.bounds.center : transform.position;

    private void Awake()
    {
        doorCollider = GetComponent<Collider2D>();
        ResolveRenderers();
        AlignArtworkLayers();
        openAudioSource = OneShotAudioUtility.FindWorldImpactSource();
        isOpen = startsOpen;
        ApplyStateImmediate();
        initialized = true;
    }

    internal void RegisterSwitch(WeightSwitch weightSwitch)
    {
        if (linkedSwitch == null || !linkedSwitch.isActiveAndEnabled)
            linkedSwitch = weightSwitch;
    }

    private void Update()
    {
        if (!showSwitchHint || switchHintShown || isOpen || Time.timeScale <= 0f
            || linkedSwitch == null || !linkedSwitch.isActiveAndEnabled || linkedSwitch.IsPressed
            || doorCollider == null || !doorCollider.enabled)
            return;

        if (hintPlayer == null && Camera.main != null)
        {
            hintPlayer = Camera.main.GetComponent<CameraFollow>()?.FollowPlayer;
            if (hintPlayer != null)
                hintPlayerCollider = hintPlayer.GetComponent<Collider2D>();
        }
        if (hintPlayer == null || !hintPlayer.isActiveAndEnabled || hintPlayer.gameObject.scene != gameObject.scene
            || hintPlayerCollider == null || !hintPlayerCollider.enabled)
            return;

        ColliderDistance2D distance = doorCollider.Distance(hintPlayerCollider);
        if (distance.isValid && distance.distance <= switchHintDistance && linkedSwitch.TryBeginSwitchReveal(this))
            switchHintShown = true;
    }

    public void Open()
    {
        SetOpenState(true);
    }

    public void Close()
    {
        SetOpenState(false);
    }

    public void Toggle()
    {
        if (isOpen)
        {
            Close();
        }
        else
        {
            Open();
        }
    }

    private void SetOpenState(bool open)
    {
        ResolveRenderers();
        bool changed = !initialized || isOpen != open;
        isOpen = open;

        if (doorCollider != null)
            doorCollider.enabled = !isOpen;

        if (!changed)
            return;

        if (!isActiveAndEnabled)
        {
            ApplyStateImmediate();
            return;
        }

        if (transitionRoutine != null)
            StopCoroutine(transitionRoutine);
        if (open)
            OneShotAudioUtility.Play2D(openAudioSource, openClip, openVolume, transform.position);
        TransitionHadIntermediateFrame = false;
        transitionRoutine = StartCoroutine(AnimateBarrier(isOpen ? 0f : 1f));

    }

    private void AlignArtworkLayers()
    {
        if (!alignArtworkLayers)
            return;

        Vector3 scale = artworkLocalScale;
        scale.y = scale.x * Mathf.Abs(transform.lossyScale.x) / Mathf.Max(0.001f, Mathf.Abs(transform.lossyScale.y));
        // Old scene instances may override the separately aligned legacy sprites.
        foreach (SpriteRenderer renderer in new[] { backRenderer, barrierRenderer, frontRenderer })
        {
            if (renderer == null)
                continue;
            renderer.transform.localPosition = artworkLocalPosition;
            renderer.transform.localRotation = Quaternion.identity;
            renderer.transform.localScale = scale;
        }
        if (barrierRenderer != null)
            barrierRenderer.sortingOrder = 1;
        if (doorCollider is BoxCollider2D blocker)
        {
            blocker.size = new Vector2(barrierThickness / Mathf.Max(0.001f, Mathf.Abs(transform.lossyScale.x)), 1.30f * scale.y);
            blocker.offset = (Vector2)artworkLocalPosition + new Vector2(0f, 0.65f * scale.y);
        }
    }

    private IEnumerator AnimateBarrier(float targetAlpha)
    {
        if (barrierRenderer == null)
            yield break;

        float startAlpha = barrierRenderer.color.a;
        float elapsed = 0f;
        float duration = Mathf.Max(0.01f, transitionDuration);

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            float alpha = Mathf.Lerp(startAlpha, targetAlpha, t);
            if (alpha > 0.001f && alpha < 0.999f)
                TransitionHadIntermediateFrame = true;
            SetBarrierAlpha(alpha);
            yield return null;
        }

        SetBarrierAlpha(targetAlpha);
        transitionRoutine = null;
    }

    private void ResolveRenderers()
    {
        if (backRenderer == null)
            backRenderer = transform.Find("Door_Back")?.GetComponent<SpriteRenderer>();
        if (barrierRenderer == null)
            barrierRenderer = transform.Find("Door_Barrier")?.GetComponent<SpriteRenderer>();
        if (frontRenderer == null)
            frontRenderer = transform.Find("Door_Front")?.GetComponent<SpriteRenderer>();
    }

    private void ApplyStateImmediate()
    {
        if (doorCollider != null)
            doorCollider.enabled = !isOpen;

        if (backRenderer != null)
        {
            if (openSprite != null)
                backRenderer.sprite = openSprite;
            backRenderer.color = openColor;
        }

        if (barrierRenderer != null)
        {
            if (closedSprite != null)
                barrierRenderer.sprite = closedSprite;
            barrierRenderer.color = closedColor;
            SetBarrierAlpha(isOpen ? 0f : 1f);
        }
    }

    private void SetBarrierAlpha(float alpha)
    {
        if (barrierRenderer == null)
            return;

        Color color = barrierRenderer.color;
        color.a = Mathf.Clamp01(alpha);
        barrierRenderer.color = color;
    }

    private void OnDisable()
    {
        if (transitionRoutine != null)
            StopCoroutine(transitionRoutine);
        transitionRoutine = null;
    }
}
