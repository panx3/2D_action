using System.Collections.Generic;
using UnityEngine;

/// <summary>上空から狙いを定めて急降下し、地面に刺さる隙を作る。速度はFlightだけが書く。</summary>
[DisallowMultipleComponent, DefaultExecutionOrder(50)]
[RequireComponent(typeof(HachidrillController), typeof(EnemyHealth))]
public sealed class HachidrillAttackController : MonoBehaviour
{
    public enum ActionState { Patrol, Windup, Dive, Embedded, Extract, Return, Dead }

    [Header("Detection (fixed at PatrolCenter, world units)")]
    [SerializeField, Min(0f)] private float detectionWidth = 20f;
    [SerializeField, Min(0f)] private float detectionHeightUp = 3f;
    [SerializeField, Min(0f)] private float detectionHeightDown = 12f;
    [SerializeField, Min(0f)] private float detectionReentryMargin = 0.15f;
    [SerializeField] private LayerMask obstacleLayers = (1 << 0) | (1 << 6);
    [SerializeField] private LayerMask playerLayers = 1 << 3;
    [Header("Dive")]
    [SerializeField, Min(0.05f), Tooltip("狙いを追う時間。最後の0.15秒は方向を固定して回避できるようにする。")]
    private float attackWindup = 0.6f;
    [SerializeField, Min(1f)] private float diveSpeed = 26f;
    [SerializeField, Range(0f, 70f)] private float maximumDiveAngle = 55f;
    [SerializeField, Min(0.1f), Tooltip("地面がない場合にも帰還するまでの最大急降下時間。")]
    private float maximumDiveTime = 1.5f;
    [SerializeField, Min(0)] private int attackDamage = 1;
    [SerializeField, Min(0f)] private float attackKnockback = 5f;
    [Header("Embedded, pop and return")]
    [SerializeField, Min(0.1f)] private float embeddedDuration = 1.4f;
    [SerializeField, Min(0f), Tooltip("物理Colliderではなく見た目だけ地面に沈める深さ。")]
    private float embedVisualDepth = 0.18f;
    [SerializeField, Min(0.05f)] private float extractionDuration = 0.22f;
    [SerializeField, Min(0f)] private float extractionSpeed = 7f;
    [SerializeField, Min(0.1f)] private float returnSpeed = 4f;
    [SerializeField, Min(0.1f)] private float returnAcceleration = 24f;
    [SerializeField, Min(0f)] private float attackCooldown = 0.9f;
    [Header("Existing attack artwork")]
    [SerializeField] private BoxCollider2D attackHitbox;
    [SerializeField] private AnimationClip attackClip;
    [SerializeField] private Sprite[] attackFrames;
    [SerializeField] private Vector2[] drillOffsets;
    [Header("Optional dive sound")]
    [SerializeField] private AudioSource attackAudioSource;
    [SerializeField] private AudioClip attackSE;
    [SerializeField, Range(0f, 1f)] private float attackSEVolume = 0.6f;

    private readonly RaycastHit2D[] sightHits = new RaycastHit2D[24];
    private readonly RaycastHit2D[] damageHits = new RaycastHit2D[24];
    private readonly Collider2D[] overlaps = new Collider2D[16];
    private readonly HashSet<PlayerHealth> hitThisDive = new HashSet<PlayerHealth>();
    private HachidrillController flight;
    private EnemyHealth health;
    private Rigidbody2D body;
    private BoxCollider2D bodyCollider;
    private PlayerHealth player;
    private Collider2D playerCollider;
    private float nextSearch, nextAttack, stateStarted, poseAngle;
    private Vector2 diveDirection = Vector2.down;
    private const int DrillFrame = 4;

    public ActionState State { get; private set; }
    public float StateElapsed => Mathf.Max(0f, Time.time - stateStarted);
    public Vector2 DiveDirection => diveDirection;
    public Vector2 ImpactPoint { get; private set; }
    public Vector2 ImpactNormal { get; private set; } = Vector2.up;
    public int DiveCount { get; private set; }
    public bool BodyContactSuppressed => State != ActionState.Patrol;
    public Bounds DetectionBounds
    {
        get
        {
            Vector2 center = flight != null ? flight.PatrolCenter : (Vector2)transform.position;
            return new Bounds(center + Vector2.up * ((detectionHeightUp - detectionHeightDown) * 0.5f),
                new Vector3(detectionWidth, detectionHeightUp + detectionHeightDown, 0f));
        }
    }

