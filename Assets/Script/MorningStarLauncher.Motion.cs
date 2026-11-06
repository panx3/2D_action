using System.Collections.Generic;
using UnityEngine;

public partial class MorningStarLauncher
{
    private const float ChargeRearDepthOffset = 0.01f;
    private const float LaunchFinalPoseExtraHold = 0.14f;

    [Header("Player Sprite Motion")]
    [SerializeField] private PlayerMotionSet playerMotionSet;

    private Sprite motionAnimatorSprite;
    private Sprite motionOverrideSprite;
    private bool motionOverrideActive;
    private MorningStarState motionVisualState;
    private float motionStateStartedAt;
    private Vector2 chargeEntryBallPosition;
    private bool motionHangingVisual;
    private SpriteRenderer motionBallRenderer;
    private bool chargeSortingApplied;
    private int originalBallSortingOrder;
    private int originalBallSortingLayer;
    private float originalBallVisualLocalZ;
    private float chargeHandlePathLength;
    private int motionLaunchLayer = -2;
    private float motionLaunchLayerWeight;
    private bool motionLaunchLayerSuppressed;
    private bool motionLaunchVisualActive;
    private float motionLaunchVisualStartedAt;
    private Vector2 motionLaunchVisualDirection;

    public PlayerMotionSet MotionSet => playerMotionSet;
    public Vector2 ChargeGripWorld => GetChargeGripWorld();
    public bool UsesChargeGrip => playerMotionSet != null && _state == MorningStarState.SpinCharging;
    public bool ChargeBallBehindPlayer => UsesChargeGrip;
    public int MotionPlayerSortingOrder => _playerBodySprite != null ? _playerBodySprite.sortingOrder : 0;
    public int MotionPlayerSortingLayerID => _playerBodySprite != null ? _playerBodySprite.sortingLayerID : 0;
    public float ChargeRearWorldZ => (_playerBodySprite != null ? _playerBodySprite.bounds.center.z : transform.position.z) + ChargeRearDepthOffset;
    public float ChargeHandlePathLength => chargeHandlePathLength;

    private void InitializePlayerMotion()
    {
        if (playerMotionSet == null)
            playerMotionSet = Resources.Load<PlayerMotionSet>("PlayerMotion/PlayerMotionV2");
        motionVisualState = _state;
        motionStateStartedAt = Time.time;
        if (morningStarRb != null) motionBallRenderer = morningStarRb.GetComponentInChildren<SpriteRenderer>();
    }

    private Vector2 MotionLocalToWorld(Vector2 local)
    {
        if (player != null && !player.FacingRight) local.x = -local.x;
        return transform.TransformPoint(local);
    }

    private PlayerMotionSet.Frame CurrentChargeFrame()
        => playerMotionSet.ChargeFrame(_spinAngle, !IsPlayerGrounded(),
            player != null && Mathf.Abs(player.MoveInputX) > 0.01f
                && (_playerRb == null || Mathf.Abs(_playerRb.linearVelocity.x) > 0.15f), Time.time);

    private Vector2 GetChargeGripWorld()
    {
        PlayerMotionSet.Frame frame = playerMotionSet != null ? CurrentChargeFrame() : null;
        return frame != null ? MotionLocalToWorld(frame.chargeGrip) : GetHandWorld();
    }

    private void PrepareMotionStateTransition(MorningStarState next)
    {
        if (next == MorningStarState.SpinCharging && _state != next && morningStarRb != null)
            chargeEntryBallPosition = morningStarRb.position;
        if (next != MorningStarState.Thrown && next != MorningStarState.Dragging && next != MorningStarState.Dropping)
            CancelMotionLaunchVisual();
        if (next != MorningStarState.SpinCharging) RestoreChargeSorting();
    }

    private void RestoreChargeSorting()
    {
        if (!chargeSortingApplied) return;
        if (motionBallRenderer != null)
        {
            motionBallRenderer.sortingOrder = originalBallSortingOrder;
            motionBallRenderer.sortingLayerID = originalBallSortingLayer;
            if (morningStarRb != null && motionBallRenderer.transform != morningStarRb.transform)
            {
                Vector3 localPosition = motionBallRenderer.transform.localPosition;
                localPosition.z = originalBallVisualLocalZ;
                motionBallRenderer.transform.localPosition = localPosition;
            }
        }
        _chargeFeedback?.SetChargeDepth(originalBallSortingLayer, originalBallSortingOrder, false);
        chargeSortingApplied = false;
    }

