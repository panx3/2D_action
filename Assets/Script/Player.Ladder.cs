using System.Collections.Generic;
using UnityEngine;

public partial class Player
{
    private const float LadderSpeed = 3f;
    private const float LadderInputThreshold = 0.5f;
    private const float LadderClearance = 0.06f;
    private const float LadderPositionTolerance = 0.015f;
    private const float LadderTopEntryHeight = 0.35f;
    private readonly HashSet<IndustrialLadder> nearbyLadders = new();
    private readonly RaycastHit2D[] ladderCastHits = new RaycastHit2D[16];
    private IndustrialLadder activeLadder;
    private bool ladderAttached;
    private bool ladderNeedsNeutral;
    private bool leavingLadderTop;
    private float ladderGravityBefore;

    public bool IsLadderClimbing => ladderAttached;
    internal static readonly Vector2 LadderChainAnchor = new(0f, -0.125f);

    public int LadderFrameIndex(int frameCount)
    {
        if (!ladderAttached || activeLadder == null || frameCount <= 0) return 0;
        float rise = GetLadderFootY() - activeLadder.BottomY;
        float phase = Mathf.Repeat(rise / (2f * IndustrialLadder.RungSpacing), 1f);
        return Mathf.RoundToInt(phase * frameCount) % frameCount;
    }

    internal void NotifyLadderOverlap(IndustrialLadder ladder, Collider2D playerCollider, bool overlapping)
    {
        if (playerCollider != _groundCheckCollider) return;
        if (overlapping) nearbyLadders.Add(ladder);
        else nearbyLadders.Remove(ladder);
    }

    private float GetLadderFootY() => _groundCheckCollider.bounds.min.y;

    private bool UpdateLadderMovement()
    {
        float vertical = _moveInput.y;
        if (!ladderAttached && nearbyLadders.Count == 0 && !ladderNeedsNeutral) return false;
        bool canClimb = InputEnabled && !_goalInputBlocked && !_gimmickInputBlocked && !_startIntroControlled
            && !IsElectricStunned && !IsEnemyHitStunned && !IsItemDrinking && _rigid != null && _rigid.simulated
            && _groundCheckCollider != null && _groundCheckCollider.enabled
            && (_playerHealth == null || (!_playerHealth.IsDead && !_playerHealth.IsRespawning))
            && (_morningStarLauncher == null || _morningStarLauncher.CanUseLadder);
        if (!canClimb)
        {
            EndLadderClimb();
            return false;
        }
        if (Mathf.Abs(vertical) < LadderInputThreshold) ladderNeedsNeutral = false;

        if (ladderAttached && (activeLadder == null || !activeLadder.Contains(_groundCheckCollider)))
        {
            EndLadderClimb();
            return false;
        }
        if (!ladderAttached)
        {
            if (ladderNeedsNeutral || Mathf.Abs(vertical) < LadderInputThreshold) return false;
            var ladder = FindLadder(vertical);
            if (ladder == null) return false;
            BeginLadderClimb(ladder);
        }

        float footY = GetLadderFootY();
        float top = activeLadder.TopY + LadderClearance;
        bool atTop = footY >= activeLadder.TopY - LadderPositionTolerance;
        if (atTop && _moveInput.x * activeLadder.ExitDirection >= LadderInputThreshold)
            leavingLadderTop = true;
        if (leavingLadderTop && vertical <= -LadderInputThreshold
            && _moveInput.x * activeLadder.ExitDirection < LadderInputThreshold)
            leavingLadderTop = false;

        float targetX = leavingLadderTop ? activeLadder.ExitX : activeLadder.CenterX;
        float dx = targetX - _rigid.position.x;
        if (leavingLadderTop && Mathf.Abs(dx) <= LadderPositionTolerance)
        {
            EndLadderClimb();
            return false;
        }
        if (!leavingLadderTop && footY <= activeLadder.BottomY + LadderPositionTolerance
            && vertical <= -LadderInputThreshold)
        {
            EndLadderClimb();
            return false;
        }

        float step = LadderSpeed * Time.fixedDeltaTime;
        Vector2 displacement;
        // At the upper landing, keep the feet clear of the platform until horizontally past its edge.
        if ((leavingLadderTop || (atTop && Mathf.Abs(dx) > LadderPositionTolerance))
            && footY < top - LadderPositionTolerance)
            displacement = Vector2.up * Mathf.Min(step, top - footY);
        else if (Mathf.Abs(dx) > LadderPositionTolerance)
            displacement = Vector2.right * Mathf.Clamp(dx, -step, step);
        else if (Mathf.Abs(vertical) >= LadderInputThreshold)
            displacement = Vector2.up * (Mathf.Clamp(footY + Mathf.Sign(vertical) * step,
                activeLadder.BottomY, top) - footY);
        else
            displacement = Vector2.zero;

        displacement = LimitLadderDisplacement(displacement);
        _rigid.linearVelocity = displacement / Time.fixedDeltaTime;
        return true;
    }

