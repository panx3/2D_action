using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class AmbientPetalSetup
{
    private const string Folder = "Assets/Art/ScreenVisuals";

    [MenuItem("鉄球少女/演出/ピンクの花びらをゲームSceneへ接続")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/CompletScene.unity") throw new InvalidOperationException("Open CompletScene first.");
        var c = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<ScreenVisualController>(true)).Single();
        if (c.profile == null) throw new InvalidOperationException("Stage profile missing.");
        string dir = Path.Combine(Path.GetTempPath(), "tekkyu-ambient-petals");
        Directory.CreateDirectory(dir);
        if (!EditorSceneManager.SaveScene(scene, Path.Combine(dir, "CompletScene.pre-install.unity"), true)) throw new IOException("Backup failed.");
        string texturePath = Folder + "/PinkPetalsAtlas.png";
        var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
        if (importer == null) throw new InvalidOperationException("Generated pink petal atlas missing.");
        importer.textureType = TextureImporterType.Default;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Point;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 128;
        importer.SaveAndReimport();
        string path = Folder + "/PinkPetals.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        bool firstInstall = material == null;
        if (firstInstall)
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/ScreenVisualPetals.shader");
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("Petal shader unavailable.");
            material = new Material(shader);
            material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            AssetDatabase.CreateAsset(material, path);
        }
        var so = new SerializedObject(c);
        so.FindProperty("petalMaterial").objectReferenceValue = material;
        so.ApplyModifiedPropertiesWithoutUndo();
        if (c.profile.petals == null) c.profile.petals = new ScreenVisualProfile.PetalSettings();
        if (firstInstall) c.profile.petals.enabled = true;
        EditorUtility.SetDirty(c.profile);
        EditorUtility.SetDirty(c);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Scene save failed.");
        Selection.activeGameObject = c.gameObject;
        Debug.Log("[AmbientPetals] Connected four generated pink petal variants to the existing ScreenVisuals owner.");
    }
}
