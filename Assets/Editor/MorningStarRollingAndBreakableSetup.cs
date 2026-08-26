using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// morningstarの物理Rootと見た目を分離し、ローリング表示の参照を安全に設定する。
/// Scene/Prefabの編集はUnity Editor API経由で行う。
/// </summary>
public static class MorningStarRollingAndBreakableSetup
{
    private const string CompletScenePath = "Assets/Scenes/CompletScene.unity";
    private const string SampleScenePath = "Assets/Scenes/SampleScene.unity";
    private const string BreakableWallPrefabPath = "Assets/Prefabs/Gimmicks/BreakableWall.prefab";

    [MenuItem("Tools/MorningStar/Apply Rolling Visual And Breakable Wall")]
    public static void Apply()
    {
        ConfigureScene(CompletScenePath);
        ConfigureScene(SampleScenePath);
        SaveBreakableWallPrefab();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[MorningStarSetup] Rolling Visual and BreakableWall setup completed.");
    }

    public static void ApplyFromCommandLine()
    {
        Apply();
    }

    private static void ConfigureScene(string scenePath)
    {
        Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        List<Transform> morningStars = FindMorningStars(scene);

        if (morningStars.Count == 0)
            throw new InvalidOperationException($"[MorningStarSetup] morningstar not found: {scenePath}");

        foreach (Transform root in morningStars)
            ConfigureMorningStar(scene, root);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[MorningStarSetup] Configured {morningStars.Count} morningstar object(s): {scenePath}");
    }

    private static void ConfigureMorningStar(Scene scene, Transform root)
    {
        Rigidbody2D body = root.GetComponent<Rigidbody2D>();
        CircleCollider2D circle = root.GetComponent<CircleCollider2D>();
        if (body == null || circle == null)
            throw new InvalidOperationException($"[MorningStarSetup] Rigidbody2D/CircleCollider2D missing: {root.name}");

        Transform visual = root.Find("Visual");
        if (visual == null)
        {
            GameObject visualObject = new GameObject("Visual");
            visualObject.layer = root.gameObject.layer;
            visual = visualObject.transform;
            visual.SetParent(root, false);
            visual.localPosition = Vector3.zero;
            visual.localRotation = Quaternion.identity;
            visual.localScale = Vector3.one;
        }

        SpriteRenderer rootRenderer = root.GetComponent<SpriteRenderer>();
        SpriteRenderer visualRenderer = visual.GetComponent<SpriteRenderer>();
        if (visualRenderer == null)
            visualRenderer = visual.gameObject.AddComponent<SpriteRenderer>();

        if (rootRenderer != null)
        {
            EditorUtility.CopySerialized(rootRenderer, visualRenderer);
            UnityEngine.Object.DestroyImmediate(rootRenderer);
        }

        if (visualRenderer.sprite == null)
            throw new InvalidOperationException($"[MorningStarSetup] SpriteRenderer sprite missing: {scene.path}/{root.name}/Visual");

        MorningStarRollingVisual rolling = root.GetComponent<MorningStarRollingVisual>();
        if (rolling == null)
            rolling = root.gameObject.AddComponent<MorningStarRollingVisual>();

        MorningStarLauncher launcher = FindLauncherForBody(scene, body);
        if (launcher == null)
            throw new InvalidOperationException($"[MorningStarSetup] matching MorningStarLauncher not found: {scene.path}");

        float worldRadius = circle.radius * Mathf.Max(
            Mathf.Abs(root.lossyScale.x),
            Mathf.Abs(root.lossyScale.y));

        SerializedObject rollingObject = new SerializedObject(rolling);
        rollingObject.FindProperty("visual").objectReferenceValue = visual;
        rollingObject.FindProperty("launcher").objectReferenceValue = launcher;
        rollingObject.FindProperty("visualRadius").floatValue = worldRadius;
        rollingObject.FindProperty("rotationStep").floatValue = 30f;
        rollingObject.FindProperty("minimumRollSpeed").floatValue = 0.1f;
        rollingObject.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(rolling);
        EditorUtility.SetDirty(visualRenderer);
        EditorUtility.SetDirty(root.gameObject);
    }

    private static MorningStarLauncher FindLauncherForBody(Scene scene, Rigidbody2D body)
    {
        foreach (MorningStarLauncher launcher in FindComponentsInScene<MorningStarLauncher>(scene))
        {
            SerializedObject launcherObject = new SerializedObject(launcher);
            SerializedProperty bodyProperty = launcherObject.FindProperty("morningStarRb");
            if (bodyProperty != null && bodyProperty.objectReferenceValue == body)
                return launcher;
        }

        return null;
    }

    private static List<Transform> FindMorningStars(Scene scene)
    {
        List<Transform> results = new List<Transform>();
        foreach (Transform transform in FindComponentsInScene<Transform>(scene))
        {
            if (transform.CompareTag("morningstar") && transform.GetComponent<Rigidbody2D>() != null)
                results.Add(transform);
        }

        return results;
    }

    private static List<T> FindComponentsInScene<T>(Scene scene) where T : Component
    {
        List<T> results = new List<T>();
        foreach (GameObject root in scene.GetRootGameObjects())
            results.AddRange(root.GetComponentsInChildren<T>(true));

        return results;
    }

    private static void SaveBreakableWallPrefab()
    {
        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(BreakableWallPrefabPath);
        try
        {
            BreakableWall wall = prefabRoot.GetComponent<BreakableWall>();
            if (wall == null)
                throw new InvalidOperationException("[MorningStarSetup] BreakableWall component missing from prefab.");

            // 新規フィールドの初期値(6)をPrefabへ明示保存する。
            // 以後このツールを再実行してもInspector値は上書きしない。
            EditorUtility.SetDirty(wall);
            PrefabUtility.SaveAsPrefabAsset(prefabRoot, BreakableWallPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }

        Debug.Log($"[MorningStarSetup] Saved existing BreakableWall prefab: {BreakableWallPrefabPath}");
    }
}
