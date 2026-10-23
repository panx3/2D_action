using UnityEngine;

/// <summary>
/// PlatformEffector2D owns one-way collision. A separate, layer-filtered solid
/// collider handles the Morning Star without changing its controller or layers.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider2D), typeof(PlatformEffector2D))]
public sealed class OneWayPlatform : MonoBehaviour
{
    [SerializeField, Tooltip("Effector surface on Ignore Raycast. MorningStarSolid on Default supplies the terrain-query shape.")]
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

    public bool SupportsPlayer(Collider2D candidate, Collider2D playerCollider)
    {
        if (surfaceCollider == null || candidate == null || !candidate.transform.IsChildOf(transform)
            || playerCollider == null
            || !surfaceCollider.enabled || !surfaceCollider.IsTouching(playerCollider))
            return false;

        // Overlap queries also see effector-disabled contacts and the ball-only
        // solid collider. Neither may provide grounding from below or from a side.
        Rigidbody2D playerBody = playerCollider.attachedRigidbody;
        return playerCollider.bounds.min.y >= surfaceCollider.bounds.max.y - 0.04f
            && (playerBody == null || playerBody.linearVelocity.y <= 0.05f);
    }
}
