using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 演出中だけPlayerと鉄球の物理所有権を借りる。初期位置は停止位置として扱う。
/// Awakeでは入力だけを止め、各Respawn管理の初期位置取得後にStartで移動する。
/// </summary>
[DisallowMultipleComponent, RequireComponent(typeof(Player), typeof(Rigidbody2D))]
[DefaultExecutionOrder(100)]
public sealed class PlayerStartIntro : MonoBehaviour
{
    public enum IntroPhase { WaitingForStage, Jump, Walk, StageTitle, Complete }

    [Header("開始")]
    [SerializeField] private bool playOnStart = true;
    [SerializeField] private CameraFollow cameraFollow;

    [Header("経路（Pointはワールド座標。未設定時は下の値を使用）")]
    [SerializeField] private Transform jumpStartPoint;
    [SerializeField] private Transform landingPoint;
    [SerializeField] private Transform finalStopPoint;
    [SerializeField, Tooltip("開始時のPlayer位置からの左上オフセット")]
    private Vector2 jumpStartOffset = new Vector2(-12f, 7f);
    [SerializeField, Tooltip("着地時のPlayerルートのワールド座標。足元座標ではありません")]
    private Vector2 landingPosition;
    [SerializeField, Min(0f)] private float walkDistance = 1.4f;
    [SerializeField, Min(0.05f)] private float introJumpDuration = 0.85f;
    [SerializeField, Min(0f), Tooltip("直線補間に加える放物線の高さ")]
    private float jumpArcHeight = 2.5f;
    [SerializeField, Min(0.05f)] private float walkSpeed = 3f;
    [SerializeField, Tooltip("演出中のPlayerルートからの鉄球位置。通常時には適用しません")]
    private Vector2 morningStarOffset = new Vector2(-2f, -1.15f);

    [Header("床合わせ（障害物のない開始区間に設定してください）")]
    [SerializeField] private bool alignToGround = true;
    [SerializeField] private LayerMask groundLayers = (1 << 0) | (1 << 6);
    [SerializeField, Min(0f)] private float groundProbeHeight = 3f;
    [SerializeField, Min(0.1f)] private float groundProbeDistance = 12f;

    [Header("ステージ名（UI未設定時は簡易Canvasを自動生成）")]
    [SerializeField] private string stageTitle = "STAGE 1";
    [SerializeField] private CanvasGroup stageTitleGroup;
    [SerializeField] private TMP_Text stageTitleText;
    [SerializeField, Min(0f)] private float stageTitleDelay = 0.12f;
    [SerializeField, Min(0f)] private float stageTitleDuration = 0.8f;

    public bool IsPlaying { get; private set; }
    public IntroPhase Phase { get; private set; } = IntroPhase.WaitingForStage;

    private Player _player;
    private Rigidbody2D _body;
    private Collider2D _collider;
    private MorningStarLauncher _launcher;
    private bool _inputWasEnabled;
    private bool _inputCaptured;
    private bool _physicsCaptured;
    private bool _bodyWasSimulated;
    private bool _cameraWasEnabled;
    private Vector2 _finalPosition;
    private GameObject _generatedTitle;

    private void Reset()
    {
        landingPosition = (Vector2)transform.position - Vector2.right * walkDistance;
    }

    private void Awake()
    {
        _player = GetComponent<Player>();
        _body = GetComponent<Rigidbody2D>();
        _collider = GetComponent<Collider2D>();
        _launcher = GetComponent<MorningStarLauncher>();
        if (playOnStart)
        {
            _inputWasEnabled = _player.InputEnabled;
            _inputCaptured = true;
            _player.SetInputEnabled(false);
        }
        SetTitleVisible(false);
    }

    private IEnumerator Start()
    {
        if (!playOnStart)
            yield break;

        Vector2 initialPosition = transform.position;
        Vector2 start = jumpStartPoint != null ? (Vector2)jumpStartPoint.position : initialPosition + jumpStartOffset;
        Vector2 landing = GroundPosition(landingPoint != null ? (Vector2)landingPoint.position : landingPosition);
        _finalPosition = GroundPosition(finalStopPoint != null
            ? (Vector2)finalStopPoint.position : landing + Vector2.right * walkDistance);

        _bodyWasSimulated = _body.simulated;
        _cameraWasEnabled = cameraFollow != null && cameraFollow.enabled;
        _physicsCaptured = true;
        IsPlaying = true;
        _body.linearVelocity = Vector2.zero;
        _body.angularVelocity = 0f;
        _body.simulated = false;
        _player.SetStartIntroControl(true);
        _player.ClearAimFacing();
        _player.SetLaunchFacing(1f);
        if (cameraFollow != null)
            cameraFollow.enabled = false;
        if (_launcher != null)
            _launcher.BeginStartIntro();
        EnsureTitleView();
        MoveTo(start);
        _player.SetStartIntroMotion(false, Vector2.up);

        // TitleStageTransitionは黒フェード中Time.timeScaleを0にする。画面が見えてから登場。
        while (TitleStageTransition.IsTransitioning)
            yield return null;

        Phase = IntroPhase.Jump;
        float duration = Mathf.Max(0.05f, introJumpDuration);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed = Mathf.Min(duration, elapsed + Time.deltaTime);
            float t = elapsed / duration;
            Vector2 position = Vector2.Lerp(start, landing, t)
                + Vector2.up * (4f * Mathf.Max(0f, jumpArcHeight) * t * (1f - t));
            Vector2 velocity = (landing - start) / duration
                + Vector2.up * (4f * Mathf.Max(0f, jumpArcHeight) * (1f - 2f * t) / duration);
            MoveTo(position);
            _player.SetStartIntroMotion(false, velocity);
            yield return null;
        }

