using System.Collections;
using UnityEngine;

public class FallingPlatform : MonoBehaviour
{
    [Header("Detect Settings")]
    [SerializeField] private string playerTag = "Player";

    [Header("Fall Settings")]
    [SerializeField] private float fallDelay = 0.8f;
    [SerializeField] private float respawnDelay = 3f;
    [SerializeField] private float gravityScaleWhenFalling = 2.5f;
    [SerializeField] private bool respawnAfterFall = true;

    [Header("Visual Settings")]
    [SerializeField] private Color idleColor = Color.white;
    [SerializeField] private Color warningColor = Color.yellow;
    [SerializeField] private Color fallingColor = Color.gray;

    [Header("Warning Shake")]
    [SerializeField, Min(0f), Tooltip("落下予告中の揺れ幅。元のスプライトのピクセル単位です。")]
    private float warningShakePixels = 2f;
    [SerializeField, Min(0f)] private float warningTiltDegrees = 1.5f;
    [SerializeField, Min(0.1f)] private float warningShakeFrequency = 9f;
    [SerializeField, Tooltip("有効にすると、揺れに加えて既存の Warning Color も適用します。")]
    private bool useWarningColor = false;

    [Header("Debug")]
    [SerializeField] private bool showDebugLog = false;

    private Rigidbody2D rb;
    private Collider2D platformCollider;
    private SpriteRenderer spriteRenderer;
    private Transform warningVisual;
    private bool isWarning;
    private float warningStartTime;

    private Vector3 startPosition;
    private Quaternion startRotation;

    private bool isTriggered = false;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        platformCollider = GetComponent<Collider2D>();
        CreateWarningVisual();

        startPosition = transform.position;
        startRotation = transform.rotation;

        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }

        UpdateVisual(idleColor);
    }

    private void CreateWarningVisual()
    {
        SpriteRenderer source = GetComponent<SpriteRenderer>();
        if (source == null) return;

        // Keep the scene's sprite overrides, but never move the physics root to shake it.
        GameObject visual = new GameObject("WarningVisual");
        visual.layer = gameObject.layer;
        warningVisual = visual.transform;
        warningVisual.SetParent(transform, false);
        spriteRenderer = visual.AddComponent<SpriteRenderer>();
        spriteRenderer.sprite = source.sprite;
        spriteRenderer.sharedMaterials = source.sharedMaterials;
        spriteRenderer.sortingLayerID = source.sortingLayerID;
        spriteRenderer.sortingOrder = source.sortingOrder;
        spriteRenderer.flipX = source.flipX;
        spriteRenderer.flipY = source.flipY;
        spriteRenderer.drawMode = source.drawMode;
        spriteRenderer.size = source.size;
        spriteRenderer.tileMode = source.tileMode;
        spriteRenderer.adaptiveModeThreshold = source.adaptiveModeThreshold;
        spriteRenderer.maskInteraction = source.maskInteraction;
        spriteRenderer.spriteSortPoint = source.spriteSortPoint;
        spriteRenderer.enabled = source.enabled;
        source.enabled = false;
    }

    private void LateUpdate()
    {
        if (!isWarning || warningVisual == null) return;

        float elapsed = Time.time - warningStartTime;
        float progress = fallDelay > 0f ? Mathf.Clamp01(elapsed / fallDelay) : 1f;
        float strength = Mathf.Lerp(0.4f, 1f, progress);
        float phase = elapsed * warningShakeFrequency * Mathf.PI * 2f;
        float pixelSize = spriteRenderer.sprite != null ? 1f / spriteRenderer.sprite.pixelsPerUnit : 0.01f;

        float offsetX = Mathf.Round(Mathf.Sin(phase) * warningShakePixels * strength);
        float offsetY = Mathf.Round(Mathf.Sin(phase * 1.7f) * warningShakePixels * strength * 0.5f);
        warningVisual.localPosition = new Vector3(offsetX * pixelSize, offsetY * pixelSize, 0f);
        warningVisual.localRotation = Quaternion.Euler(0f, 0f,
            Mathf.Sin(phase * 0.75f) * warningTiltDegrees * strength);
    }

    private void OnDisable()
    {
        ResetWarningVisual();
    }

    private void ResetWarningVisual()
    {
        isWarning = false;
        if (warningVisual == null) return;

        warningVisual.localPosition = Vector3.zero;
        warningVisual.localRotation = Quaternion.identity;
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (isTriggered) return;
        if (!collision.gameObject.CompareTag(playerTag)) return;

        StartCoroutine(FallRoutine());
    }

    private IEnumerator FallRoutine()
    {
        isTriggered = true;

        if (showDebugLog)
        {
            Debug.Log("FallingPlatform: triggered.");
        }

        warningStartTime = Time.time;
        isWarning = true;
        UpdateVisual(useWarningColor ? warningColor : idleColor);

        yield return new WaitForSeconds(fallDelay);

        Fall();

        if (respawnAfterFall)
        {
            yield return new WaitForSeconds(respawnDelay);
            Respawn();
        }
    }

    private void Fall()
    {
        ResetWarningVisual();
        UpdateVisual(fallingColor);

        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.gravityScale = gravityScaleWhenFalling;
        }

        if (showDebugLog)
        {
            Debug.Log("FallingPlatform: fall.");
        }
    }

    private void Respawn()
    {
        ResetWarningVisual();

        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;
        }

        transform.position = startPosition;
        transform.rotation = startRotation;

        if (platformCollider != null)
        {
            platformCollider.enabled = true;
        }

        UpdateVisual(idleColor);

        isTriggered = false;

        if (showDebugLog)
        {
            Debug.Log("FallingPlatform: respawn.");
        }
    }

    private void UpdateVisual(Color color)
    {
        if (spriteRenderer == null) return;

        spriteRenderer.color = color;
    }
}
