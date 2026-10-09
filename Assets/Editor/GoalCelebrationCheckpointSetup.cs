using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Goal結晶、Goal祝福UI、Checkpoint発光、HP中央Maskを再現可能に設定する。</summary>
public static class GoalCelebrationCheckpointSetup
{
    public const string RequestPath = "Temp/GoalCelebrationCheckpointSetup.request";
    private const string ScenePath = "Assets/Scenes/CompletScene.unity";
    private const string GoalPointPrefabPath = "Assets/Prefabs/Gimmicks/GoalPoint.prefab";
    private const string CheckpointPrefabPath = "Assets/Prefabs/Gimmicks/Checkpoint.prefab";
    private const string GoalMenuPrefabPath = "Assets/Prefabs/UI/GoalMenu.prefab";
    private const string FragmentPrefabPath = "Assets/Prefabs/Gimmicks/CrystalFragment.prefab";
    private const string GoalCrystalTexturePath = "Assets/image_/ゴール結晶.png";
    private const string FragmentTexturePath = "Assets/image_/ゴール結晶(破片).png";
    private const string CheckpointTexturePath = "Assets/image_/セーブポイントモニュメントドット絵.png";
    private const string HpTexturePath = "Assets/image_/体力ゲージドット絵.png";

    [MenuItem("Tools/鉄球少女/Apply Goal Celebration Checkpoint Glow And HP Alignment")]
    public static void Apply()
    {
        ApplyAssetsOnly();
        ConfigureScene();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[GoalCelebrationCheckpointSetup] Goal crystal, celebration UI, checkpoint glow and HP alignment applied.");
    }

    public static void ApplyAssetsOnly()
    {
        ConfigureTextures();
        CrystalFragment fragmentPrefab = CreateFragmentPrefab();
        ConfigureGoalPointPrefab(fragmentPrefab);
        ConfigureCheckpointPrefab();
        ConfigureGoalMenuPrefab();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[GoalCelebrationCheckpointSetup] Goal/Checkpoint/UI assets applied without opening a Scene.");
    }

    [InitializeOnLoadMethod]
    private static void QueueRequestedApply()
    {
        if (File.Exists(RequestPath))
            EditorApplication.delayCall += RunRequestedApply;
    }

    private static void RunRequestedApply()
    {
        if (!File.Exists(RequestPath))
            return;

        File.Delete(RequestPath);
        ApplyAssetsOnly();
    }

    private static void ConfigureTextures()
    {
        ConfigureMultipleSpriteTexture(GoalCrystalTexturePath, new[]
        {
            CreateSpriteRect("GoalCrystal_0_Intact", new Rect(2f, 20f, 70f, 192f)),
            CreateSpriteRect("GoalCrystal_1_Cracked", new Rect(76f, 20f, 67f, 192f)),
            CreateSpriteRect("GoalCrystal_2_Broken", new Rect(146f, 20f, 73f, 192f)),
            CreateSpriteRect("GoalCrystal_3_Shattered", new Rect(221f, 20f, 72f, 192f))
        });
        ConfigureMultipleSpriteTexture(FragmentTexturePath, new[]
        {
            CreateSpriteRect("GoalCrystalFragment", new Rect(19f, 6f, 20f, 27f))
        });
        ConfigureMultipleSpriteTexture(CheckpointTexturePath, new[]
        {
            CreateSpriteRect("CheckpointMonument_Base", new Rect(12f, 18f, 127f, 122f)),
            CreateSpriteRect("CheckpointMonument_GlowCrystal", new Rect(51f, 83f, 39f, 45f)),
            CreateSpriteRect("CheckpointMonument_GlowEmblem", new Rect(56f, 49f, 28f, 25f))
        });
        ConfigurePixelTexturePreservingSprites(HpTexturePath);
    }

    private static SpriteRect CreateSpriteRect(string name, Rect rect)
    {
        return new SpriteRect
        {
            name = name,
            rect = rect,
            alignment = SpriteAlignment.Center,
            pivot = new Vector2(0.5f, 0.5f),
            spriteID = GUID.Generate()
        };
    }

