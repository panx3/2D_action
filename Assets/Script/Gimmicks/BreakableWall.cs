using UnityEngine;

public class BreakableWall : MonoBehaviour
{
    [Header("Detect Settings")]
    [SerializeField] private string targetTag = "morningstar";

    [Header("Break Settings")]
    [SerializeField] private int hitPoint = 1;
    [SerializeField] private float minBreakSpeed = 6f;
    [SerializeField] private bool useSpeedDamage = true;
    [SerializeField] private float speedPerDamage = 6f;

    [Header("Fragment Settings")]
    [SerializeField] private GameObject fragmentPrefab;
    [SerializeField] private int fragmentCount = 10;
    [SerializeField] private float fragmentSpread = 0.3f;
    [SerializeField] private float minForce = 1.5f;
    [SerializeField] private float maxForce = 3.5f;
    [SerializeField] private float fragmentLifeTime = 2f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLog = false;

    private bool isBroken = false;
    private Collider2D wallCollider;
    private SpriteRenderer spriteRenderer;

    private void Awake()
    {
        wallCollider = GetComponent<Collider2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (isBroken) return;

        if (!collision.gameObject.CompareTag(targetTag)) return;

        float hitSpeed = collision.relativeVelocity.magnitude;

        if (showDebugLog)
        {
            Debug.Log($"BreakableWall Hit Speed: {hitSpeed:F2}");
        }

        if (hitSpeed < minBreakSpeed)
        {
            if (showDebugLog)
            {
                Debug.Log("BreakableWall: hit was too weak.");
            }

            return;
        }

        int damage = 1;

        if (useSpeedDamage)
        {
            damage = Mathf.Max(1, Mathf.FloorToInt(hitSpeed / Mathf.Max(0.01f, speedPerDamage)));
        }

        Vector2 hitDirection = GetHitDirection(collision);

        TakeDamage(damage, hitDirection);
    }

    private Vector2 GetHitDirection(Collision2D collision)
    {
        if (collision.rigidbody != null)
        {
            Vector2 velocity = collision.rigidbody.linearVelocity;

            if (velocity.sqrMagnitude > 0.01f)
            {
                return velocity.normalized;
            }
        }

        return ((Vector2)transform.position - (Vector2)collision.transform.position).normalized;
    }

    private void TakeDamage(int damage, Vector2 hitDirection)
    {
        hitPoint -= damage;

        if (showDebugLog)
        {
            Debug.Log($"BreakableWall Damage: {damage}, HP: {hitPoint}");
        }

        if (hitPoint <= 0)
        {
            Break(hitDirection);
        }
    }

    private void Break(Vector2 hitDirection)
    {
        isBroken = true;

        if (wallCollider != null)
        {
            wallCollider.enabled = false;
        }

        if (spriteRenderer != null)
        {
            spriteRenderer.enabled = false;
        }

        SpawnFragments(hitDirection);

        Destroy(gameObject, fragmentLifeTime);
    }

    private void SpawnFragments(Vector2 hitDirection)
    {
        if (fragmentPrefab == null)
        {
            if (showDebugLog)
            {
                Debug.LogWarning("Fragment Prefab is not assigned.");
            }

            return;
        }

        for (int i = 0; i < fragmentCount; i++)
        {
            Vector3 offset = new Vector3(
                Random.Range(-fragmentSpread, fragmentSpread),
                Random.Range(-fragmentSpread, fragmentSpread),
                0f
            );

            GameObject fragment = Instantiate(
                fragmentPrefab,
                transform.position + offset,
                Quaternion.Euler(0f, 0f, Random.Range(0f, 360f))
            );

            Rigidbody2D rb = fragment.GetComponent<Rigidbody2D>();

            if (rb != null)
            {
                Vector2 randomDirection = (hitDirection + Random.insideUnitCircle * 0.8f + Vector2.up * 0.3f).normalized;
                float force = Random.Range(minForce, maxForce);

                rb.AddForce(randomDirection * force, ForceMode2D.Impulse);
                rb.AddTorque(Random.Range(-180f, 180f));
            }

            Destroy(fragment, fragmentLifeTime);
        }
    }
}