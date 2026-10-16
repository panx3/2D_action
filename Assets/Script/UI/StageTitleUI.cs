using System.Collections;
using UnityEngine;

/// <summary>
/// 開始演出完了後、通常操作と並行してステージ名Spriteを一度だけ表示する。
/// 同じPrefabを各Sceneへ配置し、Stage Spriteのみ差し替えられる。
/// </summary>
[DisallowMultipleComponent, RequireComponent(typeof(CanvasGroup), typeof(Canvas))]
public sealed class StageTitleUI : MonoBehaviour
{
    [Header("参照")]
    [SerializeField, Tooltip("未設定なら同じScene内のPlayerStartIntroを取得")]
    private PlayerStartIntro startIntro;
    [SerializeField] private UnityEngine.UI.Image titleImage;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField, Tooltip("ステージごとに表示するSpriteを設定")]
    private Sprite stageSprite;

    [Header("表示サイズ・移動（Canvasの基準解像度での値）")]
    [SerializeField, Tooltip("拡大前の基準サイズ。現在サイズへ倍率を掛け続けない")]
    private Vector2 baseSize = new Vector2(630f, 141f);
    [SerializeField, Min(0.01f)] private float sizeMultiplier = 1.5f;
    [SerializeField, Min(0f), Tooltip("上下にスライドする距離。画面上端を超えない範囲に制限")]
    private float slideDistance = 48f;

    [Header("表示時間（ゲーム内秒。ポーズ中は停止）")]
    [SerializeField, Min(0f)] private float fadeInDuration = 0.4f;
    [SerializeField, Min(0f), Tooltip("表示開始からフェードアウトを始めるまでの時間")]
    private float fadeOutStart = 4f;
    [SerializeField, Min(0.1f), Tooltip("フェードイン・保持・フェードアウトを含む合計時間")]
    private float totalDuration = 5f;

    public bool HasPlayed { get; private set; }
    public bool IsShowing { get; private set; }
    private Canvas _canvas;
    private RectTransform _titleRect;
    private Vector2 _restPosition;
    private float _slideOffset;

    private void Awake()
    {
        _canvas = GetComponent<Canvas>();
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();
        if (titleImage == null)
            titleImage = GetComponentInChildren<UnityEngine.UI.Image>(true);
        if (titleImage != null)
        {
            _titleRect = titleImage.rectTransform;
            _restPosition = _titleRect.anchoredPosition;
            _titleRect.anchorMin = _titleRect.anchorMax = Vector2.one;
            _titleRect.pivot = Vector2.one;
            titleImage.preserveAspect = true;
        }
        Hide();
    }

    private void LateUpdate()
    {
        // CRTは起動時にOverlayをCamera描画へ取り込む。このUIだけはOverlayを維持し、
        // カメラ追従・揺れによるずれを避ける。既存カメラや他Canvasの設定は変更しない。
        if (_canvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.worldCamera = null;
        }
        UpdateLayout();
    }

    private void OnEnable()
    {
        if (startIntro == null)
        {
            foreach (PlayerStartIntro candidate in FindObjectsByType<PlayerStartIntro>(FindObjectsSortMode.None))
            {
                if (candidate.gameObject.scene != gameObject.scene)
                    continue;
                startIntro = candidate;
                break;
            }
        }
        if (startIntro == null)
            return;
        startIntro.Completed += ShowOnce;
        // 完了後にPrefabを有効化・生成した場合も、取りこぼさず一度だけ表示する。
        if (startIntro.Phase == PlayerStartIntro.IntroPhase.Complete)
            ShowOnce();
    }

    /// <summary>同じScene内では再通知・チェックポイント復帰・再有効化でも再生しない。</summary>
    public void ShowOnce()
    {
        if (HasPlayed || !isActiveAndEnabled)
            return;
        HasPlayed = true;
        if (titleImage == null || canvasGroup == null || stageSprite == null)
        {
            Debug.LogWarning("[StageTitleUI] Image / CanvasGroup / Stage Spriteを設定してください。", this);
            return;
        }
        titleImage.sprite = stageSprite;
        titleImage.enabled = true;
        StartCoroutine(Show());
    }

    private IEnumerator Show()
    {
        IsShowing = true;
        float duration = Mathf.Max(0.1f, totalDuration);
        float fadeIn = Mathf.Clamp(fadeInDuration, 0f, duration);
        float fadeOut = Mathf.Clamp(fadeOutStart, fadeIn, duration);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (fadeIn > 0f && elapsed < fadeIn)
            {
                float progress = EaseOut(elapsed / fadeIn);
                canvasGroup.alpha = progress;
                _slideOffset = Mathf.Lerp(slideDistance, 0f, progress);
            }
            else if (elapsed >= fadeOut && duration > fadeOut)
            {
                float progress = EaseOut((elapsed - fadeOut) / (duration - fadeOut));
                canvasGroup.alpha = 1f - progress;
                _slideOffset = Mathf.Lerp(0f, slideDistance, progress);
            }
            else
            {
                canvasGroup.alpha = 1f;
                _slideOffset = 0f;
            }
            UpdateLayout();
            yield return null;
            elapsed += Time.deltaTime;
        }
        Hide();
    }

    private void Hide()
    {
        IsShowing = false;
        _slideOffset = 0f;
        if (titleImage != null)
            titleImage.enabled = false;
        UpdateLayout();
        if (canvasGroup == null)
            return;
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
    }

    private static float EaseOut(float t)
    {
        float remaining = 1f - Mathf.Clamp01(t);
        return 1f - remaining * remaining * remaining;
    }

    private void UpdateLayout()
    {
        if (_titleRect == null || !(_titleRect.parent is RectTransform parent))
            return;
        Vector2 viewport = parent.rect.size;
        if (viewport.x <= 0f || viewport.y <= 0f)
            return;

        // 基準値から毎回算出するので、再有効化や解像度変更でも拡大が累積しない。
        Vector2 size = new Vector2(Mathf.Max(1f, baseSize.x), Mathf.Max(1f, baseSize.y))
            * Mathf.Max(0.01f, sizeMultiplier);
        float rightMargin = Mathf.Max(0f, -_restPosition.x);
        float topMargin = Mathf.Max(0f, -_restPosition.y);
        float fit = Mathf.Min(1f, Mathf.Max(1f, viewport.x - rightMargin) / size.x,
            Mathf.Max(1f, viewport.y - topMargin) / size.y);
        size *= fit;
        _titleRect.sizeDelta = size;
        Vector2 rest = new Vector2(
            -Mathf.Min(rightMargin, Mathf.Max(0f, viewport.x - size.x)),
            -Mathf.Min(topMargin, Mathf.Max(0f, viewport.y - size.y)));
        _titleRect.anchoredPosition = rest + Vector2.up * Mathf.Min(Mathf.Max(0f, _slideOffset), -rest.y);
    }

    private void OnDisable()
    {
        if (startIntro != null)
            startIntro.Completed -= ShowOnce;
        StopAllCoroutines();
        Hide();
    }

    private void OnValidate()
    {
        baseSize = new Vector2(Mathf.Max(1f, baseSize.x), Mathf.Max(1f, baseSize.y));
        sizeMultiplier = Mathf.Max(0.01f, sizeMultiplier);
        slideDistance = Mathf.Max(0f, slideDistance);
        totalDuration = Mathf.Max(0.1f, totalDuration);
        fadeInDuration = Mathf.Clamp(fadeInDuration, 0f, totalDuration);
        fadeOutStart = Mathf.Clamp(fadeOutStart, fadeInDuration, totalDuration);
    }
}
