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
    [SerializeField] private float hitStunDuration = 0.18f;
    [SerializeField] private float maxKnockbackSpeed = 8f;
    [SerializeField] private bool freezeRotationOnHit = true;
    [SerializeField] private bool stopAngularVelocityOnHit = true;
    [SerializeField] private bool debugLog;

    [Header("Hit SFX")]
    [SerializeField] private AudioSource hitAudioSource;
    [SerializeField] private AudioClip morningStarHitClip;
    [SerializeField, Range(0f, 1f)] private float morningStarHitVolume = 0.9f;

    private int _currentHp;
    private Rigidbody2D _rigidbody2D;
    private float _hitStunTimer;

    public int CurrentHp => _currentHp;
    public int MaxHp => maxHp;
    public bool IsHitStunned => _hitStunTimer > 0f;
    public int HitSoundPlayCount { get; private set; }

    private void Awake()
    {
        _currentHp = maxHp;
        _rigidbody2D = GetComponent<Rigidbody2D>();
        if (hitAudioSource == null)
            hitAudioSource = OneShotAudioUtility.FindWorldImpactSource();
        if (_rigidbody2D != null)
        {
            _rigidbody2D.freezeRotation = true;
            _rigidbody2D.angularVelocity = 0f;
        }
    }

    private void FixedUpdate()
    {
        if (_hitStunTimer > 0f)
        {
            ClampKnockbackSpeed();
            _hitStunTimer = Mathf.Max(0f, _hitStunTimer - Time.fixedDeltaTime);
        }

        if (_rigidbody2D != null && stopAngularVelocityOnHit)
            _rigidbody2D.angularVelocity = 0f;
    }

    public void OnMorningStarHit(MorningStarHitContext context)
    {
        if (_currentHp <= 0 || context.Damage <= 0)
            return;

        _currentHp = Mathf.Max(0, _currentHp - context.Damage);
        if (OneShotAudioUtility.Play2D(hitAudioSource, morningStarHitClip, morningStarHitVolume, transform.position))
            HitSoundPlayCount++;

        if (debugLog)
            Debug.Log($"Enemy hit damage={context.Damage}");

        if (_rigidbody2D != null)
        {
            if (freezeRotationOnHit)
                _rigidbody2D.freezeRotation = true;
            if (stopAngularVelocityOnHit)
                _rigidbody2D.angularVelocity = 0f;

            if (context.KnockbackImpulse.sqrMagnitude > 0.0001f)
            {
                _rigidbody2D.AddForce(context.KnockbackImpulse / knockbackResistance, ForceMode2D.Impulse);
                ClampKnockbackSpeed();
            }

            if (stopAngularVelocityOnHit)
                _rigidbody2D.angularVelocity = 0f;
        }

        _hitStunTimer = hitStunDuration;

        if (_currentHp <= 0)
            HandleDeath();
    }

    private void ClampKnockbackSpeed()
    {
        if (_rigidbody2D == null || maxKnockbackSpeed <= 0f)
            return;

        Vector2 velocity = _rigidbody2D.linearVelocity;
        if (velocity.sqrMagnitude > maxKnockbackSpeed * maxKnockbackSpeed)
            _rigidbody2D.linearVelocity = velocity.normalized * maxKnockbackSpeed;
    }

    private void HandleDeath()
    {
        if (debugLog)
            Debug.Log($"[EnemyHealth] HandleDeath on {name}", this);

        if (destroyOnDeath)
        {
            Destroy(gameObject);
            return;
        }

        foreach (Collider2D col in GetComponents<Collider2D>())
            col.enabled = false;

        if (_rigidbody2D != null)
        {
            _rigidbody2D.linearVelocity = Vector2.zero;
            _rigidbody2D.angularVelocity = 0f;
            _rigidbody2D.simulated = false;
        }

        gameObject.SetActive(false);
    }

    public void ResetHealth()
    {
        _currentHp = maxHp;
        _hitStunTimer = 0f;

        foreach (Collider2D col in GetComponents<Collider2D>())
            col.enabled = true;

        if (_rigidbody2D != null)
        {
            _rigidbody2D.simulated = true;
            _rigidbody2D.freezeRotation = true;
            _rigidbody2D.angularVelocity = 0f;
        }
    }
}
