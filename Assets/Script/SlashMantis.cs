using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D), typeof(EnemyHealth))]
public sealed class SlashMantis : MonoBehaviour
{
    private enum State { Walking, Melee, Ranged, Recover, DashWindup, Dash, DashRecovery, Stunned, Dead }

    [Header("Movement")]
    [SerializeField, Min(0f), Tooltip("地上の移動速度。")] private float walkSpeed = 1.8f;
    [SerializeField, Min(0f), Tooltip("この距離以内のプレイヤーに接近する。")] private float detectionRange = 10f;
    [SerializeField, Min(0f), Tooltip("初期位置からの巡回幅の半分。")] private float patrolHalfRange = 2f;
    [SerializeField] private LayerMask terrainLayers = (1 << 0) | (1 << 6);
    [SerializeField] private LayerMask playerLayers = 1 << 3;

    [Header("Melee")]
    [SerializeField, Min(0f)] private float meleeRange = 2.1f;
    [SerializeField, Min(0f)] private float meleeWeight = 3f;
    [SerializeField, Min(0f)] private float meleeCooldown = 0.75f;
    [SerializeField, Min(0)] private int meleeDamage = 1;
    [SerializeField, Range(0f, 1f), Tooltip("クリップ上の命中位置。0.5は8コマ中の5コマ目。")]
    private float meleeHitTime = 0.5f;
    [SerializeField] private Transform meleeAttackOrigin;
    [SerializeField] private Vector2 meleeHitSize = new Vector2(1.8f, 1.45f);
    [SerializeField] private AnimationClip meleeClip;

    [Header("Ranged")]
    [SerializeField, Min(0f)] private float rangedMinRange = 1.5f;
    [SerializeField, Min(0f)] private float rangedRange = 7f;
    [SerializeField, Min(0f)] private float rangedWeight = 2f;
    [SerializeField, Min(0f)] private float rangedCooldown = 1.1f;
    [SerializeField, Min(0)] private int rangedDamage = 1;
    [SerializeField, Range(0f, 1f), Tooltip("横薙ぎのクリップ上で弾を出す位置。")]
    private float rangedFireTime = 0.5f;
    [SerializeField] private Transform rangedProjectileSpawnPoint;
    [SerializeField] private AnimationClip rangedClip;
    [SerializeField] private SlashMantisProjectile projectilePrefab;
    [SerializeField, Min(0.1f)] private float projectileSpeed = 9f;
    [SerializeField, Min(0.1f)] private float projectileDistance = 8f;
    [SerializeField, Min(0.01f)] private float projectileLifetime = 1.2f;
    [SerializeField, Min(0.01f)] private float projectileRadius = 0.35f;
    [SerializeField, Min(0f)] private float knockback = 4f;

    [Header("Tsujigiri")]
    [SerializeField, Min(1), Tooltip("完了した通常攻撃だけを数える。抽選範囲は両端を含む。")]
    private int tsujigiriMinAttackCount = 2;
    [SerializeField, Min(1)] private int tsujigiriMaxAttackCount = 4;
    [SerializeField, Min(0f)] private float tsujigiriDetectionRange = 5f;
    [SerializeField, Min(0f), Tooltip("プレイヤーの反対側へ抜けるために使える最大距離。")]
    private float tsujigiriDashDistance = 6f;
    [SerializeField, Min(0.1f)] private float tsujigiriDashSpeed = 24f;
    [SerializeField, Min(0.01f)] private float tsujigiriWindupDuration = 0.5f;
    [SerializeField, Min(0f), Tooltip("ダッシュ中に姿を隠す最大時間。到着したら姿を表示する。")]
    private float tsujigiriDisappearDuration = 0.14f;
    [SerializeField, Min(0f), Tooltip("到着してから命中を確定するまでの秒数。")]
    private float tsujigiriDamageDelay = 0.2f;
    [SerializeField, Min(0.01f)] private float tsujigiriRecoveryDuration = 0.65f;
    [SerializeField, Min(0f)] private float tsujigiriCooldown = 3f;
    [SerializeField, Min(0)] private int tsujigiriDamage = 2;

    [Header("Visual")]
    [SerializeField] private Transform facingRoot;
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Animator animator;

