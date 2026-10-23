using System.Collections.Generic;
using UnityEngine;

/// <summary>ワールド上の小さな分割ゲージ。HPを所有せず、渡された値だけを表示する。</summary>
[DisallowMultipleComponent]
public sealed class EnemyMiniHealthGauge : MonoBehaviour
{
    [SerializeField] private Sprite frameSprite, litSprite, emptySprite;
    [SerializeField] private Vector2 cellSize = new Vector2(0.5f, 0.2f);
    [SerializeField] private Vector2 fillSize = new Vector2(0.4f, 0.12f);
    [SerializeField, Min(0f)] private float spacing = 0.05f;
    [SerializeField, Min(0.1f)] private float maxWidth = 2.8f;
    private readonly List<SpriteRenderer> fills = new List<SpriteRenderer>();
    private int capacity;
    private Vector2 effectiveFillSize;

    public void SetHealth(int current, int maximum, int sortingLayer, int order)
    {
        maximum = Mathf.Max(1, maximum);
        if (capacity != maximum) Build(maximum, sortingLayer, order);
        for (int i = 0; i < fills.Count; i++)
        {
            fills[i].sprite = i < current ? litSprite : emptySprite;
            if (fills[i].sprite != null) fills[i].transform.localScale = new Vector3(
                effectiveFillSize.x / fills[i].sprite.bounds.size.x, effectiveFillSize.y / fills[i].sprite.bounds.size.y, 1f);
        }
    }

    private void Build(int maximum, int sortingLayer, int order)
    {
        foreach (Transform child in transform) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
        fills.Clear(); capacity = maximum;
        float width = maximum * cellSize.x + (maximum - 1) * spacing;
        float factor = Mathf.Min(1f, maxWidth / Mathf.Max(0.01f, width));
        effectiveFillSize = fillSize * factor;
        for (int i = 0; i < maximum; i++)
        {
            float x = (i * (cellSize.x + spacing) - width * 0.5f + cellSize.x * 0.5f) * factor;
            // 枠の透明な開口部から点灯／消灯タイルを見せる。
            fills.Add(Part("HP" + (i + 1), litSprite, fillSize * factor, x, sortingLayer, order));
            Part("Frame" + (i + 1), frameSprite, cellSize * factor, x, sortingLayer, order + 1);
        }
    }

    private SpriteRenderer Part(string name, Sprite sprite, Vector2 size, float x, int layer, int order)
    {
        var item = new GameObject(name).AddComponent<SpriteRenderer>();
        item.transform.SetParent(transform, false); item.transform.localPosition = new Vector3(x, 0f, 0f);
        item.sprite = sprite; item.sortingLayerID = layer; item.sortingOrder = order;
        if (sprite != null) item.transform.localScale = new Vector3(size.x / sprite.bounds.size.x, size.y / sprite.bounds.size.y, 1f);
        return item;
    }
}
