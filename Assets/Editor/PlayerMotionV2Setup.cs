using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>承認素材のゲーム用書き出し・参照設定。物理パラメーターは変更しない。</summary>
public static partial class PlayerMotionV2Setup
{
    public const string MotionAssetPath = "Assets/Resources/PlayerMotion/PlayerMotionV2.asset";
    public const string SpriteFolder = "Assets/Art/Player/MotionV2";
    private const string SourceFolder = "Docs/Art/PlayerMotionV2/ProductionSources";
    private const int Cell = 192;
    private static readonly Dictionary<string, PlayerMotionSet.Frame[]> built = new Dictionary<string, PlayerMotionSet.Frame[]>();
    private static Color32[] palette;
    private static readonly Dictionary<int, Color32> colorCache = new Dictionary<int, Color32>(8192);

    public static string Build()
    {
        if (File.Exists(RefinementCalibrationPath)) return BuildArtRefinement();
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before installing motion.");
        EnsureFolder(SpriteFolder);
        EnsureFolder("Assets/Resources/PlayerMotion");
        palette = ReadPalette();
        colorCache.Clear();
        built.Clear();
        LoadCalmCalibration();
        PlayerMotionSet set = AssetDatabase.LoadAssetAtPath<PlayerMotionSet>(MotionAssetPath);
        bool preserveSuspension = calmCalibration != null && set != null;
        BuildSheet("Run", calmCalibration != null ? 6 : 4, calmCalibration != null ? 1 : 2, 0.285f);
        BuildSheet("Air", 4, 2, 0.285f);
        BuildSheet("Charge", 4, 2, 0.285f);
        BuildSheet("ChargeRun", calmCalibration != null ? 3 : 4, 2, 0.285f);
        BuildSheet("ChargeAir", 4, 2, 0.285f);
        BuildSheet("Utility", 3, 2, 0.285f);
        foreach (string name in new[] { "ThrowForward", "ThrowUpRight", "ThrowUp", "ThrowDownRight", "ThrowDown" })
            BuildSheet(name, 3, 2, 0.285f);
        if (!preserveSuspension)
        {
            BuildSheet("Hanging", 2, 1, 0.21f);
            BuildSheet("Climbing", 4, 1, 0.225f);
        }

        if (set == null) { set = ScriptableObject.CreateInstance<PlayerMotionSet>(); AssetDatabase.CreateAsset(set, MotionAssetPath); }
        set.run = Sequence("Run", 0, built["Run"].Length, built["Run"].Length / 0.85f, true);
        set.jumpUp = Sequence("Air", 0, 2, 10f, false);
        set.apex = Sequence("Air", 2, 1, 1f, false);
        set.fall = Sequence("Air", 3, 2, 1f / 0.18f, false);
        set.land = Sequence("Air", 5, 3, 30f, false);
        set.charge = Sequence("Charge", 0, 8, 16f, true);
        set.chargeRunning = Sequence("ChargeRun", 0, built["ChargeRun"].Length, built["ChargeRun"].Length / 0.85f * 0.65f, true);
        set.chargeAirborne = Sequence("ChargeAir", 0, 8, 16f, true);
        set.recall = Sequence("Utility", 0, 2, 10f, false);
        set.brace = Sequence("Utility", 2, 2, 10f, false);
        set.pulled = Sequence("Utility", 4, 2, 10f, false);
        if (!preserveSuspension)
        {
            set.hanging = Sequence("Hanging", 0, 2, 2.5f, true);
            set.climbing = Sequence("Climbing", 0, 4, 5f, true);
        }
        string[] directions = { "ThrowForward", "ThrowUpRight", "ThrowUp", "ThrowDownRight", "ThrowDown" };
        set.throws = directions.Select(name => new PlayerMotionSet.DirectionalThrow
        {
            ground = Sequence(name, 0, 3, 16.666667f, false),
            air = Sequence(name, 3, 3, 16.666667f, false)
        }).ToArray();
        set.RebuildLookup();
        EditorUtility.SetDirty(set);
        ApplyClips(set);
        SaveMotionAssets(set);
        return "Updated " + built.Values.Sum(frames => frames.Length)
            + " native frames; " + (preserveSuspension ? "6 approved suspension frames preserved; " : "") + "Idle unchanged.";
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = path.Substring(0, path.LastIndexOf('/'));
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
    }

