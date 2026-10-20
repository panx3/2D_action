using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>ネジネズミのヒット＆アウェイ。被弾の物理とHPはEnemyHealthが所有する。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D), typeof(EnemyHealth))]
public class Enemy : MonoBehaviour
{
    public enum ActionState { Idle, Approach, Windup, Dash, Brake, Retreat, Recovery }

    [SerializeField, Min(0f), Header("Approach Speed")] private float _moveSpeed = 5f;
    [SerializeField, Min(0f), InspectorName("Attack Range"), Tooltip("自分とPlayerのCollider端の水平間隔。既存の停止距離を再利用。")]
    private float _stopDistance = 0.8f;
    [Header("Contact Damage (Dash only)")]
    [SerializeField] private int _contactDamage = 1;
    [SerializeField] private float _contactKnockback = 5f;
    [SerializeField] private float _contactCooldown = 0.7f;

    [Header("Player Detection")]
    [SerializeField, Min(0f)] private float _detectionRange = 7f;
    [SerializeField, Min(0f)] private float _loseSightDelay = 0.5f;
    [SerializeField] private LayerMask _lineOfSightMask = (1 << 0) | (1 << 3) | (1 << 6);
    [SerializeField] private Transform _eyePoint;
    [SerializeField] private Vector2 _eyeOffset = new Vector2(0f, 0.15f);

    [Header("Hit And Away")]
    [SerializeField, Min(0.05f)] private float _windupDuration = 0.4f;
    [SerializeField, Min(0.1f)] private float _dashSpeed = 11f;
    [SerializeField, Min(0.1f)] private float _dashDistance = 3f;
    [SerializeField, Min(0.02f)] private float _brakeDuration = 0.12f;
    [SerializeField, Min(0.1f)] private float _retreatSpeed = 3.5f;
    [SerializeField, Min(0f)] private float _retreatDistance = 1.1f;
    [SerializeField, Tooltip("反撃待機秒数。個体ごとに抽選し、集団の攻撃タイミングをずらす。")]
    private Vector2 _recoveryDuration = new Vector2(0.95f, 1.3f);
    [Header("Terrain Safety")]
    [SerializeField] private LayerMask _terrainLayers = (1 << 0) | (1 << 6);
    [SerializeField, Min(0.01f), Tooltip("この高さを超える下り段差は崖として扱う。")]
    private float _groundDetectionDepth = 0.18f;

    [Header("Visual Animation")]
    [SerializeField] private Animator _visualAnimator;
    [SerializeField] private SpriteRenderer _visualRenderer;
    [SerializeField] private string _isMovingParameter = "IsMoving";

    private const float Skin = 0.025f;
    private const float GroundContactDistance = 0.05f;
    private readonly RaycastHit2D[] _hits = new RaycastHit2D[24];
    private readonly Collider2D[] _overlaps = new Collider2D[8];
    private Rigidbody2D _rigid;
    private Collider2D _bodyCollider;
    private EnemyHealth _health;
    private Transform _player;
    private PlayerHealth _playerHealth;
    private Collider2D _playerBodyCollider;
    private ContactFilter2D _terrainFilter;
    private ContactFilter2D _playerFilter;
    private float _lastContactDamageTime = float.NegativeInfinity;
    private float _sightMemoryRemaining;
    private float _lastSeenPlayerX;
    private int _isMovingHash;
    private int _attackDirection = -1;
    private int _retreatDirection;
    private float _stateStartX;
    private float _stateLimit;
    private float _attackReadyTime;
    private bool _dashHit;
    private System.Random _timingRandom;

    public bool IsMovingVisual { get; private set; }
    public bool CanSeePlayer { get; private set; }
    public bool IsChasing { get; private set; }
    public ActionState State { get; private set; }
    public float StateElapsed { get; private set; }
    public float WindupDuration => Mathf.Max(0.05f, _windupDuration);
    public int AttackDirection => _attackDirection;

    private void Awake()
    {
        _rigid = GetComponent<Rigidbody2D>();
        _bodyCollider = GetComponent<Collider2D>();
        _health = GetComponent<EnemyHealth>();
        if (_visualAnimator == null) _visualAnimator = GetComponentInChildren<Animator>(true);
        if (_visualRenderer == null) _visualRenderer = GetComponentInChildren<SpriteRenderer>(true);
        _isMovingHash = Animator.StringToHash(_isMovingParameter);
        _timingRandom = new System.Random(GetInstanceID());
        _terrainFilter = new ContactFilter2D { useTriggers = false };
        _terrainFilter.SetLayerMask(_terrainLayers);
        _playerFilter = new ContactFilter2D { useTriggers = false };
        _playerFilter.SetLayerMask(LayerMask.GetMask("player"));
        FindPlayer();
        _rigid.freezeRotation = true;
        _rigid.angularVelocity = 0f;
        _health.HitConfirmed += OnHitConfirmed;
    }

