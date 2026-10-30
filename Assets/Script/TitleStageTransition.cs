using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Persistent title-to-stage veil, async load, and reveal gate for the existing intro.</summary>
[DisallowMultipleComponent]
public sealed class TitleStageTransition : MonoBehaviour
{
    public enum TransitionPhase { TitleDeparture, Covering, Loading, Revealing, Complete }
    public enum WipeDirection { LeftToRight, RightToLeft }
    [Serializable]
    public sealed class Settings
    {
        [Min(0f)] public float confirmDuration = .18f;
        [Min(.01f)] public float zoomDuration = .34f;
        [Range(1f, 1.08f)] public float backgroundZoom = 1.045f;
        [Min(.01f)] public float coverDuration = .60f;
        [Min(0f)] public float minimumLoadingDuration = 1.8f;
        [Min(0f)] public float loadingUiFadeDuration = .18f;
        [Min(.01f)] public float revealDuration = .4f;
        public WipeDirection coverDirection = WipeDirection.LeftToRight;
        public WipeDirection revealDirection = WipeDirection.LeftToRight;
        [Range(3f, 18f), Tooltip("Dot size at 1080p; scales with screen height.")] public float pixelSize = 8f;
        [Range(.02f, .2f)] public float edgeScatter = .085f;
        [Range(0f, 1f)] public float patternStrength = .85f;
        [Min(.4f)] public float pendulumPeriod = 2.15f;
        [Range(5f, 40f)] public float pendulumAmplitude = 18f;
        [Range(0f, 1f)] public float wipeSoundVolume = .12f;
        public float DepartureDuration => Mathf.Max(0f, confirmDuration) + Mathf.Max(.01f, zoomDuration);
    }
    private static TitleStageTransition active;
    private static int activationCount;
    private static float lastLoadingVisibleDuration, lastTotalDuration;
    private static bool wasBlackAtActivation;
    private Material veilMaterial;
    private CanvasGroup loadingGroup;
    private RectTransform pendulum;
    private TMP_Text loadingText;
    private AudioSource audioSource;
    private AudioClip coverSound, revealSound;
    private AsyncOperation load;
    private TitleScreenController title;
    private Settings settings;
    private double startedAt, loadingShownAt;
    private float previousTimeScale;
    private bool ownsTime;
    private float wipeProgress;
    private TransitionPhase phase;
    // The same gate keeps the existing stage entrance, BGM and cursor behind either presentation.
    public static bool IsTransitioning => active != null || StageSelectController.IsEnteringStage;
    public static bool IsLoadingVisible => active != null && active.phase == TransitionPhase.Loading && active.loadingGroup.alpha > .5f;
    public static TransitionPhase Phase => active != null ? active.phase : TransitionPhase.Complete;
    public static float WipeProgress => active != null ? active.wipeProgress : 1f;
    public static int ActivationCount => activationCount;
    public static float LastLoadingVisibleDuration => lastLoadingVisibleDuration;
    public static float LastTotalDuration => lastTotalDuration;
    public static bool WasBlackAtActivation => wasBlackAtActivation;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        active = null; activationCount = 0; lastLoadingVisibleDuration = 0;
        lastTotalDuration = 0; wasBlackAtActivation = false;
    }
    public static bool Begin(string sceneName, Sprite ballSprite, TMP_FontAsset font,
        Shader wipeShader, Texture2D backdrop, Settings configuration, TitleScreenController origin)
    {
        if (active != null || origin == null || configuration == null || wipeShader == null
            || !wipeShader.isSupported || string.IsNullOrWhiteSpace(sceneName)
            || !Application.CanStreamedLevelBeLoaded(sceneName)) return false;
        var root = new GameObject(nameof(TitleStageTransition));
        DontDestroyOnLoad(root);
        active = root.AddComponent<TitleStageTransition>();
        active.settings = configuration; active.title = origin;
        active.startedAt = Time.realtimeSinceStartupAsDouble;
        active.previousTimeScale = Time.timeScale; active.ownsTime = true; Time.timeScale = 0f;
        activationCount = 0; lastLoadingVisibleDuration = 0; lastTotalDuration = 0; wasBlackAtActivation = false;
        active.BuildView(ballSprite, font, wipeShader, backdrop);
        active.audioSource = root.AddComponent<AudioSource>();
        active.audioSource.playOnAwake = false; active.audioSource.spatialBlend = 0;
        origin.PlayTransitionConfirm(active.audioSource);
        active.coverSound = CreateWipeSound(false); active.revealSound = CreateWipeSound(true);
        active.StartCoroutine(active.Run(sceneName));
        return true;
    }
    private void LateUpdate()
    {
        veilMaterial.SetFloat("_TransitionTime", (float)((Time.realtimeSinceStartupAsDouble - startedAt) % 120d));
        if (phase != TransitionPhase.Loading || loadingGroup.alpha <= 0) return;
        float t = (float)(Time.realtimeSinceStartupAsDouble - loadingShownAt);
        pendulum.localRotation = Quaternion.Euler(0, 0,
            -Mathf.Cos(t * Mathf.PI * 2f / Mathf.Max(.4f, settings.pendulumPeriod)) * settings.pendulumAmplitude);
        loadingText.alpha = .84f + .12f * Mathf.Cos(t * Mathf.PI * 2f / 3.2f);
    }
    private IEnumerator Run(string sceneName)
    {
        phase = TransitionPhase.TitleDeparture;
        title.PresentStartTransition(0);
        // Render the immediate response before requesting disk/asset work.
        yield return null;
        try
        {
            load = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            if (load != null) load.allowSceneActivation = false;
        }
        catch (Exception exception) { Debug.LogException(exception, this); }
        if (load == null) { title.RestoreAfterTransitionFailure(); Finish(); yield break; }
        float elapsed = 0, duration = settings.DepartureDuration;
        while (elapsed < duration)
        {
            elapsed = Mathf.Min(duration, elapsed + Time.unscaledDeltaTime);
            title.PresentStartTransition(elapsed); yield return null;
        }
        phase = TransitionPhase.Covering;
        audioSource.PlayOneShot(coverSound, settings.wipeSoundVolume);
        yield return Wipe(false, settings.coverDirection, settings.coverDuration);
        phase = TransitionPhase.Loading;
        loadingGroup.alpha = 0;
        while (load.progress < .9f) yield return null;
        // All stage Awake/Start calls happen beneath a completely opaque veil.
        wasBlackAtActivation = wipeProgress >= .999f && phase == TransitionPhase.Loading;
        activationCount++; load.allowSceneActivation = true;
        while (!load.isDone) yield return null;
        title = null;
        // Allow camera, intro setup and canvases to render before revealing any pixels.
        yield return null; yield return null;
        // Scene activation can stall a frame. Start the visible swing only after Awake/Start work.
        loadingShownAt = Time.realtimeSinceStartupAsDouble;
        while (Time.realtimeSinceStartupAsDouble - loadingShownAt < Mathf.Max(0, settings.minimumLoadingDuration))
        { RevealLoadingLabel(); yield return null; }
        loadingGroup.alpha = 1;
        lastLoadingVisibleDuration = (float)(Time.realtimeSinceStartupAsDouble - loadingShownAt);
        elapsed = 0;
        while (elapsed < settings.loadingUiFadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            loadingGroup.alpha = 1 - Mathf.SmoothStep(0, 1, elapsed / settings.loadingUiFadeDuration);
            yield return null;
        }
        loadingGroup.alpha = 0; phase = TransitionPhase.Revealing;
        audioSource.PlayOneShot(revealSound, settings.wipeSoundVolume * .85f);
        yield return Wipe(true, settings.revealDirection, settings.revealDuration);
        Finish();
    }
    private void RevealLoadingLabel() => loadingGroup.alpha = Mathf.SmoothStep(0, 1,
        (float)(Time.realtimeSinceStartupAsDouble - loadingShownAt) / Mathf.Max(.01f, settings.loadingUiFadeDuration));
    private IEnumerator Wipe(bool revealing, WipeDirection direction, float duration)
    {
        veilMaterial.SetFloat("_Revealing", revealing ? 1 : 0);
        veilMaterial.SetFloat("_RightToLeft", direction == WipeDirection.RightToLeft ? 1 : 0);
        float elapsed = 0; SetWipe(0);
        while (elapsed < Mathf.Max(.01f, duration))
        {
            elapsed = Mathf.Min(Mathf.Max(.01f, duration), elapsed + Time.unscaledDeltaTime);
            SetWipe(Mathf.SmoothStep(0, 1, elapsed / Mathf.Max(.01f, duration))); yield return null;
        }
        SetWipe(1);
    }
    private void SetWipe(float progress) { wipeProgress = progress; veilMaterial.SetFloat("_Progress", progress); }
    private void BuildView(Sprite ballSprite, TMP_FontAsset font, Shader shader, Texture2D backdrop)
    {
        var canvasRoot = new GameObject("TransitionCanvas", typeof(RectTransform), typeof(Canvas),
            typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
        canvasRoot.transform.SetParent(transform, false);
        var canvas = canvasRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.overrideSorting = true; canvas.sortingOrder = short.MaxValue;
        var scaler = canvasRoot.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080); scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.Expand;
        var veil = Image("DotVeil", canvasRoot.transform, Color.white); Stretch(veil.rectTransform); veil.raycastTarget = true;
        veilMaterial = new Material(shader) { name = "Title dot wipe (runtime)", hideFlags = HideFlags.HideAndDontSave };
        veilMaterial.SetFloat("_PixelSize", settings.pixelSize); veilMaterial.SetFloat("_Scatter", settings.edgeScatter);
        veilMaterial.SetFloat("_PatternStrength", settings.patternStrength);
        if (backdrop != null) veilMaterial.SetTexture("_BackdropTex", backdrop);
        veilMaterial.SetFloat("_Progress", 0); veil.material = veilMaterial;
        var loadingRoot = Rect("LoadingRoot", canvasRoot.transform);
        loadingRoot.anchorMin = loadingRoot.anchorMax = loadingRoot.pivot = new Vector2(1, 0);
        loadingRoot.anchoredPosition = new Vector2(-66, 55); loadingRoot.sizeDelta = new Vector2(280, 116);
        loadingGroup = loadingRoot.gameObject.AddComponent<CanvasGroup>(); loadingGroup.alpha = 0;
        pendulum = Rect("PendulumPivot", loadingRoot); pendulum.anchorMin = pendulum.anchorMax = Vector2.zero;
        pendulum.anchoredPosition = new Vector2(40, 102);
        pendulum.localRotation = Quaternion.Euler(0, 0, -settings.pendulumAmplitude);
        for (int i = 0; i < 8; i++)
        {
            var link = Image("ChainLink_" + i, pendulum, new Color(.79f, .69f, .75f, .85f));
            link.rectTransform.anchoredPosition = new Vector2(0, -5 - i * 7);
            link.rectTransform.sizeDelta = i % 2 == 0 ? new Vector2(4, 7) : new Vector2(2, 7);
        }
        var ball = Image("LoadingBall", pendulum, new Color(.86f, .86f, .88f)); ball.sprite = ballSprite;
        ball.preserveAspect = true; ball.rectTransform.sizeDelta = new Vector2(48, 48);
        ball.rectTransform.anchoredPosition = new Vector2(0, -78);
        var label = Rect("LoadingText", loadingRoot); label.anchorMin = label.anchorMax = Vector2.zero;
        label.pivot = new Vector2(0, .5f); label.anchoredPosition = new Vector2(94, 29); label.sizeDelta = new Vector2(184, 30);
        loadingText = label.gameObject.AddComponent<TextMeshProUGUI>(); loadingText.font = font;
        loadingText.text = "LOADING"; loadingText.fontSize = 22; loadingText.characterSpacing = 10;
        loadingText.color = new Color(.82f, .82f, .79f); loadingText.alignment = TextAlignmentOptions.MidlineLeft;
        loadingText.raycastTarget = false; loadingText.textWrappingMode = TextWrappingModes.NoWrap;
    }
    private static AudioClip CreateWipeSound(bool opening)
    {
        const int rate = 24000, count = 6720;
        var samples = new float[count]; uint seed = 72341; float filtered = 0;
        for (int i = 0; i < count; i++)
        {
            seed = seed * 1664525u + 1013904223u;
            float t = i / (float)count, noise = ((seed >> 8) / 16777215f) * 2 - 1;
            filtered = Mathf.Lerp(filtered, noise, opening ? .09f : .065f);
            float envelope = Mathf.Pow(Mathf.Sin(t * Mathf.PI), 2), pitch = opening ? 380 + t * 260 : 540 - t * 260;
            samples[i] = (filtered * .6f + Mathf.Sin(i / (float)rate * pitch * Mathf.PI * 2) * .035f) * envelope;
        }
        var clip = AudioClip.Create(opening ? "Pixel reveal air" : "Pixel cover air", count, 1, rate, false);
        clip.SetData(samples, 0); return clip;
    }
    private static RectTransform Rect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = Vector2.zero; return rect;
    }
    private static UnityEngine.UI.Image Image(string name, Transform parent, Color color)
    {
        var image = Rect(name, parent).gameObject.AddComponent<UnityEngine.UI.Image>();
        image.color = color; image.raycastTarget = false; return image;
    }
    private static void Stretch(RectTransform rect)
    { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
    private void Finish()
    {
        phase = TransitionPhase.Complete; lastTotalDuration = (float)(Time.realtimeSinceStartupAsDouble - startedAt);
        RestoreTime(); if (active == this) active = null; Destroy(gameObject);
    }
    private void RestoreTime()
    { if (!ownsTime) return; Time.timeScale = previousTimeScale; ownsTime = false; }
    private void OnDestroy()
    {
        RestoreTime();
        // Async activation cannot be cancelled; never leave Unity's loading queue blocked.
        if (load != null && !load.isDone) load.allowSceneActivation = true;
        if (active == this) active = null;
        Release(veilMaterial); Release(coverSound); Release(revealSound);
    }
    private static void Release(UnityEngine.Object value)
    {
        if (value == null) return;
        if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
    }
}
