using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed partial class SlashMantis
{
    private enum IaiPose { Draw, Loaded, Slash, Deflected, Staggered }

    private const float IaiPoseFrameTime = 0.1f;
    private const float IaiAfterimageLifetime = 0.16f;
    private const int IaiAfterimageCount = 7;
    private const float IaiWindupCue = 0.65f;
    private static readonly int IaiDraw = Animator.StringToHash("Base Layer.IaiDraw");

    [Header("Iai")]
    [SerializeField, Min(0.1f)] private float iaiRange = 8.5f;
    [SerializeField, Min(0.1f)] private float iaiWindupDuration = 0.68f;
    [SerializeField, Min(0.1f)] private float iaiRushSpeed = 58f;
    [SerializeField, Min(0f)] private float iaiDamageDelay = 0.16f;
    [SerializeField, Min(0.1f)] private float iaiRecoveryDuration = 0.91f;
    [SerializeField, Min(0.1f)] private float iaiGuardRecoveryDuration = 1.45f;
    [SerializeField, Min(0)] private int iaiDamage = 2;
    [SerializeField] private Vector2 iaiBallKnockback = new Vector2(18f, 5.5f);

    [Header("Iai Presentation")]
    [SerializeField] private SpriteRenderer iaiVisual;
    [SerializeField] private Animator iaiAnimator;
    [SerializeField] private AnimationClip iaiMotion;
    [SerializeField] private Sprite iaiAfterimageSprite;
    [SerializeField] private Material iaiAfterimageMaterial;
    [SerializeField] private GroundProjectedShadow iaiGroundShadow;
    [SerializeField] private AudioClip iaiDrawClip;
    [SerializeField] private AudioClip iaiGuardClip;
    [SerializeField] private EnemySpriteEffect defeatMotionPrefab;

    private readonly SpriteRenderer[] iaiImages = new SpriteRenderer[IaiAfterimageCount];
    private readonly float[] iaiImageExpiry = new float[IaiAfterimageCount];
    private readonly List<Collider2D> iaiGuardContacts = new List<Collider2D>(2);
    private Transform iaiImageRoot;
    private int nextIaiImage;
    private int normalAttacksUntilIai;
    private float iaiDistanceRemaining;
    private MorningStarLauncher iaiLauncher;
    private ContactFilter2D iaiContactFilter;
    private PlayerHealth iaiVictim;
    private Collider2D iaiVictimCollider;
    private int iaiVictimRecoveryVersion;
    private bool iaiPresenting;
    private bool iaiShadowHidden;
    private bool iaiStruckBall;
    private IaiPose? lastIaiPose;

    private void ResetIaiSchedule() => normalAttacksUntilIai = Random.Range(2, 5);

    private void CountNormalAttackForIai()
    {
        if (normalAttacksUntilIai > 0) normalAttacksUntilIai--;
    }

    private bool TryBeginIai(int direction)
    {
        if (normalAttacksUntilIai > 0 || iaiVisual == null || iaiAnimator == null || iaiMotion == null ||
            spriteRenderer == null || !spriteRenderer.enabled)
            return false;

        Bounds hull = bodyCollider.bounds;
        Bounds target = playerBody.bounds;
        float ahead = (target.center.x - hull.center.x) * direction;
        if (ahead <= Skin || ahead > iaiRange || target.min.y >= hull.max.y || target.max.y <= hull.min.y)
            return false;

        Face(direction);
        float finishDistance = ahead + hull.extents.x + target.extents.x + 0.50f;
        if (SafeRaisedDashDistance(finishDistance, DashFloorClearance) + 0.001f < finishDistance) return false;

        // 構えた瞬間の方向と着地点を固定し、ジャンプと鉄球の設置で対処できるようにする。
        iaiDistanceRemaining = finishDistance;
        iaiLauncher = player.GetComponentInParent<MorningStarLauncher>();
        int mask = playerLayers.value | LayerMask.GetMask("morningstar");
        if (iaiLauncher != null && iaiLauncher.BallBody != null) mask |= 1 << iaiLauncher.BallBody.gameObject.layer;
        iaiContactFilter = new ContactFilter2D { useTriggers = false };
        iaiContactFilter.SetLayerMask(mask);
        iaiVictim = null;
        iaiVictimCollider = null;
        iaiStruckBall = false;
        ResetIaiSchedule();
        SetHorizontalSpeed(0f);
        iaiPresenting = true;
        spriteRenderer.enabled = false;
        iaiVisual.enabled = true;
        lastIaiPose = null;
        SetIaiPose(IaiPose.Draw);
        Enter(State.IaiWindup);
        return true;
    }

    private void StepIaiWindup()
    {
        if (!IsLive(player))
        {
            CancelIai();
            BeginRecovery(0.2f, exposed: true);
            return;
        }

        float progress = elapsed / iaiWindupDuration;
        if (!actionFired && progress >= IaiWindupCue)
        {
            actionFired = true;
            SetIaiPose(IaiPose.Loaded);
            OneShotAudioUtility.Play2D(OneShotAudioUtility.FindWorldImpactSource(), iaiDrawClip, 0.46f, transform.position);
        }

        if (progress < 1f) return;
        iaiDistanceRemaining = SafeRaisedDashDistance(iaiDistanceRemaining, DashFloorClearance);
        if (iaiDistanceRemaining <= Skin)
        {
            CancelIai();
            ShowPose(Melee, BladesDownPose);
            BeginRecovery(meleeCooldown, exposed: true);
            return;
        }
        savedGravity = body.gravityScale;
        savedExcludedLayers = bodyCollider.excludeLayers;
        body.gravityScale = 0f;
        body.linearVelocity = Vector2.zero;
        // 鉄球とプレイヤーの接触順は居合のスイープが所有する。
        bodyCollider.excludeLayers = savedExcludedLayers.value | iaiContactFilter.layerMask.value | LayerMask.GetMask("chain");
        dashPhysicsActive = true;
        iaiVisual.enabled = false;
        if (iaiGroundShadow != null && iaiGroundShadow.enabled)
        {
            iaiGroundShadow.enabled = false;
            iaiShadowHidden = true;
        }

        // 足元に小さな床の継ぎ目があっても、胴体の通り道と着地点の接地は検査する。
        body.position += Vector2.up * DashFloorClearance;
        Enter(State.IaiRush);
        EmitIaiImage();
    }

    private void StepIaiRush()
    {
        float requested = Mathf.Min(iaiDistanceRemaining, iaiRushSpeed * Time.fixedDeltaTime);
        float distance = SafeRaisedDashDistance(requested, 0f);
        if (distance <= 0.0001f)
        {
            FinishIaiRush();
            return;
        }

        FindIaiContacts(distance, out RaycastHit2D playerHit, out RaycastHit2D ballHit);
        if (playerHit.collider != null && (ballHit.collider == null || IaiHitPrecedes(playerHit, ballHit)))
        {
            iaiVictim = player;
            iaiVictimCollider = playerHit.collider;
            iaiVictimRecoveryVersion = player.RecoveryVersion;
        }

        if (ballHit.collider != null)
        {
            AdvanceIai(Mathf.Max(0f, ballHit.distance - Skin));
            iaiStruckBall = true;
            Vector2 contact = ballHit.distance > 0f ? ballHit.point : ballHit.collider.ClosestPoint(bodyCollider.bounds.center);
            PlayIaiGuardImpact(ballHit.collider.attachedRigidbody, contact);
            FinishIaiRush();
            return;
        }

        AdvanceIai(distance);
        iaiDistanceRemaining -= distance;
        if (iaiDistanceRemaining <= 0.0001f || distance + 0.001f < requested) FinishIaiRush();
    }

    private void FindIaiContacts(float distance, out RaycastHit2D playerHit, out RaycastHit2D ballHit)
    {
        playerHit = ballHit = default;
        Rigidbody2D ironBall = iaiLauncher != null ? iaiLauncher.BallBody : null;
        Bounds hull = bodyCollider.bounds;
        PhysicsScene.BoxCast(hull.center, hull.size, 0f, Vector2.right * facing, distance, iaiContactFilter, casts);
        foreach (RaycastHit2D hit in casts)
        {
            if (hit.collider == null) continue;
            if (ironBall != null && ironBall.simulated && hit.collider.attachedRigidbody == ironBall)
            {
                if (ballHit.collider == null) ballHit = hit;
            }
            else if (iaiVictim == null && playerHit.collider == null && IsLive(player) &&
                PlayerColliderUtility.IsPlayerBody(hit.collider) && hit.collider.GetComponentInParent<PlayerHealth>() == player)
            {
                playerHit = hit;
            }
        }
    }

    private bool IaiHitPrecedes(RaycastHit2D first, RaycastHit2D second)
    {
        float difference = first.distance - second.distance;
        if (Mathf.Abs(difference) > 0.005f) return difference < 0f;
        return (first.collider.bounds.center.x - second.collider.bounds.center.x) * facing < 0f;
    }

    private void AdvanceIai(float distance)
    {
        if (distance <= 0.0001f) return;
        body.position += Vector2.right * (facing * distance);
        EmitIaiImage();
    }

    private void PlayIaiGuardImpact(Rigidbody2D ironBall, Vector2 point)
    {
        IgnoreIaiGuardContacts(ironBall);
        iaiLauncher.ApplyBladeGuardKnockback(new Vector2(facing * iaiBallKnockback.x, iaiBallKnockback.y));
        if (ironBall.TryGetComponent<MorningStarImpactSparks>(out var sparks)) sparks.PlayPressImpact(point, 24);

        OneShotAudioUtility.Play2D(OneShotAudioUtility.FindWorldImpactSource(), iaiGuardClip, 0.75f, point);
        var camera = Camera.main;
        if (camera != null && camera.TryGetComponent<CameraShake2D>(out var shake)) shake.Shake(0.14f, 0.12f);
    }

    private void IgnoreIaiGuardContacts(Rigidbody2D ironBall)
    {
        // 弾き飛ばす瞬間の接触が、鉄球からカマキリへの攻撃として再発生するのを防ぐ。
        foreach (Collider2D ballCollider in ironBall.GetComponentsInChildren<Collider2D>())
        {
            if (!ballCollider.enabled || ballCollider.isTrigger || ballCollider.attachedRigidbody != ironBall ||
                Physics2D.GetIgnoreCollision(bodyCollider, ballCollider))
                continue;

            Physics2D.IgnoreCollision(bodyCollider, ballCollider, true);
            iaiGuardContacts.Add(ballCollider);
        }
    }

    private void RestoreIaiGuardContacts(bool restoreAll)
    {
        for (int i = iaiGuardContacts.Count - 1; i >= 0; i--)
        {
            Collider2D ballCollider = iaiGuardContacts[i];
            if (bodyCollider != null && bodyCollider.isActiveAndEnabled &&
                ballCollider != null && ballCollider.isActiveAndEnabled)
            {
                if (!restoreAll)
                {
                    ColliderDistance2D separation = bodyCollider.Distance(ballCollider);
                    if (separation.isValid && separation.distance <= Skin * 2f) continue;
                }

                Physics2D.IgnoreCollision(bodyCollider, ballCollider, false);
            }

            iaiGuardContacts.RemoveAt(i);
        }
    }

    private void FinishIaiRush()
    {
        RestoreDash();
        RestoreIaiShadow();
        iaiVisual.enabled = true;
        SetIaiPose(iaiStruckBall ? IaiPose.Deflected : IaiPose.Slash);
        Enter(State.IaiRecovery);
    }

    private void StepIaiRecovery()
    {
        float duration = iaiStruckBall ? iaiGuardRecoveryDuration : iaiRecoveryDuration;
        if (iaiStruckBall && elapsed >= 0.14f)
            SetIaiPose(IaiPose.Staggered);
        else if (!iaiStruckBall && elapsed >= 0.30f)
            SetIaiPose(IaiPose.Draw);

        if (!actionFired && elapsed >= iaiDamageDelay)
        {
            actionFired = true;
            if (IsLive(iaiVictim) && iaiVictim.RecoveryVersion == iaiVictimRecoveryVersion &&
                iaiVictim.TakeEnemyDamage(iaiDamage, Vector2.right * (facing * knockback)))
            {
                var point = iaiVictimCollider != null && iaiVictimCollider.isActiveAndEnabled
                    ? iaiVictimCollider.bounds.center : iaiVictim.transform.position;
                if (iaiLauncher != null && iaiLauncher.BallBody != null &&
                    iaiLauncher.BallBody.TryGetComponent<MorningStarImpactSparks>(out var sparks))
                    sparks.PlayPressImpact(point, 12);
            }
        }

        if (elapsed < duration) return;
        CancelIai();
        Enter(State.Walking);
    }

    private void SetIaiPose(IaiPose next)
    {
        if (lastIaiPose == next) return;
        iaiAnimator.speed = 0f;
        iaiAnimator.Play(IaiDraw, 0, ((int)next + 0.5f) * IaiPoseFrameTime / iaiMotion.length);
        lastIaiPose = next;
    }

    private void RestoreIaiShadow()
    {
        if (!iaiShadowHidden) return;
        if (iaiGroundShadow != null) iaiGroundShadow.enabled = true;
        iaiShadowHidden = false;
    }

    private void CancelIai()
    {
        if (iaiVisual != null) iaiVisual.enabled = false;
        if (iaiPresenting && spriteRenderer != null) spriteRenderer.enabled = true;
        iaiPresenting = false;
        iaiVictim = null;
        iaiVictimCollider = null;
        iaiLauncher = null;
        RestoreIaiShadow();
        foreach (SpriteRenderer image in iaiImages)
            if (image != null) image.enabled = false;
    }

    private void EmitIaiImage()
    {
        if (iaiAfterimageSprite == null) return;
        if (iaiImageRoot == null)
        {
            iaiImageRoot = new GameObject("Mantis Iai Afterimages").transform;
            if (iaiImageRoot.gameObject.scene != gameObject.scene)
                SceneManager.MoveGameObjectToScene(iaiImageRoot.gameObject, gameObject.scene);
        }

        int index = nextIaiImage;
        nextIaiImage = (nextIaiImage + 1) % iaiImages.Length;
        SpriteRenderer image = iaiImages[index];
        if (image == null)
        {
            image = new GameObject("Afterimage").AddComponent<SpriteRenderer>();
            image.transform.SetParent(iaiImageRoot, false);
            image.sprite = iaiAfterimageSprite;
            if (iaiAfterimageMaterial != null) image.sharedMaterial = iaiAfterimageMaterial;
            image.sortingLayerID = iaiVisual.sortingLayerID;
            image.sortingOrder = iaiVisual.sortingOrder - 1;
            iaiImages[index] = image;
        }

        Vector3 scale = iaiVisual.transform.lossyScale;
        image.transform.localScale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), 1f);
        Vector3 offset = iaiVisual.transform.position - transform.position;
        image.transform.SetPositionAndRotation((Vector3)body.position + offset, iaiVisual.transform.rotation);
        image.flipX = facing > 0;
        image.color = new Color(0.27f, 0.80f, 0.92f, 0.62f);
        iaiImageExpiry[index] = Time.time + IaiAfterimageLifetime;
        image.enabled = true;
    }

    private void LateUpdate()
    {
        UpdateGuardPresentation();
        RestoreIaiGuardContacts(false);
        // 既存の被弾点滅が完了したフレームでも、居合の表示は専用Spriteだけが担当する。
        if (iaiPresenting && spriteRenderer != null) spriteRenderer.enabled = false;
        for (int i = 0; i < iaiImages.Length; i++)
        {
            SpriteRenderer image = iaiImages[i];
            if (image == null || !image.enabled) continue;
            float fade = Mathf.Clamp01((iaiImageExpiry[i] - Time.time) / IaiAfterimageLifetime);
            image.enabled = fade > 0f;
            if (image.enabled) image.color = new Color(0.27f, 0.80f, 0.92f, fade * 0.62f);
        }
    }

    private void DestroyIaiImages()
    {
        if (iaiImageRoot != null) Destroy(iaiImageRoot.gameObject);
    }

    private void PlayIaiDefeat()
    {
        if (defeatMotionPrefab == null || spriteRenderer == null) return;
        Transform source = spriteRenderer.transform;
        EnemySpriteEffect effect = Instantiate(defeatMotionPrefab, source.position, source.rotation);
        if (effect.gameObject.scene != gameObject.scene)
            SceneManager.MoveGameObjectToScene(effect.gameObject, gameObject.scene);
        Vector3 scale = source.lossyScale;
        effect.transform.localScale = new Vector3(Mathf.Abs(scale.x) * (facing > 0 ? -1f : 1f), Mathf.Abs(scale.y), 1f);
    }
}
