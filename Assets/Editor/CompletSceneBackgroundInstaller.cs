using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CompletSceneBackgroundInstaller
{
    private const string ScenePath = "Assets/Scenes/CompletScene.unity";
    private const string FarPath = "Assets/image_/背景_一番後ろ.png";
    private const string MidPath = "Assets/image_/背景_真ん中.png";
    private const string FrontPath = "Assets/image_/背景_一番手前.png";

    [MenuItem("鉄球少女/CompletScene/3層背景を追加")]
    public static void Install()
    {
        ConfigureTexture(FarPath);
        ConfigureTexture(MidPath);
        ConfigureTexture(FrontPath);
        AssetDatabase.Refresh();

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Camera mainCamera = FindRoot(scene, "Main Camera").GetComponent<Camera>();
        if (mainCamera == null)
            throw new InvalidOperationException("CompletSceneのMain Cameraが見つかりません。");

        GameObject existing = TryFindRoot(scene, "BackgroundRoot");
        if (existing != null)
            UnityEngine.Object.DestroyImmediate(existing);

        GameObject root = new GameObject("BackgroundRoot");
        CreateLayer(root.transform, "BG_Far", "SkySea", FarPath, mainCamera, 0f, Vector2.zero, 1.03f, -1000, 1f, 8f);
        CreateLayer(root.transform, "BG_Mid", "FloatingIslands", MidPath, mainCamera, 0.14f, new Vector2(0f, 0.45f), 1.16f, -900, 0.72f, 6f);
        CreateLayer(root.transform, "BG_Front", "ForeRuins", FrontPath, mainCamera, 0.24f, new Vector2(0f, -0.55f), 1.22f, -800, 0.82f, 4f);

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, ScenePath))
            throw new InvalidOperationException("背景追加後のCompletSceneを保存できませんでした。");

        Debug.Log("[CompletBackground] 3層背景を追加しました。Far=0.00 / Mid=0.14 / Front=0.24");
    }

    private static void ConfigureTexture(string path)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
            throw new InvalidOperationException(path + " が見つからないか、Textureとして取り込めません。");

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 100f;
        TextureImporterSettings textureSettings = new TextureImporterSettings();
        importer.ReadTextureSettings(textureSettings);
        textureSettings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(textureSettings);
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.filterMode = FilterMode.Point;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 4096;
        importer.SaveAndReimport();
    }

    private static void CreateLayer(
        Transform root,
        string groupName,
        string spriteName,
        string spritePath,
        Camera camera,
        float parallax,
        Vector2 offset,
        float padding,
        int sortingOrder,
        float alpha,
        float z)
    {
        GameObject group = new GameObject(groupName);
        group.transform.SetParent(root, false);

        GameObject image = new GameObject(spriteName);
        image.transform.SetParent(group.transform, false);
        image.transform.position = new Vector3(camera.transform.position.x, camera.transform.position.y, z);

        SpriteRenderer renderer = image.AddComponent<SpriteRenderer>();
        renderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
        if (renderer.sprite == null)
            throw new InvalidOperationException(spritePath + " のSpriteを読み込めませんでした。");
        renderer.sortingOrder = sortingOrder;
        renderer.color = new Color(1f, 1f, 1f, alpha);

        ParallaxBackgroundLayer layer = image.AddComponent<ParallaxBackgroundLayer>();
        SerializedObject serialized = new SerializedObject(layer);
        serialized.FindProperty("targetCamera").objectReferenceValue = camera;
        serialized.FindProperty("parallaxFactor").floatValue = parallax;
        serialized.FindProperty("screenOffset").vector2Value = offset;
        serialized.FindProperty("coverPadding").floatValue = padding;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static GameObject FindRoot(Scene scene, string name)
    {
        GameObject found = TryFindRoot(scene, name);
        if (found == null)
            throw new InvalidOperationException(scene.path + " に " + name + " がありません。");
        return found;
    }

    private static GameObject TryFindRoot(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root.name == name)
                return root;
        return null;
    }
}
