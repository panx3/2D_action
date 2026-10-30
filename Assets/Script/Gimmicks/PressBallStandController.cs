using UnityEngine;

/// <summary>鉄球との実接触を表示する台座。鉄球の物理・Stateには書き込まない。</summary>
[DisallowMultipleComponent]
public sealed class PressBallStandController : MonoBehaviour
{
    [SerializeField] private BoxCollider2D supportSurface;
    [SerializeField] private SpriteRenderer indicator;
    [SerializeField] private MorningStarLauncher launcher;
    [SerializeField, Min(0.01f)] private float surfaceTolerance = 0.12f;
    [SerializeField] private LineRenderer placementOutline;
    [SerializeField] private GameObject readyMark;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip placedSound;
    [SerializeField] private Color emptyColor = new Color(0.6f, 0.3f, 0.08f, 0.7f);
    [SerializeField] private Color occupiedColor = new Color(0.2f, 1f, 0.7f, 1f);
    private readonly ContactPoint2D[] contacts = new ContactPoint2D[16];
    private Collider2D ballCollider;
    public bool IsOccupied { get; private set; }
    public float FloorY => transform.position.y;
    public BoxCollider2D SupportSurface => supportSurface;
    public bool IsReadyMarkVisible => readyMark != null && readyMark.activeSelf;

    private void Awake()
    {
        if (launcher == null) launcher = FindAnyObjectByType<MorningStarLauncher>();
        if (launcher != null && launcher.BallBody != null)
            foreach (var c in launcher.BallBody.GetComponentsInChildren<Collider2D>())
                if (!c.isTrigger) { ballCollider = c; break; }
    }

    public bool TryGetSupport(out Bounds ballBounds)
    {
        ballBounds = default;
        if (!isActiveAndEnabled || supportSurface == null || !supportSurface.enabled ||
            launcher == null || !launcher.isActiveAndEnabled || ballCollider == null ||
            !ballCollider.enabled || !ballCollider.gameObject.activeInHierarchy || !launcher.BallBody.simulated)
            return false;
        ballBounds = ballCollider.bounds;
        Bounds surface = supportSurface.bounds;
        if (ballBounds.center.y <= surface.max.y || !supportSurface.IsTouching(ballCollider)) return false;
        int count = supportSurface.GetContacts(contacts);
        for (int i = 0; i < count; i++)
        {
            var contact = contacts[i];
            if ((contact.collider == ballCollider || contact.otherCollider == ballCollider) &&
                Mathf.Abs(contact.normal.y) >= 0.5f && contact.point.y >= surface.max.y - surfaceTolerance)
                return true;
        }
        return false;
    }

    private void Update()
    {
        bool occupied = TryGetSupport(out _);
        if (occupied && !IsOccupied && audioSource != null && placedSound != null)
            audioSource.PlayOneShot(placedSound, 0.3f);
        IsOccupied = occupied;
        ApplyFeedback();
    }

    private void ApplyFeedback()
    {
        Color color = IsOccupied ? occupiedColor : emptyColor;
        if (indicator != null) indicator.color = color;
        if (placementOutline != null) placementOutline.startColor = placementOutline.endColor = color;
        if (readyMark != null) readyMark.SetActive(IsOccupied);
    }
    private void OnDisable() { IsOccupied = false; ApplyFeedback(); }
    private void OnDrawGizmosSelected()
    {
        if (supportSurface == null) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(supportSurface.bounds.center, supportSurface.bounds.size);
    }
}
