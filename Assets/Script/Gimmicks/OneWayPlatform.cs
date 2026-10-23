using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// PlatformEffector2D owns one-way collision for both Player and Morning Star.
/// Their layer-filtered surfaces keep a single shape visible to terrain queries.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider2D), typeof(PlatformEffector2D))]
public sealed class OneWayPlatform : MonoBehaviour
{
    private static readonly List<OneWayPlatform> ropeSurfaces = new List<OneWayPlatform>();
    public static IReadOnlyList<OneWayPlatform> RopeSurfaces => ropeSurfaces;

    [SerializeField, Tooltip("Player effector surface on Ignore Raycast. MorningStarSolid uses its own one-way effector on Default for ball contacts and terrain queries.")]
    private BoxCollider2D surfaceCollider;

    private void Awake()
    {
        if (surfaceCollider == null)
            surfaceCollider = GetComponent<BoxCollider2D>();
    }

    private void Reset()
    {
        surfaceCollider = GetComponent<BoxCollider2D>();
    }

    private void OnEnable()
    {
        if (!ropeSurfaces.Contains(this))
            ropeSurfaces.Add(this);
    }

    private void OnDisable()
    {
        ropeSurfaces.Remove(this);
    }

    public bool TryGetRopeSurface(out Vector2 left, out Vector2 right, out Vector2 up)
    {
        left = right = up = Vector2.zero;
        if (!isActiveAndEnabled || surfaceCollider == null || !surfaceCollider.enabled)
            return false;
        Vector2 half = surfaceCollider.size * 0.5f;
        left = surfaceCollider.transform.TransformPoint(surfaceCollider.offset + new Vector2(-half.x, half.y));
        right = surfaceCollider.transform.TransformPoint(surfaceCollider.offset + half);
        up = surfaceCollider.transform.up;
        return (right - left).sqrMagnitude > 0.000001f;
    }

    public bool BlocksBodyCorrection(Rigidbody2D body, Vector2 direction, Vector2 normal)
    {
        if (!TryGetRopeSurface(out Vector2 left, out _, out Vector2 up)
            || Vector2.Dot(direction, up) >= -0.0001f || Vector2.Dot(normal, up) < 0.1f)
            return false;
        Collider2D bodyCollider = body.GetComponent<Collider2D>();
        if (bodyCollider == null)
            return false;
        Bounds bounds = bodyCollider.bounds;
        float extent = Mathf.Abs(up.x) * bounds.extents.x + Mathf.Abs(up.y) * bounds.extents.y;
        return Vector2.Dot((Vector2)bounds.center - left, up) - extent >= -0.04f;
    }

    public bool SupportsPlayer(Collider2D candidate, Collider2D playerCollider)
    {
        if (surfaceCollider == null || candidate == null || !candidate.transform.IsChildOf(transform)
            || playerCollider == null
            || !surfaceCollider.enabled || !surfaceCollider.IsTouching(playerCollider))
            return false;

        // Overlap queries also see effector-disabled contacts and the ball-only
        // surface. Neither may provide grounding from below or from a side.
        Rigidbody2D playerBody = playerCollider.attachedRigidbody;
        return playerCollider.bounds.min.y >= surfaceCollider.bounds.max.y - 0.04f
            && (playerBody == null || playerBody.linearVelocity.y <= 0.05f);
    }
}
