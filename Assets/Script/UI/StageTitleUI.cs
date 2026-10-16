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

    [Header("表示時間（ゲーム内秒。ポーズ中は停止）")]
    [SerializeField, Min(0f)] private float fadeInDuration = 0.3f;
    [SerializeField, Min(0f), Tooltip("表示開始からフェードアウトを始めるまでの時間")]
    private float fadeOutStart = 4f;
    [SerializeField, Min(0.1f), Tooltip("フェードイン・保持・フェードアウトを含む合計時間")]
    private float totalDuration = 5f;

    public bool HasPlayed { get; private set; }
    public bool IsShowing { get; private set; }
    private Canvas _canvas;

    private void Awake()
    {
        _canvas = GetComponent<Canvas>();
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();
        if (titleImage == null)
            titleImage = GetComponentInChildren<UnityEngine.UI.Image>(true);
        Hide();
    }

    private void LateUpdate()
    {
        // CRTは起動時にOverlayをCamera描画へ取り込む。このUIだけはOverlayを維持し、
        // カメラ追従・揺れによるずれを避ける。既存カメラや他Canvasの設定は変更しない。
        if (_canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            return;
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.worldCamera = null;
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
                canvasGroup.alpha = elapsed / fadeIn;
            else if (elapsed >= fadeOut && duration > fadeOut)
                canvasGroup.alpha = (duration - elapsed) / (duration - fadeOut);
            else
                canvasGroup.alpha = 1f;
            yield return null;
            elapsed += Time.deltaTime;
        }
        Hide();
    }

    private void Hide()
    {
        IsShowing = false;
        if (canvasGroup == null)
            return;
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
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
        totalDuration = Mathf.Max(0.1f, totalDuration);
        fadeInDuration = Mathf.Clamp(fadeInDuration, 0f, totalDuration);
        fadeOutStart = Mathf.Clamp(fadeOutStart, fadeInDuration, totalDuration);
    }
}
