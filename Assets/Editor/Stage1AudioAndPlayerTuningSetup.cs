using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class Stage1AudioAndPlayerTuningSetup
{
    private const string CompletScenePath = "Assets/Scenes/CompletScene.unity";
    private const string BgmPath = "Assets/Audio/BGM/tekkyu_shojo_stage1_theme_v4_european_fantasy.wav";
    private const string JumpPath = "Assets/Audio/SFX/jump_realistic.wav";
    private const string LaunchPath = "Assets/Audio/SFX/tekkyu_launch.wav";
    private const string FootstepPath = "Assets/Audio/SFX/footstep_grass.wav";
    private const string RunningPath = "Assets/Audio/SFX/Imported/走る.mp3";
    private const string EnemyHitPath = "Assets/Audio/SFX/Imported/ロボットを強く殴る2.mp3";
    private const string ImpactPath = "Assets/Audio/SFX/Imported/打撃6.mp3";
    private const string LandingPath = "Assets/Audio/SFX/Imported/ジャンプの着地.mp3";

    private static readonly string[] VoicePaths =
    {
        "Assets/Audio/SFX/Voice/yo_03.wav",
        "Assets/Audio/SFX/Voice/ei_03.wav",
        "Assets/Audio/SFX/Voice/fun_01.wav",
        "Assets/Audio/SFX/Voice/ho_01.wav"
    };

    private sealed class AudioAssets
    {
        public AudioClip Bgm;
        public AudioClip Jump;
        public AudioClip Launch;
        public AudioClip Footstep;
        public AudioClip Running;
        public AudioClip EnemyHit;
        public AudioClip Impact;
        public AudioClip Landing;
        public AudioClip[] Voices;
    }

    [MenuItem("Tools/Stage1/Apply Audio And Player Tuning")]
    public static void Apply()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        AudioAssets assets = LoadAudioAssets();

        ConfigurePlayerPrefab("Assets/Player.prefab", assets);
        ConfigurePlayerPrefab("Assets/Player 1.prefab", assets);
        ConfigureReceiverPrefab<EnemyHealth>("Assets/Prefabs/Enemy.prefab", "hitAudioSource", "morningStarHitClip", "morningStarHitVolume", assets.EnemyHit);
        ConfigureReceiverPrefab<BreakableWall>("Assets/Prefabs/Gimmicks/BreakableWall.prefab", "breakAudioSource", "breakImpactClip", "breakImpactVolume", assets.Impact);
        ConfigureCompletScene(assets);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        ValidateAll(assets);
        Debug.Log("[Stage1AudioSetup] PASS: Player tuning, Stage1 BGM and SFX references saved and validated.");
    }

    public static void ApplyFromCommandLine()
    {
        try
        {
            Apply();
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    public static void ValidateFromCommandLine()
    {
        try
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ValidateAll(LoadAudioAssets());
            Debug.Log("[Stage1AudioValidation] PASS: all serialized values and references are valid.");
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    private static AudioAssets LoadAudioAssets()
    {
        AudioClip[] voices = new AudioClip[VoicePaths.Length];
        for (int i = 0; i < VoicePaths.Length; i++)
            voices[i] = LoadClip(VoicePaths[i]);

        return new AudioAssets
        {
            Bgm = LoadClip(BgmPath),
            Jump = LoadClip(JumpPath),
            Launch = LoadClip(LaunchPath),
            Footstep = LoadClip(FootstepPath),
            Running = LoadClip(RunningPath),
            EnemyHit = LoadClip(EnemyHitPath),
            Impact = LoadClip(ImpactPath),
            Landing = LoadClip(LandingPath),
            Voices = voices
        };
    }

    private static AudioClip LoadClip(string path)
    {
        AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        if (clip == null)
            throw new InvalidOperationException($"AudioClip import failed: {path}");
        return clip;
    }

    private static void ConfigurePlayerPrefab(string prefabPath, AudioAssets assets)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            Player player = root.GetComponentInChildren<Player>(true);
            if (player == null)
                throw new InvalidOperationException($"Player component missing: {prefabPath}");

            ConfigurePlayer(player, assets, null);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ConfigureReceiverPrefab<T>(
        string prefabPath,
        string sourceProperty,
        string clipProperty,
        string volumeProperty,
        AudioClip clip) where T : Component
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            T receiver = root.GetComponentInChildren<T>(true);
            if (receiver == null)
                throw new InvalidOperationException($"{typeof(T).Name} missing: {prefabPath}");

            SerializedObject serializedReceiver = new SerializedObject(receiver);
            SetObject(serializedReceiver, sourceProperty, null);
            SetObject(serializedReceiver, clipProperty, clip);
            SetFloat(serializedReceiver, volumeProperty, 0.9f);
            serializedReceiver.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(receiver);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ConfigureCompletScene(AudioAssets assets)
    {
        Scene scene = EditorSceneManager.OpenScene(CompletScenePath, OpenSceneMode.Single);
        Player player = FindFirstInScene<Player>(scene);
        if (player == null)
            throw new InvalidOperationException("Player missing from CompletScene.");

        AudioSource worldImpactSource;
        AudioSource bgmSource = EnsureStageAudio(scene, assets.Bgm, out worldImpactSource);
        ConfigurePlayer(player, assets, worldImpactSource);

        foreach (EnemyHealth enemy in FindAllInScene<EnemyHealth>(scene))
            ConfigureReceiver(enemy, "hitAudioSource", "morningStarHitClip", "morningStarHitVolume", worldImpactSource, assets.EnemyHit);

        foreach (BreakableWall wall in FindAllInScene<BreakableWall>(scene))
            ConfigureReceiver(wall, "breakAudioSource", "breakImpactClip", "breakImpactVolume", worldImpactSource, assets.Impact);

        DisableLegacyBgmSources(scene, bgmSource);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new InvalidOperationException("Failed to save CompletScene.");
    }

    private static AudioSource EnsureStageAudio(Scene scene, AudioClip bgmClip, out AudioSource worldImpactSource)
    {
        GameObject stageAudio = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == "StageAudio")
            {
                stageAudio = root;
                break;
            }
        }

        if (stageAudio == null)
        {
            stageAudio = new GameObject("StageAudio");
            SceneManager.MoveGameObjectToScene(stageAudio, scene);
        }

        AudioSource bgmSource = stageAudio.GetComponent<AudioSource>();
        if (bgmSource == null)
            bgmSource = stageAudio.AddComponent<AudioSource>();
        ConfigureSource(bgmSource, bgmClip, true, true, 0.2f);

        Transform worldImpactTransform = stageAudio.transform.Find(OneShotAudioUtility.WorldImpactSourceName);
        if (worldImpactTransform == null)
        {
            GameObject worldImpactObject = new GameObject(OneShotAudioUtility.WorldImpactSourceName);
            worldImpactTransform = worldImpactObject.transform;
            worldImpactTransform.SetParent(stageAudio.transform, false);
        }

        worldImpactSource = worldImpactTransform.GetComponent<AudioSource>();
        if (worldImpactSource == null)
            worldImpactSource = worldImpactTransform.gameObject.AddComponent<AudioSource>();
        ConfigureSource(worldImpactSource, null, false, false, 1f);

        EditorUtility.SetDirty(stageAudio);
        EditorUtility.SetDirty(bgmSource);
        EditorUtility.SetDirty(worldImpactSource);
        return bgmSource;
    }

    private static void DisableLegacyBgmSources(Scene scene, AudioSource formalBgmSource)
    {
        foreach (AudioSource source in FindAllInScene<AudioSource>(scene))
        {
            if (source == formalBgmSource)
                continue;

            string objectName = source.gameObject.name.ToLowerInvariant();
            string clipPath = source.clip != null ? AssetDatabase.GetAssetPath(source.clip) : string.Empty;
            bool isNamedMusicSource = objectName.Contains("bgm") || objectName.Contains("music");
            bool usesBgmFolder = clipPath.StartsWith("Assets/Audio/BGM/", StringComparison.OrdinalIgnoreCase);
            if (!isNamedMusicSource && !usesBgmFolder)
                continue;

            source.Stop();
            source.playOnAwake = false;
            source.loop = false;
            source.clip = null;
            EditorUtility.SetDirty(source);
        }
    }

    private static void ConfigurePlayer(Player player, AudioAssets assets, AudioSource worldImpactSource)
    {
        AudioSource sfxSource = EnsureChildAudioSource(player.transform, "SfxAudioSource");
        ConfigureSource(sfxSource, null, false, false, 1f);

        AudioSource footstepSource = EnsureChildAudioSource(player.transform, "FootstepAudioSource");
        ConfigureSource(footstepSource, assets.Footstep, true, false, 0.55f);

        SerializedObject serializedPlayer = new SerializedObject(player);
        SetFloat(serializedPlayer, "_groundMoveForce", 70f);
        SetFloat(serializedPlayer, "_groundLinearDragX", 8f);
        SetFloat(serializedPlayer, "_airMoveFactor", 0.1f);
        SetFloat(serializedPlayer, "_airLinearDragX", 1.5f);
        SetFloat(serializedPlayer, "_jumpSpeed", 8f);
        SetFloat(serializedPlayer, "_coyoteTime", 0.1f);
        SetFloat(serializedPlayer, "_jumpBufferTime", 0.15f);
        SetFloat(serializedPlayer, "_fallGravityMultiplier", 4f);
        SetFloat(serializedPlayer, "_jumpCutMultiplier", 2f);
        SetFloat(serializedPlayer, "_maxFallSpeed", -50f);
        SetObject(serializedPlayer, "_sfxAudioSource", sfxSource);
        SetObject(serializedPlayer, "_footstepAudioSource", footstepSource);
        SetObject(serializedPlayer, "_jumpClip", assets.Jump);
        SetObject(serializedPlayer, "_footstepGrassClip", assets.Footstep);
        SetObjectArray(serializedPlayer, "_jumpVoiceClips", assets.Voices);
        SetObject(serializedPlayer, "_landingClip", assets.Landing);
        SetFloat(serializedPlayer, "_jumpVolume", 1f);
        SetFloat(serializedPlayer, "_footstepVolume", 0.55f);
        SetFloat(serializedPlayer, "_jumpVoiceVolume", 0.7f);
        SetFloat(serializedPlayer, "_landingVolume", 0.8f);
        serializedPlayer.ApplyModifiedPropertiesWithoutUndo();

        PlayerHealth health = player.GetComponent<PlayerHealth>();
        if (health != null)
        {
            SerializedObject serializedHealth = new SerializedObject(health);
            SetObject(serializedHealth, "_damageAudioSource", sfxSource);
            SetObject(serializedHealth, "_damageClip", assets.Impact);
            SetFloat(serializedHealth, "_damageVolume", 0.8f);
            serializedHealth.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(health);
        }

        MorningStarLauncher launcher = player.GetComponent<MorningStarLauncher>();
        if (launcher == null)
            throw new InvalidOperationException($"MorningStarLauncher missing from {player.name}.");

        SerializedObject serializedLauncher = new SerializedObject(launcher);
        SetObject(serializedLauncher, "sfxAudioSource", sfxSource);
        SetObject(serializedLauncher, "morningStarLaunchClip", assets.Launch);
        SetFloat(serializedLauncher, "morningStarLaunchVolume", 1f);
        SetObject(serializedLauncher, "groundImpactAudioSource", worldImpactSource);
        SetObject(serializedLauncher, "groundImpactClip", assets.Impact);
        SetFloat(serializedLauncher, "groundImpactVolume", 0.9f);
        serializedLauncher.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(player);
        EditorUtility.SetDirty(launcher);
        EditorUtility.SetDirty(sfxSource);
        EditorUtility.SetDirty(footstepSource);
    }

    private static void ConfigureReceiver(
        Component receiver,
        string sourceProperty,
        string clipProperty,
        string volumeProperty,
        AudioSource source,
        AudioClip clip)
    {
        SerializedObject serializedReceiver = new SerializedObject(receiver);
        SetObject(serializedReceiver, sourceProperty, source);
        SetObject(serializedReceiver, clipProperty, clip);
        SetFloat(serializedReceiver, volumeProperty, 0.9f);
        serializedReceiver.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(receiver);
    }

    private static AudioSource EnsureChildAudioSource(Transform parent, string childName)
    {
        Transform child = parent.Find(childName);
        if (child == null)
        {
            GameObject childObject = new GameObject(childName);
            childObject.layer = parent.gameObject.layer;
            child = childObject.transform;
            child.SetParent(parent, false);
        }

        AudioSource source = child.GetComponent<AudioSource>();
        return source != null ? source : child.gameObject.AddComponent<AudioSource>();
    }

    private static void ConfigureSource(AudioSource source, AudioClip clip, bool loop, bool playOnAwake, float volume)
    {
        source.clip = clip;
        source.loop = loop;
        source.playOnAwake = playOnAwake;
        source.spatialBlend = 0f;
        source.volume = volume;
    }

    private static void ValidateAll(AudioAssets assets)
    {
        if (assets.Running == null)
            throw new InvalidOperationException("走る.mp3 was not imported.");

        ValidatePlayerPrefab("Assets/Player.prefab", assets);
        ValidatePlayerPrefab("Assets/Player 1.prefab", assets);

        Scene scene = EditorSceneManager.OpenScene(CompletScenePath, OpenSceneMode.Single);
        Player player = FindFirstInScene<Player>(scene);
        ValidatePlayer(player, assets, CompletScenePath);

        List<AudioSource> formalBgmSources = new List<AudioSource>();
        foreach (AudioSource source in FindAllInScene<AudioSource>(scene))
        {
            if (source.clip == assets.Bgm && source.loop && source.playOnAwake)
                formalBgmSources.Add(source);
        }

        if (formalBgmSources.Count != 1)
            throw new InvalidOperationException($"Expected one formal BGM source, found {formalBgmSources.Count}.");

        AudioSource bgm = formalBgmSources[0];
        if (bgm.gameObject.name != "StageAudio"
            || !Mathf.Approximately(bgm.volume, 0.2f)
            || !Mathf.Approximately(bgm.spatialBlend, 0f))
        {
            throw new InvalidOperationException("StageAudio BGM settings are invalid.");
        }

        AudioSource world = bgm.transform.Find(OneShotAudioUtility.WorldImpactSourceName)?.GetComponent<AudioSource>();
        if (world == null || world.loop || world.playOnAwake || world.clip != null)
            throw new InvalidOperationException("WorldImpactSfx settings are invalid.");

        ValidateReceiverPrefab<EnemyHealth>("Assets/Prefabs/Enemy.prefab", "morningStarHitClip", assets.EnemyHit);
        ValidateReceiverPrefab<BreakableWall>("Assets/Prefabs/Gimmicks/BreakableWall.prefab", "breakImpactClip", assets.Impact);
    }

    private static void ValidatePlayerPrefab(string prefabPath, AudioAssets assets)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            ValidatePlayer(root.GetComponentInChildren<Player>(true), assets, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ValidatePlayer(Player player, AudioAssets assets, string owner)
    {
        if (player == null)
            throw new InvalidOperationException($"Player missing: {owner}");

        SerializedObject serializedPlayer = new SerializedObject(player);
        RequireFloat(serializedPlayer, "_groundMoveForce", 70f, owner);
        RequireFloat(serializedPlayer, "_groundLinearDragX", 8f, owner);
        RequireFloat(serializedPlayer, "_airMoveFactor", 0.1f, owner);
        RequireFloat(serializedPlayer, "_airLinearDragX", 1.5f, owner);
        RequireFloat(serializedPlayer, "_jumpSpeed", 8f, owner);
        RequireFloat(serializedPlayer, "_coyoteTime", 0.1f, owner);
        RequireFloat(serializedPlayer, "_jumpBufferTime", 0.15f, owner);
        RequireFloat(serializedPlayer, "_fallGravityMultiplier", 4f, owner);
        RequireFloat(serializedPlayer, "_jumpCutMultiplier", 2f, owner);
        RequireFloat(serializedPlayer, "_maxFallSpeed", -50f, owner);
        RequireObject(serializedPlayer, "_jumpClip", assets.Jump, owner);
        RequireObject(serializedPlayer, "_footstepGrassClip", assets.Footstep, owner);
        RequireObject(serializedPlayer, "_landingClip", assets.Landing, owner);

        SerializedProperty voices = RequireProperty(serializedPlayer, "_jumpVoiceClips");
        if (voices.arraySize != assets.Voices.Length)
            throw new InvalidOperationException($"Jump voice count mismatch: {owner}");
        for (int i = 0; i < voices.arraySize; i++)
        {
            if (voices.GetArrayElementAtIndex(i).objectReferenceValue != assets.Voices[i])
                throw new InvalidOperationException($"Jump voice reference mismatch at {i}: {owner}");
        }

        AudioSource footstep = player.transform.Find("FootstepAudioSource")?.GetComponent<AudioSource>();
        if (footstep == null || footstep.clip != assets.Footstep || !footstep.loop || footstep.playOnAwake
            || !Mathf.Approximately(footstep.volume, 0.55f))
        {
            throw new InvalidOperationException($"Footstep settings invalid: {owner}");
        }
    }

    private static void ValidateReceiverPrefab<T>(string prefabPath, string clipProperty, AudioClip clip) where T : Component
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            T receiver = root.GetComponentInChildren<T>(true);
            if (receiver == null)
                throw new InvalidOperationException($"{typeof(T).Name} missing: {prefabPath}");
            RequireObject(new SerializedObject(receiver), clipProperty, clip, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static T FindFirstInScene<T>(Scene scene) where T : Component
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            T component = root.GetComponentInChildren<T>(true);
            if (component != null)
                return component;
        }
        return null;
    }

    private static IEnumerable<T> FindAllInScene<T>(Scene scene) where T : Component
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (T component in root.GetComponentsInChildren<T>(true))
                yield return component;
        }
    }

    private static void SetObject(SerializedObject serializedObject, string name, UnityEngine.Object value)
    {
        RequireProperty(serializedObject, name).objectReferenceValue = value;
    }

    private static void SetObjectArray(SerializedObject serializedObject, string name, AudioClip[] clips)
    {
        SerializedProperty property = RequireProperty(serializedObject, name);
        property.arraySize = clips.Length;
        for (int i = 0; i < clips.Length; i++)
            property.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
    }

    private static void SetFloat(SerializedObject serializedObject, string name, float value)
    {
        RequireProperty(serializedObject, name).floatValue = value;
    }

    private static SerializedProperty RequireProperty(SerializedObject serializedObject, string name)
    {
        SerializedProperty property = serializedObject.FindProperty(name);
        if (property == null)
            throw new MissingFieldException(serializedObject.targetObject.GetType().Name, name);
        return property;
    }

    private static void RequireFloat(SerializedObject serializedObject, string name, float expected, string owner)
    {
        float actual = RequireProperty(serializedObject, name).floatValue;
        if (!Mathf.Approximately(actual, expected))
            throw new InvalidOperationException($"{name} is {actual}, expected {expected}: {owner}");
    }

    private static void RequireObject(SerializedObject serializedObject, string name, UnityEngine.Object expected, string owner)
    {
        UnityEngine.Object actual = RequireProperty(serializedObject, name).objectReferenceValue;
        if (actual != expected)
            throw new InvalidOperationException($"{name} reference mismatch: {owner}");
    }
}
