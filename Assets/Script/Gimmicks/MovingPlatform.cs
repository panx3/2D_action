using System.Collections.Generic;
using UnityEngine;

public class MovingPlatform : MonoBehaviour
{
    [Header("Move Settings")]
    [SerializeField] private Vector2 moveOffset = new Vector2(4f, 0f);
    [SerializeField] private float moveSpeed = 2f;
    [SerializeField] private float waitTimeAtEnds = 0.3f;

    [Header("Carry Settings")]
    [SerializeField] private bool carryPlayer = true;
    [SerializeField] private string playerTag = "Player";

    [Header("Debug")]
    [SerializeField] private bool showDebugLog = false;

    private Rigidbody2D rb;
    private Collider2D platformCollider;

    private Vector2 startPosition;
    private Vector2 endPosition;
    private Vector2 currentTarget;

    private float waitTimer = 0f;

    private readonly HashSet<Rigidbody2D> riders = new HashSet<Rigidbody2D>();

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        platformCollider = GetComponent<Collider2D>();

        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
        }

        startPosition = rb != null ? rb.position : (Vector2)transform.position;
        endPosition = startPosition + moveOffset;
        currentTarget = endPosition;
    }

    private void FixedUpdate()
    {
        if (rb == null) return;

        if (waitTimer > 0f)
        {
            waitTimer -= Time.fixedDeltaTime;
            return;
        }

        Vector2 beforePosition = rb.position;
        Vector2 nextPosition = Vector2.MoveTowards(
            beforePosition,
            currentTarget,
            moveSpeed * Time.fixedDeltaTime
        );

        Vector2 delta = nextPosition - beforePosition;

        rb.MovePosition(nextPosition);

        CarryRiders(delta);

        if (Vector2.Distance(nextPosition, currentTarget) <= 0.01f)
        {
            SwitchTarget();
        }
    }

    private void SwitchTarget()
    {
        currentTarget = currentTarget == endPosition ? startPosition : endPosition;
        waitTimer = waitTimeAtEnds;

        if (showDebugLog)
        {
            Debug.Log("MovingPlatform: switch target.");
        }
    }

    private void CarryRiders(Vector2 delta)
    {
        if (!carryPlayer) return;
        if (delta.sqrMagnitude <= 0.000001f) return;

        foreach (Rigidbody2D rider in riders)
        {
            if (rider == null) continue;

            rider.position += delta;
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        TryAddRider(collision);
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        TryAddRider(collision);
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        Rigidbody2D otherRb = collision.rigidbody;

        if (otherRb != null)
        {
            riders.Remove(otherRb);
        }
    }

    private void TryAddRider(Collision2D collision)
    {
        if (!carryPlayer) return;
        if (!collision.gameObject.CompareTag(playerTag)) return;

        Rigidbody2D otherRb = collision.rigidbody;
        if (otherRb == null) return;

        if (!IsObjectOnTop(collision.collider)) return;

        riders.Add(otherRb);
    }

    private bool IsObjectOnTop(Collider2D otherCollider)
    {
        if (platformCollider == null || otherCollider == null) return false;

        Bounds platformBounds = platformCollider.bounds;
        Bounds otherBounds = otherCollider.bounds;

        return otherBounds.min.y >= platformBounds.max.y - 0.15f;
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 start = Application.isPlaying ? (Vector3)startPosition : transform.position;
        Vector3 end = start + (Vector3)moveOffset;

        Gizmos.DrawLine(start, end);
        Gizmos.DrawWireSphere(start, 0.15f);
        Gizmos.DrawWireSphere(end, 0.15f);
    }
}