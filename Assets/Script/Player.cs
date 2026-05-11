using UnityEngine;
using UnityEngine.InputSystem;

public class Player : MonoBehaviour
{
    [Header("歩行（力で加減速。他システムのAddForceと合成しやすい）")]
    [SerializeField, Tooltip("地上：入力の最大時に掛ける横方向の力。Rigidbody2D.mass により実効が変わります。")]
    private float _groundMoveForce = 40f;
    [SerializeField, Tooltip("地上：横速度に比例する減速力。力と抗力のバランスで到達速度が決まります。")]
    private float _groundLinearDragX = 8f;
    [SerializeField, Range(0f, 1f), Tooltip("空中：地上に対する横移動力の割合")]
    private float _airMoveFactor = 0.35f;
    [SerializeField, Tooltip("空中：水平方向の減速（歩行より小さくすると慣性が出ます）")]
    private float _airLinearDragX = 1.5f;

    [Header("ジャンプ（Impulse）")]
    [SerializeField]
    private float _jumpSpeed = 7f;

    private Vector2 _inputDirection;
    private Rigidbody2D _rigid;
    private bool _bjump;
    private Animator _anim;
    private int _floorContactCount;
    private bool _pendingJump;

    void Start()
    {
        _rigid = GetComponent<Rigidbody2D>();
        _anim = GetComponent<Animator>();
        _bjump = false;
    }

    void Update()
    {
        if (_anim != null)
            _anim.SetBool("Walk", Mathf.Abs(_inputDirection.x) > 0.01f);
    }

    void FixedUpdate()
    {
        if (_pendingJump && !_bjump)
        {
            _rigid.AddForce(Vector2.up * _jumpSpeed, ForceMode2D.Impulse);
            _bjump = true;
            _pendingJump = false;
        }

        float h = _inputDirection.x;
        bool grounded = _floorContactCount > 0;

        float moveF = grounded ? _groundMoveForce : _groundMoveForce * _airMoveFactor;
        float drag = grounded ? _groundLinearDragX : _airLinearDragX;
        if (Mathf.Abs(h) > 0.01f)
            _rigid.AddForce(new Vector2(h * moveF, 0f), ForceMode2D.Force);
        _rigid.AddForce(new Vector2(-_rigid.linearVelocity.x * drag, 0f), ForceMode2D.Force);
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
            _floorContactCount = Mathf.Max(0, _floorContactCount - 1);
    }

    public void _OnMove(InputAction.CallbackContext context)
    {
        _inputDirection = context.ReadValue<Vector2>();
    }

    public void _OnJump(InputAction.CallbackContext context)
    {
        if (!context.performed || _bjump)
            return;
        _pendingJump = true;
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
