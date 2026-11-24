using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D))]
public sealed class FunkorogashiBomb : MonoBehaviour
{
    private enum BombState { Held, Launched, Exploded }
    [SerializeField, Min(0f)] private float explosionRadius = 1.5f;
    [SerializeField, Min(0)] private int explosionDamage = 1;
    [SerializeField, Min(0f)] private float explosionKnockback = 5f;
    [SerializeField, HideInInspector] private LayerMask playerLayers = 1 << 3; // Preserve existing prefab data.
    [SerializeField, HideInInspector] private LayerMask groundLayers = (1 << 0) | (1 << 6); // Preserve existing prefab data.
    [SerializeField, HideInInspector] private float groundArmDelay = 0.12f; // Preserve existing prefab data.
    [SerializeField, Min(0.1f), Tooltip("射出・切り離しから爆発までの秒数。保持中は点火しません。")] private float fuseDuration = 3f;
    [SerializeField] private Transform fuseRoot;
    [SerializeField] private Transform fuseFlame;
    [SerializeField, HideInInspector] private float maxFlightTime = 15f; // Preserve existing prefab data.
    [SerializeField] private Transform visualRoot;
    [SerializeField] private GameObject explosionEffectPrefab;
    [SerializeField, HideInInspector] private SpriteRenderer bombSpriteRenderer; // Preserve existing prefab data.
    [SerializeField, HideInInspector] private Sprite[] fuseSprites; // Preserve existing prefab data.
    [SerializeField, Min(0f), Tooltip("鉄球との衝突速度に対する上向き速度の割合。")]
    private float ballImpactLiftRatio = 0.35f;
    [SerializeField, Min(0f)] private float maxBallImpactLift = 6f;