    private void Awake()
    {
        flight = GetComponent<HachidrillController>(); health = GetComponent<EnemyHealth>();
        body = GetComponent<Rigidbody2D>(); bodyCollider = GetComponent<BoxCollider2D>();
        flight.BeforeFlightStep += Step; flight.FlightBlocked += OnFlightBlocked;
        health.HitConfirmed += OnHit; health.HealthReset += ResetAI;
    }
    private void OnEnable() => ResetAI();
    private void ResetAI()
    {
        nextAttack = nextSearch = 0f; DiveCount = 0; diveDirection = Vector2.down;
        SetState(ActionState.Patrol);
    }
    private void OnDisable()
    {
        DisableHitbox(); hitThisDive.Clear();
        State = health != null && health.CurrentHp <= 0 ? ActionState.Dead : ActionState.Patrol;
        if (flight == null) return;
        flight.ClearFlightTarget(); flight.PatrolPaused = false; flight.SuppressContactDamage = false;
        flight.AttackVisualOffset = Vector3.zero; flight.AttackVisualAngle = 0f;
        if (State != ActionState.Dead) Hover();
        else if (flight.VisualAnimator != null) flight.VisualAnimator.speed = 0f;
    }
    private void OnDestroy()
    {
        if (flight != null) { flight.BeforeFlightStep -= Step; flight.FlightBlocked -= OnFlightBlocked; }
        if (health != null) { health.HitConfirmed -= OnHit; health.HealthReset -= ResetAI; }
    }
    private void OnHit(MorningStarHitContext context, bool lethal)
    {
        nextAttack = Time.time + attackCooldown;
        SetState(lethal ? ActionState.Dead : ActionState.Return);
    }

    private void Step()
    {
        if (!isActiveAndEnabled) return;
        if (health.CurrentHp <= 0 || health.IsDeathHandled) { if (State != ActionState.Dead) SetState(ActionState.Dead); return; }
        if (health.IsHitStunned) return;
        switch (State)
        {
            case ActionState.Patrol:
                if (Time.time >= nextAttack && FindVisiblePlayer(detectionReentryMargin)) SetState(ActionState.Windup);
                break;
            case ActionState.Windup:
                if (!FindVisiblePlayer(0f)) { SetState(ActionState.Return); break; }
                if (StateElapsed < Mathf.Max(0f, attackWindup - 0.15f)) AimAtPlayer();
                if (StateElapsed >= attackWindup) SetState(ActionState.Dive);
                break;
            case ActionState.Dive:
                // 発射後は追尾しない。範囲を離れても同じ方向へ地面まで降下する。
                Vector2 displacement = diveDirection * (diveSpeed * Time.fixedDeltaTime);
                Vector2 safe = flight.SweepFlight(displacement, out _);
                DamageAlongDive(safe);
                flight.SetFlightVelocity(diveDirection * diveSpeed);
                if (StateElapsed >= maximumDiveTime) SetState(ActionState.Return);
                break;
            case ActionState.Embedded:
                if (StateElapsed >= embeddedDuration) SetState(ActionState.Extract);
                break;
            case ActionState.Extract:
                flight.SetFlightVelocity(ImpactNormal * extractionSpeed);
                if (StateElapsed >= extractionDuration) SetState(ActionState.Return);
                break;
            case ActionState.Return:
                // 真上へ抜けてから巡回範囲に戻る。
                float homeX = Mathf.Clamp(body.position.x, flight.PatrolCenter.x - flight.PatrolHalfRange,
                    flight.PatrolCenter.x + flight.PatrolHalfRange);
                Vector2 home = new Vector2(homeX, flight.PatrolCenter.y);
                if (Vector2.Distance(body.position, home) < 0.06f)
                {
                    nextAttack = Time.time + attackCooldown;
                    SetState(ActionState.Patrol);
                }
                else flight.SetFlightTarget(Mathf.Abs(body.position.y - home.y) > 0.06f
                    ? new Vector2(body.position.x, home.y) : home, returnSpeed, returnAcceleration);
                break;
        }
    }

