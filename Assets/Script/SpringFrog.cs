using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D), typeof(EnemyHealth))]
public sealed class SpringFrog : MonoBehaviour
{
    public enum InitialDirection { Left = -1, Right = 1 }

    [Header("Jump Patrol")]
    [SerializeField, Min(0.1f), Tooltip("上方向のImpulse。初速はJump Force / Rigidbody2D.mass。")]
    private float _jumpForce = 3.2f;
    [SerializeField, Min(0f)] private float _moveSpeed = 1.5f;
    [SerializeField, Min(0f)] private float _jumpInterval = 0.12f;
    [SerializeField] private InitialDirection _initialDirection = InitialDirection.Right;

    [Header("Terrain Checks")]
    [SerializeField] private Transform _groundCheck;
    [SerializeField, Tooltip("右向き時の前方の足元。左向きでは判定位置を左右反転します。")]
    private Transform _edgeCheck;
    [SerializeField, Tooltip("右向き時の壁判定位置。左向きでは判定位置を左右反転します。")]
    private Transform _wallCheck;
    [SerializeField] private LayerMask _groundLayer = (1 << 0) | (1 << 6);
    [SerializeField, Min(0.01f)] private float _groundCheckDistance = 0.08f;
    [SerializeField, Min(0.01f)] private float _edgeCheckDepth = 0.18f;
    [SerializeField, Min(0.01f)] private float _wallCheckDistance = 0.08f;

    [Header("Six Frame Animation")]
    [SerializeField] private SpriteRenderer _visualRenderer;
    [SerializeField] private Sprite[] _jumpFrames = new Sprite[6];
    [SerializeField, Min(1f), Tooltip("踏切・着地コマの再生速度。空中のコマは垂直速度と同期します。")]
    private float _animationSpeed = 12f;

    [Header("Contact Damage")]
    [SerializeField, Min(0)] private int _contactDamage = 1;
    [SerializeField, Min(0f)] private float _contactKnockback = 5f;
    [SerializeField, Min(0f)] private float _contactCooldown = 0.7f;

    [Header("Jump SFX")]
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip _jumpSE;
    [SerializeField, Range(0f, 1f)] private float _jumpSEVolume = 0.65f;

    private const float Skin = 0.025f;
    private readonly RaycastHit2D[] _hits = new RaycastHit2D[16];
    private Rigidbody2D _body;
    private BoxCollider2D _collider;
    private EnemyHealth _health;
    private int _direction;
    private bool _grounded;
    private bool _jumping;
    private float _airSpeed;
    private float _jumpTime;
    private float _landTime;
    private float _nextJumpTime;
    private float _lastContactTime = float.NegativeInfinity;

    private void Awake()
    {
        _body = GetComponent<Rigidbody2D>();
        _collider = GetComponent<BoxCollider2D>();
        _health = GetComponent<EnemyHealth>();
        if (_visualRenderer == null)
            _visualRenderer = GetComponentInChildren<SpriteRenderer>();
        if (_audioSource == null)
            _audioSource = GetComponent<AudioSource>();
    }

    private void OnEnable()
    {
        _direction = _initialDirection == InitialDirection.Left ? -1 : 1;
        _grounded = false;
        _jumping = false;
        _airSpeed = 0f;
        _landTime = float.NegativeInfinity;
        _nextJumpTime = Time.time;
        _lastContactTime = float.NegativeInfinity;
        UpdateVisual();
    }

    private void FixedUpdate()
    {
        if (_health.IsDeathHandled || _health.IsHitStunned)
            return;

        ContactFilter2D filter = new ContactFilter2D();
        filter.SetLayerMask(_groundLayer);
        filter.useTriggers = false;

        bool grounded = _body.linearVelocity.y <= 0.05f && IsGrounded(filter);
        if (grounded && !_grounded)
        {
            _jumping = false;
            _landTime = Time.time;
            _nextJumpTime = Time.time + _jumpInterval;
        }
        _grounded = grounded;

        if (grounded)
        {
            _body.linearVelocity = new Vector2(0f, _body.linearVelocity.y);
            if (Time.time >= _nextJumpTime)
            {
                bool canAdvance = CanHop(_direction, filter);
                if (!canAdvance && CanHop(-_direction, filter))
                {
                    _direction = -_direction;
                    canAdvance = true;
                }
                _airSpeed = canAdvance ? _direction * _moveSpeed : 0f;
                _body.linearVelocity = new Vector2(_airSpeed, 0f);
                _body.AddForce(Vector2.up * _jumpForce, ForceMode2D.Impulse);
                _jumping = true;
                _grounded = false;
                _jumpTime = Time.time;
                if (_audioSource != null && _jumpSE != null)
                    _audioSource.PlayOneShot(_jumpSE, _jumpSEVolume);
            }
        }
        else if (_jumping)
        {
            if (HasWall(_direction, filter))
                _airSpeed = 0f;
            _body.linearVelocity = new Vector2(_airSpeed, _body.linearVelocity.y);
        }
    }

    private void Update()
    {
        if (!_health.IsDeathHandled)
            UpdateVisual();
    }

