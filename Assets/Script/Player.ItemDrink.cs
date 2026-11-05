using UnityEngine;

public partial class Player
{
    private Object itemDrinkOwner;
    private bool itemDrinkActive;
    private float itemDrinkStartedAt;
    private float itemDrinkDuration;
    private int itemDrinkRecoveryVersion;
    private RigidbodyConstraints2D constraintsBeforeItemDrink;

    public bool IsItemDrinking => itemDrinkActive;
    public float ItemDrinkProgress => itemDrinkActive
        ? Mathf.Clamp01((Time.time - itemDrinkStartedAt) / itemDrinkDuration) : 0f;

    /// <summary>接地・通常操作時だけ開始する。複数アイテムは取得元ごとに順番待ちする。</summary>
    public bool TryBeginItemDrink(Object owner, float duration)
    {
        if (owner == null || itemDrinkActive || !isActiveAndEnabled || !InputEnabled || Time.timeScale <= 0f
            || _goalInputBlocked || _gimmickInputBlocked || _startIntroControlled
            || IsElectricStunned || IsEnemyHitStunned || !IsGrounded || !_rawGrounded
            || _rigid == null || !_rigid.simulated
            || (_playerHealth != null && (_playerHealth.IsDead || _playerHealth.IsRespawning))) return false;
        if (_morningStarLauncher != null && !_morningStarLauncher.CanBeginItemDrink) return false;

        itemDrinkOwner = owner;
        itemDrinkActive = true;
        itemDrinkStartedAt = Time.time;
        itemDrinkDuration = Mathf.Max(0.01f, duration);
        itemDrinkRecoveryVersion = _playerHealth != null ? _playerHealth.RecoveryVersion : 0;
        constraintsBeforeItemDrink = _rigid.constraints;
        ClearItemDrinkInput();
        if (_morningStarLauncher != null) _morningStarLauncher.BeginItemDrink();
        _rigid.linearVelocity = Vector2.zero;
        _rigid.angularVelocity = 0f;
        _rigid.constraints = RigidbodyConstraints2D.FreezeAll;
        StopFootstepAudio();
        return true;
    }

    public bool OwnsItemDrink(Object owner) => itemDrinkActive && itemDrinkOwner == owner;

    /// <summary>取得元以外は解除しない。引数なしは死亡・他演出への引き継ぎ用。</summary>
    public void EndItemDrink(Object owner = null)
    {
        if (!itemDrinkActive || (owner != null && itemDrinkOwner != owner)) return;
        itemDrinkActive = false;
        itemDrinkOwner = null;
        if (_rigid != null)
        {
            _rigid.constraints = constraintsBeforeItemDrink;
            _rigid.linearVelocity = Vector2.zero;
            _rigid.angularVelocity = 0f;
        }
        ClearItemDrinkInput();
    }

    private void UpdateItemDrink()
    {
        if (!itemDrinkActive) return;
        if (itemDrinkOwner == null || !InputEnabled || _goalInputBlocked || _gimmickInputBlocked
            || _startIntroControlled || _rigid == null || !_rigid.simulated
            || (_playerHealth != null && (_playerHealth.IsDead || _playerHealth.IsRespawning
                || _playerHealth.RecoveryVersion != itemDrinkRecoveryVersion))) EndItemDrink();
    }

    private void ClearItemDrinkInput()
    {
        _moveInput = _actionMoveInput = Vector2.zero;
        _jumpHeld = false;
        _jumpBufferTimer = _coyoteTimer = 0f;
        _trampolineJumpUntil = _lastJumpPressedAt = float.NegativeInfinity;
    }
}
