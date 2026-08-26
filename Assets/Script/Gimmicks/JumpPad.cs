using System.Collections;
using UnityEngine;

public class JumpPad : MonoBehaviour
{
    [Header("Detect Settings")]
    [SerializeField] private string playerTag = "Player";

    [Header("Jump Settings")]
    [SerializeField] private float jumpVelocity = 12f;
    [SerializeField] private bool preserveHorizontalVelocity = true;
    [SerializeField] private float bounceCooldown = 0.15f;

    [Header("Visual Settings")]
    [SerializeField] private Color idleColor = Color.cyan;
    [SerializeField] private Color activeColor = Color.white;
    [SerializeField] private float flashDuration = 0.08f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLog = true;

    private SpriteRenderer spriteRenderer;
    private float nextBounceTime;
    private Coroutine flashCoroutine;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();

        if (spriteRenderer != null)
        {
            spriteRenderer.color = idleColor;
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        TryBounce(collision);
    }

    private void TryBounce(Collision2D collision)
    {
        if (Time.time < nextBounceTime)
        {
            return;
        }

        Rigidbody2D playerRb = collision.rigidbody;

        if (playerRb == null)
        {
            return;
        }

        // Playerの子Colliderが接触しても、本体のRigidbody2Dタグで判定
        if (!playerRb.CompareTag(playerTag))
        {
            return;
        }

        if (!IsLandingFromAbove(collision, playerRb))
        {
            return;
        }

        Vector2 currentVelocity = playerRb.linearVelocity;
        float horizontalVelocity = preserveHorizontalVelocity ? currentVelocity.x : 0f;

        // 着地した瞬間だけ、指定した上向き速度に固定する
        playerRb.linearVelocity = new Vector2(horizontalVelocity, jumpVelocity);

        nextBounceTime = Time.time + bounceCooldown;

        if (showDebugLog)
        {
            Debug.Log("JumpPad: Player bounced.");
        }

        if (flashCoroutine != null)
        {
            StopCoroutine(flashCoroutine);
        }

        flashCoroutine = StartCoroutine(FlashRoutine());
    }

    private bool IsLandingFromAbove(Collision2D collision, Rigidbody2D playerRb)
    {
        bool playerIsAbove = playerRb.position.y > transform.position.y;
        bool isFallingOrLanding = playerRb.linearVelocity.y <= 0.1f;

        if (!playerIsAbove || !isFallingOrLanding)
        {
            return false;
        }

        foreach (ContactPoint2D contact in collision.contacts)
        {
            if (contact.point.y >= transform.position.y)
            {
                return true;
            }
        }

        return false;
    }

    private IEnumerator FlashRoutine()
    {
        if (spriteRenderer == null)
        {
            yield break;
        }

        spriteRenderer.color = activeColor;

        yield return new WaitForSeconds(flashDuration);

        spriteRenderer.color = idleColor;
        flashCoroutine = null;
    }
}