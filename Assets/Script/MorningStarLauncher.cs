using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// モーニングスター発射コントローラ（Recall → Throw の2段階方式）。
///
/// 設計方針:
/// - MorningStar は常に1個だけ。既存の Rigidbody2D を再利用し、Instantiate しない。
/// - 鎖長制限・引き戻しは ChainConstraint2D 側に任せる。
///   ただし RecallBeforeThrow 中は瞬間移動と干渉する可能性があるため、必要に応じて一時的に無効化。
/// - DistanceJoint2D / HingeJoint2D には一切触れない。
/// - LineRenderer 表示は ChainLineController に任せる（本クラスは描画しない）。
///
/// フロー:
///   Dragging → 左クリック / 右スティック入力 → RecallBeforeThrow（手元へ高速回収） → Thrown（発射）
///
/// クリック瞬間に発射方向を確定するため、Recall 中にマウスを動かしても方向はブレない。
/// </summary>
public class MorningStarLauncher : MonoBehaviour
{
    /// <summary>モーニングスターの状態。</summary>
    public enum MorningStarState
    {
        Dragging,
        RecallBeforeThrow,
        Thrown,
        // 将来追加予定:
        // Returning, // 自動回収中
        // Hooked,    // 壁・敵に刺さって固定中
        // Pulling,   // Hooked 状態でプレイヤーを引き寄せ中
    }

    [Header("参照")]
    [SerializeField, Tooltip("発射元アンカー（Player の手元 Transform）")]
    private Transform handAnchor;
    [SerializeField, Tooltip("発射位置の Transform。未設定なら handAnchor を使用")]
    private Transform throwSocket;
    [SerializeField, Tooltip("発射対象の MorningStar Rigidbody2D。Instantiate せずこの 1 個を再利用する")]
    private Rigidbody2D morningStarRb;
    [SerializeField, Tooltip("照準用カメラ。未設定なら Camera.main を使用")]
    private Camera mainCamera;
    [SerializeField, Tooltip("RecallBeforeThrow 中に一時的に無効化したい鎖の制約（ChainConstraint2D）。任意。")]
    private ChainConstraint2D chainConstraint;

    [Header("発射パラメータ")]
    [SerializeField, Tooltip("発射速度（m/s）。ChainConstraint2D の鎖長で止まる")]
    private float throwSpeed = 18f;
    [SerializeField, Tooltip("HandAnchor から照準位置までの最小距離。これより近い場合は発射しない")]
    private float minAimDistance = 0.2f;

    [Header("Recall (発射前回収)")]
    [SerializeField, Tooltip("発射前に手元へ戻すときの移動速度（m/s）。大きいほど一瞬で吸着する")]
    private float recallSpeed = 35f;
    [SerializeField, Tooltip("ソケットへの到達判定距離。これ以下になった時点で完了とする")]
    private float recallFinishDistance = 0.15f;
    [SerializeField, Tooltip("Recall の最大時間（秒）。到達できなくても強制的にスナップ＆発射する")]
    private float maxRecallTime = 0.12f;
    [SerializeField, Tooltip("Recall 中は ChainConstraint2D を一時無効化するか（瞬間吸着との干渉を防ぐ）")]
    private bool disableChainConstraintDuringRecall = true;

    [Header("入力: Gamepad")]
    [SerializeField, Range(0f, 1f), Tooltip("右スティックを倒したと判定する閾値（0〜1）")]
    private float gamepadStickThreshold = 0.5f;

    private MorningStarState _state = MorningStarState.Dragging;
    private bool _gamepadStickPrevHigh;
    private Vector2 _pendingThrowDirection;
    private float _recallElapsed;

    /// <summary>現在の状態（読み取り専用）。</summary>
    public MorningStarState State => _state;

    private void Awake()
    {
        if (mainCamera == null) mainCamera = Camera.main;
    }

    private void Update()
    {
        if (handAnchor == null || morningStarRb == null) return;

        // Recall 中は新規入力を受け付けない（方向ブレ防止 & 連打防止）
        if (_state == MorningStarState.RecallBeforeThrow) return;

        // --- PC操作: マウス左クリックで発射開始 ---
        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            Vector2 worldTarget = ScreenToWorld(mouse.position.ReadValue());
            TryBeginRecallThenThrow(worldTarget);
            return; // 同フレームでスティック判定はしない
        }

