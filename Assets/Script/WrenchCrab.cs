using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>壁・崖で折り返す巡回と身体接触。被弾・撃破はEnemyHealthが担当。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
[RequireComponent(typeof(EnemyHealth))]
public sealed class WrenchCrab : MonoBehaviour
{
    public enum InitialDirection { Left = -1, Right = 1 }

    [SerializeField, Min(0f)] private float _moveSpeed = 1.5f;
    [SerializeField] private InitialDirection _initialDirection = InitialDirection.Left;
    [SerializeField, Min(0.01f)] private float _wallDetectionDistance = 0.08f;
    [Tooltip("Collider前端から先の足場を確認する距離。高速時は1物理フレーム分を自動で確保します。")]
    [SerializeField, Min(0.01f)] private float _edgeDetectionDistance = 0.08f;
    [SerializeField, Min(0.01f)] private float _groundDetectionDepth = 0.15f;
    [SerializeField] private LayerMask _terrainLayers = (1 << 0) | (1 << 6);
    [SerializeField] private SpriteRenderer _visualRenderer;
    [SerializeField] private Animator _visualAnimator;

    [Header("Contact Damage")]
    [SerializeField, Min(0)] private int _contactDamage = 1;
    [SerializeField, Min(0f)] private float _contactKnockback = 5f;
    [SerializeField, Min(0f)] private float _contactCooldown = 0.7f;

    private const float Skin = 0.02f;
    private const float GroundContactDistance = 0.04f;
    private readonly RaycastHit2D[] _hits = new RaycastHit2D[16];
    private Rigidbody2D _body;
    private BoxCollider2D _collider;
    private int _direction;
    private EnemyHealth _health;
    private float _lastContactDamageTime = float.NegativeInfinity;

    private void Awake()
    {
        _body = GetComponent<Rigidbody2D>();
        _collider = GetComponent<BoxCollider2D>();
        _health = GetComponent<EnemyHealth>();
        if (_visualRenderer == null)
            _visualRenderer = GetComponentInChildren<SpriteRenderer>();
        if (_visualAnimator == null)
            _visualAnimator = GetComponentInChildren<Animator>();
        _direction = _initialDirection == InitialDirection.Right ? 1 : -1;
        UpdateVisual(false);
    }

    private void FixedUpdate()
    {
        if (_health != null && (_health.IsDeathHandled || _health.IsHitStunned))
        {
            // 被弾中の速度はEnemyHealthに任せ、巡回でノックバックを上書きしない。
            UpdateVisual(false);
            return;
        }

        ContactFilter2D filter = new ContactFilter2D();
        filter.SetLayerMask(_terrainLayers);
        filter.useTriggers = false;

        bool grounded = _body.linearVelocity.y <= 0.1f && IsGrounded(filter);
        float speed = 0f;
        if (grounded)
        {
            if (CanWalk(_direction, filter))
                speed = _direction * _moveSpeed;
            else if (CanWalk(-_direction, filter))
            {
                // 両方向とも危険な場合は反転しないため、狭い足場でも振動しない。
                _direction = -_direction;
                speed = _direction * _moveSpeed;
            }
        }

        // 垂直速度と重力は物理エンジンに任せ、空中では巡回を開始しない。
        _body.linearVelocity = new Vector2(speed, _body.linearVelocity.y);
        UpdateVisual(Mathf.Abs(speed) > 0.001f);
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
        if (_contactDamage <= 0 || !PlayerColliderUtility.IsPlayerBody(other)
            || (_health != null && _health.CurrentHp <= 0)
            || Time.time - _lastContactDamageTime < _contactCooldown)
            return;

        PlayerHealth playerHealth = other.GetComponentInParent<PlayerHealth>();
        if (playerHealth == null || playerHealth.IsDead || playerHealth.IsInvincible)
            return;

        Vector2 direction = (Vector2)other.bounds.center - (Vector2)_collider.bounds.center;
        direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
        playerHealth.TakeDamage(_contactDamage, direction * _contactKnockback);
        _lastContactDamageTime = Time.time;
    }

    private bool IsGrounded(ContactFilter2D filter)
    {
        int count = _collider.Cast(Vector2.down, filter, _hits, GroundContactDistance);
        for (int i = 0; i < count; i++)
            if (IsTerrain(_hits[i]) && _hits[i].normal.y > 0.5f)
                return true;
        return false;
    }

    private bool CanWalk(int direction, ContactFilter2D filter)
    {
        float step = _moveSpeed * Time.fixedDeltaTime + Skin;
        Vector2 forward = Vector2.right * direction;
        int count = _collider.Cast(forward, filter, _hits, Mathf.Max(_wallDetectionDistance, step));
        for (int i = 0; i < count; i++)
            if (IsTerrain(_hits[i]) && Vector2.Dot(_hits[i].normal, forward) < -0.5f)
                return false;

        Bounds bounds = _collider.bounds;
        Vector2 origin = new Vector2(
            bounds.center.x + direction * (bounds.extents.x + Mathf.Max(_edgeDetectionDistance, step)),
            bounds.min.y + GroundContactDistance);
        count = gameObject.scene.GetPhysicsScene2D().Raycast(origin, Vector2.down, GroundContactDistance + _groundDetectionDepth, filter, _hits);
        for (int i = 0; i < count; i++)
            if (IsTerrain(_hits[i]) && _hits[i].normal.y > 0.5f)
                return true;
        return false;
    }

    private bool IsTerrain(RaycastHit2D hit)
    {
        return hit.collider != null && hit.collider.attachedRigidbody != _body
            && !hit.collider.transform.IsChildOf(transform);
    }

    private void UpdateVisual(bool moving)
    {
        if (_visualRenderer != null)
            _visualRenderer.flipX = _direction > 0;
        if (_visualAnimator != null)
            _visualAnimator.speed = moving ? 1f : 0f;
    }
}


