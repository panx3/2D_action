using System.Collections.Generic;
using UnityEngine;

/// <summary>取得専用。光・影・透明余白を含めず、表示輪郭の外接範囲同士で接触を調べる。</summary>
public static class PickupContactUtility
{
    private static readonly Dictionary<Sprite, Bounds> VisibleBounds = new Dictionary<Sprite, Bounds>();
    private static readonly List<Vector2> Shape = new List<Vector2>();
    private const float ContactMargin = 0.02f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCache() => VisibleBounds.Clear();

    public static bool TryGetTouchingBody(SpriteRenderer item, ref Player player, out Collider2D body)
    {
        body = null;
        if (player == null) player = Object.FindAnyObjectByType<Player>();
        if (player == null || !player.gameObject.activeInHierarchy) return false;
        body = player.GetComponent<Collider2D>();
        return body != null && body.enabled && !body.isTrigger
            && body.attachedRigidbody != null && body.attachedRigidbody.simulated && Touches(item, player);
    }

    public static bool Touches(SpriteRenderer item, Player player)
    {
        if (item == null || !item.enabled || item.sprite == null || player == null) return false;
        SpriteRenderer body = player.BodySprite;
        if (body == null || body.sprite == null) return false;
        Bounds a = GetVisibleWorldBounds(item), b = GetVisibleWorldBounds(body);
        return a.min.x <= b.max.x + ContactMargin && a.max.x >= b.min.x - ContactMargin
            && a.min.y <= b.max.y + ContactMargin && a.max.y >= b.min.y - ContactMargin;
    }

    public static Bounds GetVisibleWorldBounds(SpriteRenderer renderer)
    {
        Sprite sprite = renderer.sprite;
        if (!VisibleBounds.TryGetValue(sprite, out Bounds local))
        {
            bool found = false;
            local = sprite.bounds;
            for (int i = 0; i < sprite.GetPhysicsShapeCount(); i++)
            {
                sprite.GetPhysicsShape(i, Shape);
                foreach (Vector2 point in Shape)
                {
                    if (!found) { local = new Bounds(point, Vector3.zero); found = true; }
                    else local.Encapsulate(point);
                }
            }
            VisibleBounds.Add(sprite, local);
        }
        Vector3 center = local.center;
        if (renderer.flipX) center.x = -center.x;
        if (renderer.flipY) center.y = -center.y;
        Vector3 x = renderer.transform.TransformVector(Vector3.right * local.extents.x);
        Vector3 y = renderer.transform.TransformVector(Vector3.up * local.extents.y);
        return new Bounds(renderer.transform.TransformPoint(center), new Vector3(
            Mathf.Abs(x.x) + Mathf.Abs(y.x), Mathf.Abs(x.y) + Mathf.Abs(y.y), 0f) * 2f);
    }
}
