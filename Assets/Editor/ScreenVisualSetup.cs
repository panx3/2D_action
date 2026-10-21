using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ScreenVisualSetup
{
    public const string Folder = "Assets/Art/ScreenVisuals";
    public const string ProfilePath = Folder + "/SkyRuins.asset";

    [MenuItem("鉄球少女/演出/画面演出を現在のゲームSceneへ接続")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/CompletScene.unity") throw new InvalidOperationException("Open CompletScene first.");
        // Preserve in-memory edits as well as disk edits before the first installation.
        string backup = Path.Combine(Path.GetTempPath(), "tekkyu-screen-visuals");
        Directory.CreateDirectory(backup);
        string copy = Path.Combine(backup, "CompletScene.pre-install.unity");
        if (!File.Exists(copy) && !EditorSceneManager.SaveScene(scene, copy, true)) throw new IOException("Scene backup failed.");
        var profile = AssetDatabase.LoadAssetAtPath<ScreenVisualProfile>(ProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<ScreenVisualProfile>();
            profile.layers = new[]
            {
                Layer("SkySea", 1, 1, 0.65f, 0),
                Layer("FloatingIslands", 0.65f, 0.45f, 1, 0.06f),
                Layer("ForeRuins", 0.25f, 0.12f, 0.4f, 0.12f)
            };
            AssetDatabase.CreateAsset(profile, ProfilePath);
        }
        CreateOptionalProfile("MoonlitRuins", new Color(0.65f, 0.74f, 0.94f), new Color(0.88f, 0.92f, 1), 0.97f, profile);
        CreateOptionalProfile("WarmRuins", new Color(0.87f, 0.82f, 0.76f), new Color(1, 0.98f, 0.94f), 1, profile);
        var importer = (TextureImporter)AssetImporter.GetAtPath(Folder + "/RuinsDustAtlas.png");
        if (importer == null) throw new InvalidOperationException("Generated dust atlas missing.");
        importer.textureType = TextureImporterType.Default;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Point;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 512;
        importer.SaveAndReimport();
        Material dust = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/RuinsDust.mat");
        if (dust == null)
        {
            dust = new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ScreenVisualParticles.shader"));
            dust.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/RuinsDustAtlas.png");
            AssetDatabase.CreateAsset(dust, Folder + "/RuinsDust.mat");
        }
        var all = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Transform>(true)).ToArray();
        ScreenVisualController controller = all.Select(t => t.GetComponent<ScreenVisualController>()).FirstOrDefault(x => x != null);
        if (controller == null)
        {
            GameObject root = new GameObject("ScreenVisuals");
            SceneManager.MoveGameObjectToScene(root, scene);
            controller = root.AddComponent<ScreenVisualController>();
            controller.profile = profile;
        }
        var so = new SerializedObject(controller);
        so.FindProperty("targetCamera").objectReferenceValue = all.Select(t => t.GetComponent<Camera>()).FirstOrDefault(x => x != null && x.CompareTag("MainCamera"));
        so.FindProperty("spriteLightingShader").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Shader>("Assets/ScreenVisualSprite.shader");
        so.FindProperty("vignetteShader").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Shader>("Assets/ScreenVisualVignette.shader");
        so.FindProperty("particleMaterial").objectReferenceValue = dust;
        so.ApplyModifiedPropertiesWithoutUndo();
        // The existing crystal is an actual light source. Do not turn the traversal magnet into a beacon.
        GoalPoint goal = all.Select(t => t.GetComponent<GoalPoint>()).FirstOrDefault(x => x != null);
        if (goal != null)
        {
            var light = goal.GetComponent<ScreenLocalLight2D>();
            if (light == null)
            {
                light = goal.gameObject.AddComponent<ScreenLocalLight2D>();
                light.color = new Color(0.73f, 0.66f, 1);
                light.intensity = 0.25f; light.radius = 4.5f; light.falloff = 1.7f;
                light.flickerEnabled = true; light.flickerAmount = 0.035f; light.flickerSpeed = 0.8f;
                light.targetLayers = ~LayerMask.GetMask("UI");
            }
            if (light.emissionSource == null) light.emissionSource = goal.GetComponentInChildren<SpriteRenderer>();
            PrefabUtility.RecordPrefabInstancePropertyModifications(light);
        }
        EditorUtility.SetDirty(controller);
        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Scene save failed.");
        Selection.activeGameObject = controller.gameObject;
        Debug.Log("[ScreenVisuals] Connected SkyRuins profile, background depth/blur/fog, dust, vignette and crystal surface light. Existing values retained.");
    }

    private static ScreenVisualProfile.LayerSettings Layer(string name, float depth, float blur, float fog, float lighting) =>
        new ScreenVisualProfile.LayerSettings { layerName = name, depthFactor = depth, blurStrength = blur, fogInfluence = fog, lightingInfluence = lighting };

    private static void CreateOptionalProfile(string name, Color atmosphere, Color ambient, float intensity, ScreenVisualProfile source)
    {
        string path = Folder + "/" + name + ".asset";
        if (AssetDatabase.LoadAssetAtPath<ScreenVisualProfile>(path) != null) return;
        var p = UnityEngine.Object.Instantiate(source);
        p.name = name;
        p.atmosphere.atmosphereColor = atmosphere;
        p.atmosphere.fogColor = atmosphere;
        p.lighting.ambientColor = ambient;
        p.lighting.ambientIntensity = intensity;
        AssetDatabase.CreateAsset(p, path);
    }
}

[CustomEditor(typeof(ScreenVisualController))]
public sealed class ScreenVisualControllerEditor : Editor
{
    private Editor profileEditor;
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var controller = (ScreenVisualController)target;
        if (controller.profile == null) return;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Stage settings (shared profile)", EditorStyles.boldLabel);
        CreateCachedEditor(controller.profile, null, ref profileEditor);
        profileEditor.OnInspectorGUI();
        if (Application.isPlaying && GUILayout.Button("Refresh spawned renderer bindings")) controller.RefreshBindings();
    }
    private void OnDisable() { if (profileEditor != null) DestroyImmediate(profileEditor); }
}
