using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Imports generated UI/VFX, preserves the supplied world sprite, and installs three editable prefab instances.</summary>
public static class PinkJewelSetup
{
    public const string Art = "Assets/Art/PinkJewel";
    public const string PickupPath = "Assets/Prefabs/Items/PinkJewel.prefab";
    public const string EffectPath = "Assets/Prefabs/Items/PinkJewelPickupVfx.prefab";
    public const string HudPath = "Assets/Prefabs/UI/PinkJewelHUD.prefab";
    public const string ResultPath = "Assets/Prefabs/UI/PinkJewelResult.prefab";
    private const string Materials = "Assets/Generated/PinkJewel";
    private static readonly Color Pink = new Color(1f, 0.4f, 0.72f, 1f);

    [MenuItem("Tools/Tekkyu/Pink Jewels/Create Assets")]
    public static void CreateAssets()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
        Directory.CreateDirectory(Materials);
        AssetDatabase.Refresh();
        ConfigureSprite(Art + "/PinkJewel.png", 32f); // Original file bytes are never rewritten.
        foreach (string name in new[] { "HudCollected", "HudEmpty", "ResultCollected", "ResultEmpty" })
        {
            bool result = name.StartsWith("Result", StringComparison.Ordinal);
            MakeIcon(name, result ? 32 : 24, result ? 40 : 32);
        }
        MakeEffectTextures();
        if (AssetDatabase.LoadAssetAtPath<GameObject>(EffectPath) == null) CreatePickupEffect();
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PickupPath) == null) CreatePickup();
        if (AssetDatabase.LoadAssetAtPath<GameObject>(HudPath) == null) CreateRow(false);
        if (AssetDatabase.LoadAssetAtPath<GameObject>(ResultPath) == null) CreateRow(true);
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Tools/Tekkyu/Pink Jewels/Rebuild Generated UI Icons")]
    public static void RebuildGeneratedIcons()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
        foreach (string name in new[] { "HudCollected", "HudEmpty", "ResultCollected", "ResultEmpty" })
        {
            bool result = name.StartsWith("Result", StringComparison.Ordinal);
            MakeIcon(name, result ? 32 : 24, result ? 40 : 32, true);
        }
        AssetDatabase.SaveAssets();
    }

    private static void MakeIcon(string name, int width, int height, bool rebuild = false)
    {
        string path = Art + "/" + name + ".png";
        if (rebuild || !File.Exists(path))
        {
            Texture2D source = ReadTexture(Art + "/Source/" + name + ".png");
            // Production preparation only: trim transparent margins, fit the generated design
            // to its dedicated logical grid, and sample nearest-neighbour. No world-art editing.
            int left = source.width, right = 0, bottom = source.height, top = 0;
            Color32[] pixels = source.GetPixels32();
            for (int y = 0; y < source.height; y++)
                for (int x = 0; x < source.width; x++)
                    if (pixels[y * source.width + x].a >= 128)
                    {
                        left = Math.Min(left, x); right = Math.Max(right, x);
                        bottom = Math.Min(bottom, y); top = Math.Max(top, y);
                    }
            var output = new Texture2D(width, height, TextureFormat.RGBA32, false);
            float scale = Mathf.Min((width - 2f) / (right - left + 1), (height - 2f) / (top - bottom + 1));
            int w = Mathf.RoundToInt((right - left + 1) * scale), h = Mathf.RoundToInt((top - bottom + 1) * scale);
            int offsetX = (width - w) / 2, offsetY = (height - h) / 2;
            var colors = new Color32[width * height];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int sx = Mathf.Clamp(left + Mathf.FloorToInt((x + 0.5f) / scale), left, right);
                    int sy = Mathf.Clamp(bottom + Mathf.FloorToInt((y + 0.5f) / scale), bottom, top);
                    Color32 color = pixels[sy * source.width + sx];
                    color = PaletteColor(color, name.EndsWith("Empty", StringComparison.Ordinal));
                    color.a = color.a >= 128 ? (byte)255 : (byte)0;
                    colors[(offsetY + y) * width + offsetX + x] = color;
                }
            output.SetPixels32(colors); output.Apply();
            File.WriteAllBytes(path, output.EncodeToPNG());
            Object.DestroyImmediate(source); Object.DestroyImmediate(output);
        }
        ConfigureSprite(path, height);
    }

    private static void MakeEffectTextures()
    {
        string[] names = { "Star", "Diamond", "Ring", "Halo", "Flash", "Glint" };
        Texture2D source = ReadTexture(Art + "/Source/EffectsAtlas.png");
        for (int i = 0; i < names.Length; i++)
        {
            string path = Art + "/" + names[i] + ".png";
            if (!File.Exists(path))
            {
                // Slice the generated 3x2 atlas, then use an actual 32px or 64px particle grid.
                int size = i == 2 || i == 3 ? 64 : 32;
                var output = new Texture2D(size, size, TextureFormat.RGBA32, false);
                int cellW = source.width / 3, cellH = source.height / 2;
                int cellX = i % 3 * cellW, cellY = (1 - i / 3) * cellH;
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        Color c = source.GetPixel(cellX + (int)((x + 0.5f) * cellW / size),
                            cellY + (int)((y + 0.5f) * cellH / size));
                        if (i != 3)
                        {
                            // Keep the generated white motif, excluding its faint surrounding haze.
                            float brightness = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
                            c.a *= Mathf.InverseLerp(0.62f, 0.92f, brightness);
                        }
                        output.SetPixel(x, y, c);
                    }
                output.Apply(); File.WriteAllBytes(path, output.EncodeToPNG()); Object.DestroyImmediate(output);
            }
            ConfigureSprite(path, i == 2 || i == 3 ? 64f : 32f);
        }
        Object.DestroyImmediate(source);
    }

    private static Color32 PaletteColor(Color32 source, bool empty)
    {
        // Palette-normalize only the generated small UI. The supplied world sprite is untouched.
        string[] hex = empty
            ? new[] { "201B29", "302637", "44344D", "58435E", "715974", "8C798E", "A594A8" }
            : new[] { "251527", "45243E", "70214B", "AC1657", "DC165D", "F5367C", "FF6199", "FF8DB6", "FFC0D7", "FFE5E9", "FFF6E9", "34333F", "555362", "8C8A96", "C5C2CA", "F1EDF4" };
        Color color = source, nearest = Color.black;
        float best = float.MaxValue;
        foreach (string value in hex)
        {
            ColorUtility.TryParseHtmlString("#" + value, out Color candidate);
            float distance = (candidate.r - color.r) * (candidate.r - color.r)
                + (candidate.g - color.g) * (candidate.g - color.g) + (candidate.b - color.b) * (candidate.b - color.b);
            if (distance < best) { best = distance; nearest = candidate; }
        }
        nearest.a = color.a;
        return nearest;
    }

    private static Texture2D ReadTexture(string path)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!texture.LoadImage(File.ReadAllBytes(path))) throw new InvalidOperationException("Cannot read " + path);
        return texture;
    }

    private static void ConfigureSprite(string path, float ppu)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = ppu;
        importer.spritePivot = new Vector2(0.5f, 0.5f);
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.maxTextureSize = 2048;
        importer.SaveAndReimport();
    }

    private static Sprite Sprite(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(Art + "/" + name + ".png");

    private static SpriteRenderer Layer(string name, Transform parent, string sprite, float scale, int order)
    {
        var root = new GameObject(name);
        root.transform.SetParent(parent, false);
        root.transform.localScale = Vector3.one * scale;
        var renderer = root.AddComponent<SpriteRenderer>();
        renderer.sprite = Sprite(sprite);
        renderer.sortingOrder = order;
        return renderer;
    }

    private static void CreatePickup()
    {
        var root = new GameObject("PinkJewel");
        try
        {
            var trigger = root.AddComponent<BoxCollider2D>();
            trigger.isTrigger = true;
            trigger.size = new Vector2(1.55f, 2.25f);
            var pickup = root.AddComponent<PinkJewelPickup>();
            var visual = new GameObject("Visual"); visual.transform.SetParent(root.transform, false);
            var jewel = Layer("OriginalJewel", visual.transform, "PinkJewel", 1f, 14);
            var glow = Layer("SoftPinkLight", visual.transform, "Halo", 4f, 12);
            glow.color = new Color(1f, 0.26f, 0.63f, 0.32f);
            var reflection = Layer("FacetReflection", visual.transform, "Glint", 0.25f, 16);
            reflection.transform.localPosition = new Vector3(-0.25f, 0.63f, 0f);
            reflection.color = Color.clear;
            ParticleSystem stars = Particles("IdleStars", visual.transform, "Star", true);
            ParticleSystem diamonds = Particles("IdleDiamonds", visual.transform, "Diamond", true);
            var shadow = Layer("GroundShadow", root.transform, "Halo", 1f, 2);
            shadow.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Generated/Tapioca/GroundShadow.png");
            shadow.transform.localScale = new Vector3(1.1f, 0.84f, 1f);
            shadow.color = new Color(0.055f, 0.04f, 0.09f, 0.52f);
            Set(pickup, "visualRoot", visual.transform); Set(pickup, "jewelRenderer", jewel);
            Set(pickup, "glowRenderer", glow); Set(pickup, "reflection", reflection);
            Set(pickup, "idleStars", stars); Set(pickup, "idleDiamonds", diamonds);
            Set(pickup, "groundShadow", shadow);
            Set(pickup, "pickupEffect", AssetDatabase.LoadAssetAtPath<GameObject>(EffectPath).GetComponent<PinkJewelPickupVfx>());
            PrefabUtility.SaveAsPrefabAsset(root, PickupPath);
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static void CreatePickupEffect()
    {
        var root = new GameObject("PinkJewelPickupVfx");
        try
        {
            var vfx = root.AddComponent<PinkJewelPickupVfx>();
            Set(vfx, "echo", Layer("JewelContactEcho", root.transform, "PinkJewel", 1f, 15));
            Set(vfx, "flash", Layer("ContactFlash", root.transform, "Flash", 2f, 16));
            Set(vfx, "halo", Layer("PinkLight", root.transform, "Halo", 3f, 16));
            SetArray(vfx, "rings", new Object[] { Layer("PinkRing", root.transform, "Ring", 1f, 16),
                Layer("WhiteRing", root.transform, "Ring", 1f, 16) });
            SetArray(vfx, "bursts", new Object[] { Particles("RadiantStars", root.transform, "Star", false),
                Particles("RadiantDiamonds", root.transform, "Diamond", false) });
            PrefabUtility.SaveAsPrefabAsset(root, EffectPath);
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static ParticleSystem Particles(string name, Transform parent, string textureName, bool idle)
    {
        var root = new GameObject(name); root.transform.SetParent(parent, false);
        var ps = root.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = idle; main.loop = idle; main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(idle ? 0.8f : 0.55f, idle ? 1.4f : 0.92f);
        main.startSpeed = idle ? 0f : 2.5f;
        main.startSize = new ParticleSystem.MinMaxCurve(idle ? 0.14f : 0.18f, idle ? 0.29f : 0.38f);
        main.maxParticles = idle ? 16 : 48;
        main.simulationSpace = idle ? ParticleSystemSimulationSpace.Local : ParticleSystemSimulationSpace.World;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Pink, 0f), new GradientColorKey(new Color(0.8f, 0.83f, 0.95f), 0.5f),
            new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        main.startColor = new ParticleSystem.MinMaxGradient(gradient) { mode = ParticleSystemGradientMode.RandomColor };
        var emission = ps.emission; emission.rateOverTime = idle ? 2.5f : 0f;
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = idle ? 0.9f : 0.12f; shape.radiusThickness = idle ? 0.4f : 1f;
        shape.scale = idle ? new Vector3(1f, 1.35f, 1f) : Vector3.one;
        shape.rotation = Vector3.zero;
        if (idle)
        {
            var velocity = ps.velocityOverLifetime; velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.22f, 0.22f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        }
        var color = ps.colorOverLifetime; color.enabled = true;
        var fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(idle ? 0f : 1f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0f, 1f) });
        color.color = fade;
        var size = ps.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.1f));
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        string path = Materials + "/" + textureName + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Sprites/Default")) { mainTexture = Sprite(textureName).texture };
            AssetDatabase.CreateAsset(material, path);
        }
        renderer.sharedMaterial = material; renderer.sortingOrder = idle ? 15 : 17;
        return ps;
    }

    private static void CreateRow(bool result)
    {
        var root = new GameObject(result ? "PinkJewelResult" : "PinkJewelHUD", typeof(RectTransform));
        try
        {
            root.layer = LayerMask.NameToLayer("UI");
            var rect = root.GetComponent<RectTransform>(); rect.sizeDelta = new Vector2(result ? 180f : 168f, result ? 48f : 64f);
            var row = root.AddComponent<PinkJewelIconRow>();
            Set(row, "showFinalResult", result);
            Set(row, "collectedIcon", Sprite(result ? "ResultCollected" : "HudCollected"));
            Set(row, "emptyIcon", Sprite(result ? "ResultEmpty" : "HudEmpty"));
            var icons = new Object[3]; var glints = new Object[3];
            for (int i = 0; i < 3; i++)
            {
                var image = Image("Jewel" + (i + 1), root.transform,
                    Sprite(result ? "ResultEmpty" : "HudEmpty"), new Vector2(result ? 36f : 36f, result ? 45f : 48f));
                image.rectTransform.anchoredPosition = new Vector2((i - 1) * 54f, 0f);
                icons[i] = image;
                var glint = Image("PickupGlint", image.transform, Sprite("Flash"), new Vector2(60f, 60f));
                glint.color = Color.clear; glints[i] = glint;
            }
            SetArray(row, "icons", icons); SetArray(row, "glints", glints);
            PrefabUtility.SaveAsPrefabAsset(root, result ? ResultPath : HudPath);
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static UnityEngine.UI.Image Image(string name, Transform parent, Sprite sprite, Vector2 size)
    {
        var root = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
        root.layer = LayerMask.NameToLayer("UI"); root.transform.SetParent(parent, false);
        var image = root.GetComponent<UnityEngine.UI.Image>();
        image.sprite = sprite; image.raycastTarget = false; image.preserveAspect = true;
        image.rectTransform.anchorMin = image.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        image.rectTransform.sizeDelta = size;
        return image;
    }

    [MenuItem("Tools/Tekkyu/Pink Jewels/Place In CompletScene")]
    public static string PlaceInActiveScene()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (EditorApplication.isPlaying || scene.path != "Assets/Scenes/CompletScene.unity")
            throw new InvalidOperationException("Open CompletScene in Edit Mode first.");
        if (Object.FindAnyObjectByType<PinkJewelCollection>(FindObjectsInactive.Include) != null)
            throw new InvalidOperationException("Jewels are already installed; existing placement/settings preserved.");
        var hp = Object.FindAnyObjectByType<SegmentHpBarUI>();
        var goal = Object.FindAnyObjectByType<GoalMenuController>(FindObjectsInactive.Include);
        Transform frame = hp.GetComponentsInChildren<RectTransform>(true).Single(t => t.name == "Frame");
        Transform safe = goal.transform.Find("CRTPresentation/PhosphorUI/SafeArea");
        if (safe == null) throw new InvalidOperationException("Existing Goal SafeArea was not found.");
        var root = new GameObject("PinkJewels", typeof(PinkJewelCollection));
        Undo.RegisterCreatedObjectUndo(root, "Install pink jewels");
        Vector3[] positions = { new Vector3(18f, 1.7f, 0f), new Vector3(68f, 3.2f, 0f), new Vector3(168f, 13.1f, 0f) };
        for (int i = 0; i < 3; i++)
        {
            var pickup = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PickupPath), root.transform);
            pickup.name = "PinkJewel_" + (i + 1);
            pickup.transform.position = positions[i];
            var settings = new SerializedObject(pickup.GetComponent<PinkJewelPickup>());
            settings.FindProperty("slot").intValue = i + 1; settings.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.RecordPrefabInstancePropertyModifications(pickup.GetComponent<PinkJewelPickup>());
        }
        var hud = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(HudPath), frame);
        Undo.RegisterCreatedObjectUndo(hud, "Add jewel HUD");
        var hudRect = hud.GetComponent<RectTransform>();
        hudRect.anchorMin = hudRect.anchorMax = new Vector2(0.5f, 0f);
        hudRect.pivot = new Vector2(0.5f, 1f); hudRect.anchoredPosition = new Vector2(0f, -16f);
        PrefabUtility.RecordPrefabInstancePropertyModifications(hudRect);
        var result = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ResultPath), safe);
        Undo.RegisterCreatedObjectUndo(result, "Add jewel result row");
        var resultRect = result.GetComponent<RectTransform>();
        resultRect.anchorMin = resultRect.anchorMax = new Vector2(0.5f, 0.5f);
        resultRect.anchoredPosition = new Vector2(0f, 38f);
        PrefabUtility.RecordPrefabInstancePropertyModifications(resultRect);
        EditorSceneManager.MarkSceneDirty(scene);
        return "Placed slots 1/2/3 at (18,1.7), (68,3.2), (168,13.1). HUD under Frame; result under Goal SafeArea. Scene not saved.";
    }

    private static void Set(Object target, string property, Object value)
    {
        var settings = new SerializedObject(target); settings.FindProperty(property).objectReferenceValue = value;
        settings.ApplyModifiedPropertiesWithoutUndo();
    }
    private static void Set(Object target, string property, bool value)
    {
        var settings = new SerializedObject(target); settings.FindProperty(property).boolValue = value;
        settings.ApplyModifiedPropertiesWithoutUndo();
    }
    private static void SetArray(Object target, string property, Object[] values)
    {
        var settings = new SerializedObject(target); var array = settings.FindProperty(property); array.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        settings.ApplyModifiedPropertiesWithoutUndo();
    }
}
