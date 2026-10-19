using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;

/// <summary>Explicit, repeatable setup. Never runs automatically or changes existing cursor tuning.</summary>
public static class GameplayPixelCursorSetup
{
    public const string ArrowPath = "Assets/image_/UI/Cursor/PixelArrow.png";
    public const string ChainPath = "Assets/image_/UI/Cursor/PixelChain.png";
    public const string PrefabPath = "Assets/Prefabs/UI/GameplayPixelCursor.prefab";

    public static string Install()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != "Assets/Scenes/CompletScene.unity" || scene.isDirty)
            throw new InvalidOperationException("Open the saved CompletScene in Edit Mode first.");
        if (UnityEngine.Object.FindAnyObjectByType<GameplayPixelCursor>(FindObjectsInactive.Include) != null)
            return "Already installed; existing settings preserved.";

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            ConfigureTexture(ArrowPath, SpriteImportMode.Single);
            ConfigureTexture(ChainPath, SpriteImportMode.Multiple);
            SliceChain();
            prefab = CreatePrefab();
        }
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        Undo.RegisterCreatedObjectUndo(instance, "Add gameplay pixel cursor");
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        return "Saved GameplayPixelCursor prefab and CompletScene. Existing gameplay and UI untouched.";
    }

    private static void ConfigureTexture(string path, SpriteImportMode mode)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = mode;
        importer.spritePixelsPerUnit = 100f;
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.crunchedCompression = false;
        importer.alphaIsTransparency = true;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.wrapMode = TextureWrapMode.Clamp;
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        settings.spriteGenerateFallbackPhysicsShape = false;
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();
    }

    private static void SliceChain()
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(ChainPath);
        var factories = new SpriteDataProviderFactories();
        factories.Init();
        var provider = factories.GetSpriteEditorDataProviderFromObject(importer);
        provider.InitSpriteEditorDataProvider();
        var capability = provider.GetDataProvider<ISpriteFrameEditCapability>();
        if (capability == null || !capability.GetEditCapability().HasCapability(EEditCapability.CreateAndDeleteSprite))
            throw new InvalidOperationException("Importer does not support safe sprite slicing.");
        var rects = new SpriteRect[2];
        var existing = provider.GetSpriteRects();
        for (int i = 0; i < rects.Length; i++)
        {
            string name = "CursorChain" + i;
            var previous = existing.FirstOrDefault(r => r.name == name);
            rects[i] = new SpriteRect
            {
                name = name, rect = new Rect(i * 18, 0, 18, 10),
                alignment = SpriteAlignment.LeftCenter, pivot = new Vector2(0f, 0.5f),
                spriteID = previous != null ? previous.spriteID : GUID.Generate()
            };
        }
        provider.SetSpriteRects(rects);
        var names = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
        if (names == null) throw new InvalidOperationException("Sprite name/ID provider is missing.");
        names.SetNameFileIdPairs(rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)));
        provider.Apply();
        importer.SaveAndReimport();
    }

    private static GameObject CreatePrefab()
    {
        var host = new GameObject("GameplayPixelCursor", typeof(RectTransform), typeof(Canvas));
        try
        {
            Canvas canvas = host.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            canvas.enabled = false;
            // No CanvasScaler or GraphicRaycaster: coordinates stay in actual screen pixels.
            var root = NewRect("CursorRoot", host.transform);
            var chain = NewRect("ChainRoot", root);
            var source = AssetDatabase.LoadAllAssetsAtPath(ChainPath).OfType<Sprite>().OrderBy(s => s.name).ToArray();
            if (source.Length != 2) throw new InvalidOperationException("Expected two chain sprites.");
            var links = new UnityEngine.UI.Image[2];
            for (int i = 0; i < 2; i++)
            {
                links[i] = NewImage("Link" + i, chain, source[i]);
                links[i].rectTransform.pivot = new Vector2(0f, 0.5f);
                links[i].rectTransform.sizeDelta = new Vector2(28.5f, 15f);
                links[i].rectTransform.anchoredPosition = Vector2.down * (27f * i);
                links[i].rectTransform.localRotation = Quaternion.Euler(0f, 0f, -90f);
            }
            // Draw the arrow over the attachment so the root enters the inner notch cleanly.
            var arrow = NewImage("ArrowSprite", root, AssetDatabase.LoadAssetAtPath<Sprite>(ArrowPath));
            arrow.rectTransform.pivot = Vector2.up;
            arrow.rectTransform.sizeDelta = new Vector2(141f, 144f);
            arrow.rectTransform.anchoredPosition = new Vector2(-48f, 42f);
            chain.anchoredPosition = new Vector2(24f, -43.5f);
            var cursor = host.AddComponent<GameplayPixelCursor>();
            var data = new SerializedObject(cursor);
            data.FindProperty("cursorRoot").objectReferenceValue = root;
            data.FindProperty("arrowSprite").objectReferenceValue = arrow;
            data.FindProperty("chainRoot").objectReferenceValue = chain;
            var list = data.FindProperty("chainLinks");
            list.arraySize = 2;
            for (int i = 0; i < 2; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = links[i];
            data.ApplyModifiedPropertiesWithoutUndo();
            return PrefabUtility.SaveAsPrefabAsset(host, PrefabPath);
        }
        finally { UnityEngine.Object.DestroyImmediate(host); }
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
        return rect;
    }

    private static UnityEngine.UI.Image NewImage(string name, Transform parent, Sprite sprite)
    {
        var image = NewRect(name, parent).gameObject.AddComponent<UnityEngine.UI.Image>();
        image.sprite = sprite;
        image.raycastTarget = false;
        return image;
    }
}