    private void OnEnable()
    {
        // リスポーン/再有効化時に前回の突進・命中済み情報を持ち越さない。
        _sightMemoryRemaining = 0f;
        _lastContactDamageTime = float.NegativeInfinity;
        _dashHit = false;
        CanSeePlayer = IsChasing = false;
        _attackReadyTime = Time.time + (float)_timingRandom.NextDouble() * 0.25f;
        SetState(ActionState.Idle);
    }

    private void OnDisable()
    {
        CanSeePlayer = IsChasing = false;
        SetMovingVisual(false);
    }

    private void OnDestroy() { if (_health != null) _health.HitConfirmed -= OnHitConfirmed; }

    private void FindPlayer()
    {
        GameObject target = GameObject.FindGameObjectWithTag("Player");
        if (target == null) return;
        _player = target.transform;
        _playerHealth = target.GetComponent<PlayerHealth>();
        _playerBodyCollider = target.GetComponent<Collider2D>();
        if (_playerBodyCollider == null) _playerBodyCollider = target.GetComponentInChildren<Collider2D>();
    }

    private void FixedUpdate()
    {
        if (_health.IsDeathHandled) return;
        if (_health.IsHitStunned)
        {
            // EnemyHealthのノックバックに移動速度を書き戻さない。
            SetMovingVisual(false);
            return;
        }
        if (_player == null) FindPlayer();
        UpdateSightState();
        StateElapsed += Time.fixedDeltaTime;
        bool grounded = IsGrounded();
        float speed = 0f;
        switch (State)
        {
            case ActionState.Idle:
                if (IsChasing) SetState(ActionState.Approach);
                break;
            case ActionState.Approach:
                if (!IsChasing) { SetState(ActionState.Idle); break; }
                if (CanSeePlayer && IsAtAttackHeight() && PlayerGap() <= _stopDistance && grounded
                    && Time.time >= _attackReadyTime)
                {
                    _attackDirection = DirectionTo(_playerBodyCollider != null
                        ? _playerBodyCollider.bounds.center.x : _player.position.x);
                    Face(_attackDirection);
                    SetState(ActionState.Windup);
                    break;
                }
                int approachDirection = DirectionTo(_lastSeenPlayerX);
                if (Mathf.Abs(_lastSeenPlayerX - _bodyCollider.bounds.center.x) > _stopDistance)
                {
                    Face(approachDirection);
                    if (grounded && CanMove(approachDirection, _moveSpeed)) speed = approachDirection * _moveSpeed;
                }
                break;
            case ActionState.Windup:
                if (!grounded || !TargetAvailable()) { SetState(ActionState.Recovery); break; }
                if (StateElapsed >= WindupDuration) SetState(ActionState.Dash);
                break;
            case ActionState.Dash:
                if (!grounded || _dashHit || !TargetAvailable()
                    || Travelled() >= _dashDistance || StateElapsed >= _stateLimit
                    || !CanMove(_attackDirection, _dashSpeed))
                    SetState(ActionState.Brake);
                else speed = _attackDirection * _dashSpeed;
                break;
            case ActionState.Brake:
                if (StateElapsed >= Mathf.Max(0.02f, _brakeDuration))
                {
                    // 現在のPlayerから離れる側を一度だけ選ぶ。後退中も向き直りを繰り返さない。
                    _retreatDirection = TargetAvailable() ? -DirectionTo(GetPlayerTargetPosition().x) : -_attackDirection;
                    if (TargetAvailable()) Face(-_retreatDirection);
                    SetState(ActionState.Retreat);
                }
                break;
            case ActionState.Retreat:
                if (!grounded || Travelled() >= _retreatDistance || StateElapsed >= _stateLimit
                    || (TargetAvailable() && PlayerGap() >= _stopDistance + _retreatDistance)
                    || !CanMove(_retreatDirection, _retreatSpeed))
                    SetState(ActionState.Recovery);
                else speed = _retreatDirection * _retreatSpeed;
                break;
            case ActionState.Recovery:
                if (StateElapsed >= _stateLimit) SetState(IsChasing ? ActionState.Approach : ActionState.Idle);
                break;
        }
        _rigid.linearVelocity = new Vector2(speed, _rigid.linearVelocity.y);
        SetMovingVisual(Mathf.Abs(speed) > 0.01f);
        if (State == ActionState.Dash) TryContactDamageOverlap();
    }