    private const float Skin = 0.025f;
    private const float GroundProbe = 0.18f;
    private const float GroundSampleSpacing = 0.18f;
    private const float DashExitClearance = 0.18f;
    private const float DashPose = 3f / 8f;
    private const float DashStrikePose = 4f / 8f;
    private static readonly int Walking = Animator.StringToHash("Base Layer.Walking");
    private static readonly int Melee = Animator.StringToHash("Base Layer.MeleeAttack");
    private static readonly int Ranged = Animator.StringToHash("Base Layer.RangedAttack");
    private static readonly int Tsujigiri = Animator.StringToHash("Base Layer.Tsujigiri");

    private readonly List<RaycastHit2D> casts = new List<RaycastHit2D>(16);
    private readonly List<Collider2D> overlaps = new List<Collider2D>(8);
    private Rigidbody2D body;
    private BoxCollider2D bodyCollider;
    private EnemyHealth health;
    private PlayerHealth player;
    private Collider2D playerBody;
    private ContactFilter2D terrainFilter;
    private ContactFilter2D playerFilter;
    private State state;
    private float elapsed;
    private float homeX;
    private float nextPlayerSearch;
    private float nextDashTime;
    private float recoveryDuration;
    private float dashRemaining;
    private float savedGravity;
    private LayerMask savedExcludedLayers;
    private int facing = -1;
    private int patrolDirection = -1;
    private int normalAttackCount;
    private int attacksUntilDash;
    private bool actionFired;
    private bool walkingAnimated;
    private bool dashPhysicsActive;
    private bool spriteHidden;
    private PlayerHealth dashVictim;
    internal int ProjectileVersion { get; private set; }

    private PhysicsScene2D PhysicsScene => gameObject.scene.GetPhysicsScene2D();

    private void OnValidate()
    {
        tsujigiriMinAttackCount = Mathf.Clamp(tsujigiriMinAttackCount, 1, int.MaxValue - 1);
        tsujigiriMaxAttackCount = Mathf.Clamp(tsujigiriMaxAttackCount, tsujigiriMinAttackCount, int.MaxValue - 1);
        rangedMinRange = Mathf.Min(rangedMinRange, rangedRange);
        meleeHitSize = Vector2.Max(meleeHitSize, Vector2.one * 0.01f);
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        bodyCollider = GetComponent<BoxCollider2D>();
        health = GetComponent<EnemyHealth>();
        homeX = body.position.x;
        health.HitConfirmed += OnHit;
        health.HealthReset += ResetAI;
    }

    private void OnEnable() => ResetAI();

    private void OnDisable()
    {
        RestoreDash();
        dashVictim = null;
        ProjectileVersion++;
    }

    private void OnDestroy()
    {
        health.HitConfirmed -= OnHit;
        health.HealthReset -= ResetAI;
    }

    private void ResetAI()
    {
        RestoreDash();
        ProjectileVersion++;
        normalAttackCount = 0;
        RollDashThreshold();
        nextDashTime = 0f;
        nextPlayerSearch = 0f;
        dashVictim = null;
        terrainFilter = new ContactFilter2D { useTriggers = false };
        terrainFilter.SetLayerMask(terrainLayers);
        playerFilter = new ContactFilter2D { useTriggers = true };
        playerFilter.SetLayerMask(playerLayers);
        Enter(State.Walking);
    }

    private void RollDashThreshold()
    {
        int min = Mathf.Clamp(tsujigiriMinAttackCount, 1, int.MaxValue - 1);
        int max = Mathf.Clamp(tsujigiriMaxAttackCount, min, int.MaxValue - 1);
        attacksUntilDash = Random.Range(min, max + 1);
    }

    private void OnHit(MorningStarHitContext hit, bool lethal)
    {
        RestoreDash();
        dashVictim = null;
        ProjectileVersion++;
        Enter(lethal ? State.Dead : State.Stunned);
    }

    private void FixedUpdate()
    {
        if (health.CurrentHp <= 0 || health.IsDeathHandled || health.IsHitStunned) return;
        elapsed += Time.fixedDeltaTime;
        switch (state)
        {
            case State.Walking: StepWalking(); break;
            case State.Melee: StepNormalAttack(true); break;
            case State.Ranged: StepNormalAttack(false); break;
            case State.Recover:
                if (elapsed >= recoveryDuration) Enter(State.Walking);
                break;
            case State.DashWindup: StepDashWindup(); break;
            case State.Dash: StepDash(); break;
            case State.DashRecovery: StepDashRecovery(); break;
            case State.Stunned: Enter(State.Walking); break;
        }
    }

