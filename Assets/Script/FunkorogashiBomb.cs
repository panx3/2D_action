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
    [SerializeField] private LayerMask groundLayers = (1 << 0) | (1 << 6);
    [SerializeField, Min(0f)] private float groundArmDelay = 0.12f;
    [SerializeField, Min(0f)] private float maxFlightTime = 15f;
    [SerializeField] private Transform visualRoot;
    [SerializeField] private GameObject explosionEffectPrefab;

    private Rigidbody2D body;
    private CircleCollider2D bombCollider;
    private BombState state;
    private float launchedAt;
    private float spinSpeed;
    private bool exploded;
    public bool IsHeld => state == BombState.Held && !exploded;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        bombCollider = GetComponent<CircleCollider2D>();
        Hold();
    }

    public void Hold()
    {
        state = BombState.Held;
        body.simulated = false;
        bombCollider.enabled = false;
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
        spinSpeed = -Mathf.Sign(velocity.x) * rotationSpeed;
        body.simulated = true;
        bombCollider.enabled = true;
        if (ownerCollider != null) Physics2D.IgnoreCollision(bombCollider, ownerCollider, true);
        body.linearVelocity = velocity;
    }

    private void FixedUpdate()
    {
        if (state != BombState.Launched || exploded) return;
        if (visualRoot != null) visualRoot.Rotate(0f, 0f, spinSpeed * Time.fixedDeltaTime);
        if (maxFlightTime > 0f && Time.time - launchedAt >= maxFlightTime) Destroy(gameObject);
    }

    private void OnCollisionEnter2D(Collision2D collision) => TryDetonate(collision.collider);
    private void OnCollisionStay2D(Collision2D collision) => TryDetonate(collision.collider);
    private void TryDetonate(Collider2D other)
    {
        if (state != BombState.Launched || exploded || other == null) return;
        int bit = 1 << other.gameObject.layer;
        if (((playerLayers.value & bit) != 0 && other.GetComponentInParent<PlayerHealth>() != null) ||
            ((groundLayers.value & bit) != 0 && Time.time - launchedAt >= groundArmDelay)) Explode();
    }

    public void Explode()
    {
        if (exploded) return;
        exploded = true;
        state = BombState.Exploded;
        bombCollider.enabled = false;
        body.simulated = false;
        Vector2 origin = transform.position;
        Collider2D[] targets = Physics2D.OverlapCircleAll(origin, explosionRadius, playerLayers);
        var damaged = new HashSet<PlayerHealth>();
        foreach (var target in targets)
        {
            PlayerHealth health = target.GetComponentInParent<PlayerHealth>();
            if (health == null || !damaged.Add(health)) continue;
            Vector2 direction = (Vector2)target.bounds.center - origin;
            if (direction.sqrMagnitude < 0.0001f) direction = Vector2.up;
            health.TakeEnemyDamage(explosionDamage, direction.normalized * explosionKnockback);
        }
        if (explosionEffectPrefab != null) Instantiate(explosionEffectPrefab, transform.position, Quaternion.identity);
        Destroy(gameObject);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.4f, 0.1f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, explosionRadius);
    }
}