    private void AimAtPlayer()
    {
        Vector2 delta = (Vector2)playerCollider.bounds.center - body.position;
        float angle = Mathf.Clamp(Mathf.Atan2(delta.x, Mathf.Max(0.2f, -delta.y)) * Mathf.Rad2Deg,
            -maximumDiveAngle, maximumDiveAngle);
        diveDirection = Quaternion.Euler(0f, 0f, angle) * Vector2.down;
        flight.Face(delta.x >= 0f ? 1 : -1);
        poseAngle = Vector2.SignedAngle(MirroredTip(), diveDirection);
        flight.AttackVisualAngle = poseAngle;
    }

    private void OnFlightBlocked(RaycastHit2D hit)
    {
        if (State != ActionState.Dive) return;
        ImpactPoint = hit.point; ImpactNormal = hit.normal;
        // 壁や天井への衝突は刺さる地面として扱わない。
        SetState(hit.normal.y >= 0.45f ? ActionState.Embedded : ActionState.Return);
    }

    private bool FindVisiblePlayer(float margin)
    {
        if ((player == null || playerCollider == null || !player.gameObject.activeInHierarchy) && Time.time >= nextSearch)
        {
            nextSearch = Time.time + 0.25f;
            GameObject root = GameObject.FindGameObjectWithTag("Player");
            player = root != null ? root.GetComponent<PlayerHealth>() : null;
            playerCollider = root != null ? root.GetComponent<Collider2D>() : null;
        }
        if (player == null || playerCollider == null || !player.gameObject.activeInHierarchy || player.IsDead || player.IsRespawning) return false;
        Bounds bounds = DetectionBounds;
        Vector2 point = playerCollider.bounds.center;
        if (point.x < bounds.min.x + margin || point.x > bounds.max.x - margin
            || point.y < bounds.min.y + margin || point.y > bounds.max.y - margin) return false;
        return point.y < body.position.y - 0.3f && ClearSight(point);
    }

    private bool ClearSight(Vector2 point)
    {
        Vector2 origin = bodyCollider.bounds.center;
        Vector2 delta = point - origin;
        if (delta.sqrMagnitude < 0.0001f) return true;
        var filter = new ContactFilter2D { useTriggers = false }; filter.SetLayerMask(obstacleLayers);
        int count = gameObject.scene.GetPhysicsScene2D().Raycast(origin, delta.normalized, delta.magnitude, filter, sightHits);
        for (int i = 0; i < count; i++)
            if (sightHits[i].collider != null && sightHits[i].collider.attachedRigidbody != body
                && !sightHits[i].collider.transform.IsChildOf(transform)) return false;
        return true;
    }

    private void DamageAlongDive(Vector2 displacement)
    {
        var filter = new ContactFilter2D { useTriggers = false }; filter.SetLayerMask(playerLayers);
        // 現在の重なりと移動区間を検査し、高速でもPlayerをすり抜けない。
        int count = bodyCollider.Overlap(filter, overlaps);
        for (int i = 0; i < count; i++) TryDamage(overlaps[i]);
        float distance = displacement.magnitude;
        if (distance > 0.0001f)
        {
            count = bodyCollider.Cast(displacement / distance, filter, damageHits, distance);
            for (int i = 0; i < count; i++) TryDamage(damageHits[i].collider);
        }
        if (attackHitbox == null || flight.VisualRenderer == null) return;
        Vector2 tip = flight.VisualRenderer.transform.TransformPoint(MirroredTip());
        Vector2 size = Vector2.Scale(attackHitbox.size, AbsScale(attackHitbox.transform.lossyScale));
        count = gameObject.scene.GetPhysicsScene2D().BoxCast(tip, size, poseAngle, diveDirection,
            distance, filter, damageHits);
        for (int i = 0; i < count; i++) TryDamage(damageHits[i].collider);
    }