    private void Enter(State next)
    {
        state = next;
        elapsed = 0f;
        actionFired = false;
        walkingAnimated = false;
        if (next == State.Walking) ShowPose(Walking, 0f);
        if (next == State.Melee) ShowPose(Melee, 0f);
        if (next == State.Ranged) ShowPose(Ranged, 0f);
        if (next == State.DashWindup) ShowPose(Tsujigiri, 0f);
        if (next == State.Stunned || next == State.Dead)
            if (animator != null) animator.speed = 0f;
    }

    private void StepWalking()
    {
        FindPlayer();
        if (!HasGround(bodyCollider.bounds, 0f))
        {
            SetHorizontalSpeed(0f);
            ShowWalking(false);
            return;
        }

        if (IsLive(player) && playerBody != null && playerBody.isActiveAndEnabled)
        {
            Vector2 delta = TargetCenter - body.position;
            if (delta.magnitude <= detectionRange)
            {
                Face(delta.x < 0f ? -1 : 1);
                if (normalAttackCount >= attacksUntilDash && Time.time >= nextDashTime && TryPlanDash(out _))
                {
                    SetHorizontalSpeed(0f);
                    Enter(State.DashWindup);
                    return;
                }

                bool clear = HasLineOfSight();
                float melee = clear && meleeClip != null && delta.magnitude <= meleeRange ? meleeWeight : 0f;
                float ranged = clear && CanShoot() ? rangedWeight : 0f;
                if (melee > 0f || ranged > 0f)
                {
                    SetHorizontalSpeed(0f);
                    bool chooseMelee = ranged <= 0f || (melee > 0f && Random.Range(0f, melee + ranged) < melee);
                    Enter(chooseMelee ? State.Melee : State.Ranged);
                    return;
                }

                Walk(facing);
                return;
            }
        }

        if (patrolHalfRange <= 0f)
        {
            Walk(0);
            return;
        }
        if ((body.position.x - homeX) * patrolDirection >= patrolHalfRange || !CanWalk(patrolDirection))
            patrolDirection = -patrolDirection;
        Walk(patrolDirection);
    }

    private Vector2 TargetCenter => playerBody.bounds.center;

    private void FindPlayer()
    {
        if (player != null && playerBody != null) return;
        if (Time.time < nextPlayerSearch) return;
        nextPlayerSearch = Time.time + 0.25f;
        var target = GameObject.FindGameObjectWithTag(PlayerColliderUtility.PlayerTag);
        if (target == null) return;
        player = target.GetComponentInParent<PlayerHealth>();
        if (player == null) return;
        foreach (var collider in player.GetComponentsInChildren<Collider2D>())
        {
            if (!PlayerColliderUtility.IsPlayerBody(collider)) continue;
            playerBody = collider;
            break;
        }
    }

    private static bool IsLive(PlayerHealth target) =>
        target != null && target.isActiveAndEnabled && !target.IsDead && !target.IsRespawning;

    private bool HasLineOfSight()
    {
        Vector2 origin = bodyCollider.bounds.center;
        Vector2 delta = TargetCenter - origin;
        return ClearRay(origin, delta);
    }

    private bool ClearRay(Vector2 origin, Vector2 delta) =>
        delta.sqrMagnitude < 0.0001f ||
        PhysicsScene.Raycast(origin, delta.normalized, delta.magnitude, terrainFilter, casts) == 0;

    private bool CanShoot()
    {
        if (rangedClip == null || projectilePrefab == null || rangedProjectileSpawnPoint == null) return false;
        float distance = Vector2.Distance(TargetCenter, body.position);
        Bounds bounds = playerBody.bounds;
        float shotY = rangedProjectileSpawnPoint.position.y;
        return distance >= rangedMinRange && distance <= rangedRange &&
            shotY >= bounds.min.y - projectileRadius && shotY <= bounds.max.y + projectileRadius;
    }

