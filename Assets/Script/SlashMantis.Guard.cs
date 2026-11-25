using UnityEngine;

public sealed partial class SlashMantis
{
    private static readonly int GuardPose = Animator.StringToHash("Base Layer.CrossGuard");
    private static readonly int ShieldImpact = Shader.PropertyToID("_Impact");
    private static readonly int ShieldContact = Shader.PropertyToID("_Contact");
    private static readonly Color GroundSparks = new Color(1f, 0.82f, 0.40f);
    private const float GuardFlashDuration = 0.26f;
    private const float GuardSlashStepTime = 0.16f;
    private const float SecondSlashDelay = 0.34f;
    private const float RaisedBladesPose = 3.5f / 8f;

    [Header("Cross Guard")]
    [SerializeField, Min(1)] private int attacksBetweenGuards = 2;
    [SerializeField, Min(0.01f)] private float openingGuardWindup = 0.46f;
    [SerializeField, Min(0.01f)] private float guardWindup = 0.24f;
    [SerializeField, Min(0.01f)] private float guardDuration = 0.80f;
    [SerializeField, Min(0f)] private float guardAdvanceSpeed = 3f;
    [SerializeField, Min(0.01f)] private float counterGuardWindup = 0.18f;
    [SerializeField] private MeshRenderer crossGuardShield;

    [Header("Guard Follow-up")]
    [SerializeField, Min(0.01f)] private float guardSlashWindup = 0.68f;
    [SerializeField, Min(0.01f)] private float guardComboWindup = 0.86f;
    [SerializeField, Min(0f), Tooltip("振り下ろす直前に踏み込むワールド距離。")]
    private float guardSlashStepDistance = 0.95f;
    [SerializeField, Min(0.1f), Tooltip("振り下ろしの正面距離。胴体の中心から測る。")]
    private float guardSlashReach = 2.25f;
    [SerializeField, Min(0.1f), Tooltip("振り下ろしの高さ。ここより上へ跳べばかわすことができる。")]
    private float guardSlashHeight = 1.15f;
    [SerializeField, Min(0)] private int guardSlashDamage = 2;
    [SerializeField, Min(0.01f)] private float guardSlashRecovery = 0.50f;
    [SerializeField, Min(0.01f)] private float guardSlashMissRecovery = 1.15f;
    [SerializeField] private AudioClip guardSlashLandingClip;
    [SerializeField, Min(0f), Tooltip("正面を弾いた後に返しの辻斬りを選ぶ最大距離。")]
    private float guardCounterDashRange = 7f;
    [SerializeField, Min(0.1f), Tooltip("返しの辻斬りが通る床からの高さ。")]
    private float guardDashStrikeHeight = 1.25f;

    private int attacksUntilGuard;
    private bool hasOpenedWithGuard;
    private bool blockedDuringGuard;
    private float currentGuardWindup;
    private float guardFlashUntil;
    private Vector2 shieldContact;
    private MaterialPropertyBlock shieldProperties;
    private bool guardSlashCombo;
    private bool guardSlashContactedPlayer;

    private bool IsShieldUp => crossGuardShield != null && crossGuardShield.enabled &&
        crossGuardShield.gameObject.activeInHierarchy;

    private bool KeepsGuardShield(State next) => next is State.CrossGuard or State.GuardSlashWindup
        || next == State.DashWindup && guardCounterDash;

    bool IEnemyMorningStarHitGuard.TryGuardMorningStarHit(MorningStarHitContext hit)
    {
        // 接触の面を見る。真上から落とした鉄球や背後から戻した鉄球はガードを抜ける。
        if (!isActiveAndEnabled || !IsShieldUp || -hit.ImpactDirection.x * facing < 0.5f)
            return false;

        if (state == State.CrossGuard) blockedDuringGuard = true;
        PlayCrossGuardImpact(hit);
        return true;
    }

    int IEnemyMorningStarDamageModifier.ModifyMorningStarDamage(MorningStarHitContext hit, int damage, int currentHp)
    {
        return Mathf.Min(damage, IsShieldUp || HasCounterOpening ? 2 : 1);
    }

