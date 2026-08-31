using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// タイトルからステージへ移る間だけ存在する、黒フェードとLoading表示。
/// Sceneを跨いで黒を維持し、ステージの最初の描画まで覆う。
/// </summary>
[DisallowMultipleComponent]
public sealed class TitleStageTransition : MonoBehaviour
{
    private static readonly Color LoadingBackground = new Color32(5, 5, 5, 255);
    private static readonly Color AccentPink = new Color32(255, 74, 181, 255);
    private const float LoadingRevealDuration = 0.08f;

    private static TitleStageTransition _active;
    private static int _activationCount;
    private static float _lastLoadingVisibleDuration;
    private static bool _wasBlackAtActivation;

    private CanvasGroup _loadingPage;
    private CanvasGroup _fadeOverlay;
    private RectTransform[] _rotatingPivots;
    private float _rotationSpeed;
    private float _timeScaleBeforeTransition;
    private bool _timeScaleCaptured;
    private double _loadingShownAt;

    public static bool IsTransitioning => _active != null;
    public static bool IsLoadingVisible => _active != null
        && _active._loadingPage != null
        && _active._loadingPage.alpha > 0.99f
        && _active._fadeOverlay != null
        && _active._fadeOverlay.alpha < 0.99f;
    public static int ActivationCount => _activationCount;
    public static float LastLoadingVisibleDuration => _lastLoadingVisibleDuration;
    public static bool WasBlackAtActivation => _wasBlackAtActivation;

    public static bool Begin(
        string sceneName,
        Sprite ballSprite,
        TMP_FontAsset font,
        float fadeOutDuration,
        float minimumLoadingDuration,
        float loadingFadeOutDuration,
        float stageFadeInDuration,
        float rotationSpeed)
    {
        if (_active != null || string.IsNullOrWhiteSpace(sceneName))
            return false;

        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError($"[TitleStageTransition] Sceneをロードできません: {sceneName}");
            return false;
        }

        _activationCount = 0;
        _lastLoadingVisibleDuration = 0f;
        _wasBlackAtActivation = false;

