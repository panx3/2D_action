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

    [Header("Debug")]
    [SerializeField] private bool showDebugLog = false;

    private Rigidbody2D rb;
    private Collider2D platformCollider;
    private SpriteRenderer spriteRenderer;

    private Vector3 startPosition;
    private Quaternion startRotation;

    private bool isTriggered = false;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        platformCollider = GetComponent<Collider2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();

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

        UpdateVisual(warningColor);

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