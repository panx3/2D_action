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

    [Header("Death Fragment")]
    [SerializeField] private GameObject fragmentPrefab;
    [SerializeField, Min(0)] private int fragmentCount = 10;
    [SerializeField, Min(0f)] private float fragmentSpread = 0.3f;
    [SerializeField, Min(0f)] private float minFragmentForce = 1.5f;
    [SerializeField, Min(0f)] private float maxFragmentForce = 3f;
    [SerializeField, Min(0f)] private float fragmentLifeTime = 2f;

    private int _currentHp;
    private Rigidbody2D _rigidbody2D;
    private float _hitStunTimer;
    private bool _deathHandled;
    private Vector2 _lastImpactDirection = Vector2.right;

    public int CurrentHp => _currentHp;
    public int MaxHp => maxHp;
    public bool IsHitStunned => _hitStunTimer > 0f;
    public bool IsDeathHandled => _deathHandled;
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

        if (context.ImpactDirection.sqrMagnitude > 0.0001f)
            _lastImpactDirection = context.ImpactDirection.normalized;
        else if (context.KnockbackImpulse.sqrMagnitude > 0.0001f)
            _lastImpactDirection = context.KnockbackImpulse.normalized;

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
        if (_deathHandled)
            return;

        _deathHandled = true;

        if (debugLog)
            Debug.Log($"[EnemyHealth] HandleDeath on {name}", this);

        DisableAfterDeath();
        FragmentBurst2D.Spawn(
            fragmentPrefab,
            transform.position,
            _lastImpactDirection,
            fragmentCount,
            fragmentSpread,
            minFragmentForce,
            maxFragmentForce,
            fragmentLifeTime);

        if (destroyOnDeath)
        {
            Destroy(gameObject);
            return;
        }

        gameObject.SetActive(false);
    }

    private void DisableAfterDeath()
    {
        Enemy movement = GetComponent<Enemy>();
        if (movement != null)
            movement.enabled = false;

        foreach (Collider2D col in GetComponentsInChildren<Collider2D>(true))
        {
            if (col != null)
                col.enabled = false;
        }

        foreach (SpriteRenderer renderer in GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (renderer != null)
                renderer.enabled = false;
        }

        if (_rigidbody2D != null)
        {
            _rigidbody2D.linearVelocity = Vector2.zero;
            _rigidbody2D.angularVelocity = 0f;
            _rigidbody2D.simulated = false;
        }
    }

    public void ResetHealth()
    {
        _currentHp = maxHp;
        _hitStunTimer = 0f;
        _deathHandled = false;
        _lastImpactDirection = Vector2.right;

        Enemy movement = GetComponent<Enemy>();
        if (movement != null)
            movement.enabled = true;

        foreach (Collider2D col in GetComponentsInChildren<Collider2D>(true))
        {
            if (col != null)
                col.enabled = true;
        }

        foreach (SpriteRenderer renderer in GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (renderer != null)
                renderer.enabled = true;
        }

        if (_rigidbody2D != null)
        {
            _rigidbody2D.simulated = true;
            _rigidbody2D.freezeRotation = true;
            _rigidbody2D.angularVelocity = 0f;
        }
    }
}
