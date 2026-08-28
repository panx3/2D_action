using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Player用AudioSourceと3つのAudioClip参照をPrefab/CompletSceneへ設定する。
/// </summary>
public static class PlayerSfxSetup
{
    private const string JumpClipPath = "Assets/Audio/SFX/jump_realistic.wav";
    private const string LaunchClipPath = "Assets/Audio/SFX/tekkyu_launch.wav";
    private const string FootstepClipPath = "Assets/Audio/SFX/footstep_grass.wav";
    private const string CompletScenePath = "Assets/Scenes/CompletScene.unity";

    [MenuItem("Tools/Player/Apply Player SFX")]
    public static void Apply()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        AudioClip jumpClip = LoadClip(JumpClipPath);
        AudioClip launchClip = LoadClip(LaunchClipPath);
        AudioClip footstepClip = LoadClip(FootstepClipPath);

        ConfigurePrefab("Assets/Player 1.prefab", jumpClip, launchClip, footstepClip);
        ConfigurePrefab("Assets/Player.prefab", jumpClip, launchClip, footstepClip);
        ConfigureCompletScene(jumpClip, launchClip, footstepClip);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[PlayerSfxSetup] Player SFX setup completed.");
    }

    public static void ApplyFromCommandLine()
    {
        Apply();
    }

    public static void ValidateFromCommandLine()
    {
        AudioClip jumpClip = LoadClip(JumpClipPath);
        AudioClip launchClip = LoadClip(LaunchClipPath);
        AudioClip footstepClip = LoadClip(FootstepClipPath);

        ValidatePrefab("Assets/Player 1.prefab", jumpClip, launchClip, footstepClip);
        ValidatePrefab("Assets/Player.prefab", jumpClip, launchClip, footstepClip);
        ValidateScene(CompletScenePath, jumpClip, launchClip, footstepClip);
        ValidateScene("Assets/Scenes/SampleScene.unity", jumpClip, launchClip, footstepClip);
        Debug.Log("[PlayerSfxValidation] Prefabs, CompletScene and SampleScene references are valid.");
    }

    private static AudioClip LoadClip(string path)
    {
        AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        if (clip == null)
            throw new InvalidOperationException($"[PlayerSfxSetup] AudioClip could not be loaded: {path}");
        return clip;
    }

    private static void ConfigurePrefab(
        string prefabPath,
        AudioClip jumpClip,
        AudioClip launchClip,
        AudioClip footstepClip)
    {
        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            Player player = prefabRoot.GetComponentInChildren<Player>(true);
            if (player == null)
                throw new InvalidOperationException($"[PlayerSfxSetup] Player missing: {prefabPath}");

            ConfigurePlayer(player, jumpClip, launchClip, footstepClip);
            PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }

        Debug.Log($"[PlayerSfxSetup] Configured prefab: {prefabPath}");
    }

    private static void ValidatePrefab(
        string prefabPath,
        AudioClip jumpClip,
        AudioClip launchClip,
        AudioClip footstepClip)
    {
        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            Player player = prefabRoot.GetComponentInChildren<Player>(true);
            ValidatePlayer(player, jumpClip, launchClip, footstepClip, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    private static void ConfigureCompletScene(
        AudioClip jumpClip,
        AudioClip launchClip,
        AudioClip footstepClip)
    {
        Scene scene = EditorSceneManager.OpenScene(CompletScenePath, OpenSceneMode.Single);
        Player player = FindPlayer(scene);
        if (player == null)
            throw new InvalidOperationException("[PlayerSfxSetup] Player missing from CompletScene.");

        ConfigurePlayer(player, jumpClip, launchClip, footstepClip);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[PlayerSfxSetup] Configured scene: {CompletScenePath}");
    }

    private static void ValidateScene(
        string scenePath,
        AudioClip jumpClip,
        AudioClip launchClip,
        AudioClip footstepClip)
    {
        Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        Player player = FindPlayer(scene);
        ValidatePlayer(player, jumpClip, launchClip, footstepClip, scenePath);
    }

    private static void ValidatePlayer(
        Player player,
        AudioClip jumpClip,
        AudioClip launchClip,
        AudioClip footstepClip,
        string ownerPath)
    {
        if (player == null)
            throw new InvalidOperationException($"[PlayerSfxValidation] Player missing: {ownerPath}");

        AudioSource sfxSource = player.transform.Find("SfxAudioSource")?.GetComponent<AudioSource>();
        AudioSource footstepSource = player.transform.Find("FootstepAudioSource")?.GetComponent<AudioSource>();
        MorningStarLauncher launcher = player.GetComponent<MorningStarLauncher>();
        if (sfxSource == null || footstepSource == null || launcher == null)
            throw new InvalidOperationException($"[PlayerSfxValidation] AudioSource/Launcher missing: {ownerPath}");

        SerializedObject playerObject = new SerializedObject(player);
        SerializedObject launcherObject = new SerializedObject(launcher);
        bool valid = playerObject.FindProperty("_sfxAudioSource").objectReferenceValue == sfxSource
            && playerObject.FindProperty("_footstepAudioSource").objectReferenceValue == footstepSource
            && playerObject.FindProperty("_jumpClip").objectReferenceValue == jumpClip
            && playerObject.FindProperty("_footstepGrassClip").objectReferenceValue == footstepClip
            && launcherObject.FindProperty("sfxAudioSource").objectReferenceValue == sfxSource
            && launcherObject.FindProperty("morningStarLaunchClip").objectReferenceValue == launchClip
            && !sfxSource.loop
            && !sfxSource.playOnAwake
            && footstepSource.clip == footstepClip
            && footstepSource.loop
            && !footstepSource.playOnAwake;

        if (!valid)
            throw new InvalidOperationException($"[PlayerSfxValidation] SFX reference/settings mismatch: {ownerPath}");
    }

    private static Player FindPlayer(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Player player = root.GetComponentInChildren<Player>(true);
            if (player != null)
                return player;
        }

        return null;
    }

    private static void ConfigurePlayer(
        Player player,
        AudioClip jumpClip,
        AudioClip launchClip,
        AudioClip footstepClip)
    {
        AudioSource sfxSource = EnsureAudioSource(player.transform, "SfxAudioSource");
        sfxSource.clip = null;
        sfxSource.loop = false;
        sfxSource.playOnAwake = false;
        sfxSource.volume = 1f;
        sfxSource.spatialBlend = 0f;

        AudioSource footstepSource = EnsureAudioSource(player.transform, "FootstepAudioSource");
        footstepSource.clip = footstepClip;
        footstepSource.loop = true;
        footstepSource.playOnAwake = false;
        footstepSource.volume = 0.55f;
        footstepSource.spatialBlend = 0f;

        PlayerHealth health = player.GetComponent<PlayerHealth>();
        SerializedObject playerObject = new SerializedObject(player);
        SetObjectReference(playerObject, "_sfxAudioSource", sfxSource);
        SetObjectReference(playerObject, "_footstepAudioSource", footstepSource);
        SetObjectReference(playerObject, "_jumpClip", jumpClip);
        SetObjectReference(playerObject, "_footstepGrassClip", footstepClip);
        SetObjectReference(playerObject, "_playerHealth", health);
        playerObject.FindProperty("_jumpVolume").floatValue = 1f;
        playerObject.FindProperty("_footstepVolume").floatValue = 0.39f;
        playerObject.FindProperty("_footstepMinHorizontalSpeed").floatValue = 0.1f;
        playerObject.ApplyModifiedPropertiesWithoutUndo();

        MorningStarLauncher launcher = player.GetComponent<MorningStarLauncher>();
        if (launcher == null)
            throw new InvalidOperationException($"[PlayerSfxSetup] MorningStarLauncher missing: {player.name}");

        SerializedObject launcherObject = new SerializedObject(launcher);
        SetObjectReference(launcherObject, "sfxAudioSource", sfxSource);
        SetObjectReference(launcherObject, "morningStarLaunchClip", launchClip);
        launcherObject.FindProperty("morningStarLaunchVolume").floatValue = 0.55f;
        launcherObject.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(sfxSource);
        EditorUtility.SetDirty(footstepSource);
        EditorUtility.SetDirty(player);
        EditorUtility.SetDirty(launcher);
    }

    private static AudioSource EnsureAudioSource(Transform playerRoot, string childName)
    {
        Transform child = playerRoot.Find(childName);
        if (child == null)
        {
            GameObject childObject = new GameObject(childName);
            childObject.layer = playerRoot.gameObject.layer;
            child = childObject.transform;
            child.SetParent(playerRoot, false);
        }

        AudioSource source = child.GetComponent<AudioSource>();
        if (source == null)
            source = child.gameObject.AddComponent<AudioSource>();
        return source;
    }

    private static void SetObjectReference(SerializedObject serializedObject, string propertyName, UnityEngine.Object value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null)
            throw new InvalidOperationException($"[PlayerSfxSetup] Serialized property missing: {propertyName}");
        property.objectReferenceValue = value;
    }
}