    private void SetState(ActionState state)
    {
        State = state;
        StateElapsed = 0f;
        _stateStartX = _rigid.position.x;
        SetMovingVisual(false);
        if (state == ActionState.Dash)
        {
            _dashHit = false;
            _stateLimit = _dashDistance / Mathf.Max(0.1f, _dashSpeed) + 0.15f;
        }
        else if (state == ActionState.Retreat)
            _stateLimit = _retreatDistance / Mathf.Max(0.1f, _retreatSpeed) + 0.15f;
        else if (state == ActionState.Recovery)
        {
            float min = Mathf.Max(0.1f, Mathf.Min(_recoveryDuration.x, _recoveryDuration.y));
            float max = Mathf.Max(min, Mathf.Max(_recoveryDuration.x, _recoveryDuration.y));
            _stateLimit = Mathf.Lerp(min, max, (float)_timingRandom.NextDouble());
        }
    }

    private void OnHitConfirmed(MorningStarHitContext context, bool lethal)
    {
        if (!isActiveAndEnabled) return;
        _dashHit = true;
        // 自分が書いた水平駆動を解除。続いてEnemyHealthが既存のImpulseを適用する。
        if (State == ActionState.Dash || State == ActionState.Approach || State == ActionState.Retreat)
            _rigid.linearVelocity = new Vector2(0f, _rigid.linearVelocity.y);
        SetState(ActionState.Recovery);
    }

    private float Travelled() => Mathf.Abs(_rigid.position.x - _stateStartX);
    private int DirectionTo(float x) => x >= _bodyCollider.bounds.center.x ? 1 : -1;
    private void Face(int direction) { if (_visualRenderer != null) _visualRenderer.flipX = direction > 0; }
    private bool TargetAvailable() => _player != null && _player.gameObject.activeInHierarchy
        && (_playerHealth == null || (!_playerHealth.IsDead && !_playerHealth.IsRespawning));

    private float PlayerGap()
    {
        if (_playerBodyCollider == null) return _player != null
            ? Mathf.Abs(_player.position.x - _bodyCollider.bounds.center.x) : float.PositiveInfinity;
        Bounds self = _bodyCollider.bounds, target = _playerBodyCollider.bounds;
        return Mathf.Max(0f, Mathf.Abs(target.center.x - self.center.x) - self.extents.x - target.extents.x);
    }

    private bool IsAtAttackHeight()
    {
        if (_playerBodyCollider == null) return Mathf.Abs(_player.position.y - _bodyCollider.bounds.center.y) < 0.8f;
        Bounds self = _bodyCollider.bounds, target = _playerBodyCollider.bounds;
        return target.min.y < self.max.y && target.max.y > self.min.y;
    }

    private bool IsGrounded()
    {
        if (_rigid.linearVelocity.y > 0.15f) return false;
        int count = _bodyCollider.Cast(Vector2.down, _terrainFilter, _hits, GroundContactDistance);
        for (int i = 0; i < count; i++)
            if (IsTerrain(_hits[i]) && _hits[i].normal.y > 0.5f) return true;
        return false;
    }

    private bool CanMove(int direction, float speed)
    {
        Vector2 forward = Vector2.right * direction;
        float step = Mathf.Max(0f, speed) * Time.fixedDeltaTime + Skin;
        int count = _bodyCollider.Cast(forward, _terrainFilter, _hits, step);
        for (int i = 0; i < count; i++)
            if (IsTerrain(_hits[i]) && Vector2.Dot(_hits[i].normal, forward) < -0.5f) return false;
        Bounds bounds = _bodyCollider.bounds;
        Vector2 origin = new Vector2(bounds.center.x + direction * (bounds.extents.x + step),
            bounds.min.y + GroundContactDistance);
        count = gameObject.scene.GetPhysicsScene2D().Raycast(origin, Vector2.down,
            GroundContactDistance + _groundDetectionDepth, _terrainFilter, _hits);
        for (int i = 0; i < count; i++)
            if (IsTerrain(_hits[i]) && _hits[i].normal.y > 0.5f) return true;
        return false;
    }

    private bool IsTerrain(RaycastHit2D hit) => hit.collider != null
        && hit.collider.attachedRigidbody != _rigid && !hit.collider.transform.IsChildOf(transform);