    private static void ConfigureMultipleSpriteTexture(string path, SpriteRect[] requestedRects)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
            throw new InvalidOperationException($"Sprite texture not found: {path}");

        ApplyPixelTextureSettings(importer);
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = 100f;

        SpriteDataProviderFactories factories = new SpriteDataProviderFactories();
        factories.Init();
        ISpriteEditorDataProvider provider = factories.GetSpriteEditorDataProviderFromObject(importer);
        provider.InitSpriteEditorDataProvider();

        Dictionary<string, GUID> existingIds = provider.GetSpriteRects()
            .ToDictionary(item => item.name, item => item.spriteID, StringComparer.Ordinal);
        foreach (SpriteRect rect in requestedRects)
        {
            if (existingIds.TryGetValue(rect.name, out GUID existingId))
                rect.spriteID = existingId;
        }

        provider.SetSpriteRects(requestedRects);
        ISpriteNameFileIdDataProvider nameProvider =
            provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
        nameProvider.SetNameFileIdPairs(requestedRects
            .Select(rect => new SpriteNameFileIdPair(rect.name, rect.spriteID))
            .ToArray());
        provider.Apply();
        importer.SaveAndReimport();
    }

    private static void ConfigurePixelTexturePreservingSprites(string path)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
            throw new InvalidOperationException($"Sprite texture not found: {path}");
        ApplyPixelTextureSettings(importer);
        importer.SaveAndReimport();
    }

    private static void ApplyPixelTextureSettings(TextureImporter importer)
    {
        importer.textureType = TextureImporterType.Sprite;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;

        TextureImporterSettings settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);

        ClearPlatformOverride(importer, "Standalone");
        ClearPlatformOverride(importer, "WebGL");
        ClearPlatformOverride(importer, "Android");
        ClearPlatformOverride(importer, "iPhone");
    }

    private static void ClearPlatformOverride(TextureImporter importer, string platformName)
    {
        TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(platformName);
        if (!settings.overridden)
            return;
        settings.overridden = false;
        importer.SetPlatformTextureSettings(settings);
    }

    private static CrystalFragment CreateFragmentPrefab()
    {
        Sprite sprite = LoadSprite(FragmentTexturePath, "GoalCrystalFragment");
        GameObject instance = new GameObject("CrystalFragment");
        try
        {
            SpriteRenderer renderer = instance.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = Color.white;
            renderer.sortingOrder = 12;

            Rigidbody2D body = instance.AddComponent<Rigidbody2D>();
            body.mass = 0.02f;
            body.gravityScale = 0.8f;
            body.linearDamping = 0.2f;
            body.angularDamping = 0.1f;
            body.collisionDetectionMode = CollisionDetectionMode2D.Discrete;

            CrystalFragment fragment = instance.AddComponent<CrystalFragment>();
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(instance, FragmentPrefabPath);
            return saved.GetComponent<CrystalFragment>();
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(instance);
        }
    }

    private static void ConfigureGoalPointPrefab(CrystalFragment fragmentPrefab)
    {
        Sprite[] stages = LoadSprites(GoalCrystalTexturePath)
            .Where(sprite => sprite.name.StartsWith("GoalCrystal_", StringComparison.Ordinal))
            .OrderBy(sprite => sprite.name, StringComparer.Ordinal)
            .ToArray();
        if (stages.Length != 4)
            throw new InvalidOperationException("Goal crystal stages must contain exactly four sprites.");

        GameObject root = PrefabUtility.LoadPrefabContents(GoalPointPrefabPath);
        try
        {
            root.transform.localScale = Vector3.one;
            int wallsLayer = LayerMask.NameToLayer("Walls");
            root.layer = wallsLayer >= 0 ? wallsLayer : 0;

            SpriteRenderer renderer = root.GetComponent<SpriteRenderer>();
            renderer.sprite = stages[0];
            renderer.color = Color.white;
            renderer.sortingOrder = 5;

            BoxCollider2D collider = root.GetComponent<BoxCollider2D>();
            collider.isTrigger = false;
            collider.size = new Vector2(0.66f, 1.82f);
            collider.offset = new Vector2(0f, -0.02f);

            GoalPoint goal = root.GetComponent<GoalPoint>();
            SerializedObject serialized = new SerializedObject(goal);
            serialized.FindProperty("requiredHits").intValue = 3;
            serialized.FindProperty("hitCooldown").floatValue = 0.15f;
            SetObjectArray(serialized.FindProperty("crystalStages"), stages.Cast<UnityEngine.Object>().ToArray());
            serialized.FindProperty("fragmentPrefab").objectReferenceValue = fragmentPrefab;
            serialized.FindProperty("fragmentsPerHit").intValue = 3;
            serialized.FindProperty("fragmentsOnBreak").intValue = 12;
            serialized.FindProperty("minFragmentForce").floatValue = 1.5f;
            serialized.FindProperty("maxFragmentForce").floatValue = 4f;
            serialized.FindProperty("goalPresentationDelay").floatValue = 0.18f;
            serialized.FindProperty("breakShakeDuration").floatValue = 0.12f;
            serialized.FindProperty("breakShakeStrength").floatValue = 0.06f;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, GoalPointPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ConfigureCheckpointPrefab()
    {
        Sprite baseSprite = LoadSprite(CheckpointTexturePath, "CheckpointMonument_Base");
        Sprite crystalGlow = LoadSprite(CheckpointTexturePath, "CheckpointMonument_GlowCrystal");
        Sprite emblemGlow = LoadSprite(CheckpointTexturePath, "CheckpointMonument_GlowEmblem");

        GameObject root = PrefabUtility.LoadPrefabContents(CheckpointPrefabPath);
        try
        {
            root.transform.localScale = Vector3.one;
            SpriteRenderer renderer = root.GetComponent<SpriteRenderer>();
            renderer.sprite = baseSprite;
            renderer.color = new Color(0.52f, 0.47f, 0.56f, 1f);
            renderer.sortingOrder = 2;

            BoxCollider2D collider = root.GetComponent<BoxCollider2D>();
            collider.isTrigger = true;
            // 旧Prefabのワールド判定寸法 0.3 x 2.0 を維持する。
            collider.size = new Vector2(0.3f, 2f);
            collider.offset = Vector2.zero;

            DestroyChildIfExists(root.transform, "GlowTopCrystal");
            DestroyChildIfExists(root.transform, "GlowFrontEmblem");
            SpriteRenderer topGlow = CreateGlowRenderer(root.transform, "GlowTopCrystal", crystalGlow,
                new Vector2(-0.05f, 0.265f), new Color(1f, 0.28f, 0.78f, 0.92f), 3);
            SpriteRenderer frontGlow = CreateGlowRenderer(root.transform, "GlowFrontEmblem", emblemGlow,
                new Vector2(-0.055f, -0.175f), new Color(1f, 0.2f, 0.68f, 0.95f), 3);

            Checkpoint checkpoint = root.GetComponent<Checkpoint>();
            SerializedObject serialized = new SerializedObject(checkpoint);
            serialized.FindProperty("inactiveColor").colorValue = new Color(0.52f, 0.47f, 0.56f, 1f);
            serialized.FindProperty("activeColor").colorValue = Color.white;
            SetObjectArray(serialized.FindProperty("glowRenderers"),
                new UnityEngine.Object[] { topGlow, frontGlow });
            serialized.FindProperty("glowTransitionDuration").floatValue = 0.25f;
            serialized.FindProperty("glowPulseMinimum").floatValue = 0.72f;
            serialized.FindProperty("glowPulseSpeed").floatValue = 1.8f;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, CheckpointPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static SpriteRenderer CreateGlowRenderer(
        Transform parent,
        string name,
        Sprite sprite,
        Vector2 localPosition,
        Color color,
        int sortingOrder)
    {
        GameObject child = new GameObject(name, typeof(SpriteRenderer));
        child.transform.SetParent(parent, false);
        child.transform.localPosition = new Vector3(localPosition.x, localPosition.y, -0.01f);
        child.transform.localScale = Vector3.one;
        SpriteRenderer renderer = child.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = color;
        renderer.sortingOrder = sortingOrder;
        return renderer;
    }

    private static void ConfigureGoalMenuPrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(GoalMenuPrefabPath);
        try
        {
            RectTransform panel = RequireRect(root.transform, "GoalStonePanel");
            RectTransform title = RequireRect(root.transform, "GoalTitle");
            Graphic overlay = RequireRect(root.transform, "DarkOverlay").GetComponent<Graphic>();
            if (overlay == null)
                throw new InvalidOperationException("Goal DarkOverlay graphic is missing.");
            Color overlayColor = overlay.color;
            overlayColor.a = 0.72f;
            overlay.color = overlayColor;

            Transform old = FindDeep(panel, "GoalCelebration");
            if (old != null)
                UnityEngine.Object.DestroyImmediate(old.gameObject);

            RectTransform celebration = CreateRect("GoalCelebration", panel);
            Stretch(celebration);
            celebration.SetSiblingIndex(title.GetSiblingIndex());

            Vector2[] positions =
            {
                new Vector2(-300f, 310f), new Vector2(-235f, 245f), new Vector2(-150f, 330f),
                new Vector2(-82f, 205f), new Vector2(85f, 205f), new Vector2(150f, 330f),
                new Vector2(235f, 245f), new Vector2(300f, 310f), new Vector2(-345f, 195f),
                new Vector2(345f, 195f), new Vector2(-350f, 365f), new Vector2(350f, 365f),
                new Vector2(-24f, 360f), new Vector2(28f, 185f)
            };
            Color[] colors =
            {
                new Color(1f, .27f, .75f, .95f), new Color(1f, .93f, 1f, .9f),
                new Color(.68f, .42f, 1f, .85f)
            };
            List<RectTransform> sparkles = new List<RectTransform>();
            for (int i = 0; i < positions.Length; i++)
            {
                RectTransform sparkle = CreateRect($"CrystalSpark_{i + 1:00}", celebration);
                sparkle.anchorMin = sparkle.anchorMax = new Vector2(0.5f, 0.5f);
                sparkle.pivot = new Vector2(0.5f, 0.5f);
                sparkle.anchoredPosition = positions[i];
                float width = i >= 8 ? 8f + (i % 3) * 3f : 13f + (i % 4) * 4f;
                float height = i % 3 == 0 ? width * 1.8f : width;
                sparkle.sizeDelta = new Vector2(width, height);
                sparkle.localRotation = Quaternion.Euler(0f, 0f, 45f + (i % 2) * 45f);
                Image image = sparkle.gameObject.AddComponent<Image>();
                image.color = colors[i % colors.Length];
                image.raycastTarget = false;
                sparkles.Add(sparkle);
            }

            GoalMenuController controller = root.GetComponent<GoalMenuController>();
            SerializedObject serialized = new SerializedObject(controller);
            serialized.FindProperty("darkOverlay").objectReferenceValue = overlay;
            serialized.FindProperty("goalTitle").objectReferenceValue = title;
            SetObjectArray(serialized.FindProperty("sparkleVisuals"), sparkles.Cast<UnityEngine.Object>().ToArray());
            serialized.FindProperty("showDuration").floatValue = 0.24f;
            serialized.FindProperty("titlePopDuration").floatValue = 0.32f;
            serialized.FindProperty("buttonRevealDuration").floatValue = 0.1f;
            serialized.FindProperty("buttonStagger").floatValue = 0.055f;
            serialized.FindProperty("sparkleRotationSpeed").floatValue = 12f;
            serialized.FindProperty("sparklePulseSpeed").floatValue = 1.15f;
            serialized.FindProperty("sparkleDriftPixels").floatValue = 3f;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, GoalMenuPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ConfigureScene()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GoalMenuController goalMenu = FindInScene<GoalMenuController>(scene);
        CameraShake2D cameraShake = FindInScene<CameraShake2D>(scene);
        foreach (GoalPoint goal in FindAllInScene<GoalPoint>(scene))
        {
            SerializedObject serialized = new SerializedObject(goal);
            serialized.FindProperty("goalMenu").objectReferenceValue = goalMenu;
            serialized.FindProperty("cameraShake").objectReferenceValue = cameraShake;
            SerializedProperty calls = serialized.FindProperty("onGoalReached")
                .FindPropertyRelative("m_PersistentCalls").FindPropertyRelative("m_Calls");
            calls.arraySize = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(goal);
        }

        SegmentHpBarUI hpUi = FindInScene<SegmentHpBarUI>(scene);
        if (hpUi == null)
            throw new InvalidOperationException("SegmentHpBarUI is missing from CompletScene.");
        RectTransform frame = FindDeep(hpUi.transform, "Frame") as RectTransform;
        ValidateFrameInvariant(frame);
        hpUi.AlignVisualsToFrame();
        ValidateFrameInvariant(frame);
        EditorUtility.SetDirty(hpUi);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static void ValidateFrameInvariant(RectTransform frame)
    {
        if (frame == null)
            throw new InvalidOperationException("HP Frame is missing.");
        bool valid = Approximately(frame.anchorMin, Vector2.zero)
            && Approximately(frame.anchorMax, Vector2.one)
            && Approximately(frame.anchoredPosition, new Vector2(20f, -254f))
            && Approximately(frame.sizeDelta, new Vector2(408.476f, 74.2252f))
            && Approximately(frame.pivot, new Vector2(0.5f, 0.5f))
            && Approximately(frame.localScale, Vector3.one)
            && Mathf.Abs(frame.localPosition.z) <= 0.0001f;
        if (!valid)
            throw new InvalidOperationException("HP Frame differs from the immutable requested RectTransform.");
    }

    private static bool Approximately(Vector2 a, Vector2 b)
    {
        return (a - b).sqrMagnitude <= 0.0001f;
    }

    private static bool Approximately(Vector3 a, Vector3 b)
    {
        return (a - b).sqrMagnitude <= 0.0001f;
    }

    private static Sprite LoadSprite(string path, string name)
    {
        Sprite sprite = LoadSprites(path).FirstOrDefault(item => item.name == name);
        if (sprite == null)
            throw new InvalidOperationException($"Sprite not found: {path} / {name}");
        return sprite;
    }

    private static Sprite[] LoadSprites(string path)
    {
        return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToArray();
    }

    private static void SetObjectArray(SerializedProperty property, UnityEngine.Object[] values)
    {
        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    private static void DestroyChildIfExists(Transform root, string name)
    {
        Transform child = FindDeep(root, name);
        if (child != null && child != root)
            UnityEngine.Object.DestroyImmediate(child.gameObject);
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject child = new GameObject(name, typeof(RectTransform));
        RectTransform rect = child.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        return rect;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name)
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform result = FindDeep(root.GetChild(i), name);
            if (result != null)
                return result;
        }
        return null;
    }

    private static RectTransform RequireRect(Transform root, string name)
    {
        RectTransform rect = FindDeep(root, name) as RectTransform;
        if (rect == null)
            throw new InvalidOperationException($"RectTransform not found: {name}");
        return rect;
    }

    private static T FindInScene<T>(Scene scene) where T : Component
    {
        return FindAllInScene<T>(scene).FirstOrDefault();
    }

    private static T[] FindAllInScene<T>(Scene scene) where T : Component
    {
        return scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<T>(true))
            .ToArray();
    }
}
