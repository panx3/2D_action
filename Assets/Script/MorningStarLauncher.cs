using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// モーニングスター: DistanceJoint2D で鎖長制限。基本は手元に引きずられる挙動。
/// マウスは「動いた方向」を見て発射。既定は手元から鎖長ぶん先端を伸ばす＋任意でその方向へ初速。
/// オプションで発射前にプレイヤー位置へ引き寄せてから撃つ。
/// </summary>
public class MorningStarLauncher : MonoBehaviour
{
    [Header("参照")]
    public Rigidbody2D morningStarRb;
    public LineRenderer lineRenderer;
    [SerializeField] Rigidbody2D playerRigidbody2D;
    [SerializeField] bool usePlayerOnSameObject = true;
    [SerializeField, Tooltip("未指定なら Camera.main。ScreenToWorld と描画範囲判定に使う")]
    Camera aimCamera;
    [SerializeField, Tooltip("オフのとき画面外の座標も使う（非推奨）")]
    bool restrictPointerToGameView = true;
    [SerializeField] float screenZ = 10f;

    [Header("鎖(常時) 手元—先端")]
    [SerializeField, Tooltip("手元: この Transform。先端: morningStarRb。ジョイント+クランプの最大距離。")]
    float maxChainLength = 5f;
    [SerializeField, Tooltip("手元—先端が鎖長を超えないよう矯正")]
    bool clampHeadToChainLength = true;
    [SerializeField, Tooltip("先端の最大速度。0 で無効")]
    float maxHeadLinearSpeed = 18f;

    [Header("引きずり（主体）")]
    [SerializeField, Tooltip("先端に毎フレーム -v×係数 の力。大きいほどプレイヤー追従が強く遅い")]
    float headFollowDrag = 10f;

    [Header("マウス＝方向で発射")]
    [SerializeField, Tooltip("この値より小さいスクリーン移動(px)は無視（ノイズ除去）")]
    float mouseDeadzonePixels = 3f;
    [SerializeField, Tooltip("オン: 手元から鎖長ぶんマウス方向へ先端を配置（メイン）。オフ: 下記インパルスのみ")]
    bool launchUsingChainReach = true;
    [SerializeField, Tooltip("鎖長延伸後、その方向に付ける速度。0 なら位置だけ（見た目は瞬間伸び）")]
    float launchReachExitSpeed = 14f;
    [SerializeField, Tooltip("launchUsingChainReach がオフのときのインパルス")]
    float aimLaunchImpulse = 5f;
    [SerializeField, Tooltip("連続発射の最短間隔(秒)。0 で毎FixedUpdate取り得る")]
    float aimLaunchCooldown = 0f;

    [Header("攻撃: 引き寄せ→発射")]
    [SerializeField, Tooltip("オン: マウス移動検知時、先にプレイヤーRigidbody位置へ移動してから発射")]
    bool snapHeadToPlayerBeforeLaunch = true;
    [SerializeField, Tooltip("引き寄せ時に線速度・角速度を0にする")]
    bool zeroVelocityOnRecall = true;
    [SerializeField, Tooltip("引き寄せ完了後、発射までの待ち秒。0 で同フレーム発射")]
    float launchWindUpDuration = 0.14f;

    [Header("Linecast(壁: 手元—先端)")]
    [SerializeField, Tooltip("0 ならフック無し")]
    LayerMask wallMask;

    DistanceJoint2D _jH;
    DistanceJoint2D _jG;
    Rigidbody2D _hRb;
    GameObject _hGo;
    Rigidbody2D _p;

    Vector2 _lastScreenForAim;
    Vector2 _lastMouseWorld;
    bool _aimSampleReady;
    float _nextAimLaunchTime;
    bool _launchPending;
    float _launchExecuteAtTime;
    Vector2 _pendingLaunchDir;

    void Awake()
    {
        if (usePlayerOnSameObject && playerRigidbody2D == null)
            playerRigidbody2D = GetComponent<Rigidbody2D>();
    }

    void OnValidate()
    {
        if (_jH != null)
            _jH.distance = maxChainLength;
    }

    void Start()
    {
        _p = playerRigidbody2D;
        if (lineRenderer)
        {
            lineRenderer.positionCount = 2;
            lineRenderer.enabled = true;
        }
        if (!morningStarRb || _p == null) return;
        _jH = GetComponent<DistanceJoint2D>();
        if (_jH == null) _jH = gameObject.AddComponent<DistanceJoint2D>();
        _jH.connectedBody = morningStarRb;
        _jH.maxDistanceOnly = true;
        _jH.autoConfigureDistance = false;
        _jH.distance = maxChainLength;
        _jH.enableCollision = false;
        _jH.enabled = true;
        _jG = gameObject.AddComponent<DistanceJoint2D>();
        _jG.enabled = false;
        _jG.enableCollision = false;
        _aimSampleReady = false;
    }