    private void StepNormalAttack(bool melee)
    {
        var clip = melee ? meleeClip : rangedClip;
        float progress = Mathf.Clamp01(elapsed / Mathf.Max(Time.fixedDeltaTime, clip.length));
        ShowPose(melee ? Melee : Ranged, progress);
        if (!actionFired)
        {
            bool targetInFront = IsLive(player) && playerBody != null && playerBody.isActiveAndEnabled &&
                (TargetCenter.x - body.position.x) * facing >= 0f;
            if (!targetInFront || !(melee ? Vector2.Distance(TargetCenter, body.position) <= meleeRange : CanShoot()) ||
                !HasLineOfSight())
            {
                BeginRecovery(melee ? meleeCooldown : rangedCooldown);
                return;
            }
            if (progress >= (melee ? meleeHitTime : rangedFireTime))
            {
                actionFired = true;
                if (melee) HitMelee();
                else FireProjectile();
            }
        }

        if (progress < 1f) return;
        if (normalAttackCount < attacksUntilDash) normalAttackCount++;
        BeginRecovery(melee ? meleeCooldown : rangedCooldown);
    }

    private void BeginRecovery(float duration)
    {
        recoveryDuration = duration;
        Enter(State.Recover);
    }

    private void HitMelee()
    {
        if (meleeAttackOrigin == null) return;
        Vector2 center = meleeAttackOrigin.position;
        PhysicsScene.OverlapBox(center, meleeHitSize, 0f, playerFilter, overlaps);
        foreach (var collider in overlaps)
        {
            if (!PlayerColliderUtility.IsPlayerBody(collider)) continue;
            var target = collider.GetComponentInParent<PlayerHealth>();
            if (!IsLive(target) || !ClearRay(bodyCollider.bounds.center,
                (Vector2)collider.bounds.center - (Vector2)bodyCollider.bounds.center)) continue;
            target.TakeEnemyDamage(meleeDamage, Vector2.right * (facing * knockback));
            break;
        }
    }