    private void TryDamage(Collider2D other)
    {
        if (!PlayerColliderUtility.IsPlayerBody(other)) return;
        var hp = other.GetComponentInParent<PlayerHealth>();
        if (hp == null || hitThisDive.Contains(hp) || hp.IsDead || hp.IsRespawning || hp.IsInvincible
            || !ClearSight(other.bounds.center)) return;
        int before = hp.CurrentHp;
        hp.TakeDamage(attackDamage, new Vector2(diveDirection.x, -0.35f).normalized * attackKnockback);
        if (hp.CurrentHp < before) { hitThisDive.Add(hp); flight.BlockContactAfterAttack(); }
    }

    private void SetState(ActionState next)
    {
        State = next; stateStarted = Time.time; DisableHitbox();
        if (flight == null) return;
        flight.ClearFlightTarget();
        flight.PatrolPaused = next == ActionState.Windup || next == ActionState.Embedded || next == ActionState.Dead;
        flight.SuppressContactDamage = BodyContactSuppressed;
        flight.AttackVisualOffset = Vector3.zero;
        if (next == ActionState.Windup) { AimAtPlayer(); ShowDrillFrame(); }
        else if (next == ActionState.Dive)
        {
            hitThisDive.Clear(); DiveCount++;
            flight.SetFlightVelocity(diveDirection * diveSpeed);
            // 初回の高速移動区間にも判定を置く。
            DamageAlongDive(flight.SweepFlight(diveDirection * (diveSpeed * Time.fixedDeltaTime), out _));
            OneShotAudioUtility.Play2D(attackAudioSource, attackSE, attackSEVolume, transform.position);
        }
        else if (next == ActionState.Embedded) flight.AttackPresentation = true;
        else if (next == ActionState.Extract) flight.SetFlightVelocity(ImpactNormal * extractionSpeed);
        else { hitThisDive.Clear(); Hover(); }
    }

    private void Hover()
    {
        flight.AttackPresentation = false; flight.AttackVisualAngle = 0f;
        Animator animator = flight.VisualAnimator;
        if (animator != null && animator.isActiveAndEnabled && animator.runtimeAnimatorController != null)
        { animator.speed = State == ActionState.Dead ? 0f : 1f; animator.Play("Hover", 0, 0f); animator.Update(0f); }
    }
    private void ShowDrillFrame()
    {
        flight.AttackPresentation = true;
        if (flight.VisualAnimator != null) flight.VisualAnimator.speed = 0f;
        if (flight.VisualRenderer != null && attackFrames != null && attackFrames.Length > DrillFrame)
            flight.VisualRenderer.sprite = attackFrames[DrillFrame];
        if (attackHitbox != null) attackHitbox.transform.localPosition = MirroredTip();
    }
    private Vector2 MirroredTip()
    {
        Vector2 tip = drillOffsets != null && drillOffsets.Length > DrillFrame ? drillOffsets[DrillFrame] : new Vector2(0f, -0.24f);
        if (flight.Facing > 0) tip.x = -tip.x;
        return tip;
    }
    private void LateUpdate()
    {
        if (health.CurrentHp <= 0 || !flight.AttackPresentation) return;
        ShowDrillFrame();
        float wiggle = State == ActionState.Embedded ? Mathf.Sin(StateElapsed * 48f) : 0f;
        flight.AttackVisualAngle = poseAngle + wiggle * 7f;
        if (State == ActionState.Embedded)
            flight.AttackVisualOffset = transform.InverseTransformVector(-ImpactNormal * embedVisualDepth)
                + Vector3.right * (wiggle * 0.012f);
        else if (State == ActionState.Windup)
            flight.AttackVisualOffset = transform.InverseTransformVector(-diveDirection
                * (0.08f * Mathf.Sin(StateElapsed / attackWindup * Mathf.PI)));
        else flight.AttackVisualOffset = Vector3.zero;
    }
    private void DisableHitbox() { if (attackHitbox != null) attackHitbox.enabled = false; }
    private static Vector2 AbsScale(Vector3 scale) => new Vector2(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.75f, 0.2f);
        Bounds bounds = DetectionBounds; Gizmos.DrawWireCube(bounds.center, bounds.size);
        if (Application.isPlaying && (State == ActionState.Windup || State == ActionState.Dive))
        { Gizmos.color = Color.red; Gizmos.DrawRay(transform.position, diveDirection * 4f); }
    }
}
