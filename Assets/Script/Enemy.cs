using UnityEngine;

/// <summary>
/// プレイヤーを追いかける簡易敵。
/// 被弾は EnemyHealth が担当。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(EnemyHealth))]
public class Enemy : MonoBehaviour
{
    [SerializeField, Header("移動速度")]
    private float _moveSpeed = 5f;

    [SerializeField, Header("追跡を止める距離")]
    private float _stopDistance = 0.8f;

    [SerializeField, Header("接触ダメージ")]
    private int _contactDamage = 1;
    [SerializeField] private float _contactKnockback = 5f;
    [SerializeField] private float _contactCooldown = 0.7f;

    [Header("Player Detection")]
    [SerializeField, Min(0f)] private float _detectionRange = 7f;
    [SerializeField, Min(0f)] private float _loseSightDelay = 0.5f;
    [SerializeField] private LayerMask _lineOfSightMask = (1 << 0) | (1 << 3) | (1 << 6);
    [SerializeField] private Transform _eyePoint;
    [SerializeField] private Vector2 _eyeOffset = new Vector2(0f, 0.15f);

    [Header("Visual Animation")]
    [SerializeField] private Animator _visualAnimator;
    [SerializeField] private SpriteRenderer _visualRenderer;
    [SerializeField] private string _isMovingParameter = "IsMoving";

    private Rigidbody2D _rigid;
    private Collider2D _bodyCollider;
    private EnemyHealth _health;
    private Transform _player;
    private Collider2D _playerBodyCollider;
    private float _lastContactDamageTime = -999f;
    private float _sightMemoryRemaining;
    private float _lastSeenPlayerX;
    private int _isMovingHash;

    public bool IsMovingVisual { get; private set; }
    public bool CanSeePlayer { get; private set; }
    public bool IsChasing { get; private set; }

    private void Awake()
    {
        _rigid = GetComponent<Rigidbody2D>();
        _bodyCollider = GetComponent<Collider2D>();
        _health = GetComponent<EnemyHealth>();
        if (_visualAnimator == null)
            _visualAnimator = GetComponentInChildren<Animator>(true);
        if (_visualRenderer == null)
            _visualRenderer = GetComponentInChildren<SpriteRenderer>(true);
        _isMovingHash = Animator.StringToHash(_isMovingParameter);

        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");

        if (playerObject != null)
        {
            _player = playerObject.transform;
            _playerBodyCollider = playerObject.GetComponent<Collider2D>();
            if (_playerBodyCollider == null)
                _playerBodyCollider = playerObject.GetComponentInChildren<Collider2D>();
        }
        else
        {
            Debug.LogWarning("Enemy: Playerタグのオブジェクトが見つかりません。");
        }

        if (_rigid != null)
        {
            _rigid.freezeRotation = true;
            _rigid.angularVelocity = 0f;
        }
    }

    private void FixedUpdate()
    {
        if (_rigid == null)
            return;

        _rigid.freezeRotation = true;
        _rigid.angularVelocity = 0f;

        if (_health != null && _health.IsHitStunned)
        {
            CanSeePlayer = false;
            IsChasing = false;
            SetMovingVisual(false);
            return;
        }

        UpdateSightState();
        SetMovingVisual(IsChasing ? MoveToward(_lastSeenPlayerX) : StopHorizontalMovement());
        TryContactDamageOverlap();
    }

    private void UpdateSightState()
    {
        CanSeePlayer = HasDirectLineOfSight();
        if (CanSeePlayer)
        {
            _lastSeenPlayerX = _player.position.x;
            _sightMemoryRemaining = Mathf.Max(0f, _loseSightDelay);
            IsChasing = true;
            return;
        }

        _sightMemoryRemaining = Mathf.Max(0f, _sightMemoryRemaining - Time.fixedDeltaTime);
        IsChasing = _sightMemoryRemaining > 0f;
    }

    private bool HasDirectLineOfSight()
    {
        if (_player == null || _detectionRange <= 0f)
            return false;

        Vector2 origin = GetEyeWorldPosition();
        Vector2 target = GetPlayerTargetPosition();
        Vector2 toPlayer = target - origin;
        float distance = toPlayer.magnitude;
        if (distance <= 0.001f)
            return true;
        if (distance > _detectionRange)
            return false;

        int playerLayerMask = 1 << _player.gameObject.layer;
        int raycastMask = _lineOfSightMask.value | playerLayerMask;
        RaycastHit2D[] hits = Physics2D.RaycastAll(origin, toPlayer / distance, distance, raycastMask);
        foreach (RaycastHit2D hit in hits)
        {
            Collider2D hitCollider = hit.collider;
            if (hitCollider == null || hitCollider.isTrigger)
                continue;
            if (hitCollider.transform == transform || hitCollider.transform.IsChildOf(transform))
                continue;

            Transform hitTransform = hitCollider.transform;
            if (hitTransform == _player || hitTransform.IsChildOf(_player))
                return true;

            // Playerより先にGround / Walls / Default上のColliderへ当たった。
            return false;
        }

        return false;
    }

