using UnityEngine;

/// <summary>
/// 敵の HP とモーニングスター被弾処理。
/// </summary>
[DisallowMultipleComponent]
public class EnemyHealth : MonoBehaviour, IMorningStarHitReceiver
{
    [SerializeField] private int maxHp = 3;
    [SerializeField, Min(0.01f)] private float knockbackResistance = 1f;
    [SerializeField] private bool destroyOnDeath = true;

    private int _currentHp;
    private Rigidbody2D _rigidbody2D;

    public int CurrentHp => _currentHp;
    public int MaxHp => maxHp;

    private void Awake()
    {
        _currentHp = maxHp;
        _rigidbody2D = GetComponent<Rigidbody2D>();
    }

    public void OnMorningStarHit(MorningStarHitContext context)
    {
        if (_currentHp <= 0)
            return;

        _currentHp = Mathf.Max(0, _currentHp - context.Damage);

        if (_rigidbody2D != null && context.KnockbackImpulse.sqrMagnitude > 0.0001f)
            _rigidbody2D.AddForce(context.KnockbackImpulse / knockbackResistance, ForceMode2D.Impulse);

        if (_currentHp <= 0)
            HandleDeath();
    }

    private void HandleDeath()
    {
        if (destroyOnDeath)
            Destroy(gameObject);
        else
            gameObject.SetActive(false);
    }

    public void ResetHealth()
    {
        _currentHp = maxHp;
    }
}