    private void FireProjectile()
    {
        var projectile = Instantiate(projectilePrefab, rangedProjectileSpawnPoint.position,
            facing > 0 ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f));
        projectile.Fire(this, facing, projectileSpeed, projectileDistance, projectileLifetime,
            projectileRadius, rangedDamage, knockback, terrainLayers, playerLayers);
    }

    private bool TryPlanDash(out float distance)
    {
        distance = 0f;
        if (!IsLive(player) || playerBody == null || !playerBody.isActiveAndEnabled) return false;
        var bounds = bodyCollider.bounds;
        var target = playerBody.bounds;
        Vector2 delta = target.center - bounds.center;
        float forwardDistance = delta.x * facing;
        if (forwardDistance <= Skin || delta.magnitude > tsujigiriDetectionRange ||
            target.min.y >= bounds.max.y || target.max.y <= bounds.min.y) return false;
        distance = forwardDistance + target.extents.x + bounds.extents.x + DashExitClearance;
        return distance <= tsujigiriDashDistance && IsDashPathSafe(distance);
    }

    private void StepDashWindup()
    {
        float progress = Mathf.Clamp01(elapsed / tsujigiriWindupDuration);
        ShowPose(Tsujigiri, progress * DashPose);
        if (progress < 1f) return;
        if (!TryPlanDash(out dashRemaining))
        {
            BeginRecovery(0.2f);
            return;
        }

        savedGravity = body.gravityScale;
        savedExcludedLayers = bodyCollider.excludeLayers;
        body.gravityScale = 0f;
        body.linearVelocity = Vector2.zero;
        bodyCollider.excludeLayers = savedExcludedLayers.value | playerLayers.value |
            LayerMask.GetMask("morningstar", "chain");
        dashPhysicsActive = true;
        dashVictim = null;
        if (tsujigiriDisappearDuration > 0f && spriteRenderer != null && spriteRenderer.enabled)
        {
            spriteRenderer.enabled = false;
            spriteHidden = true;
        }
        Enter(State.Dash);
    }

    private void StepDash()
    {
        if (elapsed >= tsujigiriDisappearDuration) Reveal();
        float distance = Mathf.Min(dashRemaining, tsujigiriDashSpeed * Time.fixedDeltaTime);
        if (!IsDashPathSafe(distance))
        {
            RestoreDash();
            dashVictim = null;
            BeginRecovery(0.2f);
            return;
        }

        Bounds bounds = bodyCollider.bounds;
        Vector2 direction = Vector2.right * facing;
        // Query the complete swept rectangle before updating the body's position.
        if (dashVictim == null)
        {
            Vector2 size = new Vector2(bounds.size.x + distance, bounds.size.y);
            PhysicsScene.OverlapBox((Vector2)bounds.center + direction * (distance * 0.5f),
                size, 0f, playerFilter, overlaps);
            foreach (var collider in overlaps)
            {
                if (!PlayerColliderUtility.IsPlayerBody(collider)) continue;
                var target = collider.GetComponentInParent<PlayerHealth>();
                if (!IsLive(target)) continue;
                dashVictim = target;
                break;
            }
        }
        body.position += direction * distance;
        dashRemaining -= distance;
        if (dashRemaining > 0.0001f) return;

        RestoreDash();
        Enter(State.DashRecovery);
        ShowPose(Tsujigiri, DashStrikePose);
    }

    private void StepDashRecovery()
    {
        float poseTime = Mathf.Max(0f, elapsed - tsujigiriDamageDelay);
        ShowPose(Tsujigiri, Mathf.Lerp(DashStrikePose, 1f,
            Mathf.Clamp01(poseTime / tsujigiriRecoveryDuration)));
        if (!actionFired && elapsed >= tsujigiriDamageDelay)
        {
            actionFired = true;
            if (IsLive(dashVictim))
                dashVictim.TakeEnemyDamage(tsujigiriDamage, Vector2.right * (facing * knockback));
        }
        if (poseTime < tsujigiriRecoveryDuration) return;
        dashVictim = null;
        normalAttackCount = 0;
        RollDashThreshold();
        nextDashTime = Time.time + tsujigiriCooldown;
        Enter(State.Walking);
    }

    private void RestoreDash()
    {
        if (dashPhysicsActive)
        {
            body.gravityScale = savedGravity;
            bodyCollider.excludeLayers = savedExcludedLayers;
            dashPhysicsActive = false;
        }
        Reveal();
    }

    private void Reveal()
    {
        if (!spriteHidden) return;
        spriteRenderer.enabled = true;
        spriteHidden = false;
    }

    private bool IsDashPathSafe(float distance)
    {
        Bounds bounds = bodyCollider.bounds;
        if (!HasGround(bounds, 0f) || TerrainBlocks(bounds, facing, distance)) return false;
        int samples = Mathf.Max(1, Mathf.CeilToInt(distance / GroundSampleSpacing));
        for (int i = 1; i <= samples; i++)
        {
            if (!HasGround(bounds, facing * distance * i / samples)) return false;
        }
        return true;
    }

    private bool TerrainBlocks(Bounds bounds, int direction, float distance)
    {
        Vector2 size = new Vector2(bounds.size.x, Mathf.Max(Skin, bounds.size.y - 2f * Skin));
        return PhysicsScene.BoxCast(bounds.center, size, 0f, Vector2.right * direction,
            distance + Skin, terrainFilter, casts) > 0;
    }

    private bool HasGround(Bounds bounds, float offset)
    {
        Vector2 foot = new Vector2(bounds.center.x + offset, bounds.min.y + Skin);
        PhysicsScene.Raycast(foot, Vector2.down, GroundProbe, terrainFilter, casts);
        foreach (var hit in casts)
            if (hit.normal.y > 0.5f && hit.distance > 0f) return true;
        return false;
    }

    private bool CanWalk(int direction)
    {
        Bounds bounds = bodyCollider.bounds;
        float step = Mathf.Max(0.06f, walkSpeed * Time.fixedDeltaTime);
        return !TerrainBlocks(bounds, direction, step) &&
            HasGround(bounds, (bounds.extents.x + step) * direction);
    }

    private void Walk(int direction)
    {
        if (direction != 0) Face(direction);
        float speed = direction != 0 && CanWalk(direction) ? direction * walkSpeed : 0f;
        SetHorizontalSpeed(speed);
        ShowWalking(speed != 0f);
    }

    private void SetHorizontalSpeed(float speed) =>
        body.linearVelocity = new Vector2(speed, body.linearVelocity.y);

    private void Face(int direction)
    {
        facing = direction;
        if (facingRoot == null) return;
        Vector3 scale = facingRoot.localScale;
        scale.x = Mathf.Abs(scale.x) * -direction;
        facingRoot.localScale = scale;
    }

    private void ShowWalking(bool moving)
    {
        if (animator == null || walkingAnimated == moving) return;
        walkingAnimated = moving;
        if (moving)
        {
            animator.Play(Walking, 0, 0f);
            animator.speed = 1f;
        }
        else ShowPose(Walking, 0f);
    }

    // Taking the sprite pose from the attack clock keeps the contact frame aligned
    // when the clip length changes and needs no Animation Events.
    private void ShowPose(int animation, float progress)
    {
        if (animator == null) return;
        animator.speed = 0f;
        animator.Play(animation, 0, progress);
    }
}
