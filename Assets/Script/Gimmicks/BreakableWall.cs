using UnityEngine;

/// <summary>
/// モーニングスターで破壊できる壁。破片演出付き。
/// 子オブジェクトのSpriteRendererも破壊直後に隠すため、
/// 木箱を子として並べた見た目でも、壊れたあとに残らない。
/// </summary>
public class BreakableWall : MonoBehaviour, IMorningStarHitReceiver
{
    [Header("Break Settings")]
    [SerializeField] private int hitPoint = 1;
    [SerializeField, Min(0f)] private float breakSpeedThreshold = 6f;

    [Header("Fragment Settings")]
    [SerializeField] private GameObject fragmentPrefab;
    [SerializeField] private int fragmentCount = 10;
    [SerializeField] private float fragmentSpread = 0.3f;
    [SerializeField] private float minForce = 1.5f;
    [SerializeField] private float maxForce = 3.5f;
    [SerializeField] private float fragmentLifeTime = 2f;

    [Header("Break SFX")]
    [SerializeField] private AudioSource breakAudioSource;
    [SerializeField] private AudioClip breakImpactClip;
    [SerializeField, Range(0f, 1f)] private float breakImpactVolume = 0.9f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLog = false;

    private bool isBroken;
    private Collider2D wallCollider;

    public bool IsBroken => isBroken;
    public float BreakSpeedThreshold => breakSpeedThreshold;
    public int BreakSoundPlayCount { get; private set; }

    private void Awake()
    {
        wallCollider = GetComponent<Collider2D>();
        if (breakAudioSource == null)
            breakAudioSource = OneShotAudioUtility.FindWorldImpactSource();
    }

    public void OnMorningStarHit(MorningStarHitContext context)
    {
        if (isBroken || context.Damage <= 0 || context.ImpactSpeed < breakSpeedThreshold)
            return;

        Vector2 hitDirection = context.ImpactDirection.sqrMagnitude > 1e-6f
            ? context.ImpactDirection
            : Vector2.right;

        ApplyDamage(context.Damage, hitDirection);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (isBroken || !TryGetMorningStarBody(collision.collider, out Rigidbody2D morningStarBody))
            return;

        // OnCollisionEnter2D の時点では物理解決後の速度が落ちている場合があるため、
        // 衝突相対速度も比較して、衝突直前の勢いを取りこぼさないようにする。
        float impactSpeed = Mathf.Max(
            collision.relativeVelocity.magnitude,
            morningStarBody.linearVelocity.magnitude);

        if (impactSpeed < breakSpeedThreshold)
            return;

        Vector2 hitDirection = GetCollisionDirection(collision, morningStarBody);
        ApplyDamage(1, hitDirection);
    }

    private static bool TryGetMorningStarBody(Collider2D other, out Rigidbody2D morningStarBody)
    {
        morningStarBody = other != null ? other.attachedRigidbody : null;
        if (morningStarBody == null)
            return false;

        // 既存の morningstar Tag と衝突転送Componentを再利用する。
        // Player / Enemy 等の別 Rigidbody2D はここを通過しない。
        return morningStarBody.CompareTag("morningstar")
            && morningStarBody.GetComponent<MorningStarCollisionReporter>() != null;
    }

    private static Vector2 GetCollisionDirection(Collision2D collision, Rigidbody2D morningStarBody)
    {
        if (collision.contactCount > 0)
        {
            Vector2 direction = -collision.GetContact(0).normal;
            if (direction.sqrMagnitude > 1e-6f)
                return direction.normalized;
        }

        if (morningStarBody.linearVelocity.sqrMagnitude > 1e-6f)
            return morningStarBody.linearVelocity.normalized;

        return Vector2.right;
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
        isBroken = true;

        if (OneShotAudioUtility.Play2D(breakAudioSource, breakImpactClip, breakImpactVolume, transform.position))
            BreakSoundPlayCount++;

        if (wallCollider != null)
            wallCollider.enabled = false;

        HideAllVisuals();
        SpawnFragments(hitDirection);

        // 破片の寿命と合わせて、親オブジェクトも後で消す。
        Destroy(gameObject, fragmentLifeTime);
    }

    private void HideAllVisuals()
    {
        SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);
        foreach (SpriteRenderer renderer in renderers)
        {
            if (renderer != null)
                renderer.enabled = false;
        }
    }

    private void SpawnFragments(Vector2 hitDirection)
    {
        if (fragmentPrefab == null)
        {
            if (showDebugLog)
                Debug.LogWarning("Fragment Prefab is not assigned.");

            return;
        }

        FragmentBurst2D.Spawn(
            fragmentPrefab,
            transform.position,
            hitDirection,
            fragmentCount,
            fragmentSpread,
            minForce,
            maxForce,
            fragmentLifeTime);
    }
}