    private static Texture2D ReadImage(string path)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!texture.LoadImage(File.ReadAllBytes(path), false)) throw new IOException("Cannot decode " + path);
        return texture;
    }

    private static Color32[] ReadPalette()
    {
        var texture = ReadImage("Assets/image_/立ち絵(sprite).png");
        var counts = new Dictionary<int, int>();
        for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++)
        {
            Color32 c = texture.GetPixel(x, y);
            if (c.a < 220) continue;
            int key = (c.r << 16) | (c.g << 8) | c.b;
            counts.TryGetValue(key, out int count); counts[key] = count + 1;
        }
        UnityEngine.Object.DestroyImmediate(texture);
        return counts.OrderByDescending(pair => pair.Value).Take(96)
            .Select(pair => new Color32((byte)(pair.Key >> 16), (byte)(pair.Key >> 8), (byte)pair.Key, 255)).ToArray();
    }

    private static Color32 NativeColor(Color32 c)
    {
        if (c.a < 192) return default;
        int key = (c.r << 16) | (c.g << 8) | c.b;
        if (colorCache.TryGetValue(key, out Color32 cached)) return cached;
        int best = int.MaxValue; Color32 result = palette[0];
        foreach (Color32 target in palette)
        {
            int dr = c.r - target.r, dg = c.g - target.g, db = c.b - target.b;
            int error = dr * dr * 2 + dg * dg * 3 + db * db * 2;
            if (error < best) { best = error; result = target; }
        }
        colorCache[key] = result;
        return result;
    }

    private static Color32 TopPixel(Color32[] pixels, int width, int height, int x, int y)
        => x < 0 || x >= width || y < 0 || y >= height ? default : pixels[(height - 1 - y) * width + x];

    private static void BuildSheet(string name, int columns, int rows, float scale)
    {
        var input = ReadImage(CalmSourcePath(name));
        Color32[] pixels = input.GetPixels32();
        int width = input.width, height = input.height;
        int count = columns * rows, outputColumns = Mathf.Min(4, count), outputRows = Mathf.CeilToInt(count / (float)outputColumns);
        int sourceColumns = CalmSourceColumns(name, columns), sourceRows = CalmSourceRows(name, rows);
        var output = new Texture2D(outputColumns * Cell, outputRows * Cell, TextureFormat.RGBA32, false);
        var result = new Color32[output.width * output.height];
        var frames = new PlayerMotionSet.Frame[count];
        var metrics = new List<string>();
        for (int i = 0; i < count; i++)
        {
            int sourceIndex = CalmSourceIndex(name, i);
            int cellX = sourceIndex % sourceColumns * width / sourceColumns, cellY = sourceIndex / sourceColumns * height / sourceRows;
            int sourceCellY = cellY;
            int right = (sourceIndex % sourceColumns + 1) * width / sourceColumns, bottom = (sourceIndex / sourceColumns + 1) * height / sourceRows;
            cellY = Mathf.Max(0, cellY - 45); bottom = Mathf.Min(height, bottom + 45);
            HashSet<int> character = FindCharacter(pixels, width, height, cellX, cellY, right, bottom);
            int minX = right, maxX = cellX, minY = bottom, maxY = cellY;
            for (int y = cellY; y < bottom; y++) for (int x = cellX; x < right; x++)
                if (character.Contains(y * width + x))
                { minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y); }
            if (maxX <= minX || maxY <= minY) throw new InvalidOperationException(name + " empty frame " + i);
            float rootX = 0f, weight = 0f;
            int skirtTop = Mathf.RoundToInt(Mathf.Lerp(minY, maxY, 0.57f)), skirtBottom = Mathf.RoundToInt(Mathf.Lerp(minY, maxY, 0.78f));
            for (int y = skirtTop; y <= skirtBottom; y++) for (int x = minX; x <= maxX; x++)
            {
                Color32 c = TopPixel(pixels, width, height, x, y);
                if (c.a >= 220 && c.r >= 20 && c.r < 95 && c.g < 80 && Mathf.Abs(c.r - c.g) < 32)
                { rootX += x; weight++; }
            }
            rootX = weight > 40 ? rootX / weight : (minX + maxX) * 0.5f;
            float foot = Cell * 0.5f + 63f;
            if (name == "Air" && i < 5) foot -= 12f;
            if (name.StartsWith("Throw", StringComparison.Ordinal) && i >= 3) foot -= 12f;
            if (name == "Utility" && i >= 4) foot -= 15f;
            if (name == "ChargeAir") foot -= 12f;
            if ((name == "Run" || name == "ChargeRun") && (i % 4 == 2 || i % 4 == 3)) foot -= 3f;
            Vector2 weapon = DetectWeaponTip(pixels, width, height, cellX, cellY, right, bottom, rootX, minY, maxY, name, i);
            Vector2 grip = DetectGrip(pixels, width, height, minX, maxX, minY, maxY);
            if (name == "Climbing")
            {
                float[] centers = { 278.61f, 840.49f, 1358.49f, 1909.94f };
                float[] tops = { 109.99f, 101.77f, 111.17f, 107.82f };
                weapon = new Vector2(centers[i] * width / 2169f, tops[i] * height / 725f);
                rootX = weapon.x;
            }
            if (name == "Hanging")
                weapon = new Vector2((i == 0 ? 558f : 1223f) * width / 1774f, 101f * height / 887f);
            PoseMapping mapping = ResolveCalmMapping(name, i, cellX, sourceCellY, maxY,
                DetectFootY(pixels, width, height, character, minY, maxY));
            int outputX = i % outputColumns * Cell, outputY = i / outputColumns * Cell;
            for (int y = 0; y < Cell; y++) for (int x = 0; x < Cell; x++)
            {
                int sourceX = Mathf.RoundToInt(mapping.valid ? mapping.SourceX(x) : rootX + (x - Cell * 0.5f) / scale);
                int sourceY = Mathf.RoundToInt(mapping.valid ? mapping.SourceY(y) : maxY + (y - foot) / scale);
                if (sourceX < cellX || sourceX >= right || sourceY < cellY || sourceY >= bottom
                    || !character.Contains(sourceY * width + sourceX)) continue;
                result[(output.height - 1 - outputY - y) * output.width + outputX + x] =
                    NativeColor(TopPixel(pixels, width, height, sourceX, sourceY));
            }
            frames[i] = new PlayerMotionSet.Frame
            {
                chainAnchor = mapping.valid ? mapping.WorldLocal(weapon) : new Vector2((weapon.x - rootX) * scale, Cell * 0.5f - foot + (maxY - weapon.y) * scale) / 32f,
                chargeGrip = mapping.valid ? mapping.WorldLocal(grip) : new Vector2((grip.x - rootX) * scale, Cell * 0.5f - foot + (maxY - grip.y) * scale) / 32f
            };
            if (TryCalmVisualAnchor(name, i, out Vector2 visualAnchor)) frames[i].chainAnchor = visualAnchor;
            metrics.Add(name + "_" + i + " bounds=" + minX + "," + minY + "," + maxX + "," + maxY
                + " rootX=" + rootX + " chain=" + frames[i].chainAnchor + " grip=" + frames[i].chargeGrip
                + (mapping.valid ? " skull=" + mapping.targetSkull + " head=" + mapping.targetHead + " bodyY=" + mapping.bodyStretch : ""));
        }
        output.SetPixels32(result); output.Apply();
        string path = SpriteFolder + "/" + name + ".png";
        File.WriteAllBytes(path, output.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(input); UnityEngine.Object.DestroyImmediate(output);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        ImportSprites(path, count, outputColumns);
        Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().OrderBy(sprite => sprite.name, StringComparer.Ordinal).ToArray();
        if (sprites.Length != count) throw new InvalidOperationException(path + " wrong sprite count.");
        for (int i = 0; i < count; i++) frames[i].sprite = sprites[i];
        built[name] = frames;
        Directory.CreateDirectory("Docs/Art/PlayerMotionV2/Implementation");
        File.WriteAllLines("Docs/Art/PlayerMotionV2/Implementation/" + name + "-frames.txt", metrics);
    }


    private static HashSet<int> FindCharacter(Color32[] pixels, int width, int height, int left, int top, int right, int bottom)
    {
        int w = right - left, h = bottom - top;
        var visited = new bool[w * h]; var queue = new Queue<int>();
        List<int> largest = null;
        for (int y = top; y < bottom; y++) for (int x = left; x < right; x++)
        {
            int index = (y - top) * w + x - left;
            if (visited[index] || TopPixel(pixels, width, height, x, y).a < 192) continue;
            visited[index] = true; queue.Enqueue(index); var component = new List<int>();
            while (queue.Count > 0)
            {
                int n = queue.Dequeue(), nx = n % w, ny = n / w;
                component.Add((ny + top) * width + nx + left);
                for (int oy = -1; oy <= 1; oy++) for (int ox = -1; ox <= 1; ox++)
                {
                    int xx = nx + ox, yy = ny + oy;
                    if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                    int next = yy * w + xx;
                    if (visited[next] || TopPixel(pixels, width, height, xx + left, yy + top).a < 192) continue;
                    visited[next] = true; queue.Enqueue(next);
                }
            }
            if (largest == null || component.Count > largest.Count) largest = component;
        }
        if (largest == null) throw new InvalidOperationException("No connected character in source cell.");
        return new HashSet<int>(largest);
    }

    private static Vector2 DetectWeaponTip(Color32[] pixels, int width, int height,
        int left, int top, int right, int bottom, float rootX, int minY, int maxY, string name, int frameIndex)
    {
        // 柄の彩度が高いピンクを検出。髪やリボンの塊ではなく、細長い連結領域を選ぶ。
        int w = right - left, h = bottom - top;
        var visited = new bool[w * h];
        var queue = new Queue<int>();
        List<Vector2> best = null; float bestScore = 0f;
        for (int y = top; y < bottom; y++) for (int x = left; x < right; x++)
        {
            int index = (y - top) * w + x - left;
            if (visited[index]) continue;
            Color32 c = TopPixel(pixels, width, height, x, y);
            if (c.a < 220 || c.r < 75 || c.g > c.r * 0.64f || c.b < c.g * 1.28f) continue;
            var cluster = new List<Vector2>();
            visited[index] = true; queue.Enqueue(index);
            while (queue.Count > 0)
            {
                int n = queue.Dequeue(), nx = n % w, ny = n / w;
                cluster.Add(new Vector2(nx + left, ny + top));
                for (int oy = -1; oy <= 1; oy++) for (int ox = -1; ox <= 1; ox++)
                {
                    int xx = nx + ox, yy = ny + oy;
                    if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                    int ni = yy * w + xx; if (visited[ni]) continue;
                    Color32 p = TopPixel(pixels, width, height, xx + left, yy + top);
                    if (p.a < 220 || p.r < 75 || p.g > p.r * 0.64f || p.b < p.g * 1.28f) continue;
                    visited[ni] = true; queue.Enqueue(ni);
                }
            }
            if (cluster.Count < 10) continue;
            Vector2 mean = Vector2.zero;
            foreach (Vector2 p in cluster) mean += p;
            mean /= cluster.Count;
            float xxv = 0f, yyv = 0f, xyv = 0f;
            foreach (Vector2 p in cluster) { Vector2 d = p - mean; xxv += d.x * d.x; yyv += d.y * d.y; xyv += d.x * d.y; }
            float trace = xxv + yyv, disc = Mathf.Sqrt((xxv - yyv) * (xxv - yyv) + 4f * xyv * xyv);
            float ratio = (trace + disc) / Mathf.Max(1f, trace - disc);
            float bodyHeight = maxY - minY;
            bool allowed;
            if (name.StartsWith("Throw", StringComparison.Ordinal))
            {
                if (frameIndex % 3 == 0) allowed = mean.x < rootX - 20f;
                else if (name == "ThrowUp") allowed = mean.y < minY + bodyHeight * 0.35f;
                else if (name == "ThrowDown" || name.StartsWith("ThrowDownRefined", StringComparison.Ordinal))
                    allowed = mean.y > minY + bodyHeight * 0.6f && mean.x > rootX;
                else allowed = mean.x > rootX + 55f;
            }
            else if (name == "Utility" && frameIndex >= 2) allowed = mean.x > rootX + 55f;
            else allowed = mean.x < rootX - 35f && mean.y > minY + bodyHeight * 0.4f;
            if (!allowed) continue;
            float score = Mathf.Min(ratio, 40f) * Mathf.Sqrt(cluster.Count);
            if (ratio > 3f && score > bestScore) { bestScore = score; best = cluster; }
        }
        if (best == null) return new Vector2(left + w * 0.12f, top + h * 0.68f);
        Vector2 center = Vector2.zero; foreach (Vector2 p in best) center += p; center /= best.Count;
        float a = 0f, b = 0f, dxy = 0f;
        foreach (Vector2 p in best) { Vector2 d = p - center; a += d.x * d.x; b += d.y * d.y; dxy += d.x * d.y; }
        float angle = 0.5f * Mathf.Atan2(2f * dxy, a - b);
        Vector2 axis = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        Vector2 body = new Vector2(rootX, Mathf.Lerp(minY, maxY, 0.5f));
        if (Vector2.Dot(axis, center - body) < 0f) axis = -axis;
        float far = 0f, near = 0f;
        foreach (Vector2 p in best) { float t = Vector2.Dot(p - center, axis); far = Mathf.Max(far, t); near = Mathf.Min(near, t); }
        float edge = far;
        for (float t = far; t <= far + 35f; t += 1f)
        {
            Vector2 p = center + axis * t;
            Color32 c = TopPixel(pixels, width, height, Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y));
            if (c.a >= 220 && c.r < 120 && c.g < 100) edge = t;
        }
        return center + axis * edge;
    }

    private static Vector2 DetectGrip(Color32[] pixels, int width, int height, int minX, int maxX, int minY, int maxY)
    {
        Vector2 sum = Vector2.zero; int count = 0;
        int left = Mathf.RoundToInt(Mathf.Lerp(minX, maxX, 0.87f));
        int top = Mathf.RoundToInt(Mathf.Lerp(minY, maxY, 0.3f)), bottom = Mathf.RoundToInt(Mathf.Lerp(minY, maxY, 0.7f));
        for (int y = top; y < bottom; y++) for (int x = left; x <= maxX; x++)
        {
            Color32 c = TopPixel(pixels, width, height, x, y);
            if (c.a > 220 && c.r > 175 && c.g > 115 && c.r - c.b > 25 && c.r - c.g > 15)
            { sum += new Vector2(x, y); count++; }
        }
        return count > 10 ? sum / count : new Vector2(maxX - 15, Mathf.Lerp(minY, maxY, 0.5f));
    }

    private static void ImportSprites(string path, int count, int columns)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = 32f;
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.alphaIsTransparency = true;
        importer.maxTextureSize = 2048;
        var textureSettings = new TextureImporterSettings();
        importer.ReadTextureSettings(textureSettings);
        textureSettings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(textureSettings);
        importer.SaveAndReimport();
        var factory = new SpriteDataProviderFactories(); factory.Init();
        var provider = factory.GetSpriteEditorDataProviderFromObject(importer); provider.InitSpriteEditorDataProvider();
        var capability = provider.GetDataProvider<ISpriteFrameEditCapability>();
        if (capability == null) throw new InvalidOperationException("Sprite edit capability unavailable: " + path);
        var flags = capability.GetEditCapability();
        foreach (EEditCapability required in new[] { EEditCapability.CreateAndDeleteSprite, EEditCapability.EditSpriteRect, EEditCapability.EditPivot, EEditCapability.EditSpriteName })
            if (!flags.HasCapability(required)) throw new InvalidOperationException("Sprite capability unavailable: " + required);
        var old = provider.GetSpriteRects().ToDictionary(rect => rect.name);
        var rects = new SpriteRect[count]; int rows = Mathf.CeilToInt(count / (float)columns);
        string prefix = Path.GetFileNameWithoutExtension(path);
        for (int i = 0; i < count; i++)
        {
            string name = prefix + "_" + i.ToString("00");
            rects[i] = new SpriteRect { name = name, rect = new Rect(i % columns * Cell, (rows - 1 - i / columns) * Cell, Cell, Cell),
                pivot = new Vector2(0.5f, 0.5f), alignment = SpriteAlignment.Custom,
                spriteID = old.TryGetValue(name, out SpriteRect previous) ? previous.spriteID : GUID.Generate() };
        }
        provider.SetSpriteRects(rects);
        var names = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
        if (names == null) throw new InvalidOperationException("Sprite name ID provider unavailable.");
        names.SetNameFileIdPairs(rects.Select(rect => new SpriteNameFileIdPair(rect.name, rect.spriteID)));
        provider.Apply(); importer.SaveAndReimport();
    }

    private static PlayerMotionSet.Sequence Sequence(string name, int start, int count, float fps, bool loop)
        => new PlayerMotionSet.Sequence { frames = built[name].Skip(start).Take(count).ToArray(), framesPerSecond = fps, loop = loop };

    public static void ApplyClips(PlayerMotionSet set)
    {
        UpdateClip("Walk", set.run, 0.85f);
        UpdateClip("JumpUp", set.jumpUp, 0.11666667f);
        UpdateClip("JumpFall", set.fall, 0.25f);
        UpdateClip("JumpLand", set.land, 0.1f);
        UpdateClip("MorningStarLaunch", set.throws[0].ground, 0.17f);
    }

    private static void UpdateClip(string name, PlayerMotionSet.Sequence sequence, float duration)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animation/" + name + ".anim");
        if (clip == null) throw new InvalidOperationException("Existing clip missing: " + name);
        var binding = new EditorCurveBinding { path = "", type = typeof(SpriteRenderer), propertyName = "m_Sprite" };
        var keys = new List<ObjectReferenceKeyframe>();
        for (int i = 0; i < sequence.frames.Length; i++)
            keys.Add(new ObjectReferenceKeyframe { time = i / sequence.framesPerSecond, value = sequence.frames[i].sprite });
        keys.Add(new ObjectReferenceKeyframe { time = Mathf.Max(keys[keys.Count - 1].time, duration - 1f / clip.frameRate), value = sequence.frames[sequence.frames.Length - 1].sprite });
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys.ToArray());
        var data = new SerializedObject(clip);
        data.FindProperty("m_AnimationClipSettings.m_LoopTime").boolValue = sequence.loop;
        data.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(clip);
    }

    public static bool ApplyInstalledClips()
    {
        var set = AssetDatabase.LoadAssetAtPath<PlayerMotionSet>(MotionAssetPath);
        if (set == null) return false;
        ApplyClips(set); SaveMotionAssets(set); return true;
    }

    private static void SaveMotionAssets(PlayerMotionSet set)
    {
        AssetDatabase.SaveAssetIfDirty(set);
        foreach (string name in new[] { "Walk", "JumpUp", "JumpFall", "JumpLand", "MorningStarLaunch" })
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animation/" + name + ".anim");
            if (clip != null) AssetDatabase.SaveAssetIfDirty(clip);
        }
    }

    public static string InstallReferences()
    {
        var set = AssetDatabase.LoadAssetAtPath<PlayerMotionSet>(MotionAssetPath);
        if (set == null) throw new InvalidOperationException("Build motions first.");
        int count = 0;
        // 既存Prefabは古い未使用フィールドを再シリアライズしない。Awakeの共有Resources参照を使う。
        Scene original = SceneManager.GetActiveScene();
        foreach (string path in new[] { "Assets/Scenes/Stage1.unity", "Assets/Scenes/Stage2.unity", "Assets/Scenes/CompletScene.unity" })
        {
            Scene scene = SceneManager.GetSceneByPath(path); bool opened = !scene.IsValid() || !scene.isLoaded;
            if (scene.IsValid() && scene.isDirty) throw new InvalidOperationException("Unsaved user scene: " + path);
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (var launcher in root.GetComponentsInChildren<MorningStarLauncher>(true)) { Assign(launcher, set); count++; }
            EditorSceneManager.SaveScene(scene);
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }
        if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
        return "Assigned shared motion set to " + count + " launchers; gameplay settings preserved.";
    }

    private static void Assign(MorningStarLauncher launcher, PlayerMotionSet set)
    {
        var data = new SerializedObject(launcher);
        data.FindProperty("playerMotionSet").objectReferenceValue = set;
        data.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(launcher);
    }
}

