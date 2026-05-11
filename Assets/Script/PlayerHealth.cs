using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// プレイヤーの HP・被弾・無敵時間・死亡処理を管理する。
/// 被弾時のノックバックは Player.ApplyExternalImpulse へ委譲する。
/// </summary>
[DisallowMultipleComponent]
public class PlayerHealth : MonoBehaviour
{
    [Header("HP 設定")]
    [SerializeField, Tooltip("プレイヤーの最大 HP。")]
    private int _maxHp = 3;
    [SerializeField, Tooltip("被弾後の無敵時間（秒）。")]
    private float _invincibleDuration = 1.2f;

    [Header("点滅表示")]
    [SerializeField, Tooltip("無敵時間中に点滅させる SpriteRenderer。未設定なら GetComponentInChildren で自動取得。")]
    private SpriteRenderer _spriteRenderer;
    [SerializeField, Tooltip("点滅の切替間隔（秒）。")]
    private float _blinkInterval = 0.1f;

    [Header("参照")]
    [SerializeField, Tooltip("ノックバック適用先の Player。未設定なら同 GameObject から自動取得。")]
    private Player _player;

    public int CurrentHp { get; private set; }
    public int MaxHp => _maxHp;
    public bool IsDead { get; private set; }
    public bool IsInvincible { get; private set; }

    public event Action OnDamaged;
    public event Action OnDead;

    private Coroutine _invincibleRoutine;

    private void Awake()
    {
        if (_player == null) _player = GetComponent<Player>();
        if (_spriteRenderer == null) _spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        CurrentHp = _maxHp;
        IsDead = false;
        IsInvincible = false;
    }

    /// <summary>
    /// ダメージを受ける。無敵中・死亡中は無視。ノックバックは knockbackImpulse で指定。
    /// </summary>
    public void TakeDamage(int amount, Vector2 knockbackImpulse = default)
    {
        if (IsDead || IsInvincible) return;
        if (amount <= 0) return;

        CurrentHp = Mathf.Max(0, CurrentHp - amount);

        if (_player != null && knockbackImpulse != Vector2.zero)
            _player.ApplyExternalImpulse(knockbackImpulse, ForceMode2D.Impulse);

        OnDamaged?.Invoke();

        if (CurrentHp <= 0)
        {
            Die();
            return;
        }

        StartInvincible();
    }

    /// <summary>
    /// 引数なし版：ノックバックを伴わない被弾。
    /// </summary>
    public void TakeDamage(int amount) => TakeDamage(amount, Vector2.zero);

    /// <summary>
    /// HP を最大まで回復し、死亡状態をリセットする（リスポーン用）。
    /// </summary>
    public void ResetHealth()
    {
        CurrentHp = _maxHp;
        IsDead = false;
        StopInvincible();
        if (_spriteRenderer != null) _spriteRenderer.enabled = true;
    }

    private void Die()
    {
        IsDead = true;
        StopInvincible();
        OnDead?.Invoke();
    }

    private void StartInvincible()
    {
        if (_invincibleDuration <= 0f) return;
        if (_invincibleRoutine != null) StopCoroutine(_invincibleRoutine);
        _invincibleRoutine = StartCoroutine(InvincibleRoutine());
    }

    private void StopInvincible()
    {
        if (_invincibleRoutine != null)
        {
            StopCoroutine(_invincibleRoutine);
            _invincibleRoutine = null;
        }
        IsInvincible = false;
        if (_spriteRenderer != null) _spriteRenderer.enabled = true;
    }

    private IEnumerator InvincibleRoutine()
    {
        IsInvincible = true;
        float elapsed = 0f;
        WaitForSeconds wait = new WaitForSeconds(_blinkInterval);

        while (elapsed < _invincibleDuration)
        {
            if (_spriteRenderer != null)
                _spriteRenderer.enabled = !_spriteRenderer.enabled;
            yield return wait;
            elapsed += _blinkInterval;
        }

        if (_spriteRenderer != null) _spriteRenderer.enabled = true;
        IsInvincible = false;
        _invincibleRoutine = null;
    }
}
