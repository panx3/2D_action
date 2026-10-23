using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>巡回と方向固定のビーム攻撃。HP・被弾Impulse・撃破はEnemyHealthが所有する。</summary>
[DisallowMultipleComponent, SelectionBase]
[RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D), typeof(EnemyHealth))]
public sealed class BeamScorpionController : MonoBehaviour
{
    public enum ActionState { Patrol, Windup, Firing, Recovery }
    [Header("Patrol")]
    [SerializeField, Min(0f)] private float moveSpeed = 0.8f;
    [SerializeField, Min(0.1f), Tooltip("初期位置を中心とした片側の巡回幅。Rootの拡縮に追従。")]
    private float patrolHalfWidth = 2.5f;
    [SerializeField] private bool startFacingRight;
    [SerializeField, Min(0f)] private float turnDelay = 0.15f;
    [SerializeField, Min(0.02f)] private float probeDistance = 0.12f;
    [SerializeField] private LayerMask groundLayer = (1 << 0) | (1 << 6);
    [SerializeField] private Transform groundCheck, edgeCheck, wallCheck;
    [Header("Attack timing")]
    [SerializeField, Min(0.1f)] private float attackInterval = 1.2f;
    [SerializeField, Min(0.1f)] private float attackCooldown = 0.55f;
    [SerializeField, Min(0.1f)] private float windupTime = 0.5f;
    [SerializeField, Min(0.02f)] private float beamDuration = 0.55f;
    [SerializeField, Min(0f)] private float detectionRange = 7f;
    [Header("Beam")]
    [SerializeField] private Transform beamOrigin;
    [SerializeField, Min(0.1f)] private float beamLength = 8f;
    [SerializeField, Min(0.01f)] private float beamThickness = 0.14f;
    [SerializeField] private Color beamColor = new Color(1f, 0.08f, 0.32f);
    [SerializeField, Min(0)] private int beamDamage = 1;
    [SerializeField, Min(0.02f)] private float damageInterval = 0.7f;
    [SerializeField, Min(0f)] private float knockback = 5f;
    [SerializeField] private LayerMask playerLayer = 1 << 3;
    [SerializeField] private LayerMask obstacleLayer = (1 << 0) | (1 << 6);
    [SerializeField] private LayerMask morningStarLayer = 1 << 7;
    [Header("Presentation (sprite-only Animator)")]
    [SerializeField] private SpriteRenderer visualRenderer;
    [SerializeField] private Animator visualAnimator;
    [Tooltip("攻撃コマごとのしっぽ先端。Visualローカル座標、画像は左向き。")]
    [SerializeField] private Sprite[] attackFrames;
    [SerializeField] private Vector2[] muzzleOffsets;
    [Header("Replaceable VFX prefabs")]
    [SerializeField] private BeamScorpionBeamVfx beamVfxPrefab;
    [SerializeField] private BeamScorpionBurstVfx chargeVfxPrefab;
    [SerializeField] private BeamScorpionBurstVfx muzzleVfxPrefab;
    [SerializeField] private BeamScorpionBurstVfx playerHitVfxPrefab;
    [SerializeField] private BeamScorpionBurstVfx environmentHitVfxPrefab;
    [SerializeField] private BeamScorpionBeamVfx afterglowVfxPrefab;
    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip beamFireSE, beamHitSE;
    [SerializeField, Range(0f, 1f)] private float beamSEVolume = 0.65f;

