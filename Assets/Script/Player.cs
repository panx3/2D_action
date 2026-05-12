using UnityEngine;
using UnityEngine.InputSystem;

public class Player : MonoBehaviour
{
    [Header("歩行（力で加減速。他システムのAddForceと合成しやすい）")]
    [SerializeField, Tooltip("地上：入力の最大時に掛ける横方向の力。Rigidbody2D.mass により実効が変わります。")]
    private float _groundMoveForce = 60f;
    [SerializeField, Tooltip("地上：横速度に比例する減速力。力と抗力のバランスで到達速度が決まります。")]
    private float _groundLinearDragX = 12f;
    [SerializeField, Range(0f, 1f), Tooltip("空中：地上に対する横移動力の割合")]
    private float _airMoveFactor = 0.35f;
    [SerializeField, Tooltip("空中：水平方向の減速（歩行より小さくすると慣性が出ます）")]
    private float _airLinearDragX = 1.5f;

    [Header("ジャンプ（Impulse）")]
    [SerializeField]
    private float _jumpSpeed = 7f;
    [SerializeField, Tooltip("崖から落ちた後もジャンプを受け付ける猶予時間（秒）")]
    private float _coyoteTime = 0.1f;
    [SerializeField, Tooltip("着地前にジャンプ入力を先行受付する時間（秒）")]
    private float _jumpBufferTime = 0.15f;

    [Header("ジャンプ物理（重力カーブ）")]
    [SerializeField, Tooltip("落下中（y速度 < 0）に掛ける重力倍率。大きいほどキビキビした落下感になる。")]
    private float _fallGravityMultiplier = 2.5f;
    [SerializeField, Tooltip("上昇中にジャンプボタンを離した時に掛ける重力倍率。可変ジャンプ高さを実現する。")]
    private float _jumpCutMultiplier = 2.0f;
    [SerializeField, Tooltip("落下速度の下限（負の値）。これより速くは落下しない。")]
    private float _maxFallSpeed = -22f;

    [Header("見た目")]
    [SerializeField, Tooltip("左右反転に使う SpriteRenderer。未設定なら自身の GameObject から自動取得。")]
    private SpriteRenderer _spriteRenderer;

    private Vector2 _inputDirection;
    private Rigidbody2D _rigid;
    private bool _bjump;
    private Animator _anim;
    private int _floorContactCount;
    private float _coyoteTimer;     // 接地から離れた後のカウントダウン
    private float _jumpBufferTimer; // ジャンプ先行入力のカウントダウン
    private bool _jumpHeld;         // ジャンプボタンが押されているか（可変ジャンプ用）

    void Start()
    {
        _rigid = GetComponent<Rigidbody2D>();
        _anim = GetComponent<Animator>();
        // SpriteRenderer が Inspector で未設定なら同 GameObject から取得
        if (_spriteRenderer == null)
            _spriteRenderer = GetComponent<SpriteRenderer>();
        _bjump = false;
    }

    void Update()
    {
        if (_anim != null)
            _anim.SetBool("Walk", Mathf.Abs(_inputDirection.x) > 0.01f);

        // 入力方向に応じてスプライトを左右反転
        if (_spriteRenderer != null)
        {
            if (_inputDirection.x > 0.01f)       _spriteRenderer.flipX = false;
            else if (_inputDirection.x < -0.01f) _spriteRenderer.flipX = true;
        }

        // タイマーをフレームごとに減らす
        _coyoteTimer     = Mathf.Max(0f, _coyoteTimer     - Time.deltaTime);
        _jumpBufferTimer = Mathf.Max(0f, _jumpBufferTimer - Time.deltaTime);
    }

    void FixedUpdate()
    {
        bool grounded = _floorContactCount > 0;
        bool canJump  = grounded || _coyoteTimer > 0f; // 接地中 or コヨーテ猶予内

        // ジャンプ実行：バッファ残りがあり、かつジャンプ可能
        bool doJump = (_jumpBufferTimer > 0f && canJump && !_bjump);
        if (doJump)
        {
            _rigid.AddForce(Vector2.up * _jumpSpeed, ForceMode2D.Impulse);
            _bjump           = true;
            _jumpBufferTimer = 0f;
            _coyoteTimer     = 0f;
        }

        float h = _inputDirection.x;
        float moveF = grounded ? _groundMoveForce : _groundMoveForce * _airMoveFactor;
        float drag = grounded ? _groundLinearDragX : _airLinearDragX;
        if (Mathf.Abs(h) > 0.01f)
            _rigid.AddForce(new Vector2(h * moveF, 0f), ForceMode2D.Force);
        _rigid.AddForce(new Vector2(-_rigid.linearVelocity.x * drag, 0f), ForceMode2D.Force);

        // --- ジャンプ物理（gravityScale=1.0 を維持し、追加重力を AddForce で補う方式） ---
        float vy = _rigid.linearVelocity.y;
        if (vy < 0f)
        {
            // 落下中：重力を強める
            float extra = Physics2D.gravity.y * (_fallGravityMultiplier - 1f);
            _rigid.AddForce(new Vector2(0f, extra * _rigid.mass), ForceMode2D.Force);
        }
        else if (vy > 0f && !_jumpHeld)
        {
            // 上昇中にボタンが離されている：ジャンプカット（短押しで低くジャンプ）
            float extra = Physics2D.gravity.y * (_jumpCutMultiplier - 1f);
            _rigid.AddForce(new Vector2(0f, extra * _rigid.mass), ForceMode2D.Force);
        }

        // 落下速度の上限クランプ（_maxFallSpeed は負の値）
        if (_rigid.linearVelocity.y < _maxFallSpeed)
            _rigid.linearVelocity = new Vector2(_rigid.linearVelocity.x, _maxFallSpeed);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Floor"))
        {
            _floorContactCount++;
            _bjump = false;
        }
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Floor"))
        {
            _floorContactCount = Mathf.Max(0, _floorContactCount - 1);
            // 接地を失った瞬間にコヨーテタイマーを開始
            if (_floorContactCount == 0 && _bjump == false)
                _coyoteTimer = _coyoteTime;
        }
    }

    public void _OnMove(InputAction.CallbackContext context)
    {
        _inputDirection = context.ReadValue<Vector2>();
    }

    public void _OnJump(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            _jumpHeld = true;
            _jumpBufferTimer = _jumpBufferTime; // バッファをセット（空中でも受け付ける）
        }
        else if (context.canceled)
        {
            _jumpHeld = false; // ボタンを離した → ジャンプカット発動条件
        }
    }

    public Rigidbody2D Rigidbody2D => _rigid;
    public bool IsGrounded => _floorContactCount > 0;

    public void ApplyExternalImpulse(Vector2 worldImpulse, ForceMode2D mode = ForceMode2D.Impulse)
    {
        if (_rigid == null) return;
        _rigid.AddForce(worldImpulse, mode);
    }

    public void ApplyMorningStarBounce(float upwardImpulse)
    {
        if (_rigid == null) return;
        _rigid.AddForce(Vector2.up * upwardImpulse, ForceMode2D.Impulse);
    }
}
