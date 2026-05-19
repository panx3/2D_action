using UnityEngine;

public class BreakableWall : MonoBehaviour
{
    [Header("Break Settings")]
    [SerializeField] private int hitPoint = 1;

    [Header("Fragment Settings")]
    [SerializeField] private GameObject fragmentPrefab;
    [SerializeField] private int fragmentCount = 10;
    [SerializeField] private float fragmentSpread = 0.3f;
    [SerializeField] private float minForce = 1.5f;
    [SerializeField] private float maxForce = 3.5f;
    [SerializeField] private float fragmentLifeTime = 2f;

    private const string MorningStarTag = "morningstar";

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

        if (collision.gameObject.CompareTag(MorningStarTag))
        {
            Vector2 hitDirection = ((Vector2)transform.position - (Vector2)collision.transform.position).normalized;
            TakeDamage(1, hitDirection);
        }
    }

    private void TakeDamage(int damage, Vector2 hitDirection)
    {
        hitPoint -= damage;

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
            Debug.LogWarning("Fragment Prefab is not assigned.");
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
                Vector2 randomDirection = (hitDirection + Random.insideUnitCircle * 0.8f + Vector2.up * 0.4f).normalized;
                float force = Random.Range(minForce, maxForce);

                rb.AddForce(randomDirection * force, ForceMode2D.Impulse);
                rb.AddTorque(Random.Range(-180f, 180f));
            }

            Destroy(fragment, fragmentLifeTime);
        }
    }
}