    private void UpdateChargeSorting()
    {
        if (!UsesChargeGrip || motionBallRenderer == null || _playerBodySprite == null)
        {
            RestoreChargeSorting();
            return;
        }
        if (!chargeSortingApplied)
        {
            originalBallSortingOrder = motionBallRenderer.sortingOrder;
            originalBallSortingLayer = motionBallRenderer.sortingLayerID;
            originalBallVisualLocalZ = motionBallRenderer.transform.localPosition.z;
            chargeSortingApplied = true;
        }
        motionBallRenderer.sortingLayerID = _playerBodySprite.sortingLayerID;
        // 同じ描画順の奥に置くと、キャラの前にある装飾の間へ鉄球が割り込まない。
        // Rigidbody自身には触れず、子のSpriteだけを微小なZ差でソートする。
        bool separateVisual = morningStarRb != null && motionBallRenderer.transform != morningStarRb.transform;
        motionBallRenderer.sortingOrder = MotionPlayerSortingOrder + (separateVisual ? 0 : -1);
        if (separateVisual)
        {
            Vector3 position = motionBallRenderer.transform.position;
            position.z = ChargeRearWorldZ;
            motionBallRenderer.transform.position = position;
        }
        _chargeFeedback?.SetChargeDepth(motionBallRenderer.sortingLayerID, motionBallRenderer.sortingOrder, true);
    }

    private void StartMotionLaunchVisual()
    {
        motionLaunchVisualActive = true;
        motionLaunchVisualStartedAt = Time.time;
        motionLaunchVisualDirection = _pendingLaunchDir;
    }

    private void CancelMotionLaunchVisual() => motionLaunchVisualActive = false;

    private Vector2 ResolveChargeOrbitPosition()
    {
        if (playerMotionSet == null)
            return GetHandWorld() + new Vector2(Mathf.Cos(_spinAngle), Mathf.Sin(_spinAngle)) * spinRadius;

        float facing = player != null && !player.FacingRight ? -1f : 1f;
        Vector2 offset = new Vector2(Mathf.Cos(_spinAngle) * facing, Mathf.Sin(_spinAngle));
        Vector2 target = GetChargeGripWorld() + offset * playerMotionSet.chargeOrbitRadius;
        float blend = Mathf.SmoothStep(0f, 1f, _chargeTime / Mathf.Max(0.01f, playerMotionSet.chargeEntryTime));
        return Vector2.Lerp(chargeEntryBallPosition, target, blend);
    }

    /// <summary>チャージ専用の表示経路。通常の物理ロープへは渡さない。</summary>
    public bool TryGetChargeVisualPath(List<Vector3> points)
    {
        if (!UsesChargeGrip || morningStarRb == null) return false;
        PlayerMotionSet.Frame frame = CurrentChargeFrame();
        if (frame == null) return false;
        points.Clear();
        Vector2 start = MotionLocalToWorld(frame.chainAnchor);
        Vector2 grip = GetChargeGripWorld();
        Vector2 ball = morningStarRb.position;
        float slack = Mathf.Max(0f, MaxRopeLength - Vector2.Distance(grip, ball) - Vector2.Distance(start, grip));
        float sag = Mathf.Min(playerMotionSet.chargeSlackDepth, slack * 0.55f);
        chargeHandlePathLength = 0f;
        for (int i = 0; i <= 12; i++)
        {
            float t = i / 12f;
            Vector2 point = Vector2.Lerp(start, grip, t);
            float bow = 4f * t * (1f - t);
            point.y -= sag * bow;
            point.x += Mathf.Sin(_spinAngle - t * 1.5f) * 0.045f * bow;
            if (points.Count > 0) chargeHandlePathLength += Vector2.Distance(points[points.Count - 1], point);
            points.Add(point);
        }
        points.Add(ball);
        return true;
    }