    private bool IsGrounded(ContactFilter2D filter)
    {
        Vector2 origin = _groundCheck != null
            ? (Vector2)_groundCheck.position
            : new Vector2(_collider.bounds.center.x, _collider.bounds.min.y + Skin);
        if (!HasGround(origin, _groundCheckDistance, filter))
            return false;
        int count = _collider.Cast(Vector2.down, filter, _hits, _groundCheckDistance);
        return HasUpwardSurface(count);
    }

    private bool CanHop(int direction, ContactFilter2D filter)
    {
        if (HasWall(direction, filter))
            return false;

        float gravity = -Physics2D.gravity.y * _body.gravityScale;
        if (gravity <= 0f)
            return false;
        float launchSpeed = _jumpForce / _body.mass;
        float flightTime = (launchSpeed + Mathf.Sqrt(launchSpeed * launchSpeed + 2f * gravity * _edgeCheckDepth)) / gravity;
        float travel = _moveSpeed * (flightTime + Time.fixedDeltaTime * 2f);
        Bounds bounds = _collider.bounds;
        Vector2 origin = CheckPosition(_edgeCheck, direction,
            new Vector2(bounds.center.x + direction * (bounds.extents.x + Skin), bounds.min.y + Skin));
        float step = Mathf.Max(0.05f, Mathf.Min(bounds.extents.x, 0.15f));
        int samples = Mathf.Max(1, Mathf.CeilToInt(travel / step));
        for (int sample = 0; sample <= samples; sample++)
        {
            Vector2 point = origin + Vector2.right * (direction * travel * sample / samples);
            if (!HasGround(point, Skin + _edgeCheckDepth, filter))
                return false;
        }
        return true;
    }

    private bool HasWall(int direction, ContactFilter2D filter)
    {
        Vector2 forward = Vector2.right * direction;
        float distance = Mathf.Max(_wallCheckDistance, Mathf.Abs(_airSpeed) * Time.fixedDeltaTime + Skin);
        int count = _collider.Cast(forward, filter, _hits, distance);
        for (int hitIndex = 0; hitIndex < count; hitIndex++)
            if (IsTerrain(_hits[hitIndex]) && Vector2.Dot(_hits[hitIndex].normal, forward) < -0.5f)
                return true;
        if (_wallCheck == null)
            return false;
        Vector2 origin = CheckPosition(_wallCheck, direction, _collider.bounds.center);
        count = gameObject.scene.GetPhysicsScene2D().Raycast(origin, forward, distance, filter, _hits);
        for (int hitIndex = 0; hitIndex < count; hitIndex++)
            if (IsTerrain(_hits[hitIndex]) && Vector2.Dot(_hits[hitIndex].normal, forward) < -0.5f)
                return true;
        return false;
    }

    private Vector2 CheckPosition(Transform check, int direction, Vector2 fallback)
    {
        if (check == null)
            return fallback;
        Vector3 local = transform.InverseTransformPoint(check.position);
        local.x = Mathf.Abs(local.x) * direction;
        return transform.TransformPoint(local);
    }

    private bool HasGround(Vector2 origin, float distance, ContactFilter2D filter)
    {
        int count = gameObject.scene.GetPhysicsScene2D().Raycast(origin, Vector2.down, distance, filter, _hits);
        return HasUpwardSurface(count);
    }

    private bool HasUpwardSurface(int count)
    {
        for (int hitIndex = 0; hitIndex < count; hitIndex++)
            if (IsTerrain(_hits[hitIndex]) && _hits[hitIndex].normal.y > 0.5f)
                return true;
        return false;
    }

    private bool IsTerrain(RaycastHit2D hit)
    {
        return hit.collider != null && hit.collider.attachedRigidbody != _body
            && !hit.collider.transform.IsChildOf(transform);
    }

    private void UpdateVisual()
    {
        if (_visualRenderer == null)
            return;
        _visualRenderer.flipX = _direction < 0;
        int frame;
        if (_grounded || !_jumping)
            frame = Time.time - _landTime < 1f / _animationSpeed ? 5 : 0;
        else if (Time.time - _jumpTime < 1f / _animationSpeed)
            frame = 1;
        else if (Mathf.Abs(_body.linearVelocity.y) < _jumpForce / _body.mass * 0.2f)
            frame = 3;
        else
            frame = _body.linearVelocity.y > 0f ? 2 : 4;
        if (_jumpFrames != null && frame < _jumpFrames.Length && _jumpFrames[frame] != null)
            _visualRenderer.sprite = _jumpFrames[frame];
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        TryContactDamage(collision.collider);
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        TryContactDamage(collision.collider);
    }

    private void TryContactDamage(Collider2D other)
    {
        if (_health.CurrentHp <= 0 || _contactDamage <= 0 || !PlayerColliderUtility.IsPlayerBody(other)
            || Time.time - _lastContactTime < _contactCooldown)
            return;
        PlayerHealth playerHealth = other.GetComponentInParent<PlayerHealth>();
        if (playerHealth == null || playerHealth.IsDead || playerHealth.IsInvincible || playerHealth.IsRespawning)
            return;
        Vector2 direction = (Vector2)other.bounds.center - (Vector2)_collider.bounds.center;
        direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
        playerHealth.TakeDamage(_contactDamage, direction * _contactKnockback);
        _lastContactTime = Time.time;
    }
}
