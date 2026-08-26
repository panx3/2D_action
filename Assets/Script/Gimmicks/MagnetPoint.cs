using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 鉄球を吸着する磁力ポイント。
/// Traversal Assist をONにすると、鉄球が磁力範囲へ入っている間だけ
/// プレイヤーにも短い引っ張りを与え、磁力ダッシュ用の支点として使える。
/// 既存Prefabへの影響を避けるため、初期値はOFF。
/// </summary>
public class MagnetPoint : MonoBehaviour
{
    [Header("Detect Settings")]
    [SerializeField] private string targetTag = "morningstar";

    [Header("Magnet Settings")]
    [SerializeField] private float attractionForce = 25f;
    [SerializeField] private float maxAttractSpeed = 8f;
    [SerializeField] private float snapDistance = 0.4f;
    [SerializeField] private bool slowNearCenter = true;

    [Header("Traversal Assist (Optional)")]
    [SerializeField] private bool pullPlayerWhileBallAttached = false;
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private float playerPullForce = 55f;
    [SerializeField] private float playerMaxPullSpeed = 12f;
    [SerializeField] private float playerReleaseDistance = 0.8f;
    [SerializeField] private float playerPassDistance = 0.25f;

    [Header("Visual Settings")]
    [SerializeField] private Color idleColor = Color.blue;
    [SerializeField] private Color activeColor = Color.cyan;
    [SerializeField] private Color nearColor = Color.magenta;

    private readonly List<Rigidbody2D> detectedBodies = new List<Rigidbody2D>();
    private SpriteRenderer spriteRenderer;

    private Rigidbody2D playerRigidbody;
    private bool traversalActive;
    private bool traversalFinished;
    private float traversalDirectionX;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        CachePlayer();
        UpdateVisual();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!IsTarget(other))
            return;

        Rigidbody2D rb = other.attachedRigidbody;
        if (rb == null)
            return;

        if (!detectedBodies.Contains(rb))
        {
            detectedBodies.Add(rb);
            BeginTraversalAssist();
            Debug.Log("MagnetPoint Detect");
        }

        UpdateVisual();
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        Rigidbody2D rb = other.attachedRigidbody;
        if (rb == null)
            return;

        if (detectedBodies.Remove(rb))
        {
            Debug.Log("MagnetPoint Release");
        }

        if (detectedBodies.Count == 0)
        {
            traversalActive = false;
            traversalFinished = false;
        }

        UpdateVisual();
    }

    private void FixedUpdate()
    {
        for (int i = detectedBodies.Count - 1; i >= 0; i--)
        {
            Rigidbody2D rb = detectedBodies[i];

            if (rb == null)
            {
                detectedBodies.RemoveAt(i);
                continue;
            }

            Attract(rb);
        }

        ApplyTraversalAssist();
        UpdateVisual();
    }

    private void Attract(Rigidbody2D rb)
    {
        Vector2 magnetPosition = transform.position;
        Vector2 targetPosition = rb.position;
        Vector2 direction = magnetPosition - targetPosition;

        float distance = direction.magnitude;
        if (distance <= 0.01f)
            return;

        Vector2 forceDirection = direction.normalized;

        if (slowNearCenter && distance <= snapDistance)
        {
            rb.linearVelocity *= 0.85f;
            rb.AddForce(forceDirection * attractionForce * 0.3f, ForceMode2D.Force);
        }
        else
        {
            rb.AddForce(forceDirection * attractionForce * rb.mass, ForceMode2D.Force);
        }

        rb.linearVelocity = Vector2.ClampMagnitude(rb.linearVelocity, maxAttractSpeed);
    }

    private void BeginTraversalAssist()
    {
        if (!pullPlayerWhileBallAttached || detectedBodies.Count == 0)
            return;

        CachePlayer();
        if (playerRigidbody == null)
            return;

        float dx = transform.position.x - playerRigidbody.position.x;
        traversalDirectionX = Mathf.Abs(dx) > 0.05f ? Mathf.Sign(dx) : 1f;
        traversalActive = true;
        traversalFinished = false;
    }

    private void ApplyTraversalAssist()
    {
        if (!pullPlayerWhileBallAttached || !traversalActive || traversalFinished || detectedBodies.Count == 0)
            return;

        CachePlayer();
        if (playerRigidbody == null)
            return;

        // 磁力点の反対側へ抜けたら、引き戻さない。
        float passedDistance = (playerRigidbody.position.x - transform.position.x) * traversalDirectionX;
        if (passedDistance >= playerPassDistance)
        {
            traversalFinished = true;
            return;
        }

        Vector2 toMagnet = (Vector2)transform.position - playerRigidbody.position;
        float distance = toMagnet.magnitude;
        if (distance <= playerReleaseDistance || distance <= 0.01f)
            return;

        Vector2 direction = toMagnet / distance;
        playerRigidbody.AddForce(direction * playerPullForce, ForceMode2D.Force);

        float towardSpeed = Vector2.Dot(playerRigidbody.linearVelocity, direction);
        if (towardSpeed > playerMaxPullSpeed)
        {
            playerRigidbody.linearVelocity -= direction * (towardSpeed - playerMaxPullSpeed);
        }
    }

    private void CachePlayer()
    {
        if (playerRigidbody != null)
            return;

        try
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag(playerTag);
            if (playerObject != null)
                playerRigidbody = playerObject.GetComponent<Rigidbody2D>();
        }
        catch (UnityException)
        {
            playerRigidbody = null;
        }
    }

    private bool IsTarget(Collider2D other)
    {
        if (other.CompareTag(targetTag))
            return true;

        return other.attachedRigidbody != null
            && other.attachedRigidbody.CompareTag(targetTag);
    }

    private void UpdateVisual()
    {
        if (spriteRenderer == null)
            return;

        if (HasNearTarget())
        {
            spriteRenderer.color = nearColor;
        }
        else if (detectedBodies.Count > 0)
        {
            spriteRenderer.color = activeColor;
        }
        else
        {
            spriteRenderer.color = idleColor;
        }
    }

    private bool HasNearTarget()
    {
        foreach (Rigidbody2D rb in detectedBodies)
        {
            if (rb == null)
                continue;

            float distance = Vector2.Distance(transform.position, rb.position);
            if (distance <= snapDistance)
                return true;
        }

        return false;
    }
}