    private void UpdateSightState()
    {
        CanSeePlayer = TargetAvailable() && HasDirectLineOfSight();
        if (CanSeePlayer)
        {
            _lastSeenPlayerX = GetPlayerTargetPosition().x;
            _sightMemoryRemaining = Mathf.Max(0f, _loseSightDelay);
            IsChasing = true;
        }
        else
        {
            _sightMemoryRemaining = TargetAvailable() ? Mathf.Max(0f, _sightMemoryRemaining - Time.fixedDeltaTime) : 0f;
            IsChasing = _sightMemoryRemaining > 0f;
        }
    }

    private bool HasDirectLineOfSight()
    {
        if (_player == null || _detectionRange <= 0f) return false;
        Vector2 origin = GetEyeWorldPosition(), toPlayer = GetPlayerTargetPosition() - origin;
        float distance = toPlayer.magnitude;
        if (distance <= 0.001f) return true;
        if (distance > _detectionRange) return false;
        int mask = _lineOfSightMask.value | (1 << _player.gameObject.layer);
        var filter = new ContactFilter2D { useTriggers = false };
        filter.SetLayerMask(mask);
        int count = gameObject.scene.GetPhysicsScene2D().Raycast(origin, toPlayer / distance, distance, filter, _hits);
        float nearest = float.PositiveInfinity;
        Collider2D first = null;
        for (int i = 0; i < count; i++)
        {
            var hit = _hits[i];
            if (hit.collider == null || hit.collider.attachedRigidbody == _rigid
                || hit.collider.transform.IsChildOf(transform) || hit.distance >= nearest) continue;
            nearest = hit.distance;
            first = hit.collider;
        }
        return first != null && (first.transform == _player || first.transform.IsChildOf(_player));
    }

    private Vector2 GetEyeWorldPosition()
    {
        if (_eyePoint != null) return _eyePoint.position;
        Collider2D body = _bodyCollider != null ? _bodyCollider : GetComponent<Collider2D>();
        Vector2 center = body != null ? body.bounds.center : (Vector2)transform.position;
        Vector3 scale = transform.lossyScale;
        return center + new Vector2(_eyeOffset.x * Mathf.Abs(scale.x), _eyeOffset.y * Mathf.Abs(scale.y));
    }

    private Vector2 GetPlayerTargetPosition() => _playerBodyCollider != null ? (Vector2)_playerBodyCollider.bounds.center
        : _player != null ? (Vector2)_player.position : Vector2.zero;

    private void SetMovingVisual(bool moving)
    {
        IsMovingVisual = moving;
        if (_visualAnimator != null) _visualAnimator.SetBool(_isMovingHash, moving);
    }

    private void OnCollisionEnter2D(Collision2D collision) => TryContactDamage(collision.collider);
    private void OnCollisionStay2D(Collision2D collision) => TryContactDamage(collision.collider);

    private void TryContactDamageOverlap()
    {
        int count = _bodyCollider.Overlap(_playerFilter, _overlaps);
        for (int i = 0; i < count; i++) TryContactDamage(_overlaps[i]);
    }

    private void TryContactDamage(Collider2D other)
    {
        if (!isActiveAndEnabled || State != ActionState.Dash || _dashHit || _contactDamage <= 0
            || _health.CurrentHp <= 0 || _health.IsHitStunned || !PlayerColliderUtility.IsPlayerBody(other)
            || Time.time - _lastContactDamageTime < _contactCooldown) return;
        PlayerHealth health = other.GetComponentInParent<PlayerHealth>();
        if (health == null || health.IsDead || health.IsRespawning || health.IsInvincible) return;
        Vector2 direction = (Vector2)other.bounds.center - (Vector2)_bodyCollider.bounds.center;
        direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right * _attackDirection;
        int hp = health.CurrentHp;
        health.TakeDamage(_contactDamage, direction * _contactKnockback);
        if (health.CurrentHp >= hp) return;
        _lastContactDamageTime = Time.time;
        _dashHit = true;
        SetState(ActionState.Brake);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.72f, 0.2f, 0.8f);
        Gizmos.DrawWireSphere(GetEyeWorldPosition(), Mathf.Max(0f, _detectionRange));
        if (_player == null) return;
        Gizmos.color = CanSeePlayer ? Color.green : Color.red;
        Gizmos.DrawLine(GetEyeWorldPosition(), GetPlayerTargetPosition());
    }
}