    private bool TryBeginCrossGuard(int direction)
    {
        if (attacksUntilGuard > 0 || !CanUseCrossGuard() ||
            !HasLineOfSight() || playerBody.bounds.min.y >= bodyCollider.bounds.max.y)
            return false;

        BeginGuardWindup(direction, hasOpenedWithGuard ? guardWindup : openingGuardWindup);
        return true;
    }

    private bool CanUseCrossGuard() => isActiveAndEnabled && animator != null && animator.isActiveAndEnabled &&
        animator.HasState(0, GuardPose) && crossGuardShield != null && crossGuardShield.gameObject.activeInHierarchy;

    private bool TryRearmGuard()
    {
        // 居合のすれ違いで既に触れた相手への一撃は、構え直す前に確定する。
        if (state == State.IaiRecovery && !actionFired && iaiVictim != null) return false;
        if (!CanUseCrossGuard() || !HasGround(bodyCollider.bounds, 0f)) return false;
        if (state == State.DashRecovery)
        {
            ClearDashAttack();
            RollRangedAttacksUntilDash();
        }
        else if (state == State.IaiRecovery)
        {
            CancelIai();
        }
        int direction = facing;
        if (HasLivePlayerBody)
        {
            float offset = TargetCenter.x - bodyCollider.bounds.center.x;
            if (Mathf.Abs(offset) > Skin) direction = (int)Mathf.Sign(offset);
        }
        BeginGuardWindup(direction, counterGuardWindup);
        return true;
    }

    private void BeginGuardWindup(int direction, float duration)
    {
        Face(direction);
        SetHorizontalSpeed(0f);
        currentGuardWindup = duration;
        hasOpenedWithGuard = true;
        blockedDuringGuard = false;
        attacksUntilGuard = Mathf.Max(1, attacksBetweenGuards);
        Enter(State.GuardWindup);
        ShowPose(Melee, 0f);
    }

    private void StepGuardWindup()
    {
        if (elapsed < currentGuardWindup)
        {
            ShowPose(Melee, Mathf.Lerp(0f, 0.31f, elapsed / currentGuardWindup));
            return;
        }

        Enter(State.CrossGuard);
        ShowPose(GuardPose, 0f);
        // 当たり判定と同じ物理ステップからX字の構えを表示する。
        animator.Update(0f);
        if (crossGuardShield != null) crossGuardShield.enabled = true;
        UpdateGuardPresentation();
        PlayGuardAudio(iaiGuardClip, 0.32f, bodyCollider.bounds.center);
    }

    private void StepCrossGuard()
    {
        float stepTime = Mathf.Min(elapsed, guardDuration) - Mathf.Min(elapsed - Time.fixedDeltaTime, guardDuration);
        AdvanceBehindGuard(guardAdvanceSpeed * stepTime);
        if (elapsed < guardDuration) return;
        if (!TryBeginGuardSlash() && !TryBeginGuardCounterDash()) Enter(State.Walking);
        animator.Update(0f);
    }

    private void AdvanceBehindGuard(float requested)
    {
        if (requested <= 0f || !HasLivePlayerBody || !HasLineOfSight()) return;
        Bounds hull = bodyCollider.bounds;
        Bounds target = playerBody.bounds;
        float ahead = (target.center.x - hull.center.x) * facing;
        if (ahead <= Skin || target.min.y >= hull.max.y || target.max.y <= hull.min.y) return;

        float availableStrikeStep = GetGroundedGuardStep(hull, guardSlashStepDistance);
        float gap = ahead - target.extents.x - guardSlashReach - availableStrikeStep;
        if (gap > 0f) AdvanceGuardStep(Mathf.Min(requested, gap));
    }

