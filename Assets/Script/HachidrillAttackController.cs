using System.Collections.Generic;
using UnityEngine;

/// <summary>固定した縄張りからの検知・滑空目標・二段攻撃。Rigidbodyの速度はFlightだけが書く。</summary>
[DisallowMultipleComponent, DefaultExecutionOrder(50)]
[RequireComponent(typeof(HachidrillController), typeof(EnemyHealth))]
public sealed class HachidrillAttackController : MonoBehaviour
{
    public enum ActionState { Patrol, Chase, Attack, Cooldown, Return, Dead }

    [Header("Detection (fixed at PatrolCenter, world units)")]
    [SerializeField, Min(0f)] private float detectionWidth = 14f;
    [SerializeField, Min(0f)] private float detectionHeightUp = 3f;
    [SerializeField, Min(0f)] private float detectionHeightDown = 5f;
    [SerializeField, Min(0f), Tooltip("再検知にはこの距離だけ範囲内へ入る必要がある。範囲外へ出た時は即中断。")]
    private float detectionReentryMargin = 0.15f;
    [SerializeField] private LayerMask obstacleLayers = (1 << 0) | (1 << 6);
    [SerializeField] private LayerMask playerLayers = 1 << 3;
    [Header("Glide and return")]
    [SerializeField, Min(0f)] private float glideSpeed = 2.4f;
    [SerializeField, Min(0.01f)] private float glideAcceleration = 7f;
    [SerializeField, Min(0.02f), Tooltip("ドリル先端が届く接近目標との距離。実際の判定が届くことも確認して攻撃する。")]
    private float attackRange = 0.12f;
    [SerializeField, Min(0f)] private float returnSpeed = 1.8f;
    [Header("Two-thrust attack (sprite frames, zero based)")]
    [SerializeField] private BoxCollider2D attackHitbox;
    [SerializeField] private AnimationClip attackClip;
    [SerializeField] private Sprite[] attackFrames;
    [SerializeField] private Vector2[] drillOffsets;
    [SerializeField] private int firstStrikeFrame = 2;
    [SerializeField] private int secondStrikeFrame = 4;
    [SerializeField, Min(0)] private int attackDamage = 1;
    [SerializeField, Min(0f)] private float attackKnockback = 5f;
    [SerializeField, Min(0.05f)] private float attackAnimationSpeed = 1f;
    [SerializeField, Min(0f)] private float attackWindup = 0.18f;
    [SerializeField, Min(0f)] private float attackCooldown = 0.9f;
    [Header("Optional attack sound (one per thrust)")]
    [SerializeField] private AudioSource attackAudioSource;
    [SerializeField] private AudioClip attackSE;
    [SerializeField, Range(0f, 1f)] private float attackSEVolume = 0.6f;

    private readonly Collider2D[] targets = new Collider2D[16];
    private readonly RaycastHit2D[] sightHits = new RaycastHit2D[24];
    private readonly HashSet<PlayerHealth> hitThisThrust = new HashSet<PlayerHealth>();
    private HachidrillController flight;
    private EnemyHealth health;
    private Rigidbody2D body;
    private PlayerHealth player;
    private Collider2D playerCollider;
    private float nextSearch, nextAttack, windupUntil, frameTimer;
    private int attackFrame = -1, activeThrust;

