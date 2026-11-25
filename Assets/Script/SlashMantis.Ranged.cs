using UnityEngine;

public sealed partial class SlashMantis
{
    [Header("Ranged")]
    [SerializeField, Min(0f)] private float rangedCooldown = 0.55f;
    [SerializeField, Min(0)] private int rangedDamage = 1;
    [SerializeField, Range(0f, 1f), Tooltip("横薙ぎのクリップ上で弾を出す位置。")]
    private float rangedFireTime = 0.5f;
    [SerializeField, Range(0.1f, 1f), Tooltip("鎌を引いて構える区間の再生速度。")]
    private float rangedWindupSpeed = 0.5f;
    [SerializeField, Range(0.1f, 1f), Tooltip("発射後の横薙ぎ区間の再生速度。")]
    private float rangedSweepSpeed = 0.65f;
    [SerializeField, Min(0.1f), Tooltip("この水平距離から準備と復帰を速める。")]
    private float rangedPressureDistance = 6.5f;
    [SerializeField, Range(0.1f, 1f)] private float rangedPressureWindupSpeed = 0.75f;
    [SerializeField, Min(0f)] private float rangedPressureCooldown = 0.30f;
    [SerializeField] private Transform rangedProjectileSpawnPoint;
    [SerializeField] private AnimationClip rangedClip;
    [SerializeField] private SlashMantisProjectile projectilePrefab;
    [SerializeField, Min(0.1f)] private float projectileSpeed = 13.5f;
    [SerializeField, Min(0.1f)] private float projectileDistance = 13f;
    [SerializeField, Min(0.01f)] private float projectileLifetime = 1.2f;
    [SerializeField, Min(0.01f)] private float projectileRadius = 0.35f;

    private const float AimCueTime = 0.22f;
    private const float MaxAimSlope = 1.10f;
    private const float AirAimHeight = 0.45f;
    private const float FollowUpReadTime = 0.16f;
    private const float FollowUpWindup = 0.24f;
    private const float FollowUpSweep = 0.20f;
    private const float LandingShotHeight = 0.65f;
    private static readonly int Ranged = Animator.StringToHash("Base Layer.RangedAttack");

    private ContactFilter2D projectileFilter;
    private Vector2 rangedDirection;
    private bool rangedAimLocked;
    private bool rangedUnderPressure;
    private bool rangedDoubleShot;
    private float followUpShotAt;

    private float ProjectileRange => Mathf.Min(projectileDistance, projectileSpeed * projectileLifetime);

    private bool TryBeginRangedAttack(int direction, bool atDistance)
    {
        if (rangedClip == null || projectilePrefab == null || rangedProjectileSpawnPoint == null) return false;

        Vector2 origin = rangedProjectileSpawnPoint.position;
        if (direction != facing && facingRoot != null)
            origin.x = 2f * facingRoot.position.x - origin.x;
        if (!TryGetRangedAim(origin, direction, out rangedDirection)) return false;

        Face(direction);
        SetHorizontalSpeed(0f);
        rangedUnderPressure = atDistance;
        rangedDoubleShot = health.CurrentHp * 2 <= health.MaxHp;
        rangedAimLocked = false;
        Enter(State.Ranged);
        return true;
    }

    private bool TryGetRangedAim(Vector2 origin, int direction, out Vector2 aim)
    {
        Vector2 delta = TargetCenter - origin;
        var movement = player.GetComponent<Player>();
        if (movement != null && !movement.IsGrounded && delta.y >= AirAimHeight &&
            delta.x * direction >= 0.5f && delta.y <= Mathf.Abs(delta.x) * MaxAimSlope)
        {
            aim = delta.normalized;
            if (CanHitPlayer(origin, aim)) return true;
        }

        aim = Vector2.right * direction;
        return CanHitPlayer(origin, aim);
    }

    private bool CanHitPlayer(Vector2 origin, Vector2 aim) =>
        SlashMantisProjectile.TryGetImpact(PhysicsScene, origin, projectileRadius, aim, ProjectileRange,
            projectileFilter, terrainLayers, casts, out _, out var target) && target == player;

    private void StepRangedAttack()
    {
        float length = Mathf.Max(Time.fixedDeltaTime, rangedClip.length);
        float speed = Mathf.Max(0.1f, rangedUnderPressure ? rangedPressureWindupSpeed : rangedWindupSpeed);
        float windup = length * rangedFireTime / speed;
        float progress = Mathf.Min(rangedFireTime, elapsed * speed / length);
        progress += Mathf.Max(0f, elapsed - windup) * Mathf.Max(0.1f, rangedSweepSpeed) / length;
        ShowPose(Ranged, Mathf.Clamp01(progress));

        if (!rangedAimLocked && elapsed >= windup - AimCueTime)
        {
            rangedAimLocked = true;
            LockRangedAim();
        }

        if (!actionFired && elapsed >= windup)
        {
            actionFired = true;
            if (HasLivePlayerBody)
            {
                FireRangedProjectile();
                if (rangedDoubleShot)
                {
                    rangedAimLocked = false;
                    Enter(State.RangedFollowUp);
                    return;
                }
            }
        }

        if (progress >= Mathf.Max(0.75f, rangedFireTime)) FinishRangedAttack();
    }

