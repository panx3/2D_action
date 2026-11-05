using UnityEngine;
using UnityEngine.InputSystem;

public partial class MorningStarLauncher
{
    private TeslaCoil electricHoldSource;
    private TeslaCoil electricHoldBlockedSource;
    private bool electricInputNeedsNeutral;

    public bool IsElectricStunned => player != null && player.IsElectricStunned;
    public bool IsElectricHeld => electricHoldSource != null;
    public bool CanBeginElectricStun => !_respawnPresentation && !_trampolineJumpActive && !_isChainClimbing
        && (_state == MorningStarState.Dragging || _state == MorningStarState.Dropping || _state == MorningStarState.Thrown);

    public void ClearElectricStunInput()
    {
        _fireBufferTimer = 0f;
        _gamepadFirePressedThisFrame = false;
        _rightStickReady = false;
        _isFireHolding = _shoulderChargeInput = _hookClickHolding = false;
        _fireHoldTime = -1f;
        electricInputNeedsNeutral = true;
        CancelHitStop();
        StopAirLaunchAssist();
        EndLaunchPose();
    }

    private bool WaitForElectricInputNeutral()
    {
        if (!electricInputNeedsNeutral) return false;
        _gamepadFirePressedThisFrame = false;
        var mouse = Mouse.current;
        var pad = Gamepad.current;
        bool held = mouse != null && (mouse.leftButton.isPressed || mouse.rightButton.isPressed);
        held |= pad != null && (pad.rightStick.ReadValue().magnitude > gamepadResetThreshold
            || pad.rightStickButton.isPressed || pad.leftShoulder.isPressed || pad.leftTrigger.isPressed || pad.buttonEast.isPressed);
        if (!held) { electricInputNeedsNeutral = false; _rightStickReady = true; }
        UpdateFallbackLineRenderer();
        return true;
    }

    private void ReleaseElectricHold()
    {
        if (electricHoldSource != null) electricHoldBlockedSource = electricHoldSource;
        electricHoldSource = null;
    }

    private void UpdateElectricHold()
    {
        if (electricHoldBlockedSource != null && !electricHoldBlockedSource.Contains(morningStarRb)) electricHoldBlockedSource = null;
        if (!CanBeginElectricStun || morningStarRb == null || !morningStarRb.simulated
            || (_playerHealth != null && (_playerHealth.IsDead || _playerHealth.IsRespawning)))
        { ReleaseElectricHold(); return; }

        float ropeLength = chainConstraint != null ? chainConstraint.CurrentRopeLength : Vector2.Distance(RopeAnchorWorld, morningStarRb.position);
        if (ropeLength >= MaxRopeLength * 0.95f) { ReleaseElectricHold(); return; }
        if (electricHoldSource != null && !electricHoldSource.Contains(morningStarRb)) ReleaseElectricHold();
        if (electricHoldSource == null)
        {
            float nearest = float.PositiveInfinity;
            foreach (var source in TeslaCoil.ActiveSources)
            {
                if (source == null || source == electricHoldBlockedSource || !source.HoldEnabled
                    || morningStarRb.linearVelocity.sqrMagnitude > source.CaptureSpeed * source.CaptureSpeed || !source.Contains(morningStarRb)) continue;
                float distance = (morningStarRb.position - source.CenterWorld).sqrMagnitude;
                if (distance < nearest) { nearest = distance; electricHoldSource = source; }
            }
        }
        if (electricHoldSource == null) return;
        if (!electricHoldSource.HoldEnabled) { ReleaseElectricHold(); return; }
        // Additive force only: state-owned mass, gravity, damping and joints remain untouched.
        morningStarRb.AddForce(electricHoldSource.GetHoldAcceleration(morningStarRb) * morningStarRb.mass, ForceMode2D.Force);
    }
}