    private IndustrialLadder FindLadder(float vertical)
    {
        IndustrialLadder best = null;
        float nearest = float.PositiveInfinity;
        float footY = GetLadderFootY();
        nearbyLadders.RemoveWhere(ladder => ladder == null);
        foreach (var ladder in nearbyLadders)
        {
            if (!ladder.Contains(_groundCheckCollider) || footY > ladder.TopY + LadderTopEntryHeight
                || (vertical < 0f && footY <= ladder.BottomY + LadderPositionTolerance)) continue;
            float dx = Mathf.Abs(_rigid.position.x - ladder.CenterX);
            if (vertical > 0f && (dx > 1.15f || footY >= ladder.TopY - LadderPositionTolerance)) continue;
            if (vertical < 0f && footY < ladder.TopY - LadderTopEntryHeight && dx > 1.15f) continue;
            if (dx >= nearest) continue;
            best = ladder;
            nearest = dx;
        }
        return best;
    }

    private Vector2 LimitLadderDisplacement(Vector2 displacement)
    {
        float distance = displacement.magnitude;
        if (distance < 0.0001f) return Vector2.zero;
        Vector2 direction = displacement / distance;
        var filter = new ContactFilter2D { useTriggers = false };
        filter.SetLayerMask(Physics2D.GetLayerCollisionMask(_groundCheckCollider.gameObject.layer));
        int count = _rigid.Cast(direction, filter, ladderCastHits, distance);
        for (int i = 0; i < count; i++)
        {
            var hit = ladderCastHits[i];
            if (hit.collider == null || Vector2.Dot(direction, hit.normal) >= -0.0001f) continue;
            var oneWay = hit.collider.GetComponentInParent<OneWayPlatform>();
            if (oneWay != null && !oneWay.BlocksBodyCorrection(_rigid, direction, hit.normal)) continue;
            distance = Mathf.Min(distance, Mathf.Max(0f, hit.distance - 0.005f));
        }
        return direction * distance;
    }

    private void BeginLadderClimb(IndustrialLadder ladder)
    {
        activeLadder = ladder;
        ladderAttached = true;
        leavingLadderTop = false;
        ladderGravityBefore = _rigid.gravityScale;
        _rigid.gravityScale = 0f;
        _bjump = _jumpHeld = false;
        _jumpBufferTimer = _coyoteTimer = 0f;
        _trampolineJumpUntil = _lastJumpPressedAt = float.NegativeInfinity;
        ClearGroundedStateForJump();
        StopFootstepAudio();
        if (_morningStarLauncher != null) _morningStarLauncher.BeginLadderClimb();
        SetWeaponHandAnchorPose(LadderChainAnchor);
    }

    private void EndLadderClimb()
    {
        if (!ladderAttached) return;
        ladderAttached = false;
        activeLadder = null;
        leavingLadderTop = false;
        ladderNeedsNeutral = true;
        if (_rigid != null)
        {
            _rigid.gravityScale = ladderGravityBefore;
            if (_rigid.simulated) _rigid.linearVelocity = Vector2.zero;
        }
        ClearWeaponHandAnchorPose();
        ApplyMovementFacingVisual();
        _groundStateInitialized = false;
    }

    private void HandleLadderDamage() => EndLadderClimb();
}