    private void LockRangedAim()
    {
        Vector2 origin = rangedProjectileSpawnPoint.position;
        if (!HasLivePlayerBody || !TryGetRangedAim(origin, facing, out Vector2 aim)) return;
        rangedDirection = aim;
        if (aim.y <= 0f) return;

        // 構えが決まる時に一度だけ、ジャンプの勢いを少し先読みする。
        var targetBody = playerBody.attachedRigidbody;
        if (targetBody != null)
        {
            Vector2 lead = targetBody.linearVelocity * 0.20f;
            lead.x = Mathf.Clamp(lead.x, -0.90f, 0.90f);
            lead.y = Mathf.Clamp(lead.y, -0.85f, 0.85f);
            Vector2 point = TargetCenter + lead;
            if (point.y >= origin.y + AirAimHeight && TryAimAtPoint(origin, point, out aim))
                rangedDirection = aim;
        }
        PlayRangedCue(0.38f);
    }

    private void StepRangedFollowUp()
    {
        if (!rangedAimLocked)
        {
            if (elapsed < FollowUpReadTime)
            {
                ShowPose(Ranged, Mathf.Lerp(rangedFireTime, 0.75f, elapsed / FollowUpReadTime));
                return;
            }

            rangedDirection = AimFollowUp(out float delay);
            followUpShotAt = elapsed + delay;
            rangedAimLocked = true;
            PlayRangedCue(0.52f);
        }

        float sinceShot = elapsed - followUpShotAt;
        if (sinceShot < 0f)
        {
            float windup = Mathf.InverseLerp(-FollowUpWindup, 0f, sinceShot);
            ShowPose(Ranged, Mathf.Lerp(Mathf.Min(0.25f, rangedFireTime), rangedFireTime, windup));
            return;
        }

        ShowPose(Ranged, Mathf.Lerp(rangedFireTime, 0.75f, sinceShot / FollowUpSweep));
        if (!actionFired)
        {
            actionFired = true;
            if (HasLivePlayerBody) FireRangedProjectile();
        }
        if (sinceShot >= FollowUpSweep) FinishRangedAttack();
    }

    private Vector2 AimFollowUp(out float delay)
    {
        delay = 0.34f;
        Vector2 flat = Vector2.right * facing;
        if (!HasLivePlayerBody) return flat;

        var movement = player.GetComponent<Player>();
        var targetBody = playerBody.attachedRigidbody;
        if (movement == null || movement.IsGrounded || targetBody == null) return flat;

        Bounds target = playerBody.bounds;
        float feet = target.min.y;
        float x = target.center.x;
        if (!TryGetLandingFloor(x, feet, out float floor)) return flat;

        float landingTime = movement.EstimateLandingTime(feet - floor);
        float leadX = x + Mathf.Clamp(targetBody.linearVelocity.x * landingTime, -1.6f, 1.6f);
        if (Mathf.Abs(leadX - x) > Skin && TryGetLandingFloor(leadX, feet, out float leadFloor))
        {
            x = leadX;
            floor = leadFloor;
            landingTime = movement.EstimateLandingTime(feet - floor);
        }

        Vector2 origin = rangedProjectileSpawnPoint.position;
        Vector2 point = new Vector2(x, floor + LandingShotHeight);
        if (!TryAimAtPoint(origin, point, out Vector2 aim)) return flat;

        // 踏み込む時間を残しながら、鉄球少女の足元に届く時刻を着地へ合わせる。
        float distanceToBody = Mathf.Max(0f, Vector2.Distance(origin, point) - target.extents.x - projectileRadius);
        delay = Mathf.Clamp(landingTime - distanceToBody / projectileSpeed + 0.035f, FollowUpWindup, 0.84f);
        return aim;
    }

    private bool TryGetLandingFloor(float x, float feet, out float height)
    {
        var origin = new Vector2(x, feet + Skin);
        PhysicsScene.Raycast(origin, Vector2.down, ProjectileRange, terrainFilter, casts);
        foreach (var hit in casts)
        {
            if (hit.distance <= 0f || hit.normal.y < 0.5f) continue;
            height = hit.point.y;
            return true;
        }

        height = 0f;
        return false;
    }

    private bool TryAimAtPoint(Vector2 origin, Vector2 point, out Vector2 aim)
    {
        Vector2 delta = point - origin;
        float distance = delta.magnitude;
        aim = delta.normalized;
        return delta.x * facing >= 0.5f && Mathf.Abs(delta.y) <= Mathf.Abs(delta.x) * MaxAimSlope &&
            distance <= ProjectileRange &&
            PhysicsScene.CircleCast(origin, projectileRadius, aim, distance, terrainFilter, casts) == 0;
    }

    private void PlayRangedCue(float volume) =>
        OneShotAudioUtility.Play2D(OneShotAudioUtility.FindWorldImpactSource(), iaiDrawClip, volume,
            rangedProjectileSpawnPoint.position);

    private void FireRangedProjectile()
    {
        var projectile = Instantiate(projectilePrefab, rangedProjectileSpawnPoint.position, Quaternion.identity);
        projectile.Fire(this, rangedDirection, projectileSpeed, projectileDistance, projectileLifetime,
            projectileRadius, rangedDamage, knockback, terrainLayers, playerLayers);
    }

    private void FinishRangedAttack()
    {
        CountNormalAttackForIai();
        if (rangedAttacksUntilDash > 0) rangedAttacksUntilDash--;
        ShowPose(Ranged, BladesDownPose);
        BeginRecovery(rangedUnderPressure ? rangedPressureCooldown : rangedCooldown);
    }
}
