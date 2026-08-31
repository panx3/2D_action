using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 鉄球だけを吸引し、到達後は MorningStarLauncher の既存Hook支点へ固定する。
/// Playerは磁力を直接受けず、鎖の張力・重力・慣性・空中入力で振り子運動する。
/// </summary>
public class MagnetPoint : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private MorningStarLauncher morningStarLauncher;

    [Header("Detect Settings")]
    [SerializeField] private string targetTag = "morningstar";

    [Header("Magnet Settings")]
    [SerializeField] private float attractionForce = 40f;
    [SerializeField] private float maxAttractSpeed = 8f;
    [SerializeField] private float snapDistance = 0.8f;
    [SerializeField] private bool slowNearCenter = true;

    [Header("Visual Settings")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Transform visualRoot;
    [SerializeField] private Color idleColor = new Color(0.78f, 0.82f, 0.9f, 1f);
    [SerializeField] private Color activeColor = new Color(0.9f, 1f, 1f, 1f);
    [SerializeField] private Color nearColor = Color.white;
    [SerializeField, Range(0f, 0.1f)] private float activePulseAmount = 0.04f;
    [SerializeField, Min(0f)] private float activePulseSpeed = 4f;

    private readonly List<Rigidbody2D> detectedBodies = new List<Rigidbody2D>();

    // Attach解除直後、Triggerを抜けるまでは再吸着させない。
    private readonly HashSet<Rigidbody2D> attachedBodies = new HashSet<Rigidbody2D>();
    private readonly HashSet<Rigidbody2D> suppressedUntilExitBodies = new HashSet<Rigidbody2D>();

    // この磁石に近づいた時、脱出用射出を渡した鉄球を記録する
    private readonly HashSet<Rigidbody2D> escapeThrowGrantedBodies =
        new HashSet<Rigidbody2D>();

    private Vector3 visualBaseScale = Vector3.one;

    private void Awake()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (visualRoot == null && spriteRenderer != null)
            visualRoot = spriteRenderer.transform;
        if (visualRoot != null)
            visualBaseScale = visualRoot.localScale;

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

    private void Update()
    {
        if (visualRoot == null)
            return;

        float pulse = 0f;
        if (detectedBodies.Count > 0 && activePulseAmount > 0f)
            pulse = (Mathf.Sin(Time.time * activePulseSpeed) * 0.5f + 0.5f) * activePulseAmount;

        visualRoot.localScale = visualBaseScale * (1f + pulse);
    }

    private void OnDisable()
    {
        if (visualRoot != null)
            visualRoot.localScale = visualBaseScale;
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
            attachedBodies.Remove(rb);
            suppressedUntilExitBodies.Remove(rb);
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
                attachedBodies.Remove(rb);
                suppressedUntilExitBodies.Remove(rb);
                escapeThrowGrantedBodies.Remove(rb);
                continue;
            }

            if (attachedBodies.Contains(rb))
            {
                if (morningStarLauncher != null
                    && morningStarLauncher.IsAttachedToMagnet(this, rb))
                {
                    continue;
                }

                attachedBodies.Remove(rb);
                suppressedUntilExitBodies.Add(rb);
            }

            if (suppressedUntilExitBodies.Contains(rb))
                continue;

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
            TryAttach(rb);
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
            TryAttach(rb);
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

    private void TryAttach(Rigidbody2D rb)
    {
        if (morningStarLauncher == null || attachedBodies.Contains(rb))
            return;

        if (!morningStarLauncher.TryAttachToMagnet(this, rb, transform.position))
            return;

        attachedBodies.Add(rb);
        rb.position = transform.position;
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
        Debug.Log("MagnetPoint Attach");
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
