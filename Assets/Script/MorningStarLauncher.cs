using UnityEngine;
using UnityEngine.InputSystem;

public class MorningStarLauncher : MonoBehaviour
{
    [Header("参照")]
    public Rigidbody2D morningStarRb;
    public LineRenderer lineRenderer;
    [SerializeField] Rigidbody2D playerRigidbody2D;
    [SerializeField] bool usePlayerOnSameObject = true;
    [SerializeField] float screenZ = 10f;

    [Header("鎖(常時) 手元—先端")]
    [SerializeField] float maxChainLength = 5f;
    [SerializeField] float headImpPerFlickSpd = 0.1f;
    [SerializeField] float headImpMax = 4f;
    [SerializeField, Tooltip("エイム|ω| rad/s → 接線力(ゲイン)")]
    float guruguruGain = 0.4f;
    [SerializeField] float guruguruMinOmega = 0.5f;
    [SerializeField] float guruguruMaxF = 9f;

    [Header("はじき: 引っ張り ＆ ブリンクA")]
    [SerializeField] float flickEps = 0.2f;
    [SerializeField] float pullFlickFrom = 0.7f;
    [SerializeField] float blinkFlickFrom = 7.5f;
    [SerializeField] float plrPullK = 0.09f;
    [SerializeField] float blinkDist = 1f;
    [SerializeField] float blinkCD = 0.35f;
    [SerializeField, Tooltip("ブリンク同Fixed: 先端弾小")]
    bool shrinkHeadOnBlink = true;
    [SerializeField, Range(0.05f, 1f)]
    float blinkHeadScale = 0.35f;
    [Header("下向はじき→上")]
    [SerializeField] float bounceFlick = 0.6f;
    [SerializeField, Range(0.2f, 0.99f)]
    float downDotBounce = 0.5f;
    [SerializeField] float bounceUpImp = 6.5f;
    [SerializeField] [Tooltip("バウンド扱いで引っ張り/ブリンク止める")]
    bool bounceBlocksPlr = true;
    [SerializeField] [Tooltip("先端を下に小さく弾く")]
    bool headDownOnBounce = true;
    [SerializeField] float headDownImp = 1.2f;

    [Header("ガード/攻撃(エイム|ω|)")]
    [SerializeField] float guardOmega = 2.1f;
    [SerializeField] float guardSustainStreak = 0.16f;
    [SerializeField] float attackFlick = 2.4f;
    [SerializeField] float headAttMult = 1.5f;
    [SerializeField, Tooltip("攻撃フレーム: 引っ張りを掛けない")]
    bool noPullOnGuardAtt = true;

    [Header("Linecast(壁: 手元—先端の線分)")]
    [SerializeField, Tooltip("Project の Walls(レイヤ6)等。0 ならフック無し")]
    LayerMask wallMask;
    [SerializeField] float unhookFlickS = 3.2f;

    [HideInInspector] public bool isGuarding;
    [HideInInspector] public bool attackThisFrame;

    float _angPrevRad;
    bool _angInited;
    bool _pInited;
    Vector2 _pW0;
    float _grdSustain;
    float _nextBl;
    DistanceJoint2D _jH;
    DistanceJoint2D _jG;
    Rigidbody2D _hRb;
    GameObject _hGo;
    Rigidbody2D _p;

    void Awake()
    {
        if (usePlayerOnSameObject && playerRigidbody2D == null)
            playerRigidbody2D = GetComponent<Rigidbody2D>();
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
        _pW0 = (Vector2)_p.position;
        _pInited = true;
    }

    void Update()
    {
        if (!lineRenderer || !morningStarRb) return;
        lineRenderer.SetPosition(0, transform.position);
        lineRenderer.SetPosition(1, morningStarRb.position);
    }

