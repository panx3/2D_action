using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

public static class GameplayFeatureIntegrator
{
    public const string RequestPath = "Temp/GameplayFeatureIntegration.request";

    private const string LauncherTexturePath = "Assets/image_/ランチャーポーズAni.png";
    private const string LauncherClipPath = "Assets/Animation/MorningStarLaunch.anim";
    private const string PlayerControllerPath = "Assets/Animation/Player.controller";
    private const string GroundTexturePath = "Assets/image_/ステージ地面素材めっちゃ石.png";
    private const string StageTexturePath = "Assets/image_/ステージテクスチャ素材.png";
    private const string PalettePath = "Assets/Pixel Adventure 1/Assets/Terrain/New Palette.prefab";
    private const string TileFolderPath = "Assets/Tiles/CustomGround";
    private const string CompletScenePath = "Assets/Scenes/CompletScene.unity";

    [MenuItem("鉄球少女/Gameplay/5項目のAsset設定を適用")]
    public static void ApplyAll()
    {
        ConfigureGroundTextures();
        IReadOnlyList<Tile> groundTiles = CreateGroundTiles();
        UpdateExistingGroundPalette(groundTiles);
        ConfigureGroundTilemapCollider();

        bool launcherConfigured = ConfigureLauncherAnimation();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string launcherResult = launcherConfigured
            ? "MorningStarLaunch animation configured"
            : $"launcher texture not found; waiting for {LauncherTexturePath}";
        Debug.Log($"[GameplayFeatureIntegrator] Complete: {launcherResult}");
    }

    [MenuItem("鉄球少女/Gameplay/Launcher Animationのみ再設定")]
    public static void ApplyLauncherAnimationOnly()
    {
        if (!ConfigureLauncherAnimation())
            throw new FileNotFoundException("Launcher texture not found.", LauncherTexturePath);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[GameplayFeatureIntegrator] MorningStarLaunch animation configured.");
    }

    private static void ConfigureGroundTextures()
    {
        ConfigureMultipleSpriteTexture(GroundTexturePath, 80f, CreateStoneGroundRects());
        ConfigureMultipleSpriteTexture(StageTexturePath, 32f, CreateStageTextureRects());
    }