    public ActionState State { get; private set; }
    public int ActiveThrust => activeThrust;
    public int AttackFrame => attackFrame;
    public int ThrustCount { get; private set; }
    public float AttackReadyAt => nextAttack;
    public bool BodyContactSuppressed => State == ActionState.Attack;
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
        flight = GetComponent<HachidrillController>(); health = GetComponent<EnemyHealth>(); body = GetComponent<Rigidbody2D>();
        if (attackAudioSource == null) attackAudioSource = OneShotAudioUtility.FindWorldImpactSource();
        flight.BeforeFlightStep += Step;
        health.HitConfirmed += OnHit; health.HealthReset += ResetAI;
    }
    private void OnEnable() => ResetAI();
    private void ResetAI()
    {
        nextAttack = nextSearch = 0f; ThrustCount = 0;
        SetState(ActionState.Patrol);
    }
    private void OnDisable()
    {
        DisableHitbox();
        State = health != null && health.CurrentHp <= 0 ? ActionState.Dead : ActionState.Patrol;
        if (flight != null)
        {
            flight.ClearFlightTarget(); flight.PatrolPaused = false; flight.SuppressContactDamage = false;
            if (State != ActionState.Dead) Hover();
            else { flight.AttackPresentation = false; if (flight.VisualAnimator != null) flight.VisualAnimator.speed = 0f; }
        }
    }
    private void OnDestroy()
    {
        if (flight != null) flight.BeforeFlightStep -= Step;
        if (health != null) { health.HitConfirmed -= OnHit; health.HealthReset -= ResetAI; }
    }
    private void OnHit(MorningStarHitContext context, bool lethal)
    {
        nextAttack = Mathf.Max(nextAttack, Time.time + attackCooldown);
        SetState(lethal ? ActionState.Dead : ActionState.Cooldown);
    }

    private void Step()
    {
        if (!isActiveAndEnabled) return;
        if (health.CurrentHp <= 0 || health.IsDeathHandled) { SetState(ActionState.Dead); return; }
        if (health.IsHitStunned) return;
        bool tracking = State == ActionState.Chase || State == ActionState.Attack || State == ActionState.Cooldown;
        bool visible = FindVisiblePlayer(tracking ? 0f : detectionReentryMargin);
        if (!visible && tracking) SetState(ActionState.Return);
        else if (visible && (State == ActionState.Patrol || State == ActionState.Return))
            SetState(Time.time >= nextAttack ? ActionState.Chase : ActionState.Cooldown);

        switch (State)
        {
            case ActionState.Chase:
                if (Mathf.Abs(playerCollider.bounds.center.x - body.position.x) > 0.15f)
                    flight.Face(playerCollider.bounds.center.x > body.position.x ? 1 : -1);
                Vector2 target = ApproachTarget();
                if (Time.time >= nextAttack && Vector2.Distance(body.position, target) <= attackRange && FirstThrustCanReach())
                    SetState(ActionState.Attack);
                else flight.SetFlightTarget(target, glideSpeed, glideAcceleration);
                break;
            case ActionState.Cooldown:
                if (Time.time >= nextAttack) SetState(visible ? ActionState.Chase : ActionState.Return);
                break;
            case ActionState.Return:
                Vector2 home = new Vector2(Mathf.Clamp(body.position.x, flight.PatrolCenter.x - flight.PatrolHalfRange,
                    flight.PatrolCenter.x + flight.PatrolHalfRange), flight.PatrolCenter.y);
                if (Vector2.Distance(body.position, home) <= 0.03f) SetState(ActionState.Patrol);
                else flight.SetFlightTarget(home, returnSpeed, glideAcceleration);
                break;
        }
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
        return ClearSight(point);
    }

    private bool ClearSight(Vector2 point)
    {
        Vector2 origin = GetComponent<BoxCollider2D>().bounds.center;
        Vector2 delta = point - origin;
        if (delta.sqrMagnitude < 0.0001f) return true;
        var filter = new ContactFilter2D { useTriggers = false }; filter.SetLayerMask(obstacleLayers);
        int count = gameObject.scene.GetPhysicsScene2D().Raycast(origin, delta.normalized, delta.magnitude, filter, sightHits);
        for (int i = 0; i < count; i++)
            if (sightHits[i].collider != null && sightHits[i].collider.attachedRigidbody != body
                && !sightHits[i].collider.transform.IsChildOf(transform)) return false;
        return true;
    }

    private Vector2 ApproachTarget()
    {
        // 原画のドリルは斜め下へ突き出す。Playerの頭上で先端が届く位置へ滑空する。
        Vector2 local = StrikeOffset(firstStrikeFrame);
        Vector2 worldOffset = flight.VisualRenderer.transform.TransformVector(local);
        float tipHeight = attackHitbox.size.y * Mathf.Abs(attackHitbox.transform.lossyScale.y);
        return new Vector2(playerCollider.bounds.center.x, playerCollider.bounds.max.y + tipHeight * 0.35f) - worldOffset;
    }

    private Vector2 StrikeOffset(int frame)
    {
        Vector2 offset = drillOffsets[frame];
        if (flight.Facing > 0) offset.x = -offset.x;
        return offset;
    }

    private bool FirstThrustCanReach()
    {
        Vector3 center = flight.VisualRenderer.transform.TransformPoint(StrikeOffset(firstStrikeFrame));
        Vector3 size = Vector3.Scale(attackHitbox.size, AbsScale(attackHitbox.transform.lossyScale));
        return new Bounds(center, size).Intersects(playerCollider.bounds);
    }

    private void SetState(ActionState next)
    {
        if (State == ActionState.Attack || next == ActionState.Dead) nextAttack = Mathf.Max(nextAttack, Time.time + attackCooldown);
        State = next; DisableHitbox(); attackFrame = -1;
        if (flight == null) return;
        flight.ClearFlightTarget();
        flight.PatrolPaused = next == ActionState.Attack || next == ActionState.Cooldown || next == ActionState.Dead;
        flight.SuppressContactDamage = next == ActionState.Attack || next == ActionState.Dead;
        if (next == ActionState.Attack)
        {
            flight.AttackPresentation = true;
            windupUntil = Time.time + attackWindup; frameTimer = 0f;
            ShowAttackFrame(0);
        }
        else Hover();
    }

    private void Hover()
    {
        flight.AttackPresentation = false;
        Animator animator = flight.VisualAnimator;
        if (animator != null && animator.isActiveAndEnabled && animator.runtimeAnimatorController != null)
        { animator.speed = 1f; animator.Play("Hover", 0, 0f); animator.Update(0f); }
    }

    private void LateUpdate()
    {
        if (State != ActionState.Attack) return;
        if (health.CurrentHp <= 0) { SetState(ActionState.Dead); return; }
        // FixedUpdateの間にPlayerが範囲外へ移動しても判定を残さない。
        if (health.IsHitStunned || !FindVisiblePlayer(0f)) { SetState(ActionState.Return); return; }
        if (Time.time < windupUntil) return;
        frameTimer += Time.deltaTime * Mathf.Max(0.05f, attackAnimationSpeed);
        float interval = 1f / Mathf.Max(1f, attackClip.frameRate);
        if (frameTimer >= interval)
        {
            // 描画が重くても突き出しコマを飛ばさない。表示したコマにだけ判定を同期する。
            frameTimer = Mathf.Min(frameTimer - interval, interval);
            if (attackFrame + 1 >= attackFrames.Length) { SetState(ActionState.Cooldown); return; }
            ShowAttackFrame(attackFrame + 1);
        }
        if (activeThrust != 0) DamageAtDrill();
    }

    private void ShowAttackFrame(int frame)
    {
        attackFrame = frame;
        Animator animator = flight.VisualAnimator;
        animator.speed = 0f;
        // コマ中央をサンプルし、浮動小数の丸めで直前コマが残るのを防ぐ。
        float sampleTime = (frame + 0.5f) / Mathf.Max(1f, attackClip.frameRate);
        animator.Play("Attack", 0, sampleTime / Mathf.Max(0.001f, attackClip.length)); animator.Update(0f);
        int thrust = frame == firstStrikeFrame ? 1 : frame == secondStrikeFrame ? 2 : 0;
        if (thrust != activeThrust)
        {
            hitThisThrust.Clear(); activeThrust = thrust;
            if (thrust != 0)
            {
                ThrustCount++;
                OneShotAudioUtility.Play2D(attackAudioSource, attackSE, attackSEVolume, transform.position);
            }
        }
        attackHitbox.transform.localPosition = StrikeOffset(frame);
        attackHitbox.enabled = thrust != 0;
    }

    private void DamageAtDrill()
    {
        Vector2 center = attackHitbox.transform.TransformPoint(attackHitbox.offset);
        Vector2 size = Vector2.Scale(attackHitbox.size, AbsScale(attackHitbox.transform.lossyScale));
        var filter = new ContactFilter2D { useTriggers = false }; filter.SetLayerMask(playerLayers);
        int count = gameObject.scene.GetPhysicsScene2D().OverlapBox(center, size, attackHitbox.transform.eulerAngles.z, filter, targets);
        for (int i = 0; i < count; i++)
        {
            Collider2D other = targets[i];
            if (!PlayerColliderUtility.IsPlayerBody(other)) continue;
            var hp = other.GetComponentInParent<PlayerHealth>();
            if (hp == null || !hitThisThrust.Add(hp) || hp.IsDead || hp.IsRespawning || hp.IsInvincible
                || !ClearSight(other.ClosestPoint(center))) continue;
            int before = hp.CurrentHp;
            hp.TakeDamage(attackDamage, new Vector2(flight.Facing, -0.2f).normalized * attackKnockback);
            if (hp.CurrentHp < before) flight.BlockContactAfterAttack();
        }
    }

    private void DisableHitbox()
    {
        if (attackHitbox != null) attackHitbox.enabled = false;
        activeThrust = 0; hitThisThrust.Clear();
    }
    private static Vector3 AbsScale(Vector3 scale) => new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));

    private void OnDrawGizmosSelected()
    {
        Vector2 center = Application.isPlaying && flight != null ? flight.PatrolCenter : (Vector2)transform.position;
        Gizmos.color = new Color(1f, 0.75f, 0.2f);
        Gizmos.DrawWireCube(center + Vector2.up * ((detectionHeightUp - detectionHeightDown) * 0.5f),
            new Vector3(detectionWidth, detectionHeightUp + detectionHeightDown));
        if (attackHitbox != null)
        {
            Gizmos.color = new Color(1f, 0.2f, 0.15f);
            Matrix4x4 old = Gizmos.matrix; Gizmos.matrix = attackHitbox.transform.localToWorldMatrix;
            Gizmos.DrawWireCube(attackHitbox.offset, attackHitbox.size); Gizmos.matrix = old;
        }
    }
}