    private Rigidbody2D body;
    private CircleCollider2D bombCollider;
    private BombState state;
    private float launchedAt;
    private Vector3 fuseInitialScale;
    private Vector3 flameInitialPosition;
    private Vector3 flameInitialScale;
    private bool exploded;
    public bool IsHeld => state == BombState.Held && !exploded;
    public event System.Action<FunkorogashiBomb> Exploded;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        bombCollider = GetComponent<CircleCollider2D>();
        if (fuseRoot != null) fuseInitialScale = fuseRoot.localScale;
        if (fuseFlame != null) { flameInitialPosition = fuseFlame.localPosition; flameInitialScale = fuseFlame.localScale; }
        Hold();
    }

    public void Hold()
    {
        state = BombState.Held;
        body.simulated = false;
        bombCollider.enabled = false;
        if (fuseFlame != null) fuseFlame.gameObject.SetActive(false);
    }

    public void Roll(float velocityX, float degreesPerSecond, float deltaTime)
    {
        if (!IsHeld || visualRoot == null || Mathf.Abs(velocityX) < 0.001f) return;
        visualRoot.Rotate(0f, 0f, -Mathf.Sign(velocityX) * degreesPerSecond * Mathf.Abs(velocityX) * deltaTime);
    }

    public void Launch(Vector2 velocity, float rotationSpeed, Collider2D ownerCollider)
    {
        if (!IsHeld) return;
        transform.SetParent(null, true);
        state = BombState.Launched;
        launchedAt = Time.time;
        if (fuseFlame != null) fuseFlame.gameObject.SetActive(true);
        body.simulated = true;
        bombCollider.enabled = true;
        if (ownerCollider != null) Physics2D.IgnoreCollision(bombCollider, ownerCollider, true);
        body.linearVelocity = velocity;
    }

    private void Update()
    {
        if (exploded || state != BombState.Launched) return;
        float remaining = Mathf.Clamp01(1f - (Time.time - launchedAt) / Mathf.Max(0.1f, fuseDuration));
        if (fuseRoot != null)
            fuseRoot.localScale = new Vector3(fuseInitialScale.x, fuseInitialScale.y * remaining, fuseInitialScale.z);
        if (fuseFlame != null)
        {
            fuseFlame.localPosition = new Vector3(flameInitialPosition.x, flameInitialPosition.y * remaining, flameInitialPosition.z);
            fuseFlame.localScale = flameInitialScale * (0.9f + 0.1f * Mathf.Sin(Time.time * 24f));
        }
    }

    private void FixedUpdate()
    {
        if (exploded) return;
        if (state == BombState.Held) { TryReleaseFromMorningStar(); return; }
        if (state != BombState.Launched) return;
        if (Time.time - launchedAt >= Mathf.Max(0.1f, fuseDuration)) { Explode(); return; }
        if (visualRoot != null)
        {
            float radius = bombCollider.radius * Mathf.Abs(transform.lossyScale.x);
            float rollSpeed = radius > 0.001f ? -body.linearVelocity.x / radius * Mathf.Rad2Deg : 0f;
            visualRoot.Rotate(0f, 0f, rollSpeed * Time.fixedDeltaTime);
        }
    }

    private void TryReleaseFromMorningStar()
    {
        Vector2 center = transform.TransformPoint(bombCollider.offset);
        Vector3 scale = transform.lossyScale;
        float radius = bombCollider.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
        foreach (Collider2D other in Physics2D.OverlapCircleAll(center, radius, LayerMask.GetMask("morningstar")))
        {
            if (other == null || other.isTrigger) continue;
            Rigidbody2D ball = other.attachedRigidbody;
            if (!other.CompareTag("morningstar") && (ball == null || ball.GetComponent<MorningStarCollisionReporter>() == null)) continue;
            var owner = GetComponentInParent<Funkorogashi>();
            Collider2D ownerCollider = owner != null ? owner.GetComponent<Collider2D>() : null;
            if (owner != null) owner.OnHeldBombReleased(this);
            Vector2 hitVelocity = ball != null ? ball.linearVelocity : Vector2.zero;
            if (hitVelocity.sqrMagnitude > 0.25f)
                hitVelocity.y = Mathf.Max(hitVelocity.y, Mathf.Min(maxBallImpactLift, hitVelocity.magnitude * ballImpactLiftRatio));
            Launch(hitVelocity, owner != null ? owner.BombRotationSpeed : 240f, ownerCollider);
            return;
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (state != BombState.Launched || exploded || collision.collider == null) return;
        Rigidbody2D ball = collision.collider.attachedRigidbody;
        if (!collision.collider.CompareTag("morningstar") && (ball == null || ball.GetComponent<MorningStarCollisionReporter>() == null)) return;
        float impactSpeed = collision.relativeVelocity.magnitude;
        if (impactSpeed < 0.5f) return;
        Vector2 velocity = body.linearVelocity;
        velocity.y = Mathf.Max(velocity.y, Mathf.Min(maxBallImpactLift, impactSpeed * ballImpactLiftRatio));
        body.linearVelocity = velocity;
    }

    public void Explode()
    {
        if (exploded) return;
        exploded = true;
        bool wasHeld = state == BombState.Held;
        state = BombState.Exploded;
        Exploded?.Invoke(this);
        if (wasHeld)
        {
            var owner = GetComponentInParent<Funkorogashi>();
            if (owner != null) owner.OnHeldBombReleased(this);
        }
        bombCollider.enabled = false;
        body.simulated = false;
        Vector2 origin = transform.TransformPoint(bombCollider.offset);
        Collider2D[] targets = Physics2D.OverlapCircleAll(origin, explosionRadius);
        var damaged = new HashSet<PlayerHealth>();
        var damagedEnemies = new HashSet<EnemyHealth>();
        foreach (var target in targets)
        {
            PlayerHealth health = target.GetComponentInParent<PlayerHealth>();
            Vector2 direction = (Vector2)target.bounds.center - origin;
            if (direction.sqrMagnitude < 0.0001f) direction = Vector2.up;
            Vector2 impulse = direction.normalized * explosionKnockback;
            if (health != null && PlayerColliderUtility.IsPlayerBody(target) && damaged.Add(health))
                health.TakeEnemyDamage(explosionDamage, impulse);
            EnemyHealth enemy = target.GetComponentInParent<EnemyHealth>();
            if (enemy != null && damagedEnemies.Add(enemy)) enemy.TakeExplosionDamage(explosionDamage, impulse, origin);
        }
        if (explosionEffectPrefab != null) Instantiate(explosionEffectPrefab, origin, Quaternion.identity);
        var sparks = FindAnyObjectByType<MorningStarImpactSparks>();
        if (sparks != null) sparks.PlayPressImpact(origin, 32);
        var camera = Camera.main;
        var shake = camera != null ? camera.GetComponent<CameraShake2D>() : null;
        if (shake != null) shake.Shake(0.18f, 0.16f);
        Destroy(gameObject);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.4f, 0.1f, 0.8f);
        var circle = GetComponent<CircleCollider2D>();
        Vector3 center = circle != null ? transform.TransformPoint(circle.offset) : transform.position;
        Gizmos.DrawWireSphere(center, explosionRadius);
    }
}
