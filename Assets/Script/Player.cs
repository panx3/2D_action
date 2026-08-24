using System.Collections;

using UnityEngine;

using UnityEngine.InputSystem;

using UnityEngine.Serialization;



/// <summary>

/// 移動入力は Update で集約し、物理は FixedUpdate で適用。武器状態では横移動を止めない。

/// </summary>

public class Player : MonoBehaviour

{

    [Header("歩行（地上）")]

    [SerializeField, Tooltip("地上の横方向加速力")]

    private float _groundMoveForce = 60f;

    [SerializeField, Tooltip("地上の横減速（速度に比例）")]

    private float _groundLinearDragX = 12f;



    [Header("歩行（空中）")]

    [SerializeField, Range(0f, 1f), Tooltip("空中の加速力＝地上×この値")]

    private float _airMoveFactor = 0.5f;

    [SerializeField, Tooltip("空中の横減速")]

    private float _airLinearDragX = 2f;



    [Header("ジャンプ（Impulse）")]

    [SerializeField] private float _jumpSpeed = 7f;

    [SerializeField, Tooltip("コヨーテタイム（秒）")]

    private float _coyoteTime = 0.12f;

    [SerializeField, Tooltip("ジャンプバッファ（秒）")]

    private float _jumpBufferTime = 0.15f;



    [Header("ジャンプ物理")]

    [SerializeField] private float _fallGravityMultiplier = 2.5f;

    [SerializeField] private float _jumpCutMultiplier = 2f;

    [SerializeField, Tooltip("最大落下速度（負の値）")]

    private float _maxFallSpeed = -22f;



    [Header("見た目")]

    [FormerlySerializedAs("_spriteRenderer")]
    [SerializeField, Tooltip("体の SpriteRenderer。未設定なら子の SpriteRenderer を使用")]
    private SpriteRenderer _bodySprite;

    [SerializeField, Tooltip("flipX が使えない場合のみ Visual の localScale.x を反転")]
    private Transform _visualRoot;



    [Header("空中発射")]

    [SerializeField] private float _airLaunchBlinkDuration = 0.22f;

    [SerializeField] private float _airLaunchBlinkInterval = 0.05f;



    private Vector2 _moveInput;
    private Vector2 _actionMoveInput;

    private Rigidbody2D _rigid;

    private bool _bjump;

    private Animator _anim;

    private int _floorContactCount;

    private float _coyoteTimer;

    private float _jumpBufferTimer;

    private bool _jumpHeld;

    private bool _backwardAim;

    private bool _hasAimTarget;

    private float _aimFacingWorldX;

    private Coroutine _airLaunchBlinkRoutine;

    private bool _facingRight = true;



    public float MoveInputX => _moveInput.x;

    public bool FacingRight => _facingRight;

    public Rigidbody2D Rigidbody2D => _rigid;

    public bool IsGrounded => _floorContactCount > 0;

    public bool IsBackwardAim => _backwardAim;



    private void Start()

    {

        _rigid = GetComponent<Rigidbody2D>();

        _anim = GetComponent<Animator>();

        if (_bodySprite == null)

            _bodySprite = GetComponentInChildren<SpriteRenderer>();

        _bjump = false;

        ApplyMovementFacingVisual();

    }



    private void Update()

    {

        PollMovementInput();



        if (_anim != null)

            _anim.SetBool("Walk", Mathf.Abs(_moveInput.x) > 0.01f);



        RefreshBackwardAimFromMoveInput();

        UpdateMovementFacing();



        _coyoteTimer = Mathf.Max(0f, _coyoteTimer - Time.deltaTime);

        _jumpBufferTimer = Mathf.Max(0f, _jumpBufferTimer - Time.deltaTime);

    }



    private void FixedUpdate()

    {

        bool grounded = IsGrounded;

        bool canJump = grounded || _coyoteTimer > 0f;



        if (_jumpBufferTimer > 0f && canJump && !_bjump)

        {

            _rigid.AddForce(Vector2.up * _jumpSpeed, ForceMode2D.Impulse);

            _bjump = true;

            _jumpBufferTimer = 0f;

            _coyoteTimer = 0f;

        }



        ApplyHorizontalMovement(grounded);

        ApplyJumpGravity();

    }



    /// <summary>Input System コールバック＋キーボード直読み。武器中も毎フレーム更新。</summary>

    private void PollMovementInput()

    {

        float x = _actionMoveInput.x;



        Keyboard kb = Keyboard.current;

        if (kb != null)

        {

            float kx = 0f;

            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) kx -= 1f;

            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) kx += 1f;

            if (Mathf.Abs(kx) > Mathf.Abs(x))