        // --- Gamepad操作: 右スティックを閾値以上倒した瞬間に発射開始 ---
        Gamepad pad = Gamepad.current;
        if (pad != null)
        {
            Vector2 stick = pad.rightStick.ReadValue();
            bool nowHigh = stick.sqrMagnitude >= gamepadStickThreshold * gamepadStickThreshold;
            if (nowHigh && !_gamepadStickPrevHigh)
            {
                Vector2 worldDir = stick.normalized;
                Vector2 worldTarget = (Vector2)handAnchor.position + worldDir;
                TryBeginRecallThenThrow(worldTarget);
            }
            _gamepadStickPrevHigh = nowHigh;
        }
        else
        {
            _gamepadStickPrevHigh = false;
        }
    }

    private void FixedUpdate()
    {
        if (_state != MorningStarState.RecallBeforeThrow) return;
        if (morningStarRb == null) return;

        Transform socket = GetThrowSocket();
        if (socket == null)
        {
            // ソケットが取れないなら即発射にフォールバック
            ExecuteThrow();
            return;
        }

        Vector2 socketPos = socket.position;
        Vector2 currentPos = morningStarRb.position;

        float step = Mathf.Max(0f, recallSpeed) * Time.fixedDeltaTime;
        Vector2 nextPos = Vector2.MoveTowards(currentPos, socketPos, step);

        // Recall 中は手動で位置を更新し、物理速度はゼロに固定
        morningStarRb.position = nextPos;
        morningStarRb.linearVelocity = Vector2.zero;
        morningStarRb.angularVelocity = 0f;

        _recallElapsed += Time.fixedDeltaTime;

        bool reachedByDistance = Vector2.Distance(nextPos, socketPos) <= recallFinishDistance;
        bool reachedByTimeout  = _recallElapsed >= maxRecallTime;
        if (reachedByDistance || reachedByTimeout)
        {
            ExecuteThrow();
        }
    }

    /// <summary>
    /// HandAnchor からワールド座標 worldTarget への方向を確定し、Recall を開始する。
    /// 距離が minAimDistance 未満なら何もしない。
    /// </summary>
    private void TryBeginRecallThenThrow(Vector2 worldTarget)
    {
        Vector2 handPos = handAnchor.position;
        Vector2 toTarget = worldTarget - handPos;
        if (toTarget.magnitude < minAimDistance) return;

        BeginRecallThenThrow(toTarget.normalized);
    }

    /// <summary>
    /// 発射方向を確定し RecallBeforeThrow へ遷移する公開 API。
    /// </summary>
    public void BeginRecallThenThrow(Vector2 worldDirection)
    {
        if (morningStarRb == null) return;
        if (worldDirection.sqrMagnitude < 1e-6f) return;

        _pendingThrowDirection = worldDirection.sqrMagnitude > 1f
            ? worldDirection.normalized
            : worldDirection;
        _recallElapsed = 0f;
        _state = MorningStarState.RecallBeforeThrow;

        // Recall 中は HardChain と干渉する可能性があるため一時無効化（任意）
        if (disableChainConstraintDuringRecall && chainConstraint != null)
            chainConstraint.enabled = false;

        morningStarRb.WakeUp();
        morningStarRb.linearVelocity = Vector2.zero;
        morningStarRb.angularVelocity = 0f;
    }

    /// <summary>
    /// Recall 完了 → ソケットへスナップ → 速度リセット → throwSpeed で発射。
    /// </summary>
    private void ExecuteThrow()
    {
        Transform socket = GetThrowSocket();
        if (socket != null)
            morningStarRb.position = socket.position;

        morningStarRb.linearVelocity = Vector2.zero;
        morningStarRb.angularVelocity = 0f;
        morningStarRb.WakeUp();

        // 制約を戻してから発射方向の速度を与える（制約は鎖長外でしか発火しない）
        if (disableChainConstraintDuringRecall && chainConstraint != null)
            chainConstraint.enabled = true;

        morningStarRb.linearVelocity = _pendingThrowDirection * throwSpeed;
        _state = MorningStarState.Thrown;
    }

    /// <summary>
    /// 状態を Dragging にリセットする。将来 Return 処理から呼ぶことを想定。
    /// 制約も確実に有効へ戻す。
    /// </summary>
    public void ResetToDragging()
    {
        if (disableChainConstraintDuringRecall && chainConstraint != null)
            chainConstraint.enabled = true;
        _state = MorningStarState.Dragging;
    }

    private Transform GetThrowSocket()
    {
        return throwSocket != null ? throwSocket : handAnchor;
    }

    private Vector2 ScreenToWorld(Vector2 screen)
    {
        Camera cam = mainCamera != null ? mainCamera : Camera.main;
        if (cam == null)
            return handAnchor != null ? (Vector2)handAnchor.position : Vector2.zero;

        // 2Dなのでカメラからの Z 距離を絶対値で渡す
        float z = Mathf.Abs(cam.transform.position.z);
        Vector3 w = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, z));
        return new Vector2(w.x, w.y);
    }
}
