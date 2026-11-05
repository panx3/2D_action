using UnityEngine;

public partial class Player
{
    private Object electricStunOwner;
    private bool electricStunActive;
    private RigidbodyConstraints2D constraintsBeforeElectricStun;

    public bool IsElectricStunned => electricStunActive;

    /// <summary>感電中のPlayer物理はPlayer自身が所有する。他の入力ロックは変更しない。</summary>
    public bool TryBeginElectricStun(Object owner)
    {
        if (owner == null || electricStunActive || !isActiveAndEnabled || !InputEnabled
            || _goalInputBlocked || _gimmickInputBlocked || _startIntroControlled
            || _rigid == null || !_rigid.simulated
            || (_playerHealth != null && (_playerHealth.IsDead || _playerHealth.IsRespawning))) return false;
        if (_morningStarLauncher != null && !_morningStarLauncher.CanBeginElectricStun) return false;

        electricStunOwner = owner;
        electricStunActive = true;
        constraintsBeforeElectricStun = _rigid.constraints;
        ClearElectricStunInput();
        _rigid.linearVelocity = Vector2.zero;
        _rigid.angularVelocity = 0f;
        _rigid.constraints = RigidbodyConstraints2D.FreezeAll;
        if (_morningStarLauncher != null) _morningStarLauncher.ClearElectricStunInput();
        StopFootstepAudio();
        return true;
    }

    public void EndElectricStun(Object owner = null)
    {
        if (!electricStunActive || (owner != null && owner != electricStunOwner)) return;
        electricStunActive = false;
        electricStunOwner = null;
        if (_rigid != null)
        {
            _rigid.constraints = constraintsBeforeElectricStun;
            _rigid.linearVelocity = Vector2.zero;
            _rigid.angularVelocity = 0f;
        }
        ClearElectricStunInput();
        if (_morningStarLauncher != null) _morningStarLauncher.ClearElectricStunInput();
    }

    private void ClearElectricStunInput()
    {
        _moveInput = _actionMoveInput = Vector2.zero;
        _jumpHeld = false;
        _jumpBufferTimer = _coyoteTimer = 0f;
        _trampolineJumpUntil = _lastJumpPressedAt = float.NegativeInfinity;
    }
}