    private void RestoreUnifiedMotionSprite()
    {
        if (!motionOverrideActive || _playerBodySprite == null) return;
        if (_playerBodySprite.sprite == motionOverrideSprite)
            _playerBodySprite.sprite = motionAnimatorSprite;
        motionOverrideActive = false;
        motionOverrideSprite = null;
        if (player != null) _playerBodySprite.flipX = !player.FacingRight;
    }

    private void SetMotionLaunchLayerSuppressed(bool suppress)
    {
        if (animator == null) return;
        if (motionLaunchLayer == -2) motionLaunchLayer = animator.GetLayerIndex(LauncherPoseLayerName);
        if (motionLaunchLayer < 0) return;
        if (suppress && !motionLaunchLayerSuppressed)
        {
            motionLaunchLayerWeight = animator.GetLayerWeight(motionLaunchLayer);
            animator.SetLayerWeight(motionLaunchLayer, 0f);
            motionLaunchLayerSuppressed = true;
        }
        else if (!suppress && motionLaunchLayerSuppressed)
        {
            animator.SetLayerWeight(motionLaunchLayer, motionLaunchLayerWeight);
            motionLaunchLayerSuppressed = false;
        }
    }

    private bool UpdateUnifiedMotionVisual()
    {
        if (playerMotionSet == null)
        {
            SetMotionLaunchLayerSuppressed(false);
            return false;
        }
        ResolvePlayerBodySprite();
        if (_playerBodySprite == null)
        {
            SetMotionLaunchLayerSuppressed(false);
            return true;
        }
        RestoreUnifiedMotionSprite();
        RestoreChainClimbVisual();
        UpdateChargeSorting();
        motionHangingVisual = false;
        if (_state == MorningStarState.StartIntro || _respawnPresentation || IsElectricStunned)
        {
            CancelMotionLaunchVisual();
            SetMotionLaunchLayerSuppressed(false);
            return true;
        }
        if (motionVisualState != _state)
        {
            motionVisualState = _state;
            motionStateStartedAt = Time.time;
        }
        float elapsed = Time.time - motionStateStartedAt;
        PlayerMotionSet.Frame selected = null;
        bool suspended = false;
        bool upperHanging = IsUpperHanging;
        if (_isChainClimbing || upperHanging) CancelMotionLaunchVisual();
        float throwElapsed = Mathf.Max(0f, Time.time - motionLaunchVisualStartedAt);
        var throwSequence = motionLaunchVisualActive ? playerMotionSet.Throw(motionLaunchVisualDirection, !IsPlayerGrounded()) : null;
        float throwVisualDuration = throwSequence != null && throwSequence.IsValid
            ? throwSequence.frames.Length / Mathf.Max(.01f, throwSequence.framesPerSecond) + LaunchFinalPoseExtraHold : 0f;
        // Animatorのレイヤー変更は次の評価から反映されるため、切替フレームも最後の絵でつなぐ。
        bool finishingThrowThisFrame = motionLaunchVisualActive && throwElapsed >= throwVisualDuration;
        if (finishingThrowThisFrame) CancelMotionLaunchVisual();
        bool returnToLocomotion = _launchPoseActive && !motionLaunchVisualActive;
        // 操作・物理用の保持時間はそのままに、投げ絵の終了後はBase Layerへ戻す。
        bool hurt = player != null && player.IsEnemyHitStunned && !_isChainClimbing && !upperHanging;
        bool drinking = player != null && player.IsItemDrinking;
        SetMotionLaunchLayerSuppressed(drinking || hurt || (returnToLocomotion && !_isChainClimbing && !upperHanging));

        if (drinking && playerMotionSet.drink.IsValid)
        {
            int index = Mathf.Min(playerMotionSet.drink.frames.Length - 1,
                Mathf.FloorToInt(player.ItemDrinkProgress * playerMotionSet.drink.frames.Length));
            selected = playerMotionSet.drink.frames[index];
        }
        else if (hurt)
        {
            var sequence = player.EnemyHitGrounded ? playerMotionSet.hurtGround : playerMotionSet.hurtAir;
            selected = sequence != null ? sequence.Sample(player.EnemyHitElapsed) : null;
        }
        else if (_isChainClimbing)
        {
            if (_chainClimbMovedThisFixedStep) _chainClimbAnimationTime += Time.deltaTime;
            selected = playerMotionSet.climbing.Sample(_chainClimbAnimationTime);
            suspended = true;
        }
        else if (upperHanging)
        {
            selected = playerMotionSet.hanging.Sample(Time.time);
            suspended = true;
        }
        else if (_state == MorningStarState.SpinCharging)
            selected = CurrentChargeFrame();
        else if (_state == MorningStarState.Returning || _state == MorningStarState.RecallBeforeThrow)
            selected = playerMotionSet.recall.Sample(elapsed);
        else
        {
            if (motionLaunchVisualActive || finishingThrowThisFrame)
                selected = throwSequence?.Sample(throwElapsed);
            else if (!IsPlayerGrounded() && _playerRb != null && Mathf.Abs(_playerRb.linearVelocity.y) < 0.7f)
                selected = playerMotionSet.apex.Sample(0f);
        }
        if (selected == null || selected.sprite == null) return true;

        if (suspended)
        {
            EnsureChainClimbSpriteRenderer();
            CopyChainClimbRendererSettings();
            _animatorDrivenSpriteBeforeChainClimb = _playerBodySprite.sprite;
            _chainClimbSpriteRenderer.sprite = selected.sprite;
            bool flip = !_isChainClimbing && player != null && !player.FacingRight;
            _chainClimbSpriteRenderer.flipX = flip;
            Vector2 anchorLocal = selected.chainAnchor;
            if (flip) anchorLocal.x = -anchorLocal.x;
            Vector2 anchor = GetUpwardPoseChainAnchorWorld();
            Vector2 direction = (RopeContactPointCount > 0 ? GetRopeContactPoint(0) : GetUpperHangSupportWorld()) - anchor;
            Quaternion rotation = direction.sqrMagnitude > 0.0001f
                ? Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f)
                : Quaternion.identity;
            Vector3 scaledAnchor = Vector3.Scale(anchorLocal, _playerBodySprite.transform.lossyScale);
            _chainClimbSpriteRenderer.transform.SetPositionAndRotation((Vector3)anchor - rotation * scaledAnchor, rotation);
            _playerBodySprite.sprite = null;
            motionHangingVisual = true;
        }
        else
        {
            motionAnimatorSprite = _playerBodySprite.sprite;
            motionOverrideSprite = selected.sprite;
            motionOverrideActive = true;
            _playerBodySprite.sprite = selected.sprite;
        }
        return true;
    }

    private bool TryGetMotionVisualAnchor(out Vector2 position)
    {
        position = default;
        if (playerMotionSet == null || _playerBodySprite == null) return false;
        if (motionHangingVisual || _isChainClimbing || IsUpperHanging)
        {
            position = GetUpwardPoseChainAnchorWorld();
            return true;
        }
        if (UsesChargeGrip)
        {
            PlayerMotionSet.Frame charge = CurrentChargeFrame();
            if (charge == null) return false;
            position = MotionLocalToWorld(charge.chainAnchor);
            return true;
        }
        if (!playerMotionSet.TryGetFrame(_playerBodySprite.sprite, out PlayerMotionSet.Frame frame))
        {
            // 投げ絵だけを終了した後、物理HandAnchorは保持時間いっぱい投擲用の位置にある。
            // Idleの柄へ表示上の鎖を戻すが、実際のHandAnchorやJointには触れない。
            if (!motionLaunchLayerSuppressed || player == null || player.WeaponHandAnchor == null)
                return false;
            Vector3 anchor = player.RightFacingHandAnchorLocalPosition;
            if (!player.FacingRight) anchor.x = -anchor.x;
            Transform parent = player.WeaponHandAnchor.parent;
            position = parent != null ? parent.TransformPoint(anchor) : player.transform.TransformPoint(anchor);
            return true;
        }
        Vector2 local = frame.chainAnchor;
        if (_playerBodySprite.flipX) local.x = -local.x;
        position = _playerBodySprite.transform.TransformPoint(local);
        return true;
    }
}