    private readonly RaycastHit2D[] hits = new RaycastHit2D[32];
    private Rigidbody2D body;
    private BoxCollider2D bodyCollider;
    private EnemyHealth health;
    private ContactFilter2D terrainFilter, beamFilter;
    private int direction;
    private float nextAttack, turnUntil, nextDamage, nextImpact, nextPlayerSearch;
    private PlayerHealth player;
    private Collider2D playerCollider, lastHitCollider;
    private BeamScorpionBeamVfx beam;
    private BeamScorpionBurstVfx charge;
    private Vector2 beamEnd;
    private bool moving;
    private Vector3 originRest;
    private float patrolCenterX;
    public ActionState State { get; private set; }
    public float StateElapsed { get; private set; }
    public int Facing => direction;
    public Vector2 BeamEnd => beamEnd;
    public bool BeamActive => beam != null;
    public int FireSoundPlayCount { get; private set; }
    public int EnvironmentImpactCount { get; private set; }
    public int PlayerImpactCount { get; private set; }

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        patrolCenterX = body.position.x;
        bodyCollider = GetComponent<BoxCollider2D>();
        health = GetComponent<EnemyHealth>();
        terrainFilter = new ContactFilter2D { useTriggers = false };
        terrainFilter.SetLayerMask(groundLayer);
        beamFilter = new ContactFilter2D { useTriggers = false };
        beamFilter.SetLayerMask(playerLayer.value | obstacleLayer.value | morningStarLayer.value);
        originRest = beamOrigin != null ? beamOrigin.localPosition : Vector3.zero;
        health.HitConfirmed += OnHit;
    }

    private void OnEnable()
    {
        direction = startFacingRight ? 1 : -1;
        nextAttack = Time.time + Mathf.Max(0.1f, attackInterval);
        turnUntil = nextDamage = nextImpact = nextPlayerSearch = 0f;
        lastHitCollider = null;
        SetState(ActionState.Patrol);
    }

    private void OnDisable()
    {
        StopEffects(false);
        moving = false;
        if (visualAnimator != null) visualAnimator.speed = 0f;
        if (audioSource != null) audioSource.Stop();
        if (body != null) body.linearVelocity = new Vector2(0f, body.linearVelocity.y);
    }

    private void OnDestroy() { if (health != null) health.HitConfirmed -= OnHit; }

    private void FixedUpdate()
    {
        if (health.CurrentHp <= 0) return;
        if (health.IsHitStunned) { moving = false; return; }
        StateElapsed += Time.fixedDeltaTime;
        UpdateOrigin();
        float speed = 0f;
        bool grounded = IsGrounded();
        switch (State)
        {
            case ActionState.Patrol:
                if (grounded && Time.time >= nextAttack && PlayerInRange(out int attackFacing))
                {
                    direction = attackFacing;
                    SetState(ActionState.Windup);
                }
                else if (grounded && Time.time >= turnUntil)
                {
                    if (WithinPatrolRange(direction) && CanWalk(direction)) speed = direction * moveSpeed;
                    else { direction = -direction; turnUntil = Time.time + turnDelay; }
                }
                break;
            case ActionState.Windup:
                if (!grounded) SetState(ActionState.Recovery);
                else if (StateElapsed >= Mathf.Max(0.1f, windupTime)) SetState(ActionState.Firing);
                break;
            case ActionState.Firing:
                if (!grounded || StateElapsed >= Mathf.Max(0.02f, beamDuration)) SetState(ActionState.Recovery);
                else TraceBeam();
                break;
            case ActionState.Recovery:
                if (StateElapsed >= Mathf.Max(0.1f, attackCooldown)) SetState(ActionState.Patrol);
                break;
        }
        body.linearVelocity = new Vector2(speed, body.linearVelocity.y);
        moving = Mathf.Abs(speed) > 0.001f;
        UpdateVisual();
    }

    private void LateUpdate()
    {
        if (health.CurrentHp <= 0) return;
        UpdateOrigin();
        if (charge != null && beamOrigin != null) charge.transform.position = beamOrigin.position;
        if (beam != null) beam.SetBeam(Origin, beamEnd, beamThickness, beamColor);
    }

    private Vector2 Origin => beamOrigin != null ? (Vector2)beamOrigin.position : (Vector2)bodyCollider.bounds.center;

    private void UpdateOrigin()
    {
        if (beamOrigin == null) return;
        Vector3 offset = originRest;
        if (visualRenderer != null && attackFrames != null && muzzleOffsets != null)
            for (int i = 0; i < Mathf.Min(attackFrames.Length, muzzleOffsets.Length); i++)
                if (visualRenderer.sprite == attackFrames[i])
                {
                    offset = visualRenderer.transform.localPosition + Vector3.Scale(muzzleOffsets[i], visualRenderer.transform.localScale);
                    break;
                }
        offset.x = Mathf.Abs(offset.x) * direction;
        beamOrigin.localPosition = offset;
    }

    private void UpdateVisual()
    {
        if (visualRenderer != null) visualRenderer.flipX = direction > 0;
        if (visualAnimator == null) return;
        visualAnimator.speed = State == ActionState.Patrol ? (moving ? 1f : 0f)
            : State == ActionState.Windup ? 1f / Mathf.Max(0.1f, windupTime)
            : State == ActionState.Recovery ? 1f / Mathf.Max(0.1f, attackCooldown) : 1f;
    }

    private void SetState(ActionState state)
    {
        if (State == ActionState.Firing || State == ActionState.Windup) StopEffects(state == ActionState.Recovery);
        State = state;
        StateElapsed = 0f;
        moving = false;
        if (visualAnimator != null && visualAnimator.runtimeAnimatorController != null)
        {
            visualAnimator.Play(state.ToString(), 0, 0f);
            visualAnimator.Update(0f);
        }
        UpdateVisual();
        UpdateOrigin();
        if (state == ActionState.Windup)
            charge = SpawnBurst(chargeVfxPrefab, Origin, Vector2.right * direction, Mathf.Max(0.1f, windupTime));
        if (state == ActionState.Firing)
        {
            lastHitCollider = null;
            nextImpact = 0f;
            if (beamVfxPrefab != null) beam = Instantiate(beamVfxPrefab, transform);
            SpawnBurst(muzzleVfxPrefab, Origin, Vector2.right * direction);
            if (OneShotAudioUtility.Play2D(audioSource, beamFireSE, beamSEVolume, Origin)) FireSoundPlayCount++;
            TraceBeam();
        }
        if (state == ActionState.Recovery) nextAttack = Time.time + Mathf.Max(0.1f, attackCooldown) + Mathf.Max(0.1f, attackInterval);
    }

    private void OnHit(MorningStarHitContext context, bool lethal)
    {
        StopEffects(false);
        moving = false;
        body.linearVelocity = new Vector2(0f, body.linearVelocity.y);
        if (!lethal) SetState(ActionState.Recovery);
    }

    private bool PlayerInRange(out int facing)
    {
        facing = direction;
        if (player == null && Time.time >= nextPlayerSearch)
        {
            nextPlayerSearch = Time.time + 0.5f;
            GameObject target = GameObject.FindGameObjectWithTag("Player");
            if (target != null) { player = target.GetComponent<PlayerHealth>(); playerCollider = target.GetComponent<Collider2D>(); }
        }
        if (player == null || playerCollider == null || !player.gameObject.activeInHierarchy || player.IsDead || player.IsRespawning) return false;
        facing = playerCollider.bounds.center.x >= bodyCollider.bounds.center.x ? 1 : -1;
        Vector2 sightOrigin = beamOrigin != null ? (Vector2)transform.TransformPoint(new Vector3(
            Mathf.Abs(beamOrigin.localPosition.x) * facing, beamOrigin.localPosition.y, beamOrigin.localPosition.z)) : Origin;
        Vector2 delta = playerCollider.ClosestPoint(sightOrigin) - sightOrigin;
        if (delta.magnitude > Mathf.Min(detectionRange, beamLength)) return false;
        // 射程内なら左右どちらからでも反応。構えに入った後は方向を固定する。
        RaycastHit2D hit = FirstHit(sightOrigin, delta.normalized, delta.magnitude + 0.02f, false);
        return hit.collider == null || hit.collider.GetComponentInParent<PlayerHealth>() == player;
    }

    private RaycastHit2D FirstHit(Vector2 origin, Vector2 forward, float distance, bool includeMorningStar = true)
    {
        int count = gameObject.scene.GetPhysicsScene2D().Raycast(origin, forward, distance, beamFilter, hits);
        RaycastHit2D nearest = default;
        float limit = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            var hit = hits[i];
            if (hit.collider == null || hit.collider.attachedRigidbody == body || hit.collider.transform.IsChildOf(transform)) continue;
            bool isBall = IsMorningStar(hit.collider);
            if (isBall && !includeMorningStar) continue;
            if ((morningStarLayer.value & (1 << hit.collider.gameObject.layer)) != 0 && !isBall) continue;
            bool isPlayerLayer = (playerLayer.value & (1 << hit.collider.gameObject.layer)) != 0;
            if (isPlayerLayer && !PlayerColliderUtility.IsPlayerBody(hit.collider)) continue;
            if (hit.distance >= limit) continue;
            nearest = hit;
            limit = hit.distance;
        }
        return nearest;
    }

    private void TraceBeam()
    {
        Vector2 forward = Vector2.right * direction;
        RaycastHit2D hit = FirstHit(Origin, forward, Mathf.Max(0.1f, beamLength));
        beamEnd = hit.collider != null ? hit.point : Origin + forward * Mathf.Max(0.1f, beamLength);
        if (beam != null) beam.SetBeam(Origin, beamEnd, beamThickness, beamColor);
        if (hit.collider == null) { lastHitCollider = null; return; }
        if (IsMorningStar(hit.collider))
        {
            // 鉄球の物理・状態へは介入せず、現在の一射を終了する。
            SpawnBurst(environmentHitVfxPrefab, hit.point, hit.normal);
            OneShotAudioUtility.Play2D(audioSource, beamHitSE, beamSEVolume * 0.45f, hit.point);
            StopEffects(false);
            SetState(ActionState.Recovery);
            return;
        }
        bool playerHit = PlayerColliderUtility.IsPlayerBody(hit.collider);
        bool impactDue = hit.collider != lastHitCollider || Time.time >= nextImpact;
        lastHitCollider = hit.collider;
        if (impactDue)
        {
            nextImpact = Time.time + 0.1f;
            SpawnBurst(playerHit ? playerHitVfxPrefab : environmentHitVfxPrefab, hit.point, hit.normal);
            if (playerHit) PlayerImpactCount++; else EnvironmentImpactCount++;
            // SEはヒット先が変わった時と一定間隔だけ。VFXの連続火花と分離する。
            if (StateElapsed < Time.fixedDeltaTime * 1.5f || StateElapsed % 0.3f < Time.fixedDeltaTime)
                OneShotAudioUtility.Play2D(audioSource, beamHitSE, beamSEVolume * 0.45f, hit.point);
        }
        if (playerHit) DamagePlayer(hit.collider, beamDamage);
    }

    private void DamagePlayer(Collider2D target, int amount)
    {
        if (!isActiveAndEnabled || health.CurrentHp <= 0 || health.IsHitStunned || amount <= 0
            || Time.time < nextDamage || !PlayerColliderUtility.IsPlayerBody(target)) return;
        PlayerHealth hp = target.GetComponentInParent<PlayerHealth>();
        if (hp == null || hp.IsDead || hp.IsRespawning || hp.IsInvincible) return;
        int before = hp.CurrentHp;
        hp.TakeDamage(amount, new Vector2(direction, 0.2f).normalized * knockback);
        if (hp.CurrentHp < before) nextDamage = Time.time + Mathf.Max(0.02f, damageInterval);
    }

    private static bool IsMorningStar(Collider2D collider)
    {
        Rigidbody2D ballBody = collider.attachedRigidbody;
        if (ballBody == null) return false;
        var reporter = ballBody.GetComponent<MorningStarCollisionReporter>();
        return reporter != null && reporter.Launcher != null && reporter.Launcher.BallBody == ballBody;
    }

    private bool WithinPatrolRange(int facing)
    {
        float offset = body.position.x - patrolCenterX;
        // 範囲外へノックバックされた場合も、中心へ戻る向きは許可する。
        return facing * offset < Mathf.Max(0.1f, patrolHalfWidth) * Mathf.Abs(transform.lossyScale.x)
            - moveSpeed * Time.fixedDeltaTime;
    }

    private bool IsTerrain(RaycastHit2D hit) => hit.collider != null && hit.collider.attachedRigidbody != body
        && !hit.collider.transform.IsChildOf(transform);

    private bool IsGrounded()
    {
        int count = bodyCollider.Cast(Vector2.down, terrainFilter, hits, 0.05f);
        for (int i = 0; i < count; i++) if (IsTerrain(hits[i]) && hits[i].normal.y > 0.5f) return true;
        return false;
    }

    private bool CanWalk(int facing)
    {
        Vector2 forward = Vector2.right * facing;
        float step = Mathf.Max(probeDistance, moveSpeed * Time.fixedDeltaTime + 0.02f);
        int count = bodyCollider.Cast(forward, terrainFilter, hits, step);
        for (int i = 0; i < count; i++)
            if (IsTerrain(hits[i]) && Vector2.Dot(hits[i].normal, forward) < -0.5f) return false;
        Bounds bounds = bodyCollider.bounds;
        Vector2 edge = new Vector2(bounds.center.x + facing * (bounds.extents.x + step), bounds.min.y + 0.05f);
        count = gameObject.scene.GetPhysicsScene2D().Raycast(edge, Vector2.down, 0.25f, terrainFilter, hits);
        for (int i = 0; i < count; i++) if (IsTerrain(hits[i]) && hits[i].normal.y > 0.5f) return true;
        return false;
    }

    private BeamScorpionBurstVfx SpawnBurst(BeamScorpionBurstVfx prefab, Vector2 point, Vector2 normal, float lifetime = -1f)
    {
        if (prefab == null) return null;
        var effect = Instantiate(prefab, point, Quaternion.identity);
        SceneManager.MoveGameObjectToScene(effect.gameObject, gameObject.scene);
        effect.Play(beamColor, normal, lifetime);
        return effect;
    }

    private void StopEffects(bool afterglow)
    {
        if (charge != null) { Destroy(charge.gameObject); charge = null; }
        if (beam == null) return;
        if (afterglow && afterglowVfxPrefab != null)
        {
            var trail = Instantiate(afterglowVfxPrefab);
            SceneManager.MoveGameObjectToScene(trail.gameObject, gameObject.scene);
            trail.SetBeam(Origin, beamEnd, beamThickness * 0.65f, beamColor);
            trail.FadeOut(0.14f);
        }
        beam.gameObject.SetActive(false);
        Destroy(beam.gameObject);
        beam = null;
    }

    private void OnDrawGizmosSelected()
    {
        float center = Application.isPlaying ? patrolCenterX : transform.position.x;
        float radius = Mathf.Max(0.1f, patrolHalfWidth) * Mathf.Abs(transform.lossyScale.x);
        Gizmos.color = new Color(0.3f, 0.8f, 1f);
        Gizmos.DrawLine(new Vector3(center - radius, transform.position.y), new Vector3(center + radius, transform.position.y));
        Gizmos.color = beamColor;
        if (beamOrigin != null) { Gizmos.DrawWireSphere(beamOrigin.position, 0.08f); Gizmos.DrawRay(beamOrigin.position, Vector3.right * (startFacingRight ? 1 : -1) * beamLength); }
        foreach (var check in new[] { groundCheck, edgeCheck, wallCheck })
            if (check != null) Gizmos.DrawWireSphere(check.position, 0.05f);
    }
}
