using UnityEngine;
using UnityEngine.Serialization;

/// <summary>HP比率に応じて右端が減る、ワールド上の一本バー。</summary>
[DisallowMultipleComponent]
public sealed class EnemyMiniHealthGauge : MonoBehaviour
{
    [SerializeField] private Sprite frameSprite, litSprite, emptySprite;
    [FormerlySerializedAs("cellSize")]
    [SerializeField] private Vector2 barSize = new Vector2(1.6f, 0.2f);
    [SerializeField] private Vector2 fillSize = new Vector2(1.48f, 0.12f);
    private SpriteRenderer frame, background, fill;

    public void SetHealth(int current, int maximum, int sortingLayer, int order)
    {
        if (fill == null)
        {
            background = Part("Background"); fill = Part("Fill"); frame = Part("Frame");
        }
        float ratio = Mathf.Clamp01(current / (float)Mathf.Max(1, maximum));
        Vector2 interior = new Vector2(Mathf.Max(0.01f, fillSize.x), Mathf.Max(0.01f, fillSize.y));
        Configure(background, emptySprite, interior, 0f, sortingLayer, order);
        // 中央PivotのSpriteでも左端を固定して、残量に応じて右端だけを減らす。
        Configure(fill, litSprite, new Vector2(interior.x * ratio, interior.y),
            -interior.x * (1f - ratio) * 0.5f, sortingLayer, order + 1);
        fill.enabled = ratio > 0f && litSprite != null;
        Configure(frame, frameSprite, barSize, 0f, sortingLayer, order + 2);
    }

    private SpriteRenderer Part(string name)
    {
        var item = new GameObject(name).AddComponent<SpriteRenderer>();
        item.transform.SetParent(transform, false);
        return item;
    }
    private static void Configure(SpriteRenderer item, Sprite sprite, Vector2 size, float x, int layer, int order)
    {
        item.transform.localPosition = new Vector3(x, 0f, 0f);
        item.sprite = sprite; item.sortingLayerID = layer; item.sortingOrder = order;
        if (sprite != null) item.transform.localScale = new Vector3(
            Mathf.Max(0f, size.x) / sprite.bounds.size.x, Mathf.Max(0.01f, size.y) / sprite.bounds.size.y, 1f);
    }
}