    void FixedUpdate()
    {
        if (_p == null || !morningStarRb) return;
        if (Time.timeScale < 0.01f) return;
        var dt = Time.fixedDeltaTime;
        if (dt < 1e-5f) return;

        if (!TryGetPointerScreen(out var sc)) return;
        var w = WorldFromScreen(sc);
        if (!_pInited) { _pW0 = w; _pInited = true; }
        var wv = (w - _pW0) / dt;
        _pW0 = w;
        var sp = wv.magnitude;
        if (sp < flickEps) { wv = Vector2.zero; sp = 0f; }
        var fdir = sp > 0.0001f ? wv / sp : Vector2.right;
        if (unhookFlickS > 0f && sp > unhookFlickS) Unhook();

        var hand = (Vector2)transform.position;
        var toP = w - hand;
        if (toP.sqrMagnitude < 1e-4f) toP = Vector2.right;
        var aNow = Mathf.Atan2(toP.y, toP.x);
        var prevA = _angPrevRad;
        float wA = 0f;
        var dA = 0f;
        if (_angInited) { dA = DeltaAngleRad(aNow, prevA); wA = Mathf.Abs(dA) / dt; }
        _angPrevRad = aNow;
        _angInited = true;
        _grdSustain = wA >= guardOmega ? _grdSustain + dt : Mathf.Max(0f, _grdSustain - dt * 1.5f);
        isGuarding = _grdSustain >= guardSustainStreak;
        var guardAttack = isGuarding && sp >= attackFlick;
        attackThisFrame = guardAttack;

        var bounce = sp >= bounceFlick && Vector2.Dot(fdir, Vector2.down) >= downDotBounce;
        var plrB = GetComponent<Player>();
        if (bounce && plrB) plrB.ApplyMorningStarBounce(bounceUpImp);
        if (bounce && headDownOnBounce) morningStarRb.AddForce(Vector2.down * headDownImp, ForceMode2D.Impulse);

        var plrFxsBlocked = bounce && bounceBlocksPlr;
        var didBlink = false;
        if (!plrFxsBlocked)
        {
            if (sp >= pullFlickFrom && sp < blinkFlickFrom)
            {
                if (!(noPullOnGuardAtt && guardAttack))
                    _p.AddForce(fdir * (sp * plrPullK), ForceMode2D.Impulse);
            }
            if (sp >= blinkFlickFrom && Time.time >= _nextBl)
            {
                _p.MovePosition((Vector2)_p.position + fdir * blinkDist);
                _nextBl = Time.time + blinkCD;
                didBlink = true;
            }
        }

        var hMult = guardAttack ? headAttMult : 1f;
        if (sp > flickEps)
        {
            var h = Mathf.Min(headImpMax, sp * headImpPerFlickSpd) * hMult;
            if (didBlink && shrinkHeadOnBlink) h *= blinkHeadScale;
            morningStarRb.AddForce(fdir * h, ForceMode2D.Impulse);
        }
        if (_angInited && wA >= guruguruMinOmega)
        {
            var r = (Vector2)morningStarRb.position - hand;
            if (r.sqrMagnitude > 1e-3f)
            {
                var perp = new Vector2(-r.y, r.x).normalized;
                var wSigned = dA / dt;
                var fT = Mathf.Clamp(wSigned * guruguruGain, -guruguruMaxF, guruguruMaxF);
                morningStarRb.AddForce(perp * fT, ForceMode2D.Force);
            }
        }
        GHook(hand);
    }

    static float DeltaAngleRad(float a, float b)
    {
        return Mathf.Atan2(Mathf.Sin(a - b), Mathf.Cos(a - b));
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

    bool TryGetPointerScreen(out Vector2 s)
    {
        s = default;
        var m = Mouse.current;
        if (m == null) return false;
        s = m.position.ReadValue();
        return true;
    }

    Vector2 WorldFromScreen(Vector2 s)
    {
        var c = Camera.main;
        if (!c) return (Vector2)transform.position;
        return c.ScreenToWorldPoint(new Vector3(s.x, s.y, screenZ));
    }
}
