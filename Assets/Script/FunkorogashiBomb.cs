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
    [SerializeField] private LayerMask playerLayers = 1 << 3;
    [SerializeField, HideInInspector] private LayerMask groundLayers = (1 << 0) | (1 << 6);
    [SerializeField, HideInInspector] private float groundArmDelay = 0.12f;
    [SerializeField, Min(0.1f), Tooltip("射出から爆発までの秒数。プレイヤー本体に触れた場合は即座に爆発する。")]
    private float fuseDuration = 3f;
    [SerializeField] private Transform fuseRoot;
    [SerializeField] private Transform fuseFlame;
    [SerializeField, HideInInspector] private float maxFlightTime = 15f;
    [SerializeField] private Transform visualRoot;
    [SerializeField] private GameObject explosionEffectPrefab;
    [SerializeField] private AudioClip explosionClip;
    [SerializeField] private SpriteRenderer bombSpriteRenderer;
    [SerializeField] private Sprite[] fuseSprites;
    [SerializeField, Min(0f), Tooltip("鉄球との衝突速度に対する上向き速度の割合。")]
    private float ballImpactLiftRatio = 0.35f;
    [SerializeField, Min(0f)] private float maxBallImpactLift = 6f;

    private readonly List<Collider2D> contacts = new List<Collider2D>(8);
    private Rigidbody2D body;
    private CircleCollider2D bombCollider;
    private ContactFilter2D ballFilter;
    private ContactFilter2D playerFilter;
    private BombState state;
    private float launchedAt;
    private Vector3 fuseInitialScale;
    private Vector3 flameInitialPosition;
    private Vector3 flameInitialScale;
    private SpriteRenderer flameRenderer;
    private Color bombBaseColor;

    public bool IsHeld => state == BombState.Held;
    private Vector2 Center => transform.TransformPoint(bombCollider.offset);
    private float WorldRadius
    {
        get
        {
            Vector3 scale = transform.lossyScale;
            return bombCollider.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
        }
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        bombCollider = GetComponent<CircleCollider2D>();
        ballFilter = new ContactFilter2D { useTriggers = false };
        ballFilter.SetLayerMask(LayerMask.GetMask("morningstar"));
        playerFilter = new ContactFilter2D { useTriggers = false };
        playerFilter.SetLayerMask(playerLayers);

        if (bombSpriteRenderer != null) bombBaseColor = bombSpriteRenderer.color;
        if (fuseRoot != null) fuseInitialScale = fuseRoot.localScale;
        if (fuseFlame != null)
        {
            flameInitialPosition = fuseFlame.localPosition;
            flameInitialScale = fuseFlame.localScale;
            flameRenderer = fuseFlame.GetComponent<SpriteRenderer>();
        }

        Hold();
    }

    public void Hold()
    {
        state = BombState.Held;
        body.simulated = false;
        bombCollider.enabled = false;
        if (fuseRoot != null) fuseRoot.localScale = fuseInitialScale;
        if (fuseFlame != null) fuseFlame.gameObject.SetActive(false);
        if (bombSpriteRenderer != null) bombSpriteRenderer.color = bombBaseColor;
    }

    internal void Roll(float velocityX, float deltaTime)
    {
        if (visualRoot == null || Mathf.Abs(velocityX) < 0.001f) return;
        float radius = WorldRadius;
        if (radius > 0.001f)
            visualRoot.Rotate(0f, 0f, -velocityX / radius * Mathf.Rad2Deg * deltaTime);
    }

    internal void SettleForThrow(float deltaTime)
    {
        if (visualRoot != null)
            visualRoot.localRotation = Quaternion.RotateTowards(visualRoot.localRotation, Quaternion.identity, 540f * deltaTime);
    }

    internal void LaunchAt(Vector2 target, float travelSpeed, Collider2D ownerCollider)
    {
        Vector2 displacement = target - Center;
        float flightTime = Mathf.Clamp(displacement.magnitude / Mathf.Max(0.1f, travelSpeed), 0.45f, 0.95f);
        Vector2 gravity = Physics2D.gravity * body.gravityScale;
        Launch(displacement / flightTime - gravity * (0.5f * flightTime), ownerCollider);
    }

    public void Launch(Vector2 velocity, Collider2D ownerCollider)
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
        UpdateFuseVisual(0f);
    }

    private void Update()
    {
        if (state == BombState.Launched) UpdateFuseVisual(Time.time - launchedAt);
    }

    private void UpdateFuseVisual(float elapsed)
    {
        float remaining = Mathf.Clamp01(1f - elapsed / Mathf.Max(0.1f, fuseDuration));
        float ignition = 1f - Mathf.Clamp01(elapsed / 0.1f);
        float urgency = Mathf.Clamp01(1f - remaining * 4f);
        float warningPulse = urgency * (0.5f + 0.5f * Mathf.Sin(elapsed * 28f));
        if (fuseRoot != null)
            fuseRoot.localScale = new Vector3(fuseInitialScale.x, fuseInitialScale.y * remaining, fuseInitialScale.z);

        if (fuseFlame != null)
        {
            fuseFlame.localPosition = new Vector3(flameInitialPosition.x, flameInitialPosition.y * remaining, flameInitialPosition.z);
            fuseFlame.rotation = Quaternion.Euler(0f, 0f, Mathf.Clamp(body.linearVelocity.x * 2.5f, -22f, 22f));
            float flicker = 0.06f * Mathf.Sin(elapsed * 18f);
            fuseFlame.localScale = flameInitialScale * (1f + flicker + 0.38f * ignition + 0.4f * warningPulse);
            if (flameRenderer != null && fuseSprites != null && fuseSprites.Length > 0)
                flameRenderer.sprite = fuseSprites[Mathf.FloorToInt(elapsed * 13f) % fuseSprites.Length];
        }

        if (bombSpriteRenderer != null)
            bombSpriteRenderer.color = Color.Lerp(bombBaseColor, new Color(1f, 0.59f, 0.36f, bombBaseColor.a), warningPulse * 0.78f);
    }

    private void FixedUpdate()
    {
        if (state == BombState.Held)
        {
            TryDetonateHeldStrike();
            return;
        }

        if (state != BombState.Launched) return;
        if (Time.time - launchedAt >= Mathf.Max(0.1f, fuseDuration) || TouchesPlayer())
        {
            Explode();
            return;
        }

        Roll(body.linearVelocity.x, Time.fixedDeltaTime);
    }

    internal bool TryDetonateHeldStrike()
    {
        if (!IsHeld) return false;
        int count = gameObject.scene.GetPhysicsScene2D().OverlapCircle(Center, WorldRadius, ballFilter, contacts);
        for (int i = 0; i < count; i++)
        {
            if (!TryGetIronBall(contacts[i], out Rigidbody2D ball)) continue;
            var reporter = ball.GetComponent<MorningStarCollisionReporter>();
            var launcher = reporter != null ? reporter.Launcher : null;
            float impactSpeed = ball.linearVelocity.magnitude;
            if (!(launcher != null ? launcher.CanDealCombatDamage(impactSpeed) : impactSpeed >= 3f)) continue;
            Explode();
            return true;
        }

        return false;
    }

    private bool TouchesPlayer()
    {
        int count = gameObject.scene.GetPhysicsScene2D().OverlapCircle(Center, WorldRadius, playerFilter, contacts);
        for (int i = 0; i < count; i++)
            if (IsLivePlayerBody(contacts[i])) return true;

        return false;
    }

    private static bool IsLivePlayerBody(Collider2D other)
    {
        if (!PlayerColliderUtility.IsPlayerBody(other)) return false;
        PlayerHealth health = other.GetComponentInParent<PlayerHealth>();
        return health != null && !health.IsDead && !health.IsRespawning;
    }

    private static bool TryGetIronBall(Collider2D other, out Rigidbody2D ball)
    {
        ball = other != null ? other.attachedRigidbody : null;
        return ball != null && (other.CompareTag("morningstar") || ball.CompareTag("morningstar") ||
            ball.TryGetComponent<MorningStarCollisionReporter>(out _));
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (!isActiveAndEnabled || state != BombState.Launched) return;
        if (IsLivePlayerBody(collision.collider))
        {
            Explode();
            return;
        }

        if (!TryGetIronBall(collision.collider, out Rigidbody2D ball)) return;
        float impactSpeed = collision.relativeVelocity.magnitude;
        if (impactSpeed < 0.5f) return;

        Vector2 away = Center - ball.worldCenterOfMass;
        if (collision.contactCount > 0)
        {
            Vector2 normal = collision.GetContact(0).normal;
            away = Vector2.Dot(normal, away) < 0f ? -normal : normal;
        }

        if (away.sqrMagnitude < 0.001f) return;
        away.Normalize();
        Vector2 velocity = body.linearVelocity;
        float kick = Mathf.Min(14f, impactSpeed * 0.8f);
        velocity += away * Mathf.Max(0f, kick - Vector2.Dot(velocity, away));
        velocity.y = Mathf.Max(velocity.y, Mathf.Min(maxBallImpactLift, impactSpeed * ballImpactLiftRatio));
        body.linearVelocity = velocity;
    }

    public void Explode() => Explode(null);

    internal void Explode(EnemyHealth alreadyDamagedEnemy)
    {
        if (state == BombState.Exploded) return;
        bool wasHeld = IsHeld;
        state = BombState.Exploded;
        if (wasHeld)
        {
            var owner = GetComponentInParent<Funkorogashi>();
            if (owner != null) owner.OnHeldBombLost(this);
        }

        bombCollider.enabled = false;
        body.simulated = false;
        if (visualRoot != null) visualRoot.gameObject.SetActive(false);
        if (TryGetComponent<GroundProjectedShadow>(out var shadow)) shadow.enabled = false;

        Vector2 origin = Center;
        var explosionFilter = new ContactFilter2D { useTriggers = Physics2D.queriesHitTriggers };
        int count = gameObject.scene.GetPhysicsScene2D().OverlapCircle(origin, explosionRadius, explosionFilter, contacts);
        var damaged = new HashSet<PlayerHealth>();
        var damagedEnemies = new HashSet<EnemyHealth>();
        for (int i = 0; i < count; i++)
        {
            Collider2D target = contacts[i];
            if (target == null) continue;
            Vector2 direction = (Vector2)target.bounds.center - origin;
            if (direction.sqrMagnitude < 0.0001f) direction = Vector2.up;
            Vector2 impulse = direction.normalized * explosionKnockback;
            PlayerHealth health = target.GetComponentInParent<PlayerHealth>();
            if (health != null && PlayerColliderUtility.IsPlayerBody(target) && damaged.Add(health))
                health.TakeEnemyDamage(explosionDamage, impulse);

            EnemyHealth enemy = target.GetComponentInParent<EnemyHealth>();
            if (enemy != null && enemy != alreadyDamagedEnemy && damagedEnemies.Add(enemy))
                enemy.TakeExplosionDamage(explosionDamage, impulse, origin, enemy.TryGetComponent<Funkorogashi>(out _));
        }

        if (explosionEffectPrefab != null) Instantiate(explosionEffectPrefab, origin, Quaternion.identity);
        OneShotAudioUtility.Play2D(OneShotAudioUtility.FindWorldImpactSource(), explosionClip, 0.46f, origin);
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