    private static List<SpriteRect> CreateStoneGroundRects()
    {
        // Source内の透明な縦区切りを実測して3列へ分割。
        int[] x = { 4, 85, 173 };
        int[] widths = { 76, 83, 80 };
        int[] y = { 165, 88, 13 };
        int[] heights = { 76, 75, 75 };
        List<SpriteRect> rects = new List<SpriteRect>(9);

        int index = 0;
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                rects.Add(CreateSpriteRect(
                    $"ステージ地面素材めっちゃ石_{index++}",
                    new Rect(x[column], y[row], widths[column], heights[row])));
            }
        }

        return rects;
    }

    private static List<SpriteRect> CreateStageTextureRects()
    {
        // 上端2段は実測幅316px内の10個の地面セル。
        // 残りは種類別の装飾ストリップとして分離し、Tile/Colliderは生成しない。
        const float contentLeft = 4f;
        const float contentWidth = 316f;
        const int columns = 10;
        List<SpriteRect> rects = new List<SpriteRect>(26);

        int index = 0;
        for (int row = 0; row < 2; row++)
        {
            float bottom = row == 0 ? 211f : 181f;
            float height = row == 0 ? 29f : 30f;
            for (int column = 0; column < columns; column++)
            {
                float left = contentLeft + Mathf.Round(contentWidth * column / columns);
                float right = contentLeft + Mathf.Round(contentWidth * (column + 1) / columns);
                rects.Add(CreateSpriteRect(
                    $"ステージテクスチャ素材_{index++}",
                    new Rect(left, bottom, right - left, height)));
            }
        }

        Rect[] decorationRows =
        {
            new Rect(4f, 151f, 316f, 30f),
            new Rect(4f, 121f, 316f, 30f),
            new Rect(4f, 91f, 316f, 30f),
            new Rect(4f, 61f, 316f, 30f),
            new Rect(4f, 31f, 316f, 30f),
            new Rect(4f, 9f, 316f, 22f)
        };

        for (int i = 0; i < decorationRows.Length; i++)
        {
            rects.Add(CreateSpriteRect(
                $"ステージテクスチャ素材_Decoration_{i}",
                decorationRows[i]));
        }

        return rects;
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

    private static SpriteRect CreateSpriteRect(string name, Rect rect, Vector2 pivot)
    {
        SpriteRect spriteRect = CreateSpriteRect(name, rect);
        spriteRect.alignment = SpriteAlignment.Custom;
        spriteRect.pivot = pivot;
        return spriteRect;
    }

    private static void ConfigureMultipleSpriteTexture(
        string assetPath,
        float pixelsPerUnit,
        List<SpriteRect> requestedRects)
    {
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
            throw new FileNotFoundException("Sprite texture not found.", assetPath);

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = pixelsPerUnit;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;

        ClearPlatformOverride(importer, "Standalone");
        ClearPlatformOverride(importer, "WebGL");
        ClearPlatformOverride(importer, "Android");
        ClearPlatformOverride(importer, "iPhone");

        SpriteDataProviderFactories factories = new SpriteDataProviderFactories();
        factories.Init();
        ISpriteEditorDataProvider provider = factories.GetSpriteEditorDataProviderFromObject(importer);
        provider.InitSpriteEditorDataProvider();

        Dictionary<string, GUID> existingIds = provider.GetSpriteRects()
            .Where(rect => !string.IsNullOrWhiteSpace(rect.name))
            .GroupBy(rect => rect.name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().spriteID, StringComparer.Ordinal);

        foreach (SpriteRect rect in requestedRects)
        {
            if (existingIds.TryGetValue(rect.name, out GUID existingId))
                rect.spriteID = existingId;
        }

        provider.SetSpriteRects(requestedRects.ToArray());
        ISpriteNameFileIdDataProvider nameProvider =
            provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
        nameProvider.SetNameFileIdPairs(requestedRects
            .Select(rect => new SpriteNameFileIdPair(rect.name, rect.spriteID))
            .ToArray());
        provider.Apply();
        importer.SaveAndReimport();
    }

    private static void ClearPlatformOverride(TextureImporter importer, string platformName)
    {
        TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(platformName);
        if (!settings.overridden)
            return;

        settings.overridden = false;
        importer.SetPlatformTextureSettings(settings);
    }

    private static IReadOnlyList<Tile> CreateGroundTiles()
    {
        EnsureFolder(TileFolderPath);

        List<Sprite> stoneSprites = AssetDatabase.LoadAllAssetsAtPath(GroundTexturePath)
            .OfType<Sprite>()
            .OrderBy(sprite => sprite.name, StringComparer.Ordinal)
            .ToList();
        List<Sprite> stageGroundSprites = AssetDatabase.LoadAllAssetsAtPath(StageTexturePath)
            .OfType<Sprite>()
            .Where(sprite => !sprite.name.Contains("Decoration", StringComparison.Ordinal))
            .OrderBy(sprite => ParseTrailingIndex(sprite.name))
            .ToList();

        if (stoneSprites.Count != 9 || stageGroundSprites.Count != 20)
            throw new InvalidOperationException(
                $"Ground sprite slicing failed. stone={stoneSprites.Count}, stage={stageGroundSprites.Count}");

        List<Tile> tiles = new List<Tile>(29);
        for (int i = 0; i < stoneSprites.Count; i++)
        {
            string path = i == 0
                ? "Assets/ステージ地面素材めっちゃ石_0.asset"
                : $"{TileFolderPath}/StoneGround_{i}.asset";
            tiles.Add(CreateOrUpdateTile(path, stoneSprites[i], Tile.ColliderType.Grid));
        }

        for (int i = 0; i < stageGroundSprites.Count; i++)
        {
            string path = i == 0
                ? "Assets/ステージテクスチャ素材_0.asset"
                : $"{TileFolderPath}/StageGround_{i}.asset";
            tiles.Add(CreateOrUpdateTile(path, stageGroundSprites[i], Tile.ColliderType.Grid));
        }

        return tiles;
    }

    private static int ParseTrailingIndex(string name)
    {
        int separator = name.LastIndexOf('_');
        return separator >= 0 && int.TryParse(name.Substring(separator + 1), out int value)
            ? value
            : int.MaxValue;
    }

    private static Tile CreateOrUpdateTile(
        string assetPath,
        Sprite sprite,
        Tile.ColliderType colliderType)
    {
        Tile tile = AssetDatabase.LoadAssetAtPath<Tile>(assetPath);
        if (tile == null)
        {
            tile = ScriptableObject.CreateInstance<Tile>();
            AssetDatabase.CreateAsset(tile, assetPath);
        }

        tile.name = Path.GetFileNameWithoutExtension(assetPath);
        tile.sprite = sprite;
        tile.color = Color.white;
        tile.transform = Matrix4x4.identity;
        tile.flags = TileFlags.LockColor | TileFlags.LockTransform;
        tile.colliderType = colliderType;
        EditorUtility.SetDirty(tile);
        return tile;
    }

    private static void UpdateExistingGroundPalette(IReadOnlyList<Tile> tiles)
    {
        if (!File.Exists(PalettePath))
            throw new FileNotFoundException("Existing ground palette not found.", PalettePath);

        GameObject root = PrefabUtility.LoadPrefabContents(PalettePath);
        try
        {
            Grid grid = root.GetComponentInChildren<Grid>(true);
            Tilemap tilemap = root.GetComponentInChildren<Tilemap>(true);
            if (grid == null || tilemap == null)
                throw new InvalidOperationException("Existing palette has no Grid/Tilemap.");

            grid.cellSize = Vector3.one;
            tilemap.ClearAllTiles();
            for (int i = 0; i < tiles.Count; i++)
                tilemap.SetTile(new Vector3Int(i % 10, -(i / 10), 0), tiles[i]);
            tilemap.CompressBounds();
            PrefabUtility.SaveAsPrefabAsset(root, PalettePath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ConfigureGroundTilemapCollider()
    {
        Scene scene = SceneManager.GetSceneByPath(CompletScenePath);
        bool openedHere = !scene.IsValid() || !scene.isLoaded;
        if (openedHere)
            scene = EditorSceneManager.OpenScene(CompletScenePath, OpenSceneMode.Additive);

        try
        {
            Tilemap ground = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Tilemap>(true))
                .FirstOrDefault(tilemap => tilemap.CompareTag("Floor"))
                ?? scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<Tilemap>(true))
                    .FirstOrDefault(tilemap => tilemap.name == "Tilemap");
            if (ground == null)
                throw new InvalidOperationException("CompletScene ground Tilemap not found.");

            TilemapCollider2D tilemapCollider = ground.GetComponent<TilemapCollider2D>();
            if (tilemapCollider == null)
                tilemapCollider = ground.gameObject.AddComponent<TilemapCollider2D>();

            Rigidbody2D body = ground.GetComponent<Rigidbody2D>();
            if (body == null)
                body = ground.gameObject.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Static;
            body.simulated = true;

            CompositeCollider2D composite = ground.GetComponent<CompositeCollider2D>();
            if (composite == null)
                composite = ground.gameObject.AddComponent<CompositeCollider2D>();
            composite.geometryType = CompositeCollider2D.GeometryType.Polygons;

            SerializedObject colliderObject = new SerializedObject(tilemapCollider);
            SerializedProperty compositeOperation = colliderObject.FindProperty("m_CompositeOperation");
            if (compositeOperation != null)
                compositeOperation.intValue = 1;
            colliderObject.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, CompletScenePath);
        }
        finally
        {
            if (openedHere && scene.IsValid() && scene.isLoaded)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static bool ConfigureLauncherAnimation()
    {
        if (!File.Exists(LauncherTexturePath))
            return false;

        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(LauncherTexturePath);
        if (texture == null || texture.width != 302 || texture.height != 148)
        {
            throw new InvalidOperationException(
                $"Launcher texture must be 302x148: {LauncherTexturePath}");
        }

        List<SpriteRect> rects = new List<SpriteRect>
        {
            // Idleの足裏位置（Pivotから下64px）と左右の足の中点を基準に合わせる。
            CreateSpriteRect(
                "ランチャーポーズAni_0",
                new Rect(0f, 0f, 151f, 148f),
                new Vector2(82f / 151f, 64f / 148f)),
            CreateSpriteRect(
                "ランチャーポーズAni_1",
                new Rect(151f, 0f, 151f, 148f),
                new Vector2(58.25f / 151f, 64f / 148f))
        };
        ConfigureMultipleSpriteTexture(LauncherTexturePath, 32f, rects);

        Sprite[] frames = AssetDatabase.LoadAllAssetsAtPath(LauncherTexturePath)
            .OfType<Sprite>()
            .OrderBy(sprite => sprite.name, StringComparer.Ordinal)
            .ToArray();
        if (frames.Length != 2)
            throw new InvalidOperationException($"Launcher sprite count must be 2, actual={frames.Length}");

        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(LauncherClipPath);
        if (clip == null)
        {
            clip = new AnimationClip { name = "MorningStarLaunch" };
            AssetDatabase.CreateAsset(clip, LauncherClipPath);
        }

        clip.frameRate = 100f;
        clip.wrapMode = WrapMode.Once;
        EditorCurveBinding binding = EditorCurveBinding.PPtrCurve(
            string.Empty,
            typeof(SpriteRenderer),
            "m_Sprite");
        ObjectReferenceKeyframe[] keyframes =
        {
            new ObjectReferenceKeyframe { time = 0f, value = frames[0] },
            new ObjectReferenceKeyframe { time = 0.06f, value = frames[1] },
            new ObjectReferenceKeyframe { time = 0.16f, value = frames[1] }
        };
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keyframes);
        SerializedObject clipObject = new SerializedObject(clip);
        SerializedProperty loopTime = clipObject.FindProperty("m_AnimationClipSettings.m_LoopTime");
        if (loopTime != null)
            loopTime.boolValue = false;
        clipObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(clip);

        ConfigureLauncherAnimatorLayer(clip);
        return true;
    }

    private static void ConfigureLauncherAnimatorLayer(AnimationClip clip)
    {
        AnimatorController controller =
            AssetDatabase.LoadAssetAtPath<AnimatorController>(PlayerControllerPath);
        if (controller == null)
            throw new FileNotFoundException("Player AnimatorController not found.", PlayerControllerPath);

        if (!controller.parameters.Any(parameter =>
                parameter.name == "LaunchFire"
                && parameter.type == AnimatorControllerParameterType.Trigger))
        {
            controller.AddParameter("LaunchFire", AnimatorControllerParameterType.Trigger);
        }

        if (!controller.parameters.Any(parameter =>
                parameter.name == "LaunchPoseActive"
                && parameter.type == AnimatorControllerParameterType.Bool))
        {
            controller.AddParameter("LaunchPoseActive", AnimatorControllerParameterType.Bool);
        }

        const string layerName = "Launcher Pose";
        int layerIndex = Array.FindIndex(controller.layers, layer => layer.name == layerName);
        if (layerIndex < 0)
        {
            controller.AddLayer(layerName);
            layerIndex = controller.layers.Length - 1;
        }

        AnimatorControllerLayer[] layers = controller.layers;
        AnimatorControllerLayer layer = layers[layerIndex];
        layer.defaultWeight = 1f;
        layer.blendingMode = AnimatorLayerBlendingMode.Override;
        AnimatorStateMachine stateMachine = layer.stateMachine;

        AnimatorState empty = stateMachine.states
            .Select(child => child.state)
            .FirstOrDefault(state => state.name == "Locomotion Passthrough")
            ?? stateMachine.AddState("Locomotion Passthrough", new Vector3(250f, 70f));
        empty.motion = null;

        AnimatorState launch = stateMachine.states
            .Select(child => child.state)
            .FirstOrDefault(state => state.name == "MorningStarLaunch")
            ?? stateMachine.AddState("MorningStarLaunch", new Vector3(500f, 70f));
        launch.motion = clip;
        launch.speed = 1f;
        stateMachine.defaultState = empty;

        foreach (AnimatorStateTransition transition in empty.transitions.ToArray())
            empty.RemoveTransition(transition);
        foreach (AnimatorStateTransition transition in launch.transitions.ToArray())
            launch.RemoveTransition(transition);
        foreach (AnimatorStateTransition transition in stateMachine.anyStateTransitions
                     .Where(transition => transition.destinationState == launch)
                     .ToArray())
        {
            stateMachine.RemoveAnyStateTransition(transition);
        }

        AnimatorStateTransition enter = stateMachine.AddAnyStateTransition(launch);
        enter.hasExitTime = false;
        enter.duration = 0f;
        enter.canTransitionToSelf = false;
        enter.AddCondition(AnimatorConditionMode.If, 0f, "LaunchFire");

        AnimatorStateTransition exit = launch.AddTransition(empty);
        exit.hasExitTime = false;
        exit.duration = 0f;
        exit.canTransitionToSelf = false;
        exit.AddCondition(AnimatorConditionMode.IfNot, 0f, "LaunchPoseActive");

        // controller.layers getterはコピーを返すため、変更したLayerを必ず戻す。
        layers[layerIndex] = layer;
        controller.layers = layers;

        EditorUtility.SetDirty(stateMachine);
        EditorUtility.SetDirty(controller);
    }

    private static void EnsureFolder(string folderPath)
    {
        string[] parts = folderPath.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}

[InitializeOnLoad]
public static class GameplayFeatureIntegrationRequestRunner
{
    static GameplayFeatureIntegrationRequestRunner()
    {
        EditorApplication.update += TryRun;
    }

    private static void TryRun()
    {
        string requestPath = Path.GetFullPath(GameplayFeatureIntegrator.RequestPath);
        if (!File.Exists(requestPath))
        {
            EditorApplication.update -= TryRun;
            return;
        }

        if (EditorApplication.isCompiling
            || EditorApplication.isUpdating
            || EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        EditorApplication.update -= TryRun;

        try
        {
            GameplayFeatureIntegrator.ApplyAll();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
        finally
        {
            if (File.Exists(requestPath))
                File.Delete(requestPath);
        }
    }
}