        GameObject root = new GameObject(nameof(TitleStageTransition));
        DontDestroyOnLoad(root);
        _active = root.AddComponent<TitleStageTransition>();
        _active._rotationSpeed = Mathf.Abs(rotationSpeed);
        _active.BuildView(ballSprite, font);
        _active.CaptureAndPauseTime();
        _active.StartCoroutine(_active.Run(
            sceneName,
            Mathf.Max(0f, fadeOutDuration),
            Mathf.Max(0f, minimumLoadingDuration),
            Mathf.Max(0f, loadingFadeOutDuration),
            Mathf.Max(0f, stageFadeInDuration)));
        return true;
    }

    private void Update()
    {
        if (_rotatingPivots == null || _loadingPage == null || _loadingPage.alpha <= 0f)
            return;

        float rotation = -_rotationSpeed * Time.unscaledDeltaTime;
        for (int i = 0; i < _rotatingPivots.Length; i++)
        {
            if (_rotatingPivots[i] != null)
                _rotatingPivots[i].Rotate(0f, 0f, rotation);
        }
    }

    private IEnumerator Run(
        string sceneName,
        float fadeOutDuration,
        float minimumLoadingDuration,
        float loadingFadeOutDuration,
        float stageFadeInDuration)
    {
        // タイトルを一度完全な黒で覆ってからLoadingを出す。
        yield return Fade(_fadeOverlay, 0f, 1f, fadeOutDuration);
        _loadingPage.alpha = 1f;
        _loadingShownAt = Time.realtimeSinceStartupAsDouble;

        AsyncOperation load = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
        if (load == null)
        {
            Debug.LogError($"[TitleStageTransition] 非同期ロードを開始できませんでした: {sceneName}");
            Abort();
            yield break;
        }

        load.allowSceneActivation = false;
        yield return Fade(_fadeOverlay, 1f, 0f, LoadingRevealDuration);

        // ロード完了準備と最低表示時間の両方を待つ。
        while (load.progress < 0.9f
               || Time.realtimeSinceStartupAsDouble - _loadingShownAt < minimumLoadingDuration)
            yield return null;

        yield return Fade(_fadeOverlay, 0f, 1f, loadingFadeOutDuration);
        _lastLoadingVisibleDuration = (float)(Time.realtimeSinceStartupAsDouble - _loadingShownAt);
        _loadingPage.alpha = 0f;

        // 完全な黒のままSceneを有効化し、ステージの初期化フレームも覆う。
        _wasBlackAtActivation = _fadeOverlay.alpha >= 0.999f;
        _activationCount++;
        load.allowSceneActivation = true;
        while (!load.isDone)
            yield return null;
        yield return null;

        yield return Fade(_fadeOverlay, 1f, 0f, stageFadeInDuration);
        Finish();
    }

    private static IEnumerator Fade(CanvasGroup group, float from, float to, float duration)
    {
        if (group == null)
            yield break;

        group.alpha = from;
        if (duration <= 0f)
        {
            group.alpha = to;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            group.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }
        group.alpha = to;
    }

    private void BuildView(Sprite ballSprite, TMP_FontAsset font)
    {
        GameObject canvasObject = new GameObject(
            "TransitionCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = short.MaxValue;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
        Stretch(canvasRect);

        RectTransform loadingRect = CreateRect("LoadingPage", canvasRect);
        Stretch(loadingRect);
        _loadingPage = loadingRect.gameObject.AddComponent<CanvasGroup>();
        _loadingPage.alpha = 0f;

        Image background = CreateImage("Background", loadingRect, LoadingBackground);
        Stretch(background.rectTransform);
        background.raycastTarget = true;

        RectTransform loadingRoot = CreateRect("LoadingRoot", loadingRect);
        loadingRoot.anchorMin = loadingRoot.anchorMax = new Vector2(1f, 0f);
        loadingRoot.pivot = new Vector2(1f, 0f);
        loadingRoot.anchoredPosition = new Vector2(-58f, 46f);
        loadingRoot.sizeDelta = new Vector2(300f, 150f);

        RectTransform trailFar = CreateFlail("FlailTrailFar", loadingRoot, ballSprite, WithAlpha(AccentPink, 0.16f), false);
        RectTransform trailNear = CreateFlail("FlailTrailNear", loadingRoot, ballSprite, WithAlpha(AccentPink, 0.3f), false);
        RectTransform flail = CreateFlail("FlailPivot", loadingRoot, ballSprite, AccentPink, true);
        trailFar.localRotation = Quaternion.Euler(0f, 0f, 20f);
        trailNear.localRotation = Quaternion.Euler(0f, 0f, 10f);
        _rotatingPivots = new[] { trailFar, trailNear, flail };

        GameObject textObject = new GameObject(
            "LoadingText",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.SetParent(loadingRoot, false);
        textRect.anchorMin = textRect.anchorMax = new Vector2(1f, 0f);
        textRect.pivot = new Vector2(1f, 0f);
        textRect.anchoredPosition = new Vector2(0f, 3f);
        textRect.sizeDelta = new Vector2(178f, 34f);

        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.text = "LOADING...";
        text.font = font != null ? font : TMP_Settings.defaultFontAsset;
        text.fontSize = 21f;
        text.fontStyle = FontStyles.Normal;
        text.characterSpacing = 4f;
        text.color = AccentPink;
        text.alignment = TextAlignmentOptions.BottomRight;
        text.raycastTarget = false;

        RectTransform fadeRect = CreateRect("FadeOverlay", canvasRect);
        Stretch(fadeRect);
        _fadeOverlay = fadeRect.gameObject.AddComponent<CanvasGroup>();
        _fadeOverlay.alpha = 0f;
        Image fade = CreateImage("Black", fadeRect, Color.black);
        Stretch(fade.rectTransform);
        fade.raycastTarget = true;
    }

    private static RectTransform CreateFlail(
        string name,
        RectTransform parent,
        Sprite ballSprite,
        Color color,
        bool includeChain)
    {
        RectTransform pivot = CreateRect(name, parent);
        pivot.anchorMin = pivot.anchorMax = new Vector2(1f, 1f);
        pivot.pivot = new Vector2(0.5f, 0.5f);
        pivot.anchoredPosition = new Vector2(-122f, -59f);
        pivot.sizeDelta = Vector2.zero;

        if (includeChain)
        {
            for (int i = 0; i < 8; i++)
            {
                Image link = CreateImage($"Link_{i:00}", pivot, WithAlpha(color, 0.72f));
                RectTransform linkRect = link.rectTransform;
                linkRect.anchorMin = linkRect.anchorMax = new Vector2(0.5f, 0.5f);
                linkRect.pivot = new Vector2(0.5f, 0.5f);
                linkRect.anchoredPosition = new Vector2(10f + i * 10f, 0f);
                linkRect.sizeDelta = new Vector2(12f, 5f);
                linkRect.localRotation = Quaternion.Euler(0f, 0f, i % 2 == 0 ? 24f : -24f);
                link.raycastTarget = false;
            }
        }

        Image ball = CreateImage("Ball", pivot, color);
        ball.sprite = ballSprite;
        ball.preserveAspect = true;
        ball.raycastTarget = false;
        RectTransform ballRect = ball.rectTransform;
        ballRect.anchorMin = ballRect.anchorMax = new Vector2(0.5f, 0.5f);
        ballRect.pivot = new Vector2(0.5f, 0.5f);
        ballRect.anchoredPosition = new Vector2(102f, 0f);
        ballRect.sizeDelta = new Vector2(49f, 49f);
        return pivot;
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform));
        RectTransform rect = gameObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private static Image CreateImage(string name, Transform parent, Color color)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        gameObject.transform.SetParent(parent, false);
        Image image = gameObject.GetComponent<Image>();
        image.color = color;
        return image;
    }

    private static Color WithAlpha(Color color, float alphaMultiplier)
    {
        color.a *= Mathf.Clamp01(alphaMultiplier);
        return color;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
    }

    private void CaptureAndPauseTime()
    {
        _timeScaleBeforeTransition = Time.timeScale;
        _timeScaleCaptured = true;
        Time.timeScale = 0f;
    }

    private void Finish()
    {
        RestoreTime();
        if (_active == this)
            _active = null;
        Destroy(gameObject);
    }

    private void Abort()
    {
        RestoreTime();
        if (_active == this)
            _active = null;
        Destroy(gameObject);
    }

    private void RestoreTime()
    {
        if (!_timeScaleCaptured)
            return;
        Time.timeScale = _timeScaleBeforeTransition;
        _timeScaleCaptured = false;
    }

    private void OnDestroy()
    {
        RestoreTime();
        if (_active == this)
            _active = null;
    }
}
