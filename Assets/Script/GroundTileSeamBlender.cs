using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>Appearance only: cached, pixel-sized colour corrections for internal Tilemap seams.</summary>
[ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(Tilemap), typeof(TilemapRenderer))]
public sealed class GroundTileSeamBlender : MonoBehaviour
{
    [Tooltip("OFF returns to original pixels. Does not affect collision or tile placement.")]
    public bool enableBlending = true;
    [Range(0, 1), Tooltip("Strength of the local boundary colour shift; original pattern contrast is retained.")]
    public float blendStrength = 0.4f;
    [Range(1, 3), Tooltip("Width on EACH side of a seam, measured in original sprite pixels.")]
    public int blendWidth = 2;
    [Range(0, 1), Tooltip("Higher values use coarser, discrete colour offsets to retain pixel-art texture.")]
    public float pixelPreservation = 0.8f;
    [Range(0, 1), Tooltip("Deterministic 4x4 ordered dithering of the colour offset; never temporal noise.")]
    public float ditherStrength = 0.25f;

    public int CorrectedPixelCount { get; private set; }
    public int SupportedTileCount { get; private set; }
    public int SkippedTileCount { get; private set; }
    public int BuildCount { get; private set; }
    public double LastBuildMilliseconds { get; private set; }
    public long CacheBytes { get; private set; }

    private sealed class Cell
    {
        public int width, height;
        public Color32[] pixels;
    }

    private readonly Dictionary<Vector3Int, Cell> cells = new Dictionary<Vector3Int, Cell>();
    private readonly Dictionary<Texture2D, Texture2D> readable = new Dictionary<Texture2D, Texture2D>();
    private readonly Dictionary<Sprite, Color32[]> spritePixels = new Dictionary<Sprite, Color32[]>();
    private readonly Dictionary<(Sprite, bool, bool), Cell> cellVariants = new Dictionary<(Sprite, bool, bool), Cell>();
    private readonly Dictionary<(Cell, Cell, int, int), Vector3[]> edgeOffsets = new Dictionary<(Cell, Cell, int, int), Vector3[]>();
    private Tilemap map;
    private TilemapRenderer target;
    private MaterialPropertyBlock block;
    private Texture2D lookup, atlas;
    private BoundsInt bounds;
    private bool dirty = true;
    private Vector4 lastSettings;
    private bool lastEnabled;
    private Matrix4x4 lastWorldToLocal;
    private double rebuildAfter;

    private void OnEnable()
    {
        map = GetComponent<Tilemap>();
        target = GetComponent<TilemapRenderer>();
        block = new MaterialPropertyBlock();
        Tilemap.tilemapTileChanged += TilesChanged;
        Tilemap.tilemapPositionsChanged += PositionsChanged;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.update += EditorTick;
        UnityEditor.Undo.undoRedoPerformed += RequestRebuild;
#endif
        RequestRebuild();
    }

    private void OnValidate()
    {
        blendStrength = Mathf.Clamp01(blendStrength);
        blendWidth = Mathf.Clamp(blendWidth, 1, 3);
        pixelPreservation = Mathf.Clamp01(pixelPreservation);
        ditherStrength = Mathf.Clamp01(ditherStrength);
        dirty = true;
    }

    private void TilesChanged(Tilemap changed, Tilemap.SyncTile[] _) { if (changed == map) RequestRebuild(); }
    private void PositionsChanged(Tilemap changed, NativeArray<Vector3Int> _) { if (changed == map) RequestRebuild(); }

    public void RequestRebuild()
    {
        dirty = true;
        rebuildAfter = Time.realtimeSinceStartupAsDouble + 0.08;
    }

    private void LateUpdate() { if (Application.isPlaying) Tick(); }
#if UNITY_EDITOR
    private void EditorTick() { if (!Application.isPlaying && this != null && isActiveAndEnabled) Tick(); }
#endif

    private void Tick()
    {
        Vector4 settings = new Vector4(blendStrength, blendWidth, pixelPreservation, ditherStrength);
        if (settings != lastSettings) dirty = true;
        if (enableBlending && dirty && Time.realtimeSinceStartupAsDouble >= rebuildAfter) RebuildNow();
        if (lastEnabled != enableBlending || lastWorldToLocal != transform.worldToLocalMatrix)
            ApplyProperties();
    }

    [ContextMenu("Rebuild Seam Cache")]
    public void RebuildNow()
    {
        if (map == null || target == null) return;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        dirty = false;
        ReleaseTextures();
        CorrectedPixelCount = SupportedTileCount = SkippedTileCount = 0;
        CacheBytes = 0;
        lastSettings = new Vector4(blendStrength, blendWidth, pixelPreservation, ditherStrength);
        cells.Clear();
        bounds = map.cellBounds;
        try
        {
            var grid = map.layoutGrid;
            if (grid == null || grid.cellLayout != GridLayout.CellLayout.Rectangle ||
                grid.cellSwizzle != GridLayout.CellSwizzle.XYZ || map.orientation != Tilemap.Orientation.XY ||
                grid.cellGap != Vector3.zero || bounds.size.z != 1 || bounds.size.x <= 0 || bounds.size.y <= 0 ||
                (long)bounds.size.x * bounds.size.y > 1000000 ||
                bounds.size.x > SystemInfo.maxTextureSize || bounds.size.y > SystemInfo.maxTextureSize)
                return;

            int slot = 0;
            foreach (Vector3Int p in bounds.allPositionsWithin)
            {
                if (!map.HasTile(p)) continue;
                Cell cell = ReadCell(p);
                if (cell == null) { SkippedTileCount++; continue; }
                cells.Add(p, cell);
                slot = Mathf.Max(slot, cell.width, cell.height);
            }
            SupportedTileCount = cells.Count;
            if (cells.Count == 0) return;
            int columns = Mathf.CeilToInt(Mathf.Sqrt(cells.Count));
            int rows = Mathf.CeilToInt((float)cells.Count / columns);
            int aw = columns * slot, ah = rows * slot;
            if (aw > SystemInfo.maxTextureSize || ah > SystemInfo.maxTextureSize || (long)aw * ah > 8000000)
                throw new InvalidOperationException("Seam atlas exceeds the safe texture budget. Split this Tilemap.");
            atlas = NewTexture(aw, ah, TextureFormat.RGBA32, false, "Ground seam colours");
            lookup = NewTexture(bounds.size.x, bounds.size.y, TextureFormat.RGBAFloat, true, "Ground seam cells");
            var colours = new Color32[aw * ah];
            var indices = new Color[bounds.size.x * bounds.size.y];
            int index = 0;
            // Position order and Bayer coordinates are deterministic; never depend on frame/camera/time.
            foreach (Vector3Int p in bounds.allPositionsWithin)
            {
                if (!cells.TryGetValue(p, out Cell cell)) continue;
                int ox = index % columns * slot, oy = index / columns * slot;
                index++;
                indices[p.x - bounds.xMin + (p.y - bounds.yMin) * bounds.size.x] =
                    new Color((float)ox / aw, (float)oy / ah, (float)cell.width / aw, (float)cell.height / ah);
                for (int y = 0; y < cell.height; y++)
                for (int x = 0; x < cell.width; x++)
                {
                    if (x >= blendWidth && y >= blendWidth && x < cell.width - blendWidth && y < cell.height - blendWidth)
                        continue;
                    if (!InteriorPixel(p, cell, x, y)) continue;
                    Color original = cell.pixels[x + y * cell.width];
                    Vector3 delta = Vector3.zero;
                    float weight = 0, maxWeight = 0;
                    Accumulate(p, cell, x, y, -1, 0, ref delta, ref weight, ref maxWeight);
                    Accumulate(p, cell, x, y, 1, 0, ref delta, ref weight, ref maxWeight);
                    Accumulate(p, cell, x, y, 0, -1, ref delta, ref weight, ref maxWeight);
                    Accumulate(p, cell, x, y, 0, 1, ref delta, ref weight, ref maxWeight);
                    if (weight <= 0) continue;
                    delta *= blendStrength * maxWeight / weight;
                    // Quantise only the small colour offset, preserving original texture contrast.
                    float threshold = Bayer(p.x * cell.width + x, p.y * cell.height + y);
                    float step = Mathf.Lerp(1f / 255, 1f / 32, pixelPreservation);
                    for (int c = 0; c < 3; c++)
                    {
                        float offset = Mathf.Clamp(delta[c], -0.15f, 0.15f);
                        float q = Mathf.Floor(offset / step + Mathf.Lerp(0.5f, threshold, ditherStrength)) * step;
                        original[c] = Mathf.Clamp01(original[c] + q);
                    }
                    Color32 corrected = original;
                    Color32 source = cell.pixels[x + y * cell.width];
                    if (corrected.r == source.r && corrected.g == source.g && corrected.b == source.b) continue;
                    corrected.a = 255; // Selection mask only. The shader always retains the source alpha.
                    colours[ox + x + (oy + y) * aw] = corrected;
                    CorrectedPixelCount++;
                }
            }
            atlas.SetPixels32(colours); atlas.Apply(false, true);
            lookup.SetPixels(indices); lookup.Apply(false, true);
            CacheBytes = (long)aw * ah * 4 + (long)indices.Length * 16;
        }
        catch (Exception e)
        {
            ReleaseTextures();
            Debug.LogWarning("[GroundTileSeamBlender] Original rendering retained: " + e.Message, this);
        }
        finally
        {
            foreach (Texture2D texture in readable.Values) DestroyOwned(texture);
            readable.Clear(); spritePixels.Clear(); cellVariants.Clear(); edgeOffsets.Clear(); cells.Clear();
            BuildCount++;
            LastBuildMilliseconds = watch.Elapsed.TotalMilliseconds;
            ApplyProperties();
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.SceneView.RepaintAll();
#endif
        }
    }

    private Cell ReadCell(Vector3Int p)
    {
        // Animated/custom tiles and non-filling/transformed geometry fail closed to the original.
        if (!(map.GetTile(p) is Tile)) return null;
        Sprite sprite = map.GetSprite(p);
        if (sprite == null || sprite.packed || map.GetColor(p) != Color.white) return null;
        Vector3 cellSize = map.layoutGrid.cellSize;
        Matrix4x4 matrix = map.GetTransformMatrix(p);
        Vector2 size = sprite.rect.size / sprite.pixelsPerUnit;
        Vector3 a = matrix.MultiplyPoint3x4(new Vector3(-sprite.pivot.x / sprite.pixelsPerUnit, -sprite.pivot.y / sprite.pixelsPerUnit));
        Vector3 b = matrix.MultiplyPoint3x4(new Vector3(size.x - sprite.pivot.x / sprite.pixelsPerUnit, size.y - sprite.pivot.y / sprite.pixelsPerUnit));
        Vector3 anchor = Vector3.Scale(map.tileAnchor, cellSize);
        // Support axis-aligned scale/flip only. Slopes, offsets and rotation remain unchanged.
        if (Mathf.Abs(matrix.m01) + Mathf.Abs(matrix.m10) + Mathf.Abs(matrix.m02) + Mathf.Abs(matrix.m12) > 0.0001f ||
            Mathf.Abs(Mathf.Min(a.x, b.x) + anchor.x) > 0.0001f ||
            Mathf.Abs(Mathf.Min(a.y, b.y) + anchor.y) > 0.0001f ||
            Mathf.Abs(Mathf.Abs(b.x - a.x) - cellSize.x) > 0.0001f ||
            Mathf.Abs(Mathf.Abs(b.y - a.y) - cellSize.y) > 0.0001f) return null;
        int w = Mathf.RoundToInt(sprite.rect.width), h = Mathf.RoundToInt(sprite.rect.height);
        if (w < 3 || h < 3) return null;
        var variant = (sprite, matrix.m00 < 0, matrix.m11 < 0);
        if (cellVariants.TryGetValue(variant, out Cell cached)) return cached;
        if (!spritePixels.TryGetValue(sprite, out Color32[] source))
        {
            if (!readable.TryGetValue(sprite.texture, out Texture2D texture))
            {
                texture = ReadTexture(sprite.texture);
                readable.Add(sprite.texture, texture);
            }
            var all = texture.GetPixels32();
            source = new Color32[w * h];
            for (int y = 0; y < h; y++)
                Array.Copy(all, (Mathf.RoundToInt(sprite.rect.y) + y) * texture.width + Mathf.RoundToInt(sprite.rect.x), source, y * w, w);
            spritePixels.Add(sprite, source);
        }
        var pixels = new Color32[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            pixels[x + y * w] = source[(matrix.m00 > 0 ? x : w - 1 - x) + (matrix.m11 > 0 ? y : h - 1 - y) * w];
        var result = new Cell { width = w, height = h, pixels = pixels };
        cellVariants.Add(variant, result);
        return result;
    }

    private Color Sample(Vector3Int p, float u, float v)
    {
        int dx = Mathf.FloorToInt(u), dy = Mathf.FloorToInt(v);
        if (!cells.TryGetValue(p + new Vector3Int(dx, dy, 0), out Cell c)) return Color.clear;
        int x = Mathf.Clamp(Mathf.FloorToInt((u - dx) * c.width), 0, c.width - 1);
        int y = Mathf.Clamp(Mathf.FloorToInt((v - dy) * c.height), 0, c.height - 1);
        return c.pixels[x + y * c.width];
    }

    private bool InteriorPixel(Vector3Int p, Cell c, int x, int y)
    {
        // Protect transparent silhouettes, exposed edges, and incomplete 2x2 corners.
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int sx = x + dx, sy = y + dy;
            if (sx >= 0 && sy >= 0 && sx < c.width && sy < c.height)
            {
                if (c.pixels[sx + sy * c.width].a < 250) return false;
            }
            else if (Sample(p, (sx + 0.5f) / c.width, (sy + 0.5f) / c.height).a < 0.98f) return false;
        }
        return true;
    }

    private void Accumulate(Vector3Int p, Cell c, int x, int y, int dx, int dy,
        ref Vector3 delta, ref float weight, ref float maxWeight)
    {
        int distance = dx < 0 ? x : dx > 0 ? c.width - 1 - x : dy < 0 ? y : c.height - 1 - y;
        if (distance >= blendWidth || !cells.TryGetValue(p + new Vector3Int(dx, dy, 0), out Cell neighbor)) return;
        var key = (c, neighbor, dx, dy);
        if (!edgeOffsets.TryGetValue(key, out Vector3[] offsets))
        {
            // Each sprite-pair edge is measured once, shared by repeated tiles and band pixels.
            offsets = new Vector3[dx == 0 ? c.width : c.height];
            for (int along = 0; along < offsets.Length; along++)
            {
                Vector3 difference = Vector3.zero;
                int samples = 0;
                for (int tangent = -2; tangent <= 2; tangent++)
                for (int depth = 0; depth < 2; depth++)
                {
                    int sx = dx == 0 ? Mathf.Clamp(along + tangent, 0, c.width - 1) : dx < 0 ? depth : c.width - 1 - depth;
                    int sy = dy == 0 ? Mathf.Clamp(along + tangent, 0, c.height - 1) : dy < 0 ? depth : c.height - 1 - depth;
                    int nx = dx == 0 ? Mathf.Min(neighbor.width - 1, (int)((sx + 0.5f) * neighbor.width / c.width)) : dx < 0 ? neighbor.width - 1 - depth : depth;
                    int ny = dy == 0 ? Mathf.Min(neighbor.height - 1, (int)((sy + 0.5f) * neighbor.height / c.height)) : dy < 0 ? neighbor.height - 1 - depth : depth;
                    Color32 a = c.pixels[sx + sy * c.width], b = neighbor.pixels[nx + ny * neighbor.width];
                    if (a.a < 250 || b.a < 250) continue;
                    difference += new Vector3(b.r - a.r, b.g - a.g, b.b - a.b);
                    samples++;
                }
                if (samples >= 4) offsets[along] = difference * (0.5f / (255 * samples));
            }
            edgeOffsets.Add(key, offsets);
        }
        float w = (float)(blendWidth - distance) / blendWidth;
        delta += offsets[dx == 0 ? x : y] * w;
        weight += w;
        maxWeight = Mathf.Max(maxWeight, w);
    }

