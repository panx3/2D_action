using UnityEngine;

/// <summary>
/// PlatformEffector2D owns one-way collision for both Player and Morning Star.
/// Their layer-filtered surfaces keep a single shape visible to terrain queries.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider2D), typeof(PlatformEffector2D))]
public sealed class OneWayPlatform : MonoBehaviour
{
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
