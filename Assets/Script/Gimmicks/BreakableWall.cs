using UnityEngine;

/// <summary>
/// モーニングスターで破壊できる壁。破片演出付き。
/// </summary>
public class BreakableWall : MonoBehaviour, IMorningStarHitReceiver
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

    [Header("Debug")]
    [SerializeField] private bool showDebugLog = false;

    private bool _isBroken;
    private Collider2D _wallCollider;
    private SpriteRenderer _spriteRenderer;

    private void Awake()
    {
        _wallCollider = GetComponent<Collider2D>();
        _spriteRenderer = GetComponent<SpriteRenderer>();
    }

    public void OnMorningStarHit(MorningStarHitContext context)
    {
        if (_isBroken)
            return;

        Vector2 hitDirection = context.ImpactDirection.sqrMagnitude > 1e-6f
            ? context.ImpactDirection
            : Vector2.right;

        ApplyDamage(context.Damage, hitDirection);
    }

    private void ApplyDamage(int damage, Vector2 hitDirection)
    {
        hitPoint -= damage;

        if (showDebugLog)
            Debug.Log($"BreakableWall Damage: {damage}, HP: {hitPoint}");

        if (hitPoint <= 0)
            Break(hitDirection);
    }

    private void Break(Vector2 hitDirection)
    {
        _isBroken = true;

        if (_wallCollider != null)
            _wallCollider.enabled = false;

        if (_spriteRenderer != null)
            _spriteRenderer.enabled = false;

        SpawnFragments(hitDirection);
        Destroy(gameObject, fragmentLifeTime);
    }

    private void SpawnFragments(Vector2 hitDirection)
    {
        if (fragmentPrefab == null)
        {
            if (showDebugLog)
                Debug.LogWarning("Fragment Prefab is not assigned.");

            return;
        }

        for (int i = 0; i < fragmentCount; i++)
        {
            Vector3 offset = new Vector3(
                Random.Range(-fragmentSpread, fragmentSpread),
                Random.Range(-fragmentSpread, fragmentSpread),
                0f);

            GameObject fragment = Instantiate(
                fragmentPrefab,
                transform.position + offset,
                Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)));

            Rigidbody2D rb = fragment.GetComponent<Rigidbody2D>();
            if (rb == null)
                continue;

            Vector2 randomDirection = (hitDirection + Random.insideUnitCircle * 0.8f + Vector2.up * 0.4f).normalized;
            float force = Random.Range(minForce, maxForce);
            rb.AddForce(randomDirection * force, ForceMode2D.Impulse);
            rb.AddTorque(Random.Range(-180f, 180f));
            Destroy(fragment, fragmentLifeTime);
        }
    }
}
