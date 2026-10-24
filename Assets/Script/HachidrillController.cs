using UnityEngine;

/// <summary>飛行速度とホバリングの所有者。攻撃AIは目標位置を渡し、HP/被弾はEnemyHealthに委譲する。</summary>
[DisallowMultipleComponent, SelectionBase]
[RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D), typeof(EnemyHealth))]
public sealed class HachidrillController : MonoBehaviour
{
    public enum InitialDirection { Left = -1, Right = 1 }

    [Header("Flight patrol")]
    [SerializeField, Min(0f), InspectorName("Patrol Speed")] private float moveSpeed = 1.2f;
    [SerializeField, Min(0f), Tooltip("初期配置位置から左右への距離（ワールド単位）。")]
    private float patrolHalfRange = 3f;
    [SerializeField] private InitialDirection startDirection = InitialDirection.Left;
    [SerializeField] private LayerMask terrainLayers = (1 << 0) | (1 << 6);
    [Header("Presentation (no collider movement)")]
    [SerializeField] private Transform visualRoot;
    [SerializeField] private SpriteRenderer visualRenderer;
    [SerializeField] private Animator visualAnimator;
    [SerializeField, Min(0f), Tooltip("VisualRootのローカル単位。初期値は原画の約1～2ピクセル。")]
    private float hoverAmplitude = 0.03f;
    [SerializeField, Min(0f), Tooltip("1秒あたりの上下往復回数（Hz）。")]
    private float hoverFrequency = 0.4f;
    [SerializeField, Min(0f)] private float animationFPS = 6f;
    [Header("Body contact")]
    [SerializeField] private bool enableContactDamage = true;
    [SerializeField, Min(0)] private int contactDamage = 1;
    [SerializeField, Min(0f)] private float contactKnockback = 5f;
    [SerializeField, Min(0.02f)] private float contactCooldown = 0.7f;

    private const float Skin = 0.02f;
    private readonly RaycastHit2D[] hits = new RaycastHit2D[24];
    private Rigidbody2D body;
    private BoxCollider2D bodyCollider;
    private EnemyHealth health;
    private Vector2 patrolCenter;
    private Vector3 visualRest;
    private int direction;
    private float hoverTime, nextContact;
    private bool hasFlightTarget;
    private Vector2 flightTarget;
    private float flightSpeed, flightAcceleration;
    private bool hasFlightVelocity;
    private Vector2 flightVelocity;
    private Quaternion visualRotation;

    public bool PatrolPaused { get; set; }
    public bool AttackPresentation { get; set; }
    public bool SuppressContactDamage { get; set; }
    public event System.Action BeforeFlightStep;
    public event System.Action<RaycastHit2D> FlightBlocked;
    public Vector2 PatrolCenter => patrolCenter;
    public float PatrolHalfRange => Mathf.Max(0f, patrolHalfRange);
    public int Facing => direction;
    public SpriteRenderer VisualRenderer => visualRenderer;
    public Animator VisualAnimator => visualAnimator;
    public Vector3 AttackVisualOffset { get; set; }
    public float AttackVisualAngle { get; set; }

    public void SetFlightTarget(Vector2 target, float speed, float acceleration)
    {
        hasFlightVelocity = false;
        hasFlightTarget = true; flightTarget = target;
        flightSpeed = Mathf.Max(0f, speed); flightAcceleration = Mathf.Max(0.01f, acceleration);
    }

    public void SetFlightVelocity(Vector2 velocity)
    {
        hasFlightTarget = false;
        hasFlightVelocity = true;
        flightVelocity = velocity;
    }

    public void ClearFlightTarget() => hasFlightTarget = hasFlightVelocity = false;
    public void BlockContactAfterAttack() => nextContact = Mathf.Max(nextContact, Time.time + Mathf.Max(0.02f, contactCooldown));
    public void Face(int facing) => direction = facing >= 0 ? 1 : -1;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        bodyCollider = GetComponent<BoxCollider2D>();
        health = GetComponent<EnemyHealth>();
        patrolCenter = body.position;
        visualRest = visualRoot != null ? visualRoot.localPosition : Vector3.zero;
        visualRotation = visualRoot != null ? visualRoot.localRotation : Quaternion.identity;
        health.HitConfirmed += OnHit;
        health.HealthReset += ResetPatrol;
    }

    private void OnEnable() => ResetPatrol();

    private void ResetPatrol()
    {
        direction = startDirection == InitialDirection.Right ? 1 : -1;
        hoverTime = nextContact = 0f;
        PatrolPaused = AttackPresentation = SuppressContactDamage = hasFlightTarget = hasFlightVelocity = false;
        AttackVisualOffset = Vector3.zero;
        AttackVisualAngle = 0f;
    }

    private void OnDisable()
    {
        if (body != null) body.linearVelocity = Vector2.zero;
        if (visualAnimator != null) visualAnimator.speed = 0f;
        if (visualRoot != null)
        {
            visualRoot.localPosition = visualRest;
            visualRoot.localRotation = visualRotation;
        }
    }

    private void OnDestroy()
    {
        if (health == null) return;
        health.HitConfirmed -= OnHit;
        health.HealthReset -= ResetPatrol;
    }

    private void OnHit(MorningStarHitContext context, bool lethal)
    {
        // EnemyHealthがImpulseを加える前に巡回速度を解除する。
        body.linearVelocity = Vector2.zero;
    }

    private void FixedUpdate()
    {
        BeforeFlightStep?.Invoke();
        if (health.CurrentHp <= 0 || health.IsDeathHandled) return;
        // 被弾硬直中はEnemyHealthだけがノックバック速度を扱う。
        if (health.IsHitStunned) return;
        if (hasFlightVelocity && !PatrolPaused)
        {
            SetAltitudeLock(false);
            Vector2 safe = SweepFlight(flightVelocity * Time.fixedDeltaTime, out RaycastHit2D hit);
            body.linearVelocity = safe / Time.fixedDeltaTime;
            if (hit.collider != null) FlightBlocked?.Invoke(hit);
            return;
        }
        if (hasFlightTarget && !PatrolPaused)
        {
            SetAltitudeLock(false);
            DriveToTarget(flightTarget, flightSpeed, flightAcceleration);
            return;
        }
        // AIを手動で無効化した場合も、初期高度へ復帰してから巡回を再開する。
        if (!PatrolPaused && Mathf.Abs(body.position.y - patrolCenter.y) > 0.03f)
        {
            SetAltitudeLock(false);
            DriveToTarget(new Vector2(Mathf.Clamp(body.position.x, patrolCenter.x - PatrolHalfRange,
                patrolCenter.x + PatrolHalfRange), patrolCenter.y), moveSpeed, 8f);
            return;
        }
        SetAltitudeLock(true);
        if (PatrolPaused || patrolHalfRange <= 0f || moveSpeed <= 0f)
        {
            body.linearVelocity = Vector2.zero;
            return;
        }

        float left = patrolCenter.x - patrolHalfRange;
        float right = patrolCenter.x + patrolHalfRange;
        if (body.position.x >= right - 0.0001f) direction = -1;
        else if (body.position.x <= left + 0.0001f) direction = 1;

        // 地形は既存敵と同じLayer。1ステップ先まで掃引し、高速設定でも壁を跨がない。
        var filter = new ContactFilter2D { useTriggers = false };
        filter.SetLayerMask(terrainLayers);
        float step = moveSpeed * Time.fixedDeltaTime;
        if (FreeDistance(direction, step, filter) <= 0.0001f)
            direction = -direction;
        float rangeRemaining = direction > 0 ? right - body.position.x : body.position.x - left;
        float distance = Mathf.Min(step, Mathf.Max(0f, rangeRemaining), FreeDistance(direction, step, filter));
        body.linearVelocity = new Vector2(direction * distance / Time.fixedDeltaTime, 0f);
    }

    private void SetAltitudeLock(bool locked)
    {
        var constraints = body.constraints;
        var desired = locked ? constraints | RigidbodyConstraints2D.FreezePositionY
            : constraints & ~RigidbodyConstraints2D.FreezePositionY;
        if (constraints != desired) body.constraints = desired;
    }

    private void DriveToTarget(Vector2 target, float speed, float acceleration)
    {
        Vector2 delta = target - body.position;
        float distance = delta.magnitude;
        if (distance < 0.015f) { body.linearVelocity = Vector2.zero; return; }
        float desiredSpeed = Mathf.Min(speed, Mathf.Sqrt(2f * acceleration * distance), distance / Time.fixedDeltaTime);
        Vector2 velocity = Vector2.MoveTowards(body.linearVelocity, delta / distance * desiredSpeed,
            acceleration * Time.fixedDeltaTime);
        // 斜め移動も本体Colliderで掃引。地形沿いの成分だけを残し、壁/地面を跨がない。
        Vector2 displacement = velocity * Time.fixedDeltaTime;
        Vector2 safe = Sweep(displacement, out Vector2 normal);
        if (normal != Vector2.zero)
        {
            Vector2 remaining = displacement - safe;
            Vector2 slide = remaining - normal * Vector2.Dot(remaining, normal);
            // Castは現在位置を基準にするため、保守的に滑り成分だけを採用する。
            if (safe.sqrMagnitude < 0.000001f) safe = Sweep(slide, out _);
        }
        body.linearVelocity = safe / Time.fixedDeltaTime;
    }

    private Vector2 Sweep(Vector2 displacement, out Vector2 blockingNormal)
    {
        Vector2 safe = SweepFlight(displacement, out RaycastHit2D hit);
        blockingNormal = hit.collider != null ? hit.normal : Vector2.zero;
        return safe;
    }

    // AIと移動本体で同じ地形掃引を使う。速度の書き込みは本体だけが行う。
    public Vector2 SweepFlight(Vector2 displacement, out RaycastHit2D blockingHit)
    {
        blockingHit = default;
        float distance = displacement.magnitude;
        if (distance < 0.00001f) return Vector2.zero;
        Vector2 forward = displacement / distance;
        var filter = new ContactFilter2D { useTriggers = false }; filter.SetLayerMask(terrainLayers);
        int count = bodyCollider.Cast(forward, filter, hits, distance + Skin);
        float free = distance;
        for (int i = 0; i < count; i++)
        {
            var hit = hits[i];
            if (hit.collider == null || hit.collider.attachedRigidbody == body || hit.collider.transform.IsChildOf(transform)
                || Vector2.Dot(hit.normal, forward) >= -0.001f) continue;
            float available = Mathf.Max(0f, hit.distance - Skin);
            if (available <= free) { free = available; blockingHit = hit; }
        }
        return forward * free;
    }

    private float FreeDistance(int facing, float step, ContactFilter2D filter)
    {
        Vector2 forward = Vector2.right * facing;
        int count = bodyCollider.Cast(forward, filter, hits, step + Skin);
        float free = step;
        for (int i = 0; i < count; i++)
        {
            var hit = hits[i];
            if (hit.collider == null || hit.collider.attachedRigidbody == body
                || hit.collider.transform.IsChildOf(transform) || Vector2.Dot(hit.normal, forward) > -0.5f) continue;
            free = Mathf.Min(free, Mathf.Max(0f, hit.distance - Skin));
        }
        return free;
    }

    private void LateUpdate()
    {
        if (health.CurrentHp <= 0) return;
        if (visualRenderer != null) visualRenderer.flipX = direction > 0;
        if (visualAnimator != null && !AttackPresentation) visualAnimator.speed = animationFPS / 6f;
        hoverTime += Time.deltaTime;
        if (visualRoot == null) return;
        float offset = AttackPresentation ? 0f : Mathf.Sin(hoverTime * hoverFrequency * Mathf.PI * 2f) * hoverAmplitude;
        if (visualRenderer != null && visualRenderer.sprite != null)
        {
            // 原画ピクセルの整数倍に揃える。AnimatorはSpriteだけを差し替える。
            float pixel = Mathf.Abs(visualRenderer.transform.localScale.y) / visualRenderer.sprite.pixelsPerUnit;
            if (pixel > 0f) offset = Mathf.Round(offset / pixel) * pixel;
        }
        visualRoot.localPosition = visualRest + Vector3.up * offset + AttackVisualOffset;
        visualRoot.localRotation = visualRotation * Quaternion.Euler(0f, 0f, AttackVisualAngle);
    }

    private void OnCollisionEnter2D(Collision2D collision) => TryContactDamage(collision.collider);
    private void OnCollisionStay2D(Collision2D collision) => TryContactDamage(collision.collider);

    private void TryContactDamage(Collider2D other)
    {
        if (!isActiveAndEnabled || SuppressContactDamage || !enableContactDamage || contactDamage <= 0 || health.CurrentHp <= 0
            || health.IsDeathHandled || health.IsHitStunned || Time.time < nextContact
            || !PlayerColliderUtility.IsPlayerBody(other)) return;
        var player = other.GetComponentInParent<PlayerHealth>();
        if (player == null || player.IsDead || player.IsRespawning || player.IsInvincible) return;
        float away = other.bounds.center.x >= bodyCollider.bounds.center.x ? 1f : -1f;
        int before = player.CurrentHp;
        player.TakeDamage(contactDamage, new Vector2(away, 0.2f).normalized * contactKnockback);
        if (player.CurrentHp < before) nextContact = Time.time + Mathf.Max(0.02f, contactCooldown);
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 center = Application.isPlaying ? (Vector3)patrolCenter : transform.position;
        float half = Mathf.Max(0f, patrolHalfRange);
        Vector3 left = center + Vector3.left * half, right = center + Vector3.right * half;
        Gizmos.color = new Color(0.3f, 0.85f, 1f);
        Gizmos.DrawLine(left, right);
        Gizmos.DrawLine(left + Vector3.down * 0.2f, left + Vector3.up * 0.2f);
        Gizmos.DrawLine(right + Vector3.down * 0.2f, right + Vector3.up * 0.2f);
        Gizmos.DrawWireSphere(center, 0.06f);
    }
}