    private bool TryBeginGuardSlash()
    {
        if (meleeClip == null || !HasLivePlayerBody) return false;
        // 床の継ぎ目などで踏み出せない場合は、実際に届く距離を使う。
        float reachable = guardSlashReach + GetGroundedGuardStep(bodyCollider.bounds, guardSlashStepDistance);
        if (!TryGetGuardSlashTarget(reachable + 0.001f, guardSlashHeight + 0.4f))
            return false;

        guardSlashCombo = rangedClip != null && health.CurrentHp * 2 <= health.MaxHp;
        guardSlashContactedPlayer = false;
        Enter(State.GuardSlashWindup);
        ShowPose(guardSlashCombo ? Ranged : Melee, guardSlashCombo ? RaisedBladesPose : 0.25f);
        PlayGuardAudio(iaiDrawClip, 0.42f, bodyCollider.bounds.center);
        return true;
    }

    private bool TryBeginGuardCounterDash()
    {
        if (!blockedDuringGuard || !HasLivePlayerBody || !HasLineOfSight()) return false;
        Bounds hull = bodyCollider.bounds;
        Bounds target = playerBody.bounds;
        float ahead = (target.center.x - hull.center.x) * facing;
        if (ahead <= Skin || ahead > guardCounterDashRange ||
            target.min.y >= hull.max.y || target.max.y <= hull.min.y || IsInMeleeRange())
            return false;

        float distance = ahead + hull.extents.x + target.extents.x + DashExitClearance;
        if (!HasSafeGuardDashPath(distance)) return false;

        guardCounterDash = true;
        guardDashDestinationX = body.position.x + facing * distance;
        Enter(State.DashWindup);
        return true;
    }

    private bool HasSafeGuardDashPath(float distance) =>
        SafeRaisedDashDistance(distance, DashFloorClearance) + 0.001f >= distance;

    private void StepGuardSlashWindup()
    {
        float duration = guardSlashCombo ? guardComboWindup : guardSlashWindup;
        float progress = Mathf.Clamp01(elapsed / duration);
        if (guardSlashCombo && !actionFired && progress >= 0.50f)
        {
            actionFired = true;
            PlayGuardAudio(iaiDrawClip, 0.50f, bodyCollider.bounds.center);
        }
        if (!guardSlashCombo) ShowPose(Melee, Mathf.Lerp(0.25f, RaisedBladesPose, progress));
        if (progress < 1f) return;

        Enter(State.GuardSlash);
        ShowPose(Melee, RaisedBladesPose);
    }

    private void StepGuardSlash()
    {
        if (!actionFired)
        {
            float stepTime = Mathf.Min(elapsed, GuardSlashStepTime)
                - Mathf.Min(elapsed - Time.fixedDeltaTime, GuardSlashStepTime);
            AdvanceGuardStep(guardSlashStepDistance * stepTime / GuardSlashStepTime);
            if (elapsed < GuardSlashStepTime) return;

            actionFired = true;
            guardSlashContactedPlayer = HitGuardSlash();
            ShowPose(Melee, BladesDownPose);
            if (!guardSlashCombo) FinishGuardSlash();
            return;
        }

        float sinceFirstSlash = elapsed - GuardSlashStepTime;
        if (sinceFirstSlash < SecondSlashDelay)
        {
            if (sinceFirstSlash >= 0.10f) ShowPose(Ranged, RaisedBladesPose);
            return;
        }

        guardSlashContactedPlayer |= HitGuardSlash();
        FinishGuardSlash();
    }

    private void AdvanceGuardStep(float requested)
    {
        if (requested <= 0f) return;
        float distance = GetGroundedGuardStep(bodyCollider.bounds, requested);
        body.position += Vector2.right * (facing * distance);
    }

    private float GetGroundedGuardStep(Bounds hull, float requested)
    {
        if (requested <= 0f) return 0f;
        float distance = SafeTravelOnGround(requested, hull, GroundProbe);
        if (HasLivePlayerBody)
        {
            Bounds target = playerBody.bounds;
            float ahead = (target.center.x - hull.center.x) * facing;
            if (ahead > 0f && target.min.y < hull.max.y && target.max.y > hull.min.y)
                distance = Mathf.Min(distance, Mathf.Max(0f, ahead - hull.extents.x - target.extents.x - Skin));
        }
        return distance;
    }

