using System.Collections.Generic;
using UnityEngine;

public class MagnetPoint : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private MorningStarLauncher morningStarLauncher;

    [Header("Detect Settings")]
    [SerializeField] private string targetTag = "morningstar";

    [Header("Magnet Settings")]
    [SerializeField] private float attractionForce = 25f;
    [SerializeField] private float maxAttractSpeed = 8f;
    [SerializeField] private float snapDistance = 0.4f;
    [SerializeField] private bool slowNearCenter = true;

    [Header("Visual Settings")]
    [SerializeField] private Color idleColor = Color.blue;
    [SerializeField] private Color activeColor = Color.cyan;
    [SerializeField] private Color nearColor = Color.magenta;

    private readonly List<Rigidbody2D> detectedBodies = new List<Rigidbody2D>();

    // この磁石に近づいた時、脱出用射出を渡した鉄球を記録する
    private readonly HashSet<Rigidbody2D> escapeThrowGrantedBodies =
        new HashSet<Rigidbody2D>();

    private SpriteRenderer spriteRenderer;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();

        // Inspector未設定でも、Playerから自動取得を試す
        if (morningStarLauncher == null)
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");

            if (playerObject != null)
            {
                morningStarLauncher =
                    playerObject.GetComponent<MorningStarLauncher>();
            }
        }

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
            escapeThrowGrantedBodies.Remove(rb);
            Debug.Log("MagnetPoint Release");
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
                escapeThrowGrantedBodies.Remove(rb);
                continue;
            }

            Attract(rb);
        }

        UpdateVisual();
    }

    private void Attract(Rigidbody2D rb)
    {
        Vector2 magnetPosition = transform.position;
        Vector2 targetPosition = rb.position;
        Vector2 direction = magnetPosition - targetPosition;

        float distance = direction.magnitude;

        if (distance <= 0.01f)
        {
            TryGrantMagnetEscapeThrow(rb);
            return;
        }

        Vector2 forceDirection = direction.normalized;

        if (slowNearCenter && distance <= snapDistance)
        {
            rb.linearVelocity *= 0.85f;
            rb.AddForce(
                forceDirection * attractionForce * 0.3f,
                ForceMode2D.Force
            );

            // 鉄球が磁石につかまった時だけ、
            // 脱出用の空中射出を1回回復する
            TryGrantMagnetEscapeThrow(rb);
        }
        else
        {
            rb.AddForce(
                forceDirection * attractionForce * rb.mass,
                ForceMode2D.Force
            );
        }

        rb.linearVelocity = Vector2.ClampMagnitude(
            rb.linearVelocity,
            maxAttractSpeed
        );
    }

    private void TryGrantMagnetEscapeThrow(Rigidbody2D rb)
    {
        if (morningStarLauncher == null)
            return;

        // 同じ磁石につかまっている間は1回だけ
        if (!escapeThrowGrantedBodies.Add(rb))
            return;

        morningStarLauncher.GrantMagnetEscapeThrow();
        Debug.Log("MagnetPoint: Escape throw granted");
    }

    private bool IsTarget(Collider2D other)
    {
        if (other.CompareTag(targetTag))
            return true;

        if (other.attachedRigidbody != null &&
            other.attachedRigidbody.CompareTag(targetTag))
        {
            return true;
        }

        return false;
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

            float distance = Vector2.Distance(
                transform.position,
                rb.position
            );

            if (distance <= snapDistance)
                return true;
        }

        return false;
    }
}