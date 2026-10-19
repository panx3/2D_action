using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// CPU point-sampled cursor composition. The OS moves the whole arrow/chain/shadow
/// between game frames, so the attachment cannot lag behind a hardware arrow.
/// No camera rendering, ReadPixels, new input polling or per-frame allocations.
/// </summary>
internal sealed class GameplayNativeCursorRaster : IDisposable
{
    private sealed class Source
    {
        public Color32[] pixels;
        public int width, height;
        public Rect opaque;
    }

    private readonly Dictionary<Sprite, Source> sources = new Dictionary<Sprite, Source>();
    private Color32[] pixels;
    private Rect bounds;
    private int lastHash;
    private bool applied;
    public Texture2D Texture { get; private set; }
    public Vector2 Hotspot { get; private set; }
    public int UploadCount { get; private set; }

    public bool Render(RectTransform root, UnityEngine.UI.Image arrow, UnityEngine.UI.Image[] links,
        UnityEngine.UI.Image[] noise, bool withChain, bool withNoise, float scale, float chainLength,
        float swingLimit, Vector2 attachment, Color shadow, Vector2 shadowOffset)
    {
        Source arrowSource = GetSource(arrow.sprite);
        if (arrowSource == null) return false;
        Rect arrowRect = arrow.rectTransform.rect;
        Vector2 origin = arrow.rectTransform.anchoredPosition;
        Rect opaque = arrowSource.opaque;
        Vector2 min = origin + arrowRect.min + Vector2.Scale(opaque.min, arrowRect.size) / new Vector2(arrowSource.width, arrowSource.height);
        Vector2 max = origin + arrowRect.min + Vector2.Scale(opaque.max, arrowRect.size) / new Vector2(arrowSource.width, arrowSource.height);
        // Reserve all poses, both designs and the complete switch noise, so moving
        // the chain never resizes/repositions the native image or changes its hotspot.
        float halfWidth = chainLength * scale * 0.5f * 10f / 18f * 0.5f + scale;
        float side = chainLength * scale * Mathf.Sin(swingLimit * Mathf.Deg2Rad) + halfWidth;
        min = Vector2.Min(min, attachment + new Vector2(-side, -chainLength * scale - halfWidth));
        max = Vector2.Max(max, attachment + new Vector2(side, halfWidth));
        min = Vector2.Min(min, new Vector2(-2f, -21f) * scale);
        max = Vector2.Max(max, new Vector2(22f, 1f) * scale);
        min = Vector2.Min(min, min + shadowOffset);
        max = Vector2.Max(max, max + shadowOffset);
        min = new Vector2(Mathf.Floor(min.x) - 1f, Mathf.Floor(min.y) - 1f);
        max = new Vector2(Mathf.Ceil(max.x) + 1f, Mathf.Ceil(max.y) + 1f);
        int width = Mathf.NextPowerOfTwo(Mathf.CeilToInt(max.x - min.x));
        int height = Mathf.NextPowerOfTwo(Mathf.CeilToInt(max.y - min.y));
        // Conservative desktop hardware bounds; unusually large Inspector settings
        // keep the existing UI renderer instead of clipping or scaling the artwork.
        if (width > 256 || height > 256) return false;
        Rect nextBounds = new Rect(min.x, max.y - height, width, height);
        int hash = LayoutHash(arrow, 17);
        hash = Hash(hash, nextBounds.GetHashCode());
        hash = Hash(hash, shadow.GetHashCode()); hash = Hash(hash, shadowOffset.GetHashCode());
        hash = Hash(hash, withChain ? 1 : 0); hash = Hash(hash, withNoise ? 1 : 0);
        if (withChain)
            foreach (var link in links) hash = LayoutHash(link, hash);
        if (withNoise && noise != null)
            foreach (var image in noise) hash = LayoutHash(image, hash);
        if (applied && hash == lastHash) return true;

        if (Texture == null || Texture.width != width || Texture.height != height)
        {
            if (Texture != null) UnityEngine.Object.Destroy(Texture);
            Texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "Gameplay Cursor (Native Runtime)",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            pixels = new Color32[width * height];
        }
        bounds = nextBounds;
        Hotspot = new Vector2(-bounds.xMin, bounds.yMax);
        Array.Clear(pixels, 0, pixels.Length);
        Matrix4x4 rootInverse = root.worldToLocalMatrix;
        if (withChain)
            foreach (var link in links)
                if (!DrawImage(link, rootInverse, shadow, shadowOffset, true)) return false;
        if (!DrawImage(arrow, rootInverse, shadow, shadowOffset, true)) return false;
        if (withNoise && noise != null)
            foreach (var image in noise)
                DrawSolid(image, rootInverse);
        Texture.SetPixels32(pixels);
        Texture.Apply(false, false);
        Cursor.SetCursor(Texture, Hotspot, CursorMode.Auto);
        lastHash = hash;
        applied = true;
        UploadCount++;
        return true;
    }

    // Reapply after focus loss even if the cached shape itself has not changed.
    public void Invalidate() => applied = false;

    private Source GetSource(Sprite sprite)
    {
        if (sprite == null || !sprite.texture.isReadable) return null;
        if (sources.TryGetValue(sprite, out Source cached)) return cached;
        Rect rect = sprite.textureRect;
        int width = Mathf.RoundToInt(rect.width), height = Mathf.RoundToInt(rect.height);
        var texturePixels = sprite.texture.GetPixels32();
        var source = new Source { width = width, height = height, pixels = new Color32[width * height] };
        int left = width, right = 0, bottom = height, top = 0;
        for (int y = 0; y < height; y++)
        {
            Array.Copy(texturePixels, ((int)rect.y + y) * sprite.texture.width + (int)rect.x, source.pixels, y * width, width);
            for (int x = 0; x < width; x++)
                if (source.pixels[y * width + x].a != 0)
                { left = Mathf.Min(left, x); right = Mathf.Max(right, x + 1); bottom = Mathf.Min(bottom, y); top = Mathf.Max(top, y + 1); }
        }
        source.opaque = Rect.MinMaxRect(left, bottom, right, top);
        sources.Add(sprite, source);
        return source;
    }

    private bool DrawImage(UnityEngine.UI.Image image, Matrix4x4 rootInverse, Color shadow, Vector2 offset, bool withShadow)
    {
        Source source = GetSource(image.sprite);
        if (source == null) return false;
        Matrix4x4 local = rootInverse * image.rectTransform.localToWorldMatrix;
        if (withShadow) Raster(source, image.rectTransform.rect, Matrix4x4.Translate(offset) * local, shadow * new Color(1f, 1f, 1f, image.color.a), true);
        Raster(source, image.rectTransform.rect, local, image.color, false);
        return true;
    }

    private void DrawSolid(UnityEngine.UI.Image image, Matrix4x4 rootInverse)
    {
        if (image.color.a <= 0f) return;
        Raster(null, image.rectTransform.rect, rootInverse * image.rectTransform.localToWorldMatrix, image.color, false);
    }

    private void Raster(Source source, Rect rect, Matrix4x4 matrix, Color tint, bool silhouette)
    {
        // Skip transparent source margins rather than scanning a full cursor square.
        Rect ink = rect;
        if (source != null)
        {
            Vector2 pixelSize = new Vector2(rect.width / source.width, rect.height / source.height);
            ink = new Rect(rect.min + Vector2.Scale(source.opaque.min, pixelSize), Vector2.Scale(source.opaque.size, pixelSize));
        }
        Vector2 a = matrix.MultiplyPoint3x4(ink.min);
        Vector2 b = matrix.MultiplyPoint3x4(ink.max);
        Vector2 c = matrix.MultiplyPoint3x4(new Vector2(ink.xMin, ink.yMax));
        Vector2 d = matrix.MultiplyPoint3x4(new Vector2(ink.xMax, ink.yMin));
        Vector2 min = Vector2.Min(Vector2.Min(a, b), Vector2.Min(c, d));
        Vector2 max = Vector2.Max(Vector2.Max(a, b), Vector2.Max(c, d));
        int x0 = Mathf.Clamp(Mathf.FloorToInt(min.x - bounds.xMin), 0, Texture.width);
        int x1 = Mathf.Clamp(Mathf.CeilToInt(max.x - bounds.xMin), 0, Texture.width);
        int y0 = Mathf.Clamp(Mathf.FloorToInt(min.y - bounds.yMin), 0, Texture.height);
        int y1 = Mathf.Clamp(Mathf.CeilToInt(max.y - bounds.yMin), 0, Texture.height);
        Matrix4x4 inverse = matrix.inverse;
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                Vector2 point = inverse.MultiplyPoint3x4(new Vector3(bounds.xMin + x + 0.5f, bounds.yMin + y + 0.5f));
                float u = (point.x - rect.xMin) / rect.width, v = (point.y - rect.yMin) / rect.height;
                if (u < 0f || v < 0f || u >= 1f || v >= 1f) continue;
                Color sample = source == null ? Color.white : source.pixels[(int)(v * source.height) * source.width + (int)(u * source.width)];
                if (sample.a <= 0f) continue;
                // Shadows use silhouette alpha, not the original sprite's RGB.
                if (silhouette) sample = new Color(1f, 1f, 1f, sample.a);
                Color over = sample * tint;
                int index = y * Texture.width + x;
                Color under = pixels[index];
                float alpha = over.a + under.a * (1f - over.a);
                if (alpha <= 0f) continue;
                pixels[index] = new Color((over.r * over.a + under.r * under.a * (1f - over.a)) / alpha,
                    (over.g * over.a + under.g * under.a * (1f - over.a)) / alpha,
                    (over.b * over.a + under.b * under.a * (1f - over.a)) / alpha, alpha);
            }
    }

    private static int LayoutHash(UnityEngine.UI.Image image, int hash)
    {
        RectTransform rect = image.rectTransform;
        hash = Hash(hash, image.sprite != null ? image.sprite.GetHashCode() : 0);
        hash = Hash(hash, Mathf.RoundToInt(rect.anchoredPosition.x * 4f));
        hash = Hash(hash, Mathf.RoundToInt(rect.anchoredPosition.y * 4f));
        hash = Hash(hash, rect.rect.GetHashCode());
        hash = Hash(hash, Mathf.RoundToInt(Mathf.DeltaAngle(0f, rect.localEulerAngles.z) * 4f));
        hash = Hash(hash, ((Color32)image.color).GetHashCode());
        return hash;
    }

    private static int Hash(int hash, int value) => unchecked(hash * 31 + value);

    public void Dispose()
    {
        if (Texture != null) UnityEngine.Object.Destroy(Texture);
        Texture = null;
        pixels = null;
        sources.Clear();
    }
}