        MoveTo(landing);
        _player.SetStartIntroMotion(true, Vector2.zero);
        // Player.Updateに接地の瞬間を渡し、既存のLand遷移・着地音を一度だけ使う。
        yield return null;

        Phase = IntroPhase.Walk;
        Vector2 direction = (_finalPosition - landing).normalized;
        float distance = Vector2.Distance(landing, _finalPosition);
        float walked = 0f;
        while (walked < distance)
        {
            walked = Mathf.Min(distance, walked + Mathf.Max(0.05f, walkSpeed) * Time.deltaTime);
            MoveTo(Vector2.Lerp(landing, _finalPosition, distance > 0f ? walked / distance : 1f));
            _player.SetStartIntroMotion(true, direction * Mathf.Max(0.05f, walkSpeed));
            yield return null;
        }
        MoveTo(_finalPosition);
        _player.SetStartIntroMotion(true, Vector2.zero);
        yield return null;
        if (stageTitleDelay > 0f)
            yield return new WaitForSeconds(stageTitleDelay);

        Phase = IntroPhase.StageTitle;
        SetTitleVisible(true);
        if (stageTitleDuration > 0f)
            yield return new WaitForSeconds(stageTitleDuration);
        Finish(true);
    }

    private Vector2 GroundPosition(Vector2 position)
    {
        if (!alignToGround || _collider == null)
            return position;
        float feetOffset = transform.position.y - _collider.bounds.min.y;
        RaycastHit2D[] hits = Physics2D.RaycastAll(position + Vector2.up * groundProbeHeight,
            Vector2.down, groundProbeDistance, groundLayers);
        foreach (RaycastHit2D hit in hits)
        {
            if (hit.collider.isTrigger || hit.collider.transform.IsChildOf(transform)
                || hit.collider.gameObject.scene != gameObject.scene
                || (hit.rigidbody != null && hit.rigidbody.bodyType != RigidbodyType2D.Static)
                || hit.normal.y < 0.5f)
                continue;
            return new Vector2(position.x, hit.point.y + feetOffset + 0.01f);
        }
        Debug.LogWarning("[PlayerStartIntro] 経路の床が見つからないため指定座標を使用します。", this);
        return position;
    }

    private void MoveTo(Vector2 position)
    {
        _body.position = position;
        transform.position = new Vector3(position.x, position.y, transform.position.z);
        if (_launcher != null)
            _launcher.SetStartIntroBallPosition(position + morningStarOffset);
    }

    private void Finish(bool completed)
    {
        SetTitleVisible(false);
        if (_physicsCaptured)
        {
            MoveTo(_finalPosition);
            if (_launcher != null)
                _launcher.EndStartIntro();
            _body.linearVelocity = Vector2.zero;
            _body.angularVelocity = 0f;
            _body.simulated = _bodyWasSimulated;
            Physics2D.SyncTransforms();
            _player.SetStartIntroControl(false);
            if (cameraFollow != null)
            {
                cameraFollow.ResetLookAhead();
                cameraFollow.enabled = _cameraWasEnabled;
            }
            _physicsCaptured = false;
        }
        if (_inputCaptured)
        {
            _player.SetInputEnabled(_inputWasEnabled);
            _inputCaptured = false;
        }
        IsPlaying = false;
        if (completed)
            Phase = IntroPhase.Complete;
    }

    private void SetTitleVisible(bool visible)
    {
        if (stageTitleText != null)
            stageTitleText.text = stageTitle;
        if (stageTitleGroup == null)
            return;
        stageTitleGroup.alpha = visible ? 1f : 0f;
        stageTitleGroup.interactable = false;
        stageTitleGroup.blocksRaycasts = false;
    }

    private void EnsureTitleView()
    {
        if (stageTitleGroup != null)
            return;
        _generatedTitle = new GameObject("StageIntroCanvas", typeof(RectTransform), typeof(Canvas),
            typeof(UnityEngine.UI.CanvasScaler), typeof(CanvasGroup));
        SceneManager.MoveGameObjectToScene(_generatedTitle, gameObject.scene);
        Canvas canvas = _generatedTitle.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 400;
        UnityEngine.UI.CanvasScaler scaler = _generatedTitle.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        stageTitleGroup = _generatedTitle.GetComponent<CanvasGroup>();

        GameObject label = new GameObject("StageTitle", typeof(RectTransform), typeof(TextMeshProUGUI));
        label.transform.SetParent(_generatedTitle.transform, false);
        stageTitleText = label.GetComponent<TextMeshProUGUI>();
        stageTitleText.font = TMP_Settings.defaultFontAsset;
        stageTitleText.fontSize = 72f;
        stageTitleText.fontStyle = FontStyles.Bold;
        stageTitleText.alignment = TextAlignmentOptions.Center;
        stageTitleText.color = Color.white;
        stageTitleText.raycastTarget = false;
        stageTitleText.rectTransform.anchorMin = new Vector2(0.15f, 0.65f);
        stageTitleText.rectTransform.anchorMax = new Vector2(0.85f, 0.85f);
        stageTitleText.rectTransform.offsetMin = stageTitleText.rectTransform.offsetMax = Vector2.zero;
        SetTitleVisible(false);
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        Finish(false);
    }

    private void OnDestroy()
    {
        if (_generatedTitle != null)
            Destroy(_generatedTitle);
    }
}