    private static float Bayer(int x, int y)
    {
        // 4x4 ordered pattern; no random seed and no temporal noise.
        int a = ((x & 1) ^ (y & 1)) * 2 + (y & 1);
        int b = (((x >> 1) & 1) ^ ((y >> 1) & 1)) * 2 + ((y >> 1) & 1);
        return (a * 4 + b + 0.5f) / 16;
    }

    private void ApplyProperties()
    {
        if (target == null || block == null) return;
        target.GetPropertyBlock(block);
        block.SetFloat("_GroundSeamEnabled", isActiveAndEnabled && enableBlending && atlas != null && lookup != null ? 1 : 0);
        block.SetTexture("_GroundSeamAtlas", atlas != null ? atlas : Texture2D.blackTexture);
        block.SetTexture("_GroundSeamLookup", lookup != null ? lookup : Texture2D.blackTexture);
        if (atlas != null) block.SetVector("_GroundSeamAtlas_TexelSize", new Vector4(1f / atlas.width, 1f / atlas.height, atlas.width, atlas.height));
        block.SetVector("_GroundSeamBounds", new Vector4(bounds.xMin, bounds.yMin, Mathf.Max(1, bounds.size.x), Mathf.Max(1, bounds.size.y)));
        Vector3 size = map.layoutGrid != null ? map.layoutGrid.cellSize : Vector3.one;
        block.SetVector("_GroundSeamCellSize", new Vector4(size.x, size.y, 0, 0));
        lastWorldToLocal = transform.worldToLocalMatrix;
        block.SetMatrix("_GroundSeamWorldToLocal", lastWorldToLocal);
        target.SetPropertyBlock(block);
        lastEnabled = enableBlending;
    }

    private static Texture2D NewTexture(int w, int h, TextureFormat format, bool linear, string label)
    {
        return new Texture2D(w, h, format, false, linear) {
            name = label, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
    }

    private static Texture2D ReadTexture(Texture2D source)
    {
        RenderTexture previous = RenderTexture.active;
        bool previousSrgb = GL.sRGBWrite;
        RenderTexture rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Texture2D result = null;
        try
        {
            GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
            Graphics.Blit(source, rt);
            RenderTexture.active = rt;
            result = NewTexture(source.width, source.height, TextureFormat.RGBA32, false, "Seam source readback");
            result.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            result.Apply(false, false);
            return result;
        }
        catch { DestroyOwned(result); throw; }
        finally
        {
            RenderTexture.active = previous;
            GL.sRGBWrite = previousSrgb;
            RenderTexture.ReleaseTemporary(rt);
        }
    }

    private void OnDisable()
    {
        Tilemap.tilemapTileChanged -= TilesChanged;
        Tilemap.tilemapPositionsChanged -= PositionsChanged;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.update -= EditorTick;
        UnityEditor.Undo.undoRedoPerformed -= RequestRebuild;
#endif
        ReleaseTextures();
        ApplyProperties();
    }

    private void ReleaseTextures()
    {
        DestroyOwned(lookup); DestroyOwned(atlas);
        lookup = atlas = null;
    }

    private static void DestroyOwned(UnityEngine.Object value)
    {
        if (value == null) return;
        if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
    }
}
