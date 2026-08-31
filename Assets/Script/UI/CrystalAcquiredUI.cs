using System;
using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// 最終Goal結晶の破壊後、既存Goalメニューの前に短い獲得演出を再生する。
/// </summary>
[DisallowMultipleComponent]
public sealed class CrystalAcquiredUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject presentationRoot;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private RectTransform contentRoot;
    [SerializeField] private TMP_Text mainText;
    [SerializeField] private TMP_Text subText;
    [SerializeField] private TMP_Text hintText;
    [SerializeField] private RectTransform[] sparkleVisuals;

    [Header("Timing (Unscaled Time)")]
    [SerializeField, Min(0f)] private float showDelay = 0.25f;
    [SerializeField, Min(0f)] private float fadeInDuration = 0.3f;
    [SerializeField, Min(0f)] private float displayDuration = 1.4f;
    [SerializeField, Min(0f)] private float fadeOutDuration = 0.3f;

    [Header("Motion")]
    [SerializeField] private Vector3 popScale = new Vector3(0.85f, 1.05f, 1f);
    [SerializeField, Min(0f)] private float sparkleRotationSpeed = 24f;
    [SerializeField, Min(0f)] private float sparklePulseSpeed = 2.2f;
    [SerializeField, Range(0f, 0.5f)] private float sparklePulseAmount = 0.16f;
    [SerializeField, Min(0f)] private float sparkleDriftPixels = 3f;

    private Coroutine _playRoutine;
    private Action _onComplete;
    private Vector2[] _sparkleBasePositions;
    private Vector3[] _sparkleBaseScales;
    private bool _hasPlayed;

    public bool IsPlaying => _playRoutine != null;
    public bool HasPlayed => _hasPlayed;

    private void Awake()
    {
        CacheSparkleLayout();
        SetCopy();
        HideImmediate();
    }

    /// <summary>一度だけ獲得演出を再生し、フェードアウト後に完了通知する。</summary>
    public bool Play(Action onComplete)
    {
        if (_hasPlayed || _playRoutine != null)
            return false;

        _hasPlayed = true;
        _onComplete = onComplete;
        if (presentationRoot != null)
            presentationRoot.SetActive(true);

        SetCopy();
        SetAlpha(0f);
        if (contentRoot != null)
            contentRoot.localScale = Vector3.one * popScale.x;

        _playRoutine = StartCoroutine(PlayRoutine());
        return true;
    }

    private IEnumerator PlayRoutine()
    {
        if (showDelay > 0f)
            yield return new WaitForSecondsRealtime(showDelay);

        yield return AnimateAlpha(0f, 1f, fadeInDuration, true);

        float elapsed = 0f;
        while (elapsed < displayDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            AnimateSparkles(elapsed);
            yield return null;
        }

        yield return AnimateAlpha(1f, 0f, fadeOutDuration, false);

        HideImmediate();
        _playRoutine = null;
        Action callback = _onComplete;
        _onComplete = null;
        callback?.Invoke();
    }

    private IEnumerator AnimateAlpha(float from, float to, float duration, bool animatePop)
    {
        if (duration <= 0f)
        {
            SetAlpha(to);
            if (animatePop && contentRoot != null)
                contentRoot.localScale = Vector3.one;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = Mathf.SmoothStep(0f, 1f, t);
            SetAlpha(Mathf.Lerp(from, to, eased));

            if (animatePop && contentRoot != null)
            {
                float scale = t < 0.65f
                    ? Mathf.Lerp(popScale.x, popScale.y, Mathf.SmoothStep(0f, 1f, t / 0.65f))
                    : Mathf.Lerp(popScale.y, popScale.z,
                        Mathf.SmoothStep(0f, 1f, (t - 0.65f) / 0.35f));
                contentRoot.localScale = Vector3.one * scale;
            }

            AnimateSparkles(elapsed);
            yield return null;
        }

        SetAlpha(to);
        if (animatePop && contentRoot != null)
            contentRoot.localScale = Vector3.one * popScale.z;
    }

    private void AnimateSparkles(float elapsed)
    {
        if (sparkleVisuals == null)
            return;

        for (int i = 0; i < sparkleVisuals.Length; i++)
        {
            RectTransform sparkle = sparkleVisuals[i];
            if (sparkle == null)
                continue;

            float phase = elapsed * sparklePulseSpeed + i * 0.83f;
            float pulse = 1f + Mathf.Sin(phase * Mathf.PI * 2f) * sparklePulseAmount;
            Vector3 baseScale = i < _sparkleBaseScales.Length ? _sparkleBaseScales[i] : Vector3.one;
            sparkle.localScale = baseScale * pulse;
            sparkle.Rotate(0f, 0f,
                sparkleRotationSpeed * Time.unscaledDeltaTime * (i % 2 == 0 ? 1f : -1f));

            Vector2 basePosition = i < _sparkleBasePositions.Length
                ? _sparkleBasePositions[i]
                : sparkle.anchoredPosition;
            sparkle.anchoredPosition = basePosition + new Vector2(
                Mathf.Cos(phase) * sparkleDriftPixels,
                Mathf.Sin(phase * 0.8f) * sparkleDriftPixels);
        }
    }

    private void CacheSparkleLayout()
    {
        int count = sparkleVisuals != null ? sparkleVisuals.Length : 0;
        _sparkleBasePositions = new Vector2[count];
        _sparkleBaseScales = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            RectTransform sparkle = sparkleVisuals[i];
            _sparkleBasePositions[i] = sparkle != null ? sparkle.anchoredPosition : Vector2.zero;
            _sparkleBaseScales[i] = sparkle != null ? sparkle.localScale : Vector3.one;
        }
    }

    private void SetCopy()
    {
        if (mainText != null)
            mainText.text = "クリスタルを獲得！";
        if (subText != null)
            subText.text = "すべて集めた！";
        if (hintText != null)
            hintText.text = "このあとゴール画面へ";
    }

    private void SetAlpha(float alpha)
    {
        if (canvasGroup != null)
            canvasGroup.alpha = Mathf.Clamp01(alpha);
    }

    private void HideImmediate()
    {
        SetAlpha(0f);
        if (canvasGroup != null)
        {
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
        if (contentRoot != null)
            contentRoot.localScale = Vector3.one;
        if (presentationRoot != null)
            presentationRoot.SetActive(false);
    }

    private void OnValidate()
    {
        showDelay = Mathf.Max(0f, showDelay);
        fadeInDuration = Mathf.Max(0f, fadeInDuration);
        displayDuration = Mathf.Max(0f, displayDuration);
        fadeOutDuration = Mathf.Max(0f, fadeOutDuration);
    }
}
