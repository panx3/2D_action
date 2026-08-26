using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// CompletScene / SampleScene の既存 Main Camera と MorningStarLauncher を
/// 地面衝突シェイク用に接続する。CameraFollow の設定値は変更しない。
/// </summary>
public static class MorningStarGroundImpactShakeSetup
{
    private static readonly string[] ScenePaths =
    {
        "Assets/Scenes/CompletScene.unity",
        "Assets/Scenes/SampleScene.unity"
    };

    [MenuItem("Tools/MorningStar/Apply Ground Impact Camera Shake")]
    public static void Apply()
    {
        foreach (string scenePath in ScenePaths)
            ConfigureScene(scenePath);

        AssetDatabase.SaveAssets();
        Debug.Log("[GroundImpactShakeSetup] CompletScene / SampleScene configured without changing CameraFollow settings.");
    }

    public static void ApplyBatch()
    {
        try
        {
            Apply();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            throw;
        }
    }

    private static void ConfigureScene(string scenePath)
    {
        Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        Camera mainCamera = FindMainCamera(scene);
        if (mainCamera == null)
            throw new InvalidOperationException($"[GroundImpactShakeSetup] Main Camera missing: {scenePath}");

        CameraFollow cameraFollow = mainCamera.GetComponent<CameraFollow>();
        if (cameraFollow == null)
            throw new InvalidOperationException($"[GroundImpactShakeSetup] CameraFollow missing: {scenePath}");

        string cameraFollowBefore = EditorJsonUtility.ToJson(cameraFollow);

        CameraShake2D cameraShake = mainCamera.GetComponent<CameraShake2D>();
        if (cameraShake == null)
            cameraShake = mainCamera.gameObject.AddComponent<CameraShake2D>();
        cameraShake.enabled = true;

        int launcherCount = 0;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (MorningStarLauncher launcher in root.GetComponentsInChildren<MorningStarLauncher>(true))
            {
                ConfigureLauncher(launcher, cameraShake);
                launcherCount++;
            }
        }

        if (launcherCount == 0)
            throw new InvalidOperationException($"[GroundImpactShakeSetup] MorningStarLauncher missing: {scenePath}");

        string cameraFollowAfter = EditorJsonUtility.ToJson(cameraFollow);
        if (!string.Equals(cameraFollowBefore, cameraFollowAfter, StringComparison.Ordinal))
            throw new InvalidOperationException($"[GroundImpactShakeSetup] CameraFollow changed unexpectedly: {scenePath}");

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new InvalidOperationException($"[GroundImpactShakeSetup] Could not save scene: {scenePath}");

        Debug.Log($"[GroundImpactShakeSetup] Configured {scenePath} (launchers={launcherCount}).");
    }

    private static Camera FindMainCamera(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
            {
                if (camera.CompareTag("MainCamera"))
                    return camera;
            }
        }

        return null;
    }

    private static void ConfigureLauncher(MorningStarLauncher launcher, CameraShake2D cameraShake)
    {
        SerializedObject serializedLauncher = new SerializedObject(launcher);
        serializedLauncher.FindProperty("cameraShake").objectReferenceValue = cameraShake;
        serializedLauncher.FindProperty("minimumGroundImpactSpeed").floatValue = 7f;
        serializedLauncher.FindProperty("shakeDuration").floatValue = 0.10f;
        serializedLauncher.FindProperty("minimumShakeStrength").floatValue = 0.06f;
        serializedLauncher.FindProperty("maximumShakeStrength").floatValue = 0.16f;
        serializedLauncher.FindProperty("maxImpactSpeed").floatValue = 20f;
        serializedLauncher.FindProperty("shakeCooldown").floatValue = 0.10f;
        serializedLauncher.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(launcher);
    }
}
