using UnityEngine;

/// <summary>奥側にある鉄球専用の支持面。鉄球の物理・Stateには書き込まない。</summary>
[DisallowMultipleComponent]
public sealed class PressBallStandController : MonoBehaviour
{
    [SerializeField] private BoxCollider2D supportSurface;
    [SerializeField] private SpriteRenderer indicator;
    [SerializeField] private MorningStarLauncher launcher;
    [SerializeField, Min(0.05f)] private float placementHalfWidth = 0.6f;
    [SerializeField, Min(0.01f)] private float surfaceTolerance = 0.12f;
    [SerializeField, Min(0.1f)] private float maximumVerticalSpeed = 1.2f;
    private Collider2D ballCollider;
    private FallingPressController owner;
    public bool IsOccupied { get; private set; }
    public float FloorY => transform.position.y;
    public BoxCollider2D SupportSurface => supportSurface;

    private void Awake()
    {
        if (launcher == null) launcher = FindAnyObjectByType<MorningStarLauncher>();
        if (launcher != null && launcher.BallBody != null)
            foreach (var c in launcher.BallBody.GetComponentsInChildren<Collider2D>())
                if (!c.isTrigger) { ballCollider = c; break; }
    }

    public bool TryClaim(FallingPressController press)
    {
        if (owner != null && owner != press) return false;
        owner = press;
        return true;
    }

    public void Release(FallingPressController press) { if (owner == press) owner = null; }

    public bool TryGetSupport(out Bounds ballBounds)
    {
        ballBounds = default;
        if (!isActiveAndEnabled || supportSurface == null || !supportSurface.enabled ||
            launcher == null || !launcher.isActiveAndEnabled || ballCollider == null ||
            !ballCollider.enabled || !ballCollider.gameObject.activeInHierarchy || !launcher.BallBody.simulated)
            return false;
        var state = launcher.State;
        if (state != MorningStarLauncher.MorningStarState.Dragging &&
            state != MorningStarLauncher.MorningStarState.Dropping &&
            state != MorningStarLauncher.MorningStarState.Thrown) return false;
        ballBounds = ballCollider.bounds;
        Bounds surface = supportSurface.bounds;
        return Mathf.Abs(ballBounds.center.x - surface.center.x) <= placementHalfWidth &&
            Mathf.Abs(ballBounds.min.y - surface.max.y) <= surfaceTolerance &&
            Mathf.Abs(launcher.BallBody.linearVelocity.y) <= maximumVerticalSpeed;
    }

    private void Update()
    {
        IsOccupied = TryGetSupport(out _);
        if (indicator != null) indicator.color = IsOccupied ? new Color(0.5f, 1f, 1f) : Color.white;
    }

    private void OnDisable() { IsOccupied = false; if (indicator != null) indicator.color = Color.white; }
    private void OnDrawGizmosSelected()
    {
        if (supportSurface == null) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(supportSurface.bounds.center + Vector3.up * 0.85f,
            new Vector3(placementHalfWidth * 2f, 1.7f, 0f));
    }
}