    private Vector2 GetEyeWorldPosition()
    {
        if (_eyePoint != null)
            return _eyePoint.position;

        Vector2 center = _bodyCollider != null ? _bodyCollider.bounds.center : (Vector2)transform.position;
        Vector3 scale = transform.lossyScale;
        return center + new Vector2(
            _eyeOffset.x * Mathf.Abs(scale.x),
            _eyeOffset.y * Mathf.Abs(scale.y));
    }

    private Vector2 GetPlayerTargetPosition()
    {
        if (_playerBodyCollider != null)
            return _playerBodyCollider.bounds.center;
        return _player != null ? (Vector2)_player.position : Vector2.zero;
    }

    private bool MoveToward(float targetX)
    {
        if (_player == null || _rigid == null)
            return false;

        float distanceX = targetX - transform.position.x;

        if (Mathf.Abs(distanceX) <= _stopDistance)
        {
            _rigid.linearVelocity = new Vector2(0f, _rigid.linearVelocity.y);
            return false;
        }

        float direction = Mathf.Sign(distanceX);

        _rigid.linearVelocity = new Vector2(
            direction * _moveSpeed,
            _rigid.linearVelocity.y
        );

        // 元絵は左向き。Colliderを持つRootは反転せずVisualだけをflipXする。
        if (_visualRenderer != null && direction != 0f)
            _visualRenderer.flipX = direction > 0f;

        return Mathf.Abs(_rigid.linearVelocity.x) > 0.01f;
    }

    private bool StopHorizontalMovement()
    {
        if (_rigid != null)
            _rigid.linearVelocity = new Vector2(0f, _rigid.linearVelocity.y);
        return false;
    }

    private void SetMovingVisual(bool moving)
    {
        IsMovingVisual = moving;
        if (_visualAnimator != null)
            _visualAnimator.SetBool(_isMovingHash, moving);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        TryContactDamage(collision.collider);
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        TryContactDamage(collision.collider);
    }

    private void TryContactDamageOverlap()
    {
        if (_contactDamage <= 0)
            return;

        Collider2D body = GetComponent<Collider2D>();
        if (body == null)
            return;

        Bounds bounds = body.bounds;
        Vector2 size = bounds.size * 1.05f;

        Collider2D hit = Physics2D.OverlapBox(
            bounds.center,
            size,
            0f,
            LayerMask.GetMask("player"));

        if (hit != null)
            TryContactDamage(hit);
    }

    private void TryContactDamage(Collider2D other)
    {
        if (!PlayerColliderUtility.IsPlayerBody(other))
            return;

        if (_health != null && _health.CurrentHp <= 0)
            return;

        if (Time.time - _lastContactDamageTime < _contactCooldown)
            return;

        PlayerHealth playerHealth = other.GetComponentInParent<PlayerHealth>();

        if (playerHealth == null)
            playerHealth = other.GetComponent<PlayerHealth>();

        if (playerHealth == null || playerHealth.IsDead || playerHealth.IsInvincible)
            return;

        Vector2 knockDir = (Vector2)(other.transform.position - transform.position);

        if (knockDir.sqrMagnitude < 0.0001f)
            knockDir = Vector2.right;
        else
            knockDir.Normalize();

        playerHealth.TakeDamage(_contactDamage, knockDir * _contactKnockback);
        _lastContactDamageTime = Time.time;
    }

    private void OnDrawGizmosSelected()
    {
        Vector2 origin;
        if (_eyePoint != null)
        {
            origin = _eyePoint.position;
        }
        else
        {
            Collider2D body = _bodyCollider != null ? _bodyCollider : GetComponent<Collider2D>();
            Vector2 center = body != null ? body.bounds.center : (Vector2)transform.position;
            Vector3 scale = transform.lossyScale;
            origin = center + new Vector2(
                _eyeOffset.x * Mathf.Abs(scale.x),
                _eyeOffset.y * Mathf.Abs(scale.y));
        }

        Gizmos.color = new Color(1f, 0.72f, 0.2f, 0.8f);
        Gizmos.DrawWireSphere(origin, Mathf.Max(0f, _detectionRange));

        if (_player == null)
            return;

        Gizmos.color = CanSeePlayer ? Color.green : Color.red;
        Gizmos.DrawLine(origin, GetPlayerTargetPosition());
    }
}