    void Update()
    {
        if (!lineRenderer || !morningStarRb) return;
        var handW = transform.position;
        var headW = (Vector3)morningStarRb.position;
        var off = headW - handW;
        var maxLen = maxChainLength;
        if (off.sqrMagnitude > maxLen * maxLen)
            headW = handW + off.normalized * maxLen;
        lineRenderer.SetPosition(0, handW);
        lineRenderer.SetPosition(1, headW);
    }

    void FixedUpdate()
    {
        if (_p == null || !morningStarRb) return;
        if (Time.timeScale < 0.01f) return;
        var dt = Time.fixedDeltaTime;
        if (dt < 1e-5f) return;

        var hand = (Vector2)transform.position;
        if (_jH != null && !Mathf.Approximately(_jH.distance, maxChainLength))
            _jH.distance = maxChainLength;
        if (clampHeadToChainLength)
            ClampHeadToChain(hand);

        ApplyHeadFollowDrag();

        ProcessPendingLaunch();

        if (!TryGetAimPointerScreen(out var sc))
        {
            ClearAimPointerState();
            CancelPendingLaunch();
            if (maxHeadLinearSpeed > 0f)
                ClampHeadSpeed();
            if (clampHeadToChainLength)
                ClampHeadToChain(hand);
            return;
        }

        var w = WorldFromScreen(sc);
        if (!_aimSampleReady)
        {
            _lastScreenForAim = sc;
            _lastMouseWorld = w;
            _aimSampleReady = true;
        }
        else
        {
            var screenDelta = sc - _lastScreenForAim;
            _lastScreenForAim = sc;
            var deltaWorld = w - _lastMouseWorld;
            _lastMouseWorld = w;
            if (!_launchPending
                && screenDelta.sqrMagnitude >= mouseDeadzonePixels * mouseDeadzonePixels
                && Time.time >= _nextAimLaunchTime
                && deltaWorld.sqrMagnitude > 1e-12f)
            {
                if (snapHeadToPlayerBeforeLaunch)
                    RecallMorningStarToPlayer(hand);
                if (launchWindUpDuration > 0f)
                    QueueDelayedLaunch(deltaWorld);
                else
                {
                    ApplyAimLaunchDirection(deltaWorld);
                    if (aimLaunchCooldown > 0f)
                        _nextAimLaunchTime = Time.time + aimLaunchCooldown;
                }
            }
        }

        GHook(hand);
        if (maxHeadLinearSpeed > 0f)
            ClampHeadSpeed();
        if (clampHeadToChainLength)
            ClampHeadToChain(hand);
    }

    /// <summary> マウス／スティックの移動方向で発射（鎖長延伸 or インパルスは Inspector）。 </summary>
    public void ApplyAimLaunchDirection(Vector2 worldDirection)
    {
        if (morningStarRb == null) return;
        var d = worldDirection;
        if (d.sqrMagnitude < 1e-12f) return;
        var hand = (Vector2)transform.position;
        if (launchUsingChainReach)
            LaunchHeadAlongChainReach(d, hand);
        else
        {
            d.Normalize();
            morningStarRb.AddForce(d * aimLaunchImpulse, ForceMode2D.Impulse);
        }
    }

    /// <summary> 手元から鎖長ぶん、指定方向へ先端を伸ばす（ワールド）。その後の物理で軌道が決まる。 </summary>
    public void LaunchHeadAlongChainReach(Vector2 worldDirection, Vector2 handWorld)
    {
        if (morningStarRb == null) return;
        var d = worldDirection;
        if (d.sqrMagnitude < 1e-12f) return;
        d.Normalize();
        morningStarRb.position = handWorld + d * maxChainLength;
        morningStarRb.WakeUp();
        morningStarRb.angularVelocity = 0f;
        morningStarRb.linearVelocity = launchReachExitSpeed > 0f ? d * launchReachExitSpeed : Vector2.zero;
    }

    /// <summary> 引き寄せ（プレイヤー位置）→ 方向発射。ゲームパッド用。FixedUpdate 内推奨。 </summary>
    public void ApplyRecallThenLaunch(Vector2 worldDirection)
    {
        if (_p == null || morningStarRb == null) return;
        var hand = (Vector2)transform.position;
        if (snapHeadToPlayerBeforeLaunch)
            RecallMorningStarToPlayer(hand);
        if (launchWindUpDuration > 0f)
            QueueDelayedLaunch(worldDirection);
        else
        {
            ApplyAimLaunchDirection(worldDirection);
            if (aimLaunchCooldown > 0f)
                _nextAimLaunchTime = Time.time + aimLaunchCooldown;
        }
    }

