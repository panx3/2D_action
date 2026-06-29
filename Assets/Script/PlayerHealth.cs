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
    private int _maxHp = 14;
    [SerializeField, Tooltip("被弾後の無敵時間（秒）。")]
    private float _invincibleDuration = 1.2f;

    [Header("デバッグ")]
    [SerializeField, Tooltip("H=1ダメージ / R=全回復（テスト用）")]
    private bool _enableDebugKeys;
    [SerializeField, Tooltip("Heal / ResetToFullHp 呼び出し時にログを出す")]
    private bool _debugLogHeal;

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
    /// <summary>現在 HP と最大 HP（UI 更新用）。</summary>
    public event Action<int, int> OnHealthChanged;

    /// <summary>OnHealthChanged の別名（SegmentHpBarUI 等向け）。</summary>
    public event Action<int, int> OnHpChanged
    {
        add => OnHealthChanged += value;
        remove => OnHealthChanged -= value;
    }

    private Coroutine _invincibleRoutine;
    private bool _initialized;

    private void Awake()
    {
        if (_player == null) _player = GetComponent<Player>();
        if (_spriteRenderer == null) _spriteRenderer = GetComponentInChildren<SpriteRenderer>();

        if (_initialized)
            return;

        _initialized = true;
        CurrentHp = _maxHp;
        IsDead = false;
        IsInvincible = false;
        NotifyHealthChanged();
    }

    private void Update()
    {
        if (!_enableDebugKeys) return;
        if (Input.GetKeyDown(KeyCode.H)) TakeDamage(1);
        if (Input.GetKeyDown(KeyCode.R)) ResetToFullHp();
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
        NotifyHealthChanged();

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

    /// <summary>HP を回復する（最大値を超えない）。</summary>
    // Reserved for future heal items. Do not call this from enemy death rewards.
    public void Heal(int amount)
    {
        if (IsDead || amount <= 0) return;
        int before = CurrentHp;
        CurrentHp = Mathf.Min(_maxHp, CurrentHp + amount);
        if (_debugLogHeal && CurrentHp != before)
            Debug.Log($"[PlayerHealth] Heal({amount}) {before} -> {CurrentHp} (caller: {GetHealCallerHint()})", this);
        NotifyHealthChanged();
    }

    /// <summary>
    /// ステージ開始・ゴール後・明示リトライ専用。通常戦闘・敵撃破では呼ばない。
    /// </summary>
    public void ResetToFullHp()
    {
        int before = CurrentHp;
        CurrentHp = _maxHp;
        IsDead = false;
        StopInvincible();
        if (_spriteRenderer != null) _spriteRenderer.enabled = true;
        if (_debugLogHeal && before != CurrentHp)
            Debug.Log($"[PlayerHealth] ResetToFullHp {before} -> {CurrentHp} (caller: {GetHealCallerHint()})", this);
        NotifyHealthChanged();
    }

    /// <summary>
    /// HP を変えずに死亡状態だけ解除（手動リスポーン等）。HP0 のリトライは ResetToFullHp を使う。
    /// </summary>
    public void ReviveKeepCurrentHp()
    {
        if (CurrentHp <= 0)
            return;

        IsDead = false;
        StopInvincible();
        if (_spriteRenderer != null) _spriteRenderer.enabled = true;
        NotifyHealthChanged();
    }

    /// <summary>互換用。新規コードは ResetToFullHp を使う。</summary>
    public void ResetHealth() => ResetToFullHp();

    private void NotifyHealthChanged()
    {
        OnHealthChanged?.Invoke(CurrentHp, _maxHp);
    }

    private static string GetHealCallerHint()
    {
        var trace = new System.Diagnostics.StackTrace(2, false);
        for (int i = 0; i < trace.FrameCount; i++)
        {
            var method = trace.GetFrame(i)?.GetMethod();
            if (method == null)
                continue;
            if (method.DeclaringType == typeof(PlayerHealth))
                continue;
            return $"{method.DeclaringType?.Name}.{method.Name}";
        }
        return "unknown";
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
