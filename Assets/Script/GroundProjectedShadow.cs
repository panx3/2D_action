using UnityEngine;

/// <summary>タピオカと共通の影を足元の床に投影する。物理とキャラクターの姿勢は変更しない。</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(1100)]
public sealed class GroundProjectedShadow : MonoBehaviour
{
    [SerializeField] private Collider2D body;
    [SerializeField] private SpriteRenderer groundShadow;
    [SerializeField, Min(0.1f)] private float width = 1.5f;
    [SerializeField, Tooltip("配置した敵のRootスケールに合わせて影の幅を拡縮する。既存の固定幅の影はOFF。")]
    private bool scaleWithOwner;
    [SerializeField, Range(0f, 1f)] private float opacity = 0.52f;
    [SerializeField, Min(0.1f)] private float probeDistance = 8f;
    [SerializeField] private LayerMask groundLayers = (1 << 0) | (1 << 6) | (1 << 10);

    private readonly RaycastHit2D[] hits = new RaycastHit2D[12];
    private ContactFilter2D filter;

    private void Awake()
    {
        if (body == null) body = GetComponent<Collider2D>();
        filter = new ContactFilter2D { useTriggers = false };
        filter.SetLayerMask(groundLayers);
    }

    private void LateUpdate()
    {
        if (groundShadow == null) return;
        groundShadow.enabled = false;
        if (body == null || !body.enabled || !body.gameObject.activeInHierarchy || groundShadow.sprite == null)
            return;

        Bounds bounds = body.bounds;
        // 接地時にも床の内側からRayを開始しない。天井や垂直壁の側面は投影先にしない。
        Vector2 origin = new Vector2(bounds.center.x, bounds.min.y + 0.12f);
        int count = Physics2D.Raycast(origin, Vector2.down, filter, hits, probeDistance + 0.12f);
        float closest = float.PositiveInfinity;
        RaycastHit2D floor = default;
        for (int i = 0; i < count; i++)
        {
            RaycastHit2D hit = hits[i];
            if (hit.collider == body || hit.distance <= 0f || hit.normal.y < 0.45f || hit.distance >= closest)
                continue;
            if (hit.collider.attachedRigidbody != null && hit.collider.attachedRigidbody == body.attachedRigidbody)
                continue;
            closest = hit.distance;
            floor = hit;
        }
        if (floor.collider == null) return;

        float height = Mathf.Max(0f, bounds.min.y - floor.point.y);
        float distance01 = Mathf.Clamp01(height / probeDistance);
        float fade = 1f - Mathf.SmoothStep(0f, 1f, distance01);
        if (fade <= 0.01f) return;

        Transform shadow = groundShadow.transform;
        shadow.SetPositionAndRotation(new Vector3(floor.point.x + floor.normal.x * 0.025f,
            floor.point.y + floor.normal.y * 0.025f, transform.position.z),
            Quaternion.FromToRotation(Vector3.up, floor.normal));
        // 回転する鉄球の子でも影は床に固定。拡縮は親のスケールから独立させる。
        shadow.localScale = Vector3.one;
        Vector3 inherited = shadow.lossyScale;
        float worldWidth = width * (scaleWithOwner ? Mathf.Abs(transform.lossyScale.x) : 1f);
        float scale = worldWidth * Mathf.Lerp(1f, 0.65f, distance01) / groundShadow.sprite.bounds.size.x;
        shadow.localScale = new Vector3(scale / Mathf.Max(0.0001f, Mathf.Abs(inherited.x)),
            scale * 0.72f / Mathf.Max(0.0001f, Mathf.Abs(inherited.y)), 1f);
        Renderer floorRenderer = floor.collider.GetComponent<Renderer>();
        groundShadow.sortingLayerID = floorRenderer != null ? floorRenderer.sortingLayerID : 0;
        groundShadow.sortingOrder = floorRenderer != null ? floorRenderer.sortingOrder + 1 : 1;
        groundShadow.color = new Color(0.055f, 0.04f, 0.09f, opacity * fade);
        groundShadow.enabled = true;
    }

    private void OnDisable()
    {
        if (groundShadow != null) groundShadow.enabled = false;
    }
}