    void QueueDelayedLaunch(Vector2 deltaWorld)
    {
        _pendingLaunchDir = deltaWorld;
        _launchExecuteAtTime = Time.time + launchWindUpDuration;
        _launchPending = true;
    }

    void ProcessPendingLaunch()
    {
        if (!_launchPending || morningStarRb == null) return;
        if (Time.time < _launchExecuteAtTime) return;
        ApplyAimLaunchDirection(_pendingLaunchDir);
        _launchPending = false;
        if (aimLaunchCooldown > 0f)
            _nextAimLaunchTime = Time.time + aimLaunchCooldown;
    }

    void CancelPendingLaunch()
    {
        _launchPending = false;
    }

    void RecallMorningStarToPlayer(Vector2 handAnchor)
    {
        if (morningStarRb == null || _p == null) return;
        Unhook();
        morningStarRb.position = _p.position;
        morningStarRb.WakeUp();
        if (zeroVelocityOnRecall)
        {
            morningStarRb.linearVelocity = Vector2.zero;
            morningStarRb.angularVelocity = 0f;
        }
        if (clampHeadToChainLength)
            ClampHeadToChain(handAnchor);
    }

    void ApplyHeadFollowDrag()
    {
        if (headFollowDrag <= 0f || morningStarRb == null) return;
        var v = morningStarRb.linearVelocity;
        morningStarRb.AddForce(-v * headFollowDrag, ForceMode2D.Force);
    }

    void ClampHeadToChain(Vector2 handAnchor)
    {
        var head = morningStarRb.position;
        var off = (Vector2)head - handAnchor;
        var sql = off.sqrMagnitude;
        var maxSq = maxChainLength * maxChainLength;
        if (sql <= maxSq || sql < 1e-10f) return;
        var dir = off.normalized;
        morningStarRb.position = handAnchor + dir * maxChainLength;
        var v = morningStarRb.linearVelocity;
        var radialOut = Vector2.Dot(v, dir);
        if (radialOut > 0f)
            morningStarRb.linearVelocity = v - dir * radialOut;
    }

    void ClampHeadSpeed()
    {
        var v = morningStarRb.linearVelocity;
        var mag = v.magnitude;
        if (mag > maxHeadLinearSpeed && mag > 1e-5f)
            morningStarRb.linearVelocity = v * (maxHeadLinearSpeed / mag);
    }

    void GHook(Vector2 hand)
    {
        if (wallMask.value == 0)
        {
            if (_jG != null) _jG.enabled = false;
            return;
        }
        if (_jG == null) return;
        if (_p == null || !morningStarRb) return;
        var hpos = (Vector2)morningStarRb.position;
        var r = Physics2D.Linecast(hand, hpos, wallMask);
        if (r.collider == null) { Unhook(); return; }
        if (_hGo == null)
        {
            _hGo = new GameObject("MorningStar_Grapple");
            _hGo.transform.SetParent(null);
            _hRb = _hGo.AddComponent<Rigidbody2D>();
            _hRb.bodyType = RigidbodyType2D.Static;
        }
        _hGo.SetActive(true);
        _hGo.transform.position = r.point;
        _jG.enabled = true;
        _jG.connectedBody = _hRb;
        _jG.autoConfigureDistance = false;
        _jG.maxDistanceOnly = true;
        _jG.distance = Vector2.Distance((Vector2)_p.position, (Vector2)r.point);
    }

    void Unhook()
    {
        if (_jG != null) _jG.enabled = false;
    }

    bool TryGetAimPointerScreen(out Vector2 s)
    {
        s = default;
        var m = Mouse.current;
        if (m == null) return false;
        s = m.position.ReadValue();
        if (!restrictPointerToGameView) return true;
        var c = GetAimCamera();
        if (c == null) return true;
        return c.pixelRect.Contains(s);
    }

    void ClearAimPointerState()
    {
        _aimSampleReady = false;
    }

    Camera GetAimCamera() => aimCamera != null ? aimCamera : Camera.main;

    Vector2 WorldFromScreen(Vector2 s)
    {
        var c = GetAimCamera();
        if (!c) return (Vector2)transform.position;
        return c.ScreenToWorldPoint(new Vector3(s.x, s.y, screenZ));
    }
}
