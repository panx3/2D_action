using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider2D))]
public sealed class IndustrialLadder : MonoBehaviour
{
    public enum PlatformSide { Right, Left }

    [SerializeField, Min(2), Tooltip("段数。下の床面を原点、上の床面を段数×1 Unity単位に合わせます。")]
    private int rungCount = 8;
    [SerializeField, Tooltip("上端で乗り移る足場の向き。はしごの中心を足場の縁から横に約1 Unity単位離して配置します。")]
    private PlatformSide platformSide = PlatformSide.Right;
    [SerializeField] private SpriteRenderer railAndRungs;
    [SerializeField] private SpriteRenderer topFittings;

    public const float RungSpacing = 1f;
    private const float SideReach = 1.85f;
    private const float OppositeReach = 0.9f;
    private const float HandrailHeight = 3f;
    private BoxCollider2D climbTrigger;

    public float CenterX => transform.position.x;
    public float BottomY => transform.position.y;
    public float TopY => BottomY + Mathf.Max(2, rungCount) * RungSpacing;
    public float ExitDirection => platformSide == PlatformSide.Left ? -1f : 1f;
    public float ExitX => CenterX + ExitDirection * SideReach;

    private void Awake() => RefreshGeometry();

    internal bool Contains(Collider2D body) => isActiveAndEnabled && climbTrigger != null
        && climbTrigger.enabled && climbTrigger.bounds.Intersects(body.bounds);

    public void RefreshGeometry()
    {
        float height = Mathf.Max(2, rungCount) * RungSpacing;
        climbTrigger = GetComponent<BoxCollider2D>();
        climbTrigger.isTrigger = true;
        climbTrigger.size = new Vector2(SideReach + OppositeReach, height + HandrailHeight);
        climbTrigger.offset = new Vector2((SideReach - OppositeReach) * 0.5f * ExitDirection,
            climbTrigger.size.y * 0.5f);

        if (railAndRungs != null && railAndRungs.sprite != null)
        {
            railAndRungs.size = new Vector2(railAndRungs.sprite.bounds.size.x, height + HandrailHeight);
            railAndRungs.transform.localPosition = Vector2.up * railAndRungs.size.y * 0.5f;
        }
        if (topFittings != null)
            topFittings.transform.localPosition = Vector2.up * (height + HandrailHeight);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.attachedRigidbody != null && other.attachedRigidbody.TryGetComponent(out Player player))
            player.NotifyLadderOverlap(this, other, true);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.attachedRigidbody != null && other.attachedRigidbody.TryGetComponent(out Player player))
            player.NotifyLadderOverlap(this, other, false);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.9f, 0.72f, 0.26f);
        Gizmos.DrawLine(new Vector3(CenterX, TopY, transform.position.z), new Vector3(ExitX, TopY, transform.position.z));
    }
}