                x = kx;

        }



        Gamepad pad = Gamepad.current;

        if (pad != null)

        {

            Vector2 stick = pad.leftStick.ReadValue();

            if (Mathf.Abs(stick.x) > Mathf.Abs(x))

                x = stick.x;

        }



        x = Mathf.Clamp(x, -1f, 1f);
        _moveInput = new Vector2(x, 0f);

    }



    private void ApplyHorizontalMovement(bool grounded)

    {

        float h = _moveInput.x;

        float moveF = grounded ? _groundMoveForce : _groundMoveForce * _airMoveFactor;

        float drag = grounded ? _groundLinearDragX : _airLinearDragX;



        if (Mathf.Abs(h) > 0.01f)

            _rigid.AddForce(new Vector2(h * moveF, 0f), ForceMode2D.Force);



        _rigid.AddForce(new Vector2(-_rigid.linearVelocity.x * drag, 0f), ForceMode2D.Force);

    }



    private void ApplyJumpGravity()

    {

        float vy = _rigid.linearVelocity.y;

        if (vy < 0f)

        {

            float extra = Physics2D.gravity.y * (_fallGravityMultiplier - 1f);

            _rigid.AddForce(new Vector2(0f, extra * _rigid.mass), ForceMode2D.Force);

        }

        else if (vy > 0f && !_jumpHeld)

        {

            float extra = Physics2D.gravity.y * (_jumpCutMultiplier - 1f);

            _rigid.AddForce(new Vector2(0f, extra * _rigid.mass), ForceMode2D.Force);

        }



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

            if (_floorContactCount == 0 && !_bjump)

                _coyoteTimer = _coyoteTime;

        }

    }



    public void _OnMove(InputAction.CallbackContext context)

    {

        Vector2 v = context.ReadValue<Vector2>();

        _actionMoveInput = new Vector2(v.x, 0f);

    }



    public void _OnJump(InputAction.CallbackContext context)

    {

        if (context.performed)

        {

            _jumpHeld = true;

            _jumpBufferTimer = _jumpBufferTime;

        }

        else if (context.canceled)

        {

            _jumpHeld = false;

        }

    }



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



    public void SetAimFacing(float aimWorldX, bool backwardAim)

    {

        _hasAimTarget = true;

        _aimFacingWorldX = aimWorldX;

        _backwardAim = backwardAim;

        if (_anim != null && HasAnimatorParam("BackwardAim", AnimatorControllerParameterType.Bool))

            _anim.SetBool("BackwardAim", backwardAim);

        UpdateMovementFacing();

    }



    public void ClearAimFacing()

    {

        _hasAimTarget = false;

        _backwardAim = false;

        if (_anim != null && HasAnimatorParam("BackwardAim", AnimatorControllerParameterType.Bool))

            _anim.SetBool("BackwardAim", false);

        UpdateMovementFacing();

    }



    public void PlayAirLaunchBlink()

    {

        if (ResolveBodySprite() == null) return;

        if (_airLaunchBlinkRoutine != null)

            StopCoroutine(_airLaunchBlinkRoutine);

        _airLaunchBlinkRoutine = StartCoroutine(AirLaunchBlinkRoutine());

    }



    private void RefreshBackwardAimFromMoveInput()

    {

        if (!_hasAimTarget) return;



        float moveX = _moveInput.x;

        if (Mathf.Abs(moveX) < 0.01f) return;



        float aimDirX = _aimFacingWorldX - transform.position.x;

        if (Mathf.Abs(aimDirX) < 0.01f) return;



        bool backward = Mathf.Sign(moveX) != Mathf.Sign(aimDirX);

        if (backward == _backwardAim) return;



        _backwardAim = backward;

        if (_anim != null && HasAnimatorParam("BackwardAim", AnimatorControllerParameterType.Bool))

            _anim.SetBool("BackwardAim", backward);

    }



    private void UpdateMovementFacing()

    {

        float moveX = _moveInput.x;

        if (moveX > 0.01f)

            _facingRight = true;

        else if (moveX < -0.01f)

            _facingRight = false;



        ApplyMovementFacingVisual();

    }



    private SpriteRenderer ResolveBodySprite()

    {

        if (_bodySprite != null)

            return _bodySprite;

        return GetComponentInChildren<SpriteRenderer>();

    }



    private void ApplyMovementFacingVisual()

    {

        SpriteRenderer sprite = ResolveBodySprite();

        if (sprite != null)

        {

            sprite.flipX = !_facingRight;

            return;

        }



        if (_visualRoot == null)

            return;



        Vector3 scale = _visualRoot.localScale;

        float absX = Mathf.Abs(scale.x);

        if (absX < 1e-4f)

            absX = 1f;

        scale.x = _facingRight ? absX : -absX;

        _visualRoot.localScale = scale;

    }



    private IEnumerator AirLaunchBlinkRoutine()

    {

        SpriteRenderer sprite = ResolveBodySprite();

        if (sprite == null)

        {

            _airLaunchBlinkRoutine = null;

            yield break;

        }



        float elapsed = 0f;

        var wait = new WaitForSeconds(_airLaunchBlinkInterval);

        while (elapsed < _airLaunchBlinkDuration)

        {

            sprite.enabled = !sprite.enabled;

            yield return wait;

            elapsed += _airLaunchBlinkInterval;

        }



        sprite.enabled = true;

        _airLaunchBlinkRoutine = null;

    }



    private static bool HasAnimatorParam(Animator anim, string name, AnimatorControllerParameterType type)

    {

        if (anim == null || anim.runtimeAnimatorController == null) return false;

        foreach (AnimatorControllerParameter p in anim.parameters)

        {

            if (p.name == name && p.type == type) return true;

        }

        return false;

    }



    private bool HasAnimatorParam(string name, AnimatorControllerParameterType type)

        => HasAnimatorParam(_anim, name, type);

}


