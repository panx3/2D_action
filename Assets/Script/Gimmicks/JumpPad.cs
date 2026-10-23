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

    [Header("Spring Animation")]
    [SerializeField] private SpriteRenderer visualRenderer;
    [SerializeField] private Vector2 compressedScale = new Vector2(1.08f, 0.58f);
    [SerializeField] private Vector2 stretchedScale = new Vector2(0.94f, 1.24f);
    [SerializeField, Min(0.01f)] private float compressionDuration = 0.04f;
    [SerializeField, Min(0.01f)] private float extensionDuration = 0.08f;
    [SerializeField, Min(0.01f)] private float settleDuration = 0.22f;

    [Header("Bounce Audio")]
    [SerializeField] private AudioClip bounceClip;
    [SerializeField, Range(0f, 1f)] private float bounceVolume = 0.8f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLog = true;

    private SpriteRenderer spriteRenderer;
    private float nextBounceTime;
    private AudioSource audioSource;
    private Coroutine feedbackCoroutine;
    private Vector3 visualRestPosition;
    private Vector3 visualRestScale;
    private float visualBottom;
    private float feedbackStartTime;
    private Vector2 currentVisualScale = Vector2.one;

    private void Awake()
    {
        spriteRenderer = visualRenderer;

        // Keep the existing root SpriteRenderer (and scene sprite overrides) as the source.
        // Only its visual copy moves; the Rigidbody2D and Collider2D stay unchanged.
        if (spriteRenderer == null || spriteRenderer.transform == transform)
        {
            SpriteRenderer source = spriteRenderer != null ? spriteRenderer : GetComponent<SpriteRenderer>();
            if (source != null)
            {
                GameObject visual = new GameObject("SpringVisual");
                visual.layer = gameObject.layer;
                visual.transform.SetParent(transform, false);
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
                spriteRenderer.maskInteraction = source.maskInteraction;
                spriteRenderer.spriteSortPoint = source.spriteSortPoint;
                source.enabled = false;
            }
        }

        audioSource = OneShotAudioUtility.FindWorldImpactSource();

        if (spriteRenderer != null)
        {
            spriteRenderer.color = idleColor;
            visualRestPosition = spriteRenderer.transform.localPosition;
            visualRestScale = spriteRenderer.transform.localScale;
            visualBottom = spriteRenderer.localBounds.min.y;
        }
    }

    private void OnDisable()
    {
        if (feedbackCoroutine != null)
        {
            StopCoroutine(feedbackCoroutine);
            feedbackCoroutine = null;
        }

        ResetVisual();
        nextBounceTime = 0f;
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

        // 跳躍中の鉄球・鎖の扱いは物理の所有者へ任せる。
        if (playerRb.TryGetComponent<MorningStarLauncher>(out var launcher))
        {
            launcher.BeginTrampolineJump();
        }

        nextBounceTime = Time.time + bounceCooldown;

        if (showDebugLog)
        {
            Debug.Log("JumpPad: Player bounced.");
        }

        OneShotAudioUtility.Play2D(audioSource, bounceClip, bounceVolume, transform.position);

        if (feedbackCoroutine != null)
        {
            StopCoroutine(feedbackCoroutine);
        }

        if (spriteRenderer != null)
        {
            feedbackCoroutine = StartCoroutine(BounceFeedbackRoutine());
        }
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

    private IEnumerator BounceFeedbackRoutine()
    {
        feedbackStartTime = Time.time;
        spriteRenderer.color = activeColor;
        yield return AnimateVisual(currentVisualScale, compressedScale, compressionDuration);
        yield return AnimateVisual(compressedScale, stretchedScale, extensionDuration);
        Vector2 settleScale = new Vector2(1.025f, 0.94f);
        yield return AnimateVisual(stretchedScale, settleScale, settleDuration * 0.65f);
        yield return AnimateVisual(settleScale, Vector2.one, settleDuration * 0.35f);
        ResetVisual();
        feedbackCoroutine = null;
    }

    private IEnumerator AnimateVisual(Vector2 from, Vector2 to, float duration)
    {
        float elapsed = 0f;
        duration = Mathf.Max(0.01f, duration);
        while (elapsed < duration)
        {
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            ApplyVisualScale(Vector2.LerpUnclamped(from, to, t));
            spriteRenderer.color = Time.time - feedbackStartTime < flashDuration ? activeColor : idleColor;
            yield return null;
            elapsed += Time.deltaTime;
        }

        ApplyVisualScale(to);
    }

    private void ApplyVisualScale(Vector2 scale)
    {
        currentVisualScale = scale;
        spriteRenderer.transform.localScale = Vector3.Scale(visualRestScale, new Vector3(scale.x, scale.y, 1f));
        // Anchor the bottom so compression looks planted instead of floating.
        spriteRenderer.transform.localPosition = visualRestPosition +
            spriteRenderer.transform.localRotation * Vector3.up * (visualBottom * visualRestScale.y * (1f - scale.y));
    }

    private void ResetVisual()
    {
        currentVisualScale = Vector2.one;
        if (spriteRenderer != null)
        {
            spriteRenderer.transform.localPosition = visualRestPosition;
            spriteRenderer.transform.localScale = visualRestScale;
            spriteRenderer.color = idleColor;
        }
    }
}
