using UnityEngine;
using UnityEngine.UI;

/// <summary>Draws the generated HP-chain artwork with a small, continuous bend.</summary>
[DisallowMultipleComponent]
public sealed class HpBarChainGraphic : MaskableGraphic
{
    private Sprite artwork;
    private Vector2 displaySize;
    private float sway;
    private bool draped;
    private Vector2 drapedEnd;
    private float drapedSag;
    private float strandWidth;
    private readonly Vector2[] drapePoints = new Vector2[65];
    private readonly float[] drapeDistances = new float[65];

    public Sprite Artwork => artwork;
    public Vector2 DisplaySize => displaySize;
    public bool IsDraped => draped;
    public Vector2 DrapedEnd => drapedEnd;
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
        if (!draped && displaySize == size && Mathf.Approximately(sway, angle))
            return;
        draped = false;
        displaySize = size;
        sway = angle;
        // Reserve bounds for the bent tip without changing the artwork's proportions.
        rectTransform.sizeDelta = new Vector2(size.x + size.y * 0.55f, size.y + 1f);
        SetVerticesDirty();
    }

    public void SetDrapedPose(Vector2 end, float sag, float width, float angle)
    {
        sag = Mathf.Max(1f, sag);
        width = Mathf.Max(1f, width);
        if (draped && drapedEnd == end && Mathf.Approximately(drapedSag, sag)
            && Mathf.Approximately(strandWidth, width) && Mathf.Approximately(sway, angle)) return;
        draped = true;
        drapedEnd = end;
        drapedSag = sag;
        strandWidth = width;
        sway = angle;
        displaySize = new Vector2(Mathf.Abs(end.x), sag + Mathf.Abs(end.y));
        rectTransform.sizeDelta = displaySize + Vector2.one * width * 2f;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (artwork == null || displaySize.y <= 0f) return;
        if (draped)
        {
            PopulateDrapedMesh(vh);
            return;
        }
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

    private void PopulateDrapedMesh(VertexHelper vh)
    {
        float sidewaysSway = Mathf.Sin(sway * Mathf.Deg2Rad) * drapedSag * 0.25f;
        drapeDistances[0] = 0f;
        for (int i = 0; i < drapePoints.Length; i++)
        {
            float t = i / (float)(drapePoints.Length - 1);
            float arc = 4f * t * (1f - t);
            drapePoints[i] = drapedEnd * t + new Vector2(sidewaysSway, -drapedSag) * arc;
            if (i > 0)
                drapeDistances[i] = drapeDistances[i - 1] + Vector2.Distance(drapePoints[i - 1], drapePoints[i]);
        }

        float length = drapeDistances[drapeDistances.Length - 1];
        if (length <= 0.001f) return;
        float tileLength = strandWidth * artwork.rect.height / artwork.rect.width;
        int tiles = Mathf.Clamp(Mathf.RoundToInt(length / tileLength), 2, 12);
        Vector4 uv = UnityEngine.Sprites.DataUtility.GetOuterUV(artwork);
        const int sections = 6;
        for (int tile = 0; tile < tiles; tile++)
        {
            // Slight overlap interlocks the open top of the next pair with the previous
            // pair's bottom oval. Arc-length spacing keeps link thickness consistent.
            float overlap = length / tiles * 0.025f;
            float start = Mathf.Max(0f, length * tile / tiles - overlap);
            float end = Mathf.Min(length, length * (tile + 1) / tiles + overlap);
            int first = vh.currentVertCount;
            for (int row = 0; row <= sections; row++)
            {
                float t = row / (float)sections;
                SampleDrape(Mathf.Lerp(start, end, t), out Vector2 center, out Vector2 tangent);
                Vector2 right = new Vector2(-tangent.y, tangent.x) * (strandWidth * 0.5f);
                float v = Mathf.Lerp(uv.w, uv.y, t);
                vh.AddVert(center - right, color, new Vector2(uv.x, v));
                vh.AddVert(center + right, color, new Vector2(uv.z, v));
                if (row == sections) continue;
                int index = first + row * 2;
                vh.AddTriangle(index, index + 1, index + 2);
                vh.AddTriangle(index + 1, index + 3, index + 2);
            }
        }
    }

    private void SampleDrape(float distance, out Vector2 center, out Vector2 tangent)
    {
        int last = drapePoints.Length - 1;
        int i = 0;
        while (i < last - 1 && drapeDistances[i + 1] < distance) i++;
        float span = drapeDistances[i + 1] - drapeDistances[i];
        float t = span > 0.0001f ? Mathf.Clamp01((distance - drapeDistances[i]) / span) : 0f;
        center = Vector2.Lerp(drapePoints[i], drapePoints[i + 1], t);
        tangent = (drapePoints[i + 1] - drapePoints[i]).normalized;
    }
}
