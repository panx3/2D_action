using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>レンチカニ。安全な巡回・低速接近から、足を止めて前方を叩く近接制圧型。</summary>
[DisallowMultipleComponent]
[SelectionBase]
[RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
[RequireComponent(typeof(EnemyHealth))]
public sealed class WrenchCrab : MonoBehaviour
{
    public enum InitialDirection { Left = -1, Right = 1 }
    public enum ActionState { Patrol, Approach, Brace, Slam, Recovery }

    [SerializeField, Min(0f)] private float _moveSpeed = 1.5f;
    [SerializeField] private InitialDirection _initialDirection = InitialDirection.Left;
    [SerializeField, Min(0.01f)] private float _wallDetectionDistance = 0.08f;
    [Tooltip("Collider前端から先の足場を確認する距離。1物理フレーム分を自動で確保します。")]
    [SerializeField, Min(0.01f)] private float _edgeDetectionDistance = 0.08f;
    [SerializeField, Min(0.01f)] private float _groundDetectionDepth = 0.15f;
    [SerializeField] private LayerMask _terrainLayers = (1 << 0) | (1 << 6);
    [SerializeField] private SpriteRenderer _visualRenderer;
    [SerializeField] private Animator _visualAnimator;

    [Header("Ground Slam")]
    [SerializeField, Min(0f)] private float _detectionRange = 5.5f;
    [SerializeField, Min(0.1f), Tooltip("胴体判定の前端から先へのリーチ。Rootの拡縮に追従。")]
    private float _attackReach = 1.35f;
    [SerializeField, Min(0.1f), Tooltip("地面からこの高さまでを叩く。ジャンプで回避可能。")]
    private float _attackHeight = 0.75f;
    [SerializeField, Min(0.1f)] private float _windupDuration = 0.65f;
    [SerializeField, Min(0.02f)] private float _slamDuration = 0.18f;
    [SerializeField, Min(0.1f)] private float _recoveryDuration = 1.15f;

    [Header("Claw Damage (existing contact values)")]
    [SerializeField, Min(0), InspectorName("Attack Damage")] private int _contactDamage = 1;
    [SerializeField, Min(0f), InspectorName("Attack Knockback")] private float _contactKnockback = 5f;
    [SerializeField, Min(0f)] private float _contactCooldown = 0.7f;

    private const float Skin = 0.02f;
    private const float GroundContactDistance = 0.04f;
    private readonly RaycastHit2D[] _hits = new RaycastHit2D[24];
    private readonly Collider2D[] _targets = new Collider2D[8];
    private Rigidbody2D _body;
    private BoxCollider2D _collider;
    private int _direction;
    private EnemyHealth _health;
    private Transform _player;
    private Collider2D _playerCollider;
    private PlayerHealth _playerHealth;
    private ContactFilter2D _terrainFilter, _sightFilter, _playerFilter;
    private float _lastContactDamageTime = float.NegativeInfinity;
    private float _recoveryLimit;
    private bool _attackHit;
    private System.Random _timing;

    public ActionState State { get; private set; }
    public float StateElapsed { get; private set; }
    public bool IsMovingVisual { get; private set; }
    public bool CanSeePlayer { get; private set; }
    public int Facing => _direction;
    public float WindupDuration => Mathf.Max(0.1f, _windupDuration);
    public event System.Action Slammed;

    private void Awake()
    {
        _body = GetComponent<Rigidbody2D>();
        _collider = GetComponent<BoxCollider2D>();
        _health = GetComponent<EnemyHealth>();
        if (_visualRenderer == null) _visualRenderer = GetComponentInChildren<SpriteRenderer>();
        if (_visualAnimator == null) _visualAnimator = GetComponentInChildren<Animator>();
        _terrainFilter = new ContactFilter2D { useTriggers = false };
        _terrainFilter.SetLayerMask(_terrainLayers);
        _sightFilter = _terrainFilter;
        _sightFilter.SetLayerMask(_terrainLayers.value | LayerMask.GetMask("player"));
        _playerFilter = new ContactFilter2D { useTriggers = false };
        _playerFilter.SetLayerMask(LayerMask.GetMask("player"));
        _timing = new System.Random(GetInstanceID());
        _health.HitConfirmed += OnHitConfirmed;
        FindPlayer();
    }

    private void OnEnable()
    {
        _direction = _initialDirection == InitialDirection.Right ? 1 : -1;
        _attackHit = false;
        _lastContactDamageTime = float.NegativeInfinity;
        CanSeePlayer = false;
        SetState(ActionState.Patrol);
    }

    private void OnDisable() { CanSeePlayer = false; UpdateVisual(false); }
    private void OnDestroy() { if (_health != null) _health.HitConfirmed -= OnHitConfirmed; }

    private void FindPlayer()
    {
        var target = GameObject.FindGameObjectWithTag("Player");
        if (target == null) return;
        _player = target.transform;
        _playerCollider = target.GetComponent<Collider2D>();
        if (_playerCollider == null) _playerCollider = target.GetComponentInChildren<Collider2D>();
        _playerHealth = target.GetComponent<PlayerHealth>();
    }

    private void FixedUpdate()
    {
        if (_health.IsDeathHandled) { UpdateVisual(false); return; }
        if (_health.IsHitStunned)
        {
            // ノックバック中は既存EnemyHealthの速度を上書きしない。
            UpdateVisual(false);
            return;
        }
        if (_player == null) FindPlayer();
        CanSeePlayer = TargetAvailable() && CanSeeTarget();
        StateElapsed += Time.fixedDeltaTime;
        bool grounded = _body.linearVelocity.y <= 0.1f && IsGrounded(_terrainFilter);
        float speed = 0f;
        switch (State)
        {
            case ActionState.Patrol:
                if (CanSeePlayer) { SetState(ActionState.Approach); break; }
                if (grounded)
                {
                    if (CanWalk(_direction, _terrainFilter)) speed = _direction * _moveSpeed;
                    else if (CanWalk(-_direction, _terrainFilter))
                    {
                        _direction = -_direction;
                        speed = _direction * _moveSpeed;
                    }
                }
                break;
            case ActionState.Approach:
                if (!CanSeePlayer) { SetState(ActionState.Patrol); break; }
                float dx = _playerCollider.bounds.center.x - _collider.bounds.center.x;
                if (Mathf.Abs(dx) > 0.02f) _direction = dx > 0f ? 1 : -1;
                float gap = Mathf.Max(0f, Mathf.Abs(dx) - _collider.bounds.extents.x - _playerCollider.bounds.extents.x);
                float playerFeet = _playerCollider.bounds.min.y - _collider.bounds.min.y;
                if (grounded && gap <= WorldReach * 0.85f
                    && playerFeet < WorldAttackHeight && _playerCollider.bounds.max.y > _collider.bounds.min.y)
                    SetState(ActionState.Brace);
                else if (grounded && Mathf.Abs(dx) > 0.15f && CanWalk(_direction, _terrainFilter))
                    speed = _direction * _moveSpeed;
                break;
            case ActionState.Brace:
                // 構え開始時の方向を保持。横を抜けたPlayerを追って向き直らない。
                if (!grounded || !TargetAvailable()) SetState(ActionState.Recovery);
                else if (StateElapsed >= WindupDuration) SetState(ActionState.Slam);
                break;
            case ActionState.Slam:
                if (!grounded || !TargetAvailable()) SetState(ActionState.Recovery);
                else if (StateElapsed >= Mathf.Max(0.02f, _slamDuration)) SetState(ActionState.Recovery);
                else TrySlamDamage();
                break;
            case ActionState.Recovery:
                // 空振り・命中・被弾中断のいずれも、その場で反撃できる時間を設ける。
                if (StateElapsed >= _recoveryLimit) SetState(CanSeePlayer ? ActionState.Approach : ActionState.Patrol);
                break;
        }
        _body.linearVelocity = new Vector2(speed, _body.linearVelocity.y);
        UpdateVisual(Mathf.Abs(speed) > 0.001f);
    }

    private bool TargetAvailable() => _player != null && _playerCollider != null && _player.gameObject.activeInHierarchy
        && (_playerHealth == null || (!_playerHealth.IsDead && !_playerHealth.IsRespawning));
    private float WorldReach => Mathf.Max(0.1f, _attackReach * Mathf.Abs(transform.lossyScale.x));
    private float WorldAttackHeight => Mathf.Max(0.1f, _attackHeight * Mathf.Abs(transform.lossyScale.y));

    private Vector2 Eye => new Vector2(_collider.bounds.center.x,
        _collider.bounds.min.y + Mathf.Min(_collider.bounds.size.y * 0.65f, WorldAttackHeight * 0.65f));

    private bool CanSeeTarget()
    {
        Vector2 target = _playerCollider.ClosestPoint(Eye);
        return Vector2.Distance(Eye, target) <= _detectionRange && HasClearPath(target);
    }

    private bool HasClearPath(Vector2 target)
    {
        Vector2 origin = Eye, delta = target - origin;
        float distance = delta.magnitude;
        if (distance < 0.001f) return true;
        int count = gameObject.scene.GetPhysicsScene2D().Raycast(origin, delta / distance,
            distance + Skin, _sightFilter, _hits);
        float nearest = float.PositiveInfinity;
        Collider2D first = null;
        for (int i = 0; i < count; i++)
        {
            var hit = _hits[i];
            if (hit.collider == null || hit.collider.attachedRigidbody == _body
                || hit.collider.transform.IsChildOf(transform) || hit.distance >= nearest) continue;
            nearest = hit.distance;
            first = hit.collider;
        }
        return first == null || first.transform == _player || first.transform.IsChildOf(_player);
    }

    private void SetState(ActionState state)
    {
        State = state;
        StateElapsed = 0f;
        UpdateVisual(false);
        if (state == ActionState.Brace) _attackHit = false;
        if (state == ActionState.Recovery)
            _recoveryLimit = Mathf.Max(0.1f, _recoveryDuration) + (float)_timing.NextDouble() * 0.15f;
        if (state == ActionState.Slam) Slammed?.Invoke();
    }

    private void OnHitConfirmed(MorningStarHitContext context, bool lethal)
    {
        if (!isActiveAndEnabled) return;
        if (IsMovingVisual) _body.linearVelocity = new Vector2(0f, _body.linearVelocity.y);
        _attackHit = true;
        SetState(ActionState.Recovery);
    }

    public Bounds GetAttackBounds()
    {
        Bounds bounds = _collider.bounds;
        float reach = WorldReach;
        Vector2 forward = Vector2.right * _direction;
        int count = _collider.Cast(forward, _terrainFilter, _hits, reach);
        for (int i = 0; i < count; i++)
            if (IsTerrain(_hits[i]) && Vector2.Dot(_hits[i].normal, forward) < -0.5f)
                reach = Mathf.Min(reach, Mathf.Max(0f, _hits[i].distance - Skin));
        return new Bounds(new Vector3(bounds.center.x + _direction * (bounds.extents.x + reach * 0.5f),
            bounds.min.y + WorldAttackHeight * 0.5f, transform.position.z), new Vector3(reach, WorldAttackHeight, 1f));
    }

    public bool TryGetAttackGround(float x, out Vector2 point)
    {
        Vector2 origin = new Vector2(x, _collider.bounds.min.y + 0.12f);
        int count = gameObject.scene.GetPhysicsScene2D().Raycast(origin, Vector2.down, 0.32f, _terrainFilter, _hits);
        float closest = float.PositiveInfinity;
        point = default;
        for (int i = 0; i < count; i++)
        {
            if (!IsTerrain(_hits[i]) || _hits[i].normal.y < 0.5f || _hits[i].distance <= 0f || _hits[i].distance >= closest) continue;
            closest = _hits[i].distance;
            point = _hits[i].point + Vector2.up * 0.035f;
        }
        return !float.IsPositiveInfinity(closest);
    }

    private void TrySlamDamage()
    {
        Bounds area = GetAttackBounds();
        if (area.size.x <= Skin) return;
        int count = gameObject.scene.GetPhysicsScene2D().OverlapBox(area.center, area.size, 0f, _playerFilter, _targets);
        for (int i = 0; i < count; i++) TryContactDamage(_targets[i]);
    }

    private void OnCollisionEnter2D(Collision2D collision) => TryContactDamage(collision.collider);
    private void OnCollisionStay2D(Collision2D collision) => TryContactDamage(collision.collider);

    private void TryContactDamage(Collider2D other)
    {
        if (!isActiveAndEnabled || State != ActionState.Slam || _attackHit || _health.IsHitStunned
            || _health.CurrentHp <= 0 || _contactDamage <= 0 || !PlayerColliderUtility.IsPlayerBody(other)
            || Time.time - _lastContactDamageTime < _contactCooldown || !GetAttackBounds().Intersects(other.bounds)) return;
        PlayerHealth playerHealth = other.GetComponentInParent<PlayerHealth>();
        if (playerHealth == null || playerHealth.IsDead || playerHealth.IsInvincible || playerHealth.IsRespawning
            || !HasClearPath(other.ClosestPoint(Eye))) return;
        int before = playerHealth.CurrentHp;
        playerHealth.TakeDamage(_contactDamage, new Vector2(_direction, 0.2f).normalized * _contactKnockback);
        if (playerHealth.CurrentHp >= before) return;
        _attackHit = true;
        _lastContactDamageTime = Time.time;
    }

    private bool IsGrounded(ContactFilter2D filter)
    {
        int count = _collider.Cast(Vector2.down, filter, _hits, GroundContactDistance);
        for (int i = 0; i < count; i++)
            if (IsTerrain(_hits[i]) && _hits[i].normal.y > 0.5f) return true;
        return false;
    }

    private bool CanWalk(int direction, ContactFilter2D filter)
    {
        float step = _moveSpeed * Time.fixedDeltaTime + Skin;
        Vector2 forward = Vector2.right * direction;
        int count = _collider.Cast(forward, filter, _hits, Mathf.Max(_wallDetectionDistance, step));
        for (int i = 0; i < count; i++)
            if (IsTerrain(_hits[i]) && Vector2.Dot(_hits[i].normal, forward) < -0.5f) return false;
        Bounds bounds = _collider.bounds;
        Vector2 origin = new Vector2(bounds.center.x + direction * (bounds.extents.x + Mathf.Max(_edgeDetectionDistance, step)),
            bounds.min.y + GroundContactDistance);
        count = gameObject.scene.GetPhysicsScene2D().Raycast(origin, Vector2.down,
            GroundContactDistance + _groundDetectionDepth, filter, _hits);
        for (int i = 0; i < count; i++)
            if (IsTerrain(_hits[i]) && _hits[i].normal.y > 0.5f) return true;
        return false;
    }

    private bool IsTerrain(RaycastHit2D hit) => hit.collider != null && hit.collider.attachedRigidbody != _body
        && !hit.collider.transform.IsChildOf(transform);

    private void UpdateVisual(bool moving)
    {
        IsMovingVisual = moving;
        if (_visualRenderer != null) _visualRenderer.flipX = _direction > 0;
        if (_visualAnimator != null) _visualAnimator.speed = moving ? 1f : 0f;
    }
}
