using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 最終Goal結晶の破壊直後に、添付ロゴを使った短い祝福演出を再生する。
/// 既存クラス名はScene/Prefab参照を壊さないため維持している。
/// </summary>
[DisallowMultipleComponent]
public sealed class CrystalAcquiredUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject presentationRoot;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Image darkOverlay;
    [SerializeField] private Image flashOverlay;
    [SerializeField] private RectTransform celebrationLogo;
    [SerializeField] private Image logoImage;
    [SerializeField] private RectTransform[] sparkleVisuals;
    [SerializeField] private PauseMenuController pauseMenu;

    [Header("Logo (Unscaled Time)")]
    [SerializeField, Min(0f)] private float logoStartDelay = 0.16f;
    [SerializeField, Min(0f)] private float popDuration = 0.42f;
    [SerializeField, Min(0f)] private float logoDisplayDuration = 1.15f;
    [SerializeField, Min(0f)] private float fadeOutDuration = 0.16f;
    [SerializeField, Min(0f)] private float clearScreenDelay = 0.08f;
    [SerializeField, Min(0f)] private float logoFinalScale = 1f;
    [SerializeField, Min(0f)] private float logoStartScale = 0.25f;
    [SerializeField, Min(0f)] private float overshootScale = 1.16f;
    [SerializeField, Min(0f)] private float undershootScale = 0.95f;
    [SerializeField] private float logoStartYOffset = -34f;
    [SerializeField] private float logoStartRotation = -4f;

    [Header("Impact")]
    [SerializeField, Min(0f)] private float flashDuration = 0.1f;
    [SerializeField, Range(0.01f, 1f)] private float slowMotionScale = 0.18f;
    [SerializeField, Min(0f)] private float slowMotionDuration = 0.24f;

    [Header("Background")]
    [SerializeField, Range(0f, 1f)] private float darkenAlpha = 0.42f;
    [SerializeField, Min(0f)] private float darkenDelay = 0.1f;
    [SerializeField, Min(0f)] private float darkenFadeDuration = 0.18f;

    [Header("Pixel Sparkles")]
    [SerializeField] private bool particlesEnabled = true;
    [SerializeField, Min(0f)] private float sparkleBurstDuration = 0.46f;
    [SerializeField, Min(0f)] private float sparkleRotationSpeed = 95f;
    [SerializeField, Min(0f)] private float sparklePulseSpeed = 3.2f;
    [SerializeField, Range(0f, 0.5f)] private float sparklePulseAmount = 0.18f;

    private Coroutine _playRoutine;
    private Action _onComplete;
    private Vector2[] _sparkleTargetPositions = Array.Empty<Vector2>();
    private Vector3[] _sparkleBaseScales = Array.Empty<Vector3>();
    private Quaternion[] _sparkleBaseRotations = Array.Empty<Quaternion>();
    private CanvasGroup[] _sparkleGroups = Array.Empty<CanvasGroup>();
    private Vector2 _logoFinalPosition;
    private Quaternion _logoFinalRotation = Quaternion.identity;
    private float _timeScaleBeforeSlowMotion = 1f;
    private float _ownedSlowMotionScale = 1f;
    private bool _ownsTimeScale;
    private bool _ownsPauseBlock;
    private bool _hasPlayed;

    public bool IsPlaying => _playRoutine != null;
    public bool HasPlayed => _hasPlayed;

    private void Awake()
    {
        EnsureRuntimePresentation();
        CacheLayout();
        HideImmediate();
    }

    /// <summary>一度だけ演出を再生し、終了時に既存Goal処理へ制御を戻す。</summary>
    public bool Play(Action onComplete)
    {
        if (_hasPlayed || _playRoutine != null)
            return false;

        _hasPlayed = true;
        _onComplete = onComplete;
        BlockPauseMenu();
        PreparePresentation();
        BeginSlowMotion();
        _playRoutine = StartCoroutine(PlayRoutine());
        return true;
    }

    private IEnumerator PlayRoutine()
    {
        float fadeStart = logoStartDelay + popDuration + logoDisplayDuration;
        float sequenceEnd = fadeStart + fadeOutDuration;
        float elapsed = 0f;

        while (elapsed < sequenceEnd)
        {
            elapsed += Time.unscaledDeltaTime;
            UpdateSlowMotion(elapsed);
            AnimateFlash(elapsed);
            AnimateDarkOverlay(elapsed);
            AnimateLogo(elapsed);
            AnimateSparkles(elapsed);

            if (canvasGroup != null)
            {
                float fade = fadeOutDuration > 0f
                    ? Mathf.Clamp01((elapsed - fadeStart) / fadeOutDuration)
                    : elapsed >= fadeStart ? 1f : 0f;
                canvasGroup.alpha = 1f - Smooth01(fade);
            }

            yield return null;
        }

        RestoreTimeScaleIfOwned();
        HideImmediate(false);

        if (clearScreenDelay > 0f)
            yield return new WaitForSecondsRealtime(clearScreenDelay);

        _playRoutine = null;
        Action callback = _onComplete;
        _onComplete = null;
        callback?.Invoke();
        ReleasePauseBlockUnlessGoalMenuOwnsIt();
    }

    private void PreparePresentation()
    {
        if (presentationRoot != null)
            presentationRoot.SetActive(true);
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        SetImageAlpha(darkOverlay, 0f);
        SetImageAlpha(flashOverlay, 1f);
        SetImageAlpha(logoImage, 0f);

        if (celebrationLogo != null)
        {
            celebrationLogo.anchoredPosition = _logoFinalPosition + Vector2.up * logoStartYOffset;
            celebrationLogo.localScale = Vector3.one * (logoFinalScale * logoStartScale);
            celebrationLogo.localRotation = _logoFinalRotation * Quaternion.Euler(0f, 0f, logoStartRotation);
        }

        for (int i = 0; i < _sparkleGroups.Length; i++)
        {
            RectTransform sparkle = sparkleVisuals[i];
            if (sparkle != null)
            {
                sparkle.anchoredPosition = _sparkleTargetPositions[i] * 0.12f;
                sparkle.localScale = Vector3.zero;
                sparkle.localRotation = _sparkleBaseRotations[i];
            }
            if (_sparkleGroups[i] != null)
                _sparkleGroups[i].alpha = 0f;
        }
    }

    private void AnimateFlash(float elapsed)
    {
        float t = flashDuration > 0f ? Mathf.Clamp01(elapsed / flashDuration) : 1f;
        SetImageAlpha(flashOverlay, 1f - t * t);
    }

    private void AnimateDarkOverlay(float elapsed)
    {
        float t = darkenFadeDuration > 0f
            ? Mathf.Clamp01((elapsed - darkenDelay) / darkenFadeDuration)
            : elapsed >= darkenDelay ? 1f : 0f;
        SetImageAlpha(darkOverlay, darkenAlpha * Smooth01(t));
    }

    private void AnimateLogo(float elapsed)
    {
        if (celebrationLogo == null)
            return;

        float localTime = elapsed - logoStartDelay;
        if (localTime < 0f)
            return;

        SetImageAlpha(logoImage, 1f);
        float t = popDuration > 0f ? Mathf.Clamp01(localTime / popDuration) : 1f;
        float scale;
        float y;
        float angle;

        if (t < 0.52f)
        {
            float rise = EaseOutCubic(t / 0.52f);
            scale = Mathf.Lerp(logoStartScale, overshootScale, rise);
            y = Mathf.Lerp(logoStartYOffset, 12f, rise);
            angle = Mathf.Lerp(logoStartRotation, 1.2f, rise);
        }
        else if (t < 0.76f)
        {
            float settle = Smooth01((t - 0.52f) / 0.24f);
            scale = Mathf.Lerp(overshootScale, undershootScale, settle);
            y = Mathf.Lerp(12f, -4f, settle);
            angle = Mathf.Lerp(1.2f, -0.35f, settle);
        }
        else
        {
            float land = Smooth01((t - 0.76f) / 0.24f);
            scale = Mathf.Lerp(undershootScale, 1f, land);
            y = Mathf.Lerp(-4f, 0f, land);
            angle = Mathf.Lerp(-0.35f, 0f, land);
        }

        celebrationLogo.localScale = Vector3.one * (logoFinalScale * scale);
        celebrationLogo.anchoredPosition = _logoFinalPosition + Vector2.up * y;
        celebrationLogo.localRotation = _logoFinalRotation * Quaternion.Euler(0f, 0f, angle);
    }

    private void AnimateSparkles(float elapsed)
    {
        if (sparkleVisuals == null)
            return;

        float localTime = elapsed - logoStartDelay - 0.035f;
        bool visible = particlesEnabled && localTime >= 0f;
        float burst = sparkleBurstDuration > 0f
            ? Mathf.Clamp01(localTime / sparkleBurstDuration)
            : visible ? 1f : 0f;

        for (int i = 0; i < sparkleVisuals.Length; i++)
        {
            RectTransform sparkle = sparkleVisuals[i];
            CanvasGroup group = i < _sparkleGroups.Length ? _sparkleGroups[i] : null;
            if (sparkle == null)
                continue;

            if (!visible)
            {
                if (group != null)
                    group.alpha = 0f;
                continue;
            }

            float stagger = Mathf.Clamp01(burst * 1.28f - (i % 4) * 0.07f);
            float travel = EaseOutCubic(stagger);
            sparkle.anchoredPosition = Vector2.Lerp(_sparkleTargetPositions[i] * 0.12f,
                _sparkleTargetPositions[i], travel);

            float phase = localTime * sparklePulseSpeed + i * 0.79f;
            float pulse = 1f + Mathf.Sin(phase * Mathf.PI * 2f) * sparklePulseAmount;
            float appearScale = Mathf.Sin(Mathf.Clamp01(stagger) * Mathf.PI * 0.5f);
            sparkle.localScale = _sparkleBaseScales[i] * (appearScale * pulse);
            sparkle.localRotation = _sparkleBaseRotations[i]
                * Quaternion.Euler(0f, 0f, sparkleRotationSpeed * localTime * (i % 2 == 0 ? 1f : -1f));

            if (group != null)
            {
                float twinkle = Mathf.Lerp(0.55f, 1f, (Mathf.Sin(phase * Mathf.PI * 2f) + 1f) * 0.5f);
                group.alpha = Smooth01(stagger) * twinkle;
            }
        }
    }

    private void BeginSlowMotion()
    {
        if (slowMotionDuration <= 0f || Time.timeScale <= 0f)
            return;

        _timeScaleBeforeSlowMotion = Time.timeScale;
        _ownedSlowMotionScale = Mathf.Max(0.0001f, _timeScaleBeforeSlowMotion * slowMotionScale);
        Time.timeScale = _ownedSlowMotionScale;
        _ownsTimeScale = true;
    }

    private void UpdateSlowMotion(float elapsed)
    {
        if (_ownsTimeScale && elapsed >= slowMotionDuration)
            RestoreTimeScaleIfOwned();
    }

    private void RestoreTimeScaleIfOwned()
    {
        if (!_ownsTimeScale)
            return;

        // Pause等が別の値へ変更した場合は、その新しい状態を上書きしない。
        if (Mathf.Approximately(Time.timeScale, _ownedSlowMotionScale))
            Time.timeScale = _timeScaleBeforeSlowMotion;
        _ownsTimeScale = false;
    }

    private void CacheLayout()
    {
        if (celebrationLogo != null)
        {
            _logoFinalPosition = celebrationLogo.anchoredPosition;
            _logoFinalRotation = celebrationLogo.localRotation;
        }

        int count = sparkleVisuals != null ? sparkleVisuals.Length : 0;
        _sparkleTargetPositions = new Vector2[count];
        _sparkleBaseScales = new Vector3[count];
        _sparkleBaseRotations = new Quaternion[count];
        _sparkleGroups = new CanvasGroup[count];
        for (int i = 0; i < count; i++)
        {
            RectTransform sparkle = sparkleVisuals[i];
            if (sparkle == null)
                continue;
            _sparkleTargetPositions[i] = sparkle.anchoredPosition;
            _sparkleBaseScales[i] = sparkle.localScale;
            _sparkleBaseRotations[i] = sparkle.localRotation;
            _sparkleGroups[i] = sparkle.GetComponent<CanvasGroup>();
        }
    }

    /// <summary>
    /// 古いText版PrefabがSceneに残っていても二重表示せず、新演出へ自己移行する。
    /// Editorセットアップ済みなら何も生成しない。
    /// </summary>
    private void EnsureRuntimePresentation()
    {
        Canvas canvas = GetComponent<Canvas>();
        if (canvas != null)
        {
            canvas.overrideSorting = true;
            canvas.sortingOrder = 650;
        }

        RectTransform rootRect = transform as RectTransform;
        if (rootRect != null)
            rootRect.localScale = Vector3.one;

        if (presentationRoot != null && darkOverlay != null && flashOverlay != null
            && celebrationLogo != null && logoImage != null)
            return;

        if (presentationRoot != null)
        {
            presentationRoot.SetActive(false);
            Destroy(presentationRoot);
        }

        RectTransform presentation = CreateRuntimeRect("GoalCelebrationPresentation", transform);
        StretchRuntime(presentation);
        presentationRoot = presentation.gameObject;
        canvasGroup = presentation.gameObject.AddComponent<CanvasGroup>();

        darkOverlay = CreateRuntimeImage("DarkOverlay", presentation, Color.black);
        StretchRuntime(darkOverlay.rectTransform);

        RectTransform sparkleLayer = CreateRuntimeRect("PixelSparkleLayer", presentation);
        StretchRuntime(sparkleLayer);
        sparkleVisuals = CreateRuntimeSparkles(sparkleLayer);

        logoImage = CreateRuntimeImage("CongratulationLogo", presentation, Color.white);
        celebrationLogo = logoImage.rectTransform;
        CenterRuntime(celebrationLogo, new Vector2(705f, 516f));
        logoImage.sprite = Resources.Load<Sprite>("GoalCelebration/GoalCelebrationLogo");
        logoImage.preserveAspect = true;

        flashOverlay = CreateRuntimeImage("WhiteFlash", presentation, Color.white);
        StretchRuntime(flashOverlay.rectTransform);

        foreach (Graphic graphic in presentation.GetComponentsInChildren<Graphic>(true))
            graphic.raycastTarget = false;
        presentationRoot.SetActive(false);
    }

    private static RectTransform[] CreateRuntimeSparkles(Transform parent)
    {
        Vector2[] positions =
        {
            new Vector2(-420f, 225f), new Vector2(-330f, 120f), new Vector2(-245f, 270f),
            new Vector2(-155f, 165f), new Vector2(-72f, 305f), new Vector2(72f, 305f),
            new Vector2(155f, 165f), new Vector2(245f, 270f), new Vector2(330f, 120f),
            new Vector2(420f, 225f), new Vector2(-455f, -30f), new Vector2(-325f, -165f),
            new Vector2(-185f, -245f), new Vector2(185f, -245f), new Vector2(325f, -165f),
            new Vector2(455f, -30f), new Vector2(-515f, 95f), new Vector2(515f, 95f)
        };
        Color[] colors =
        {
            new Color(1f, 0.24f, 0.72f, 1f), new Color(1f, 0.94f, 1f, 1f),
            new Color(0.72f, 0.42f, 1f, 1f)
        };
        RectTransform[] results = new RectTransform[positions.Length];
        for (int i = 0; i < positions.Length; i++)
        {
            RectTransform sparkle = CreateRuntimeRect($"PixelSpark_{i + 1:00}", parent);
            CenterRuntime(sparkle, new Vector2(28f, 28f));
            sparkle.anchoredPosition = positions[i];
            sparkle.localRotation = Quaternion.Euler(0f, 0f, i % 3 == 0 ? 45f : 0f);
            sparkle.gameObject.AddComponent<CanvasGroup>();

            float length = 12f + (i % 4) * 4f;
            float thickness = i % 5 == 0 ? 5f : 3f;
            Image horizontal = CreateRuntimeImage("Horizontal", sparkle, colors[i % colors.Length]);
            CenterRuntime(horizontal.rectTransform, new Vector2(length, thickness));
            Image vertical = CreateRuntimeImage("Vertical", sparkle, colors[i % colors.Length]);
            CenterRuntime(vertical.rectTransform, new Vector2(thickness, length));
            results[i] = sparkle;
        }
        return results;
    }

    private static Image CreateRuntimeImage(string name, Transform parent, Color color)
    {
        RectTransform rect = CreateRuntimeRect(name, parent);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static RectTransform CreateRuntimeRect(string name, Transform parent)
    {
        GameObject child = new GameObject(name, typeof(RectTransform));
        int uiLayer = LayerMask.NameToLayer("UI");
        child.layer = uiLayer >= 0 ? uiLayer : 0;
        RectTransform rect = child.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        return rect;
    }

    private static void StretchRuntime(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void CenterRuntime(RectTransform rect, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = size;
    }

    private void BlockPauseMenu()
    {
        if (pauseMenu == null)
            pauseMenu = FindAnyObjectByType<PauseMenuController>(FindObjectsInactive.Include);
        if (pauseMenu == null || pauseMenu.IsExternallyBlocked)
            return;
        pauseMenu.SetExternalPauseBlocked(true);
        _ownsPauseBlock = true;
    }

    private void ReleasePauseBlockUnlessGoalMenuOwnsIt()
    {
        if (!_ownsPauseBlock || pauseMenu == null)
            return;
        GoalMenuController goalMenu = FindAnyObjectByType<GoalMenuController>(FindObjectsInactive.Include);
        if (goalMenu == null || !goalMenu.IsGoalReached)
            pauseMenu.SetExternalPauseBlocked(false);
        _ownsPauseBlock = false;
    }

    private void HideImmediate(bool restoreTime = true)
    {
        if (restoreTime)
            RestoreTimeScaleIfOwned();
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
        if (presentationRoot != null)
            presentationRoot.SetActive(false);
    }

    private static void SetImageAlpha(Image image, float alpha)
    {
        if (image == null)
            return;
        Color color = image.color;
        color.a = Mathf.Clamp01(alpha);
        image.color = color;
    }

    private static float Smooth01(float value)
    {
        value = Mathf.Clamp01(value);
        return value * value * (3f - 2f * value);
    }

    private static float EaseOutCubic(float value)
    {
        value = Mathf.Clamp01(value);
        float inverse = 1f - value;
        return 1f - inverse * inverse * inverse;
    }

    private void OnDisable()
    {
        RestoreTimeScaleIfOwned();
    }

    private void OnDestroy()
    {
        RestoreTimeScaleIfOwned();
        ReleasePauseBlockUnlessGoalMenuOwnsIt();
    }

    private void OnValidate()
    {
        logoStartDelay = Mathf.Max(0f, logoStartDelay);
        popDuration = Mathf.Max(0f, popDuration);
        logoDisplayDuration = Mathf.Max(0f, logoDisplayDuration);
        fadeOutDuration = Mathf.Max(0f, fadeOutDuration);
        clearScreenDelay = Mathf.Max(0f, clearScreenDelay);
        logoFinalScale = Mathf.Max(0f, logoFinalScale);
        logoStartScale = Mathf.Max(0f, logoStartScale);
        overshootScale = Mathf.Max(0f, overshootScale);
        undershootScale = Mathf.Max(0f, undershootScale);
        flashDuration = Mathf.Max(0f, flashDuration);
        slowMotionDuration = Mathf.Max(0f, slowMotionDuration);
        darkenDelay = Mathf.Max(0f, darkenDelay);
        darkenFadeDuration = Mathf.Max(0f, darkenFadeDuration);
        sparkleBurstDuration = Mathf.Max(0f, sparkleBurstDuration);
    }
}
