using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

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
    [SerializeField, Min(0f), Tooltip("プレイヤーの本体Colliderの高さを1とした近接半径。")]
    private float meleeRadiusInPlayerHeights = 1f;
    [SerializeField, Min(0f)] private float meleeCooldown = 0.75f;
    [SerializeField, Min(0)] private int meleeDamage = 1;
    [SerializeField, Range(0f, 1f), Tooltip("クリップ上の命中位置。0.5は8コマ中の5コマ目。")]
    private float meleeHitTime = 0.5f;
    [SerializeField, Range(0.1f, 1f), Tooltip("鎌を振り上げる区間の再生速度。")]
    private float meleeWindupSpeed = 0.55f;
    [SerializeField] private Transform meleeAttackOrigin;
    [FormerlySerializedAs("meleeHitSize")]
    [SerializeField, Tooltip("振り下ろしのエフェクトの基準サイズ。")]
    private Vector2 meleeSlashSize = new Vector2(1.8f, 1.45f);
    [SerializeField] private AnimationClip meleeClip;

    [Header("Ranged")]
    [SerializeField, Min(0f)] private float rangedCooldown = 1.1f;
    [SerializeField, Min(0)] private int rangedDamage = 1;
    [SerializeField, Range(0f, 1f), Tooltip("横薙ぎのクリップ上で弾を出す位置。")]
    private float rangedFireTime = 0.5f;
    [SerializeField, Range(0.1f, 1f), Tooltip("鎌を引いて構える区間の再生速度。")]
    private float rangedWindupSpeed = 0.5f;
    [SerializeField, Range(0.1f, 1f), Tooltip("発射後の横薙ぎ区間の再生速度。")]
    private float rangedSweepSpeed = 0.65f;
    [SerializeField] private Transform rangedProjectileSpawnPoint;
    [SerializeField] private AnimationClip rangedClip;
    [SerializeField] private SlashMantisProjectile projectilePrefab;
    [SerializeField, Min(0.1f)] private float projectileSpeed = 13.5f;
    [SerializeField, Min(0.1f)] private float projectileDistance = 8f;
    [SerializeField, Min(0.01f)] private float projectileLifetime = 1.2f;
    [SerializeField, Min(0.01f)] private float projectileRadius = 0.35f;
    [SerializeField, Min(0f)] private float knockback = 4f;

    [Header("Tsujigiri")]
    [SerializeField, Range(1, 5), Tooltip("辻斬りまでに完了する長距離攻撃の回数を、1からこの値までで抽選する。")]
    private int tsujigiriMaxRangedAttacks = 5;
    [SerializeField, Min(0.1f)] private float tsujigiriDashSpeed = 24f;
    [SerializeField, Min(0.01f)] private float tsujigiriWindupDuration = 0.5f;
    [SerializeField, Min(0f), Tooltip("ダッシュ中に姿を隠す最大時間。到着したら姿を表示する。")]
    private float tsujigiriDisappearDuration = 0.14f;
    [SerializeField, Min(0f), Tooltip("到着してから命中を確定するまでの秒数。")]
    private float tsujigiriDamageDelay = 0.2f;
    [SerializeField, Min(0.01f)] private float tsujigiriRecoveryDuration = 0.65f;
    [SerializeField, Min(0)] private int tsujigiriDamage = 2;

    [Header("Visual")]
    [SerializeField] private Transform facingRoot;
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Animator animator;

    [Header("Attack Effects")]
    [SerializeField] private Sprite meleeSlashSprite;
    [SerializeField] private Sprite dashSlashSprite;
    [SerializeField] private Sprite[] dashImpactFrames;
    [SerializeField] private Sprite hitSparkSprite;

    private const float Skin = 0.025f;
    private const float GroundProbe = 0.18f;
    private const float GroundSampleSpacing = 0.18f;
    private const float DashExitClearance = 0.18f;
    private const float FallbackScreenWidth = 18f;
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
    private ContactFilter2D projectileFilter;
    private State state;
    private float elapsed;
    private float homeX;
    private float nextPlayerSearch;
    private int rangedAttacksUntilDash;
    private float recoveryDuration;
    private float dashRemaining;
    private float savedGravity;
    private LayerMask savedExcludedLayers;
    private int facing = -1;
    private int patrolDirection = -1;
    private bool actionFired;
    private bool walkingAnimated;
    private bool dashPhysicsActive;
    private bool spriteHidden;
    private PlayerHealth dashVictim;
    private Collider2D dashVictimBody;
    private int dashVictimRecoveryVersion;
    internal int ProjectileVersion { get; private set; }

    private PhysicsScene2D PhysicsScene => gameObject.scene.GetPhysicsScene2D();

    private void OnValidate()
    {
        meleeSlashSize = Vector2.Max(meleeSlashSize, Vector2.one * 0.01f);
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
        RollRangedAttacksUntilDash();
        nextPlayerSearch = 0f;
        dashVictim = null;
        terrainFilter = new ContactFilter2D { useTriggers = false };
        terrainFilter.SetLayerMask(terrainLayers);
        playerFilter = new ContactFilter2D { useTriggers = true };
        playerFilter.SetLayerMask(playerLayers);
        projectileFilter = SlashMantisProjectile.CreateHitFilter(terrainLayers, playerLayers);
        Enter(State.Walking);
    }

    private void RollRangedAttacksUntilDash()
    {
        rangedAttacksUntilDash = Random.Range(1, Mathf.Clamp(tsujigiriMaxRangedAttacks, 1, 5) + 1);
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
            Vector2 delta = TargetCenter - (Vector2)bodyCollider.bounds.center;
            int toward = delta.x < 0f ? -1 : 1;
            bool near = IsInMeleeRange();
            if (near)
            {
                Face(toward);
                SetHorizontalSpeed(0f);
                ShowWalking(false);
                if (meleeClip != null && HasLineOfSight()) Enter(State.Melee);
                return;
            }

            bool detected = delta.sqrMagnitude <= detectionRange * detectionRange;
            if (rangedAttacksUntilDash == 0)
            {
                Face(toward);
                if (TryGetDashRange(out _, out _))
                {
                    SetHorizontalSpeed(0f);
                    Enter(State.DashWindup);
                    return;
                }
            }

            if (CanShoot(toward))
            {
                Face(toward);
                SetHorizontalSpeed(0f);
                Enter(State.Ranged);
                return;
            }

            if (detected)
            {
                Walk(toward);
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

    private bool IsInMeleeRange()
    {
        float radius = playerBody.bounds.size.y * meleeRadiusInPlayerHeights;
        return (TargetCenter - (Vector2)bodyCollider.bounds.center).sqrMagnitude <= radius * radius;
    }

    private void FindPlayer()
    {
        if (player != null && playerBody != null && playerBody.isActiveAndEnabled) return;
        if (Time.time < nextPlayerSearch) return;
        nextPlayerSearch = Time.time + 0.25f;
        playerBody = null;
        var target = GameObject.FindGameObjectWithTag(PlayerColliderUtility.PlayerTag);
        if (target == null) return;
        player = target.GetComponentInParent<PlayerHealth>();
        if (player == null) return;
        foreach (var collider in player.GetComponentsInChildren<Collider2D>())
        {
            if (!collider.isActiveAndEnabled || collider.isTrigger || !PlayerColliderUtility.IsPlayerBody(collider)) continue;
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

    private bool CanShoot(int direction)
    {
        if (rangedClip == null || projectilePrefab == null || rangedProjectileSpawnPoint == null) return false;
        Vector2 origin = rangedProjectileSpawnPoint.position;
        if (direction != facing && facingRoot != null)
            origin.x = 2f * facingRoot.position.x - origin.x;
        float distance = Mathf.Min(projectileDistance, projectileSpeed * projectileLifetime);
        return SlashMantisProjectile.TryGetImpact(PhysicsScene, origin, projectileRadius, Vector2.right * direction,
            distance, projectileFilter, terrainLayers, casts, out _, out var target) && target == player;
    }

    private void StepNormalAttack(bool melee)
    {
        var clip = melee ? meleeClip : rangedClip;
        float actionTime = melee ? meleeHitTime : rangedFireTime;
        float windupSpeed = Mathf.Max(0.1f, melee ? meleeWindupSpeed : rangedWindupSpeed);
        float time = elapsed / Mathf.Max(Time.fixedDeltaTime, clip.length);
        float windupLength = actionTime / windupSpeed;
        float afterWindup = Mathf.Max(0f, time - windupLength);
        float sweepLength = melee ? 0f : Mathf.Min(0.25f, 1f - actionTime);
        float sweepSpeed = Mathf.Max(0.1f, rangedSweepSpeed);
        float sweepTime = sweepLength / sweepSpeed;
        float progress = Mathf.Min(actionTime, time * windupSpeed);
        progress += Mathf.Min(afterWindup, sweepTime) * sweepSpeed + Mathf.Max(0f, afterWindup - sweepTime);
        progress = Mathf.Clamp01(progress);
        ShowPose(melee ? Melee : Ranged, progress);
        if (!actionFired)
        {
            bool canStrike = IsLive(player) && (!melee ||
                (playerBody != null && playerBody.isActiveAndEnabled &&
                (TargetCenter.x - body.position.x) * facing >= 0f && IsInMeleeRange() && HasLineOfSight()));
            if (!canStrike)
            {
                BeginRecovery(melee ? meleeCooldown : rangedCooldown);
                return;
            }
            // The ranged attack commits to its horizontal aim when windup starts.
            if (time >= windupLength)
            {
                actionFired = true;
                if (melee) HitMelee();
                else FireProjectile();
            }
        }

        if (progress < 1f) return;
        if (!melee && actionFired && rangedAttacksUntilDash > 0)
            rangedAttacksUntilDash--;
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
        SlashMantisEffect.Play(meleeSlashSprite, spriteRenderer, gameObject.scene, center, facing,
            meleeSlashSize + new Vector2(0.12f, 0.3f), 0.23f);
        if (player.TakeEnemyDamage(meleeDamage, Vector2.right * (facing * knockback)))
            PlayHitSpark(playerBody.ClosestPoint(center));
    }

    private void FireProjectile()
    {
        var projectile = Instantiate(projectilePrefab, rangedProjectileSpawnPoint.position,
            facing > 0 ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f));
        projectile.Fire(this, facing, projectileSpeed, projectileDistance, projectileLifetime,
            projectileRadius, rangedDamage, knockback, terrainLayers, playerLayers);
    }

    private bool TryGetDashRange(out float minimum, out float maximum)
    {
        minimum = maximum = 0f;
        if (!IsLive(player) || playerBody == null || !playerBody.isActiveAndEnabled || IsInMeleeRange()) return false;
        var bounds = bodyCollider.bounds;
        var target = playerBody.bounds;
        float forwardDistance = (target.center.x - bounds.center.x) * facing;
        if (forwardDistance <= Skin || target.min.y >= bounds.max.y || target.max.y <= bounds.min.y)
            return false;
        var camera = Camera.main;
        float screenWidth = camera != null && camera.orthographic
            ? 2f * camera.orthographicSize * camera.aspect : FallbackScreenWidth;
        float passDistance = forwardDistance + target.extents.x + bounds.extents.x + DashExitClearance;
        if (passDistance > screenWidth) return false;
        maximum = SafeDashDistance(screenWidth);
        if (maximum < passDistance) return false;

        // Prefer half a screen, but allow shorter crossings when the exit is safe.
        float preferredMinimum = Mathf.Max(screenWidth * 0.5f, passDistance);
        minimum = maximum >= preferredMinimum ? preferredMinimum : passDistance;
        return true;
    }

    private void StepDashWindup()
    {
        float progress = Mathf.Clamp01(elapsed / tsujigiriWindupDuration);
        ShowPose(Tsujigiri, progress * DashPose);
        if (progress < 1f) return;
        if (!TryGetDashRange(out float minimum, out float maximum))
        {
            BeginRecovery(0.2f);
            return;
        }

        dashRemaining = Random.Range(minimum, maximum);
        savedGravity = body.gravityScale;
        savedExcludedLayers = bodyCollider.excludeLayers;
        body.gravityScale = 0f;
        body.linearVelocity = Vector2.zero;
        bodyCollider.excludeLayers = savedExcludedLayers.value | playerLayers.value |
            LayerMask.GetMask("morningstar", "chain");
        dashPhysicsActive = true;
        dashVictim = null;
        dashVictimBody = null;
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
        if (SafeDashDistance(distance) < distance)
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
                if (collider.isTrigger || !PlayerColliderUtility.IsPlayerBody(collider)) continue;
                var target = collider.GetComponentInParent<PlayerHealth>();
                if (!IsLive(target)) continue;
                dashVictim = target;
                dashVictimBody = collider;
                dashVictimRecoveryVersion = target.RecoveryVersion;
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
            if (IsLive(dashVictim) && dashVictim.RecoveryVersion == dashVictimRecoveryVersion)
            {
                Vector2 point = dashVictimBody != null && dashVictimBody.isActiveAndEnabled
                    ? (Vector2)dashVictimBody.bounds.center : (Vector2)dashVictim.transform.position;
                SlashMantisEffect.Play(dashSlashSprite, spriteRenderer, gameObject.scene, point, facing,
                    new Vector2(3.1f, 1.05f), 0.48f, dashImpactFrames);
                if (dashVictim.TakeEnemyDamage(tsujigiriDamage, Vector2.right * (facing * knockback)))
                    PlayHitSpark(point);
            }
        }
        if (poseTime < tsujigiriRecoveryDuration) return;
        dashVictim = null;
        RollRangedAttacksUntilDash();
        Enter(State.Walking);
    }

    private void PlayHitSpark(Vector2 point) =>
        SlashMantisEffect.Play(hitSparkSprite, spriteRenderer, gameObject.scene, point, facing,
            Vector2.one * 0.8f, 0.21f);

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

    private float SafeDashDistance(float distance)
    {
        Bounds bounds = bodyCollider.bounds;
        if (!HasGround(bounds, 0f)) return 0f;
        CastTerrain(bounds, facing, distance);
        foreach (var hit in casts)
            distance = Mathf.Min(distance, Mathf.Max(0f, hit.distance - Skin));
        // Keep the usable part of the path when the full screen width would cross a ledge.
        float supported = 0f;
        while (supported < distance)
        {
            float next = Mathf.Min(distance, supported + GroundSampleSpacing);
            if (!HasGround(bounds, facing * next)) break;
            supported = next;
        }
        return supported;
    }

    private int CastTerrain(Bounds bounds, int direction, float distance)
    {
        Vector2 size = new Vector2(bounds.size.x, Mathf.Max(Skin, bounds.size.y - 2f * Skin));
        return PhysicsScene.BoxCast(bounds.center, size, 0f, Vector2.right * direction,
            distance + Skin, terrainFilter, casts);
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
        return CastTerrain(bounds, direction, step) == 0 &&
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
