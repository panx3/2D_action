using UnityEngine;
using UnityEngine.UI;

/// <summary>Draws the generated HP-chain artwork with a small, continuous bend.</summary>
[DisallowMultipleComponent]
public sealed class HpBarChainGraphic : MaskableGraphic
{
    private Sprite artwork;
    private Vector2 displaySize;
    private float sway;

    public Sprite Artwork => artwork;
    public Vector2 DisplaySize => displaySize;
    public override Texture mainTexture => artwork != null ? artwork.texture : base.mainTexture;

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
    }

    public void SetArtwork(Sprite sprite)
    {
        if (artwork == sprite) return;
        artwork = sprite;
        SetAllDirty();
    }

    public void SetPose(Vector2 size, float angle)
    {
        size.x = Mathf.Max(1f, size.x);
        size.y = Mathf.Max(1f, size.y);
        if (displaySize == size && Mathf.Approximately(sway, angle))
            return;
        displaySize = size;
        sway = angle;
        // Reserve bounds for the bent tip without changing the artwork's proportions.
        rectTransform.sizeDelta = new Vector2(size.x + size.y * 0.55f, size.y + 1f);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (artwork == null || displaySize.y <= 0f) return;
        const int sections = 6;
        Vector4 uv = UnityEngine.Sprites.DataUtility.GetOuterUV(artwork);
        float bend = Mathf.Sin(sway * Mathf.Deg2Rad);
        for (int i = 0; i <= sections; i++)
        {
            float t = i / (float)sections;
            Vector2 center = new Vector2(bend * displaySize.y * t * t, -displaySize.y * t);
            Vector2 right = new Vector2(1f, 2f * bend * t).normalized * (displaySize.x * 0.5f);
            float v = Mathf.Lerp(uv.w, uv.y, t);
            vh.AddVert(center - right, color, new Vector2(uv.x, v));
            vh.AddVert(center + right, color, new Vector2(uv.z, v));
            if (i == sections) continue;
            int first = i * 2;
            vh.AddTriangle(first, first + 1, first + 2);
            vh.AddTriangle(first + 1, first + 3, first + 2);
        }
    }
}