    private bool TryGetGuardSlashTarget(float reach, float height)
    {
        Bounds hull = bodyCollider.bounds;
        Bounds target = playerBody.bounds;
        float ahead = (target.center.x - hull.center.x) * facing;
        return ahead > 0f && ahead - target.extents.x <= reach &&
            target.min.y < hull.min.y + height && target.max.y > hull.min.y && HasLineOfSight();
    }

    private bool HitGuardSlash()
    {
        Bounds hull = bodyCollider.bounds;
        Vector2 center = new Vector2(hull.center.x + facing * guardSlashReach * 0.59f,
            hull.min.y + guardSlashHeight * 0.64f);
        SlashMantisEffect.Play(meleeSlashSprite, spriteRenderer, gameObject.scene, center, facing,
            new Vector2(guardSlashReach * 0.9f, guardSlashHeight * 1.4f) / SlashMantisEffect.SizeMultiplier,
            0.30f, tint: new Color(1f, 0.92f, 0.77f));

        Vector2 tip = new Vector2(hull.center.x + facing * guardSlashReach * 0.73f, hull.min.y + 0.07f);
        if (HasGround(hull, tip.x - hull.center.x))
        {
            SlashMantisEffect.Play(hitSparkSprite, spriteRenderer, gameObject.scene, tip, facing,
                new Vector2(0.47f, 0.18f), 0.23f, tint: GroundSparks);
            PlayGuardAudio(guardSlashLandingClip, 0.42f, tip);
        }

        if (!HasLivePlayerBody || !TryGetGuardSlashTarget(guardSlashReach, guardSlashHeight))
            return false;

        // プレイヤーが別の被弾で無敵中でも、刃が届いたら命中側の復帰を使う。
        if (player.TakeEnemyDamage(guardSlashDamage, Vector2.right * (facing * knockback)))
            PlayHitSpark(playerBody.ClosestPoint(center));
        return true;
    }

    private void FinishGuardSlash()
    {
        CountNormalAttackForIai();
        ShowPose(Melee, BladesDownPose);
        BeginRecovery(guardSlashContactedPlayer ? guardSlashRecovery : guardSlashMissRecovery,
            exposed: !guardSlashContactedPlayer, pullBladesFree: true);
    }

    private void PlayCrossGuardImpact(MorningStarHitContext hit)
    {
        guardFlashUntil = Time.time + GuardFlashDuration;
        if (crossGuardShield != null)
        {
            Vector3 local = crossGuardShield.transform.InverseTransformPoint(hit.ImpactPoint);
            shieldContact = new Vector2(Mathf.Clamp(local.x + 0.5f, 0.18f, 0.82f),
                Mathf.Clamp(local.y + 0.5f, 0.14f, 0.86f));
        }
        UpdateGuardPresentation();

        var ball = hit.Source != null ? hit.Source.BallBody : null;
        if (ball != null && ball.TryGetComponent<MorningStarImpactSparks>(out var sparks))
            sparks.PlayPressImpact(hit.ImpactPoint, 16);
        PlayGuardAudio(iaiGuardClip, 0.96f, hit.ImpactPoint);
    }

    private void UpdateGuardPresentation()
    {
        if (crossGuardShield == null || !crossGuardShield.enabled) return;
        shieldProperties ??= new MaterialPropertyBlock();
        float flash = Mathf.Clamp01((guardFlashUntil - Time.time) / GuardFlashDuration);
        shieldProperties.SetFloat(ShieldImpact, flash);
        shieldProperties.SetVector(ShieldContact, new Vector4(shieldContact.x, shieldContact.y, 0f, 0f));
        crossGuardShield.SetPropertyBlock(shieldProperties);
    }

    private void ClearGuardPresentation()
    {
        guardFlashUntil = 0f;
        if (crossGuardShield != null) crossGuardShield.enabled = false;
    }

    private void PlayGuardAudio(AudioClip clip, float volume, Vector2 point) =>
        OneShotAudioUtility.Play2D(OneShotAudioUtility.FindWorldImpactSource(), clip, volume, point);
}
