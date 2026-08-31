using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>BGM切り替え設定を既存Sceneへ再現可能に適用する。</summary>
public static class BgmSwitchingSetup
{
    public const string RequestPath = "Temp/BgmSwitchingSetup.request";

    private const string TitleScenePath = "Assets/Scenes/TitleScene.unity";
    private const string StageScenePath = "Assets/Scenes/CompletScene.unity";
    private const string TitleClipPath = "Assets/Audio/BGM/TitleBGM.mp3";
    private const string StageClipPath = "Assets/Audio/BGM/Peritune_Winds_Embrace.ogg";
    private const string GoalClipPath = "Assets/Audio/BGM/GoalBGM.mp3";

    [MenuItem("Tools/鉄球少女/Apply BGM Switching")]
    public static void Apply()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        AudioClip titleClip = LoadClip(TitleClipPath);
        AudioClip stageClip = LoadClip(StageClipPath);
        AudioClip goalClip = LoadClip(GoalClipPath);

        ConfigureScene(TitleScenePath, scene => ConfigureTitle(scene, titleClip));
        ConfigureScene(StageScenePath, scene => ConfigureStage(scene, stageClip, goalClip));

        AssetDatabase.SaveAssets();
        Debug.Log("[BgmSwitchingSetup] PASS: Title/Stage/Goal BGM references and single-source switching configured.");
    }

    private static void ConfigureTitle(Scene scene, AudioClip titleClip)
    {
        TitleScreenController title = FindInScene<TitleScreenController>(scene);
        if (title == null)
            throw new InvalidOperationException("TitleScreenControllerがTitleSceneにありません。");

        SerializedObject serializedTitle = new SerializedObject(title);
        AudioSource source = serializedTitle.FindProperty("bgmSource").objectReferenceValue as AudioSource;
        if (source == null)
            throw new InvalidOperationException("TitleScreenController.bgmSourceが未設定です。");

        ConfigureSource(source, titleClip, 0.2f);
        GameBgmController controller = source.GetComponent<GameBgmController>();
        if (controller == null)
            controller = Undo.AddComponent<GameBgmController>(source.gameObject);
        ConfigureController(controller, source, GameBgmController.BgmState.Title,
            titleClip, null, null, 0.2f, 0.15f, 0.15f);

        ValidateSingleController(scene, controller);
    }

    private static void ConfigureStage(Scene scene, AudioClip stageClip, AudioClip goalClip)
    {
        GameObject stageAudio = scene.GetRootGameObjects()
            .FirstOrDefault(root => root.name == "StageAudio");
        if (stageAudio == null)
            throw new InvalidOperationException("StageAudioがCompletSceneにありません。");

        AudioSource source = stageAudio.GetComponent<AudioSource>();
        if (source == null)
            throw new InvalidOperationException("StageAudioのAudioSourceがありません。");

        // 現行Sceneのステージ音量0.15と既存Mixer/Source設定は維持する。
        ConfigureSource(source, stageClip, 0.15f);
        GameBgmController controller = stageAudio.GetComponent<GameBgmController>();
        if (controller == null)
            controller = Undo.AddComponent<GameBgmController>(stageAudio);
        ConfigureController(controller, source, GameBgmController.BgmState.Stage,
            null, stageClip, goalClip, 0.2f, 0.15f, 0.15f);

        ValidateSingleController(scene, controller);
    }

    private static void ConfigureSource(AudioSource source, AudioClip clip, float volume)
    {
        Undo.RecordObject(source, "Configure BGM AudioSource");
        source.clip = clip;
        source.playOnAwake = false;
        source.loop = true;
        source.volume = volume;
        source.spatialBlend = 0f;
        EditorUtility.SetDirty(source);
    }

    private static void ConfigureController(
        GameBgmController controller,
        AudioSource source,
        GameBgmController.BgmState initialState,
        AudioClip titleClip,
        AudioClip stageClip,
        AudioClip goalClip,
        float titleVolume,
        float stageVolume,
        float goalVolume)
    {
        SerializedObject serialized = new SerializedObject(controller);
        serialized.FindProperty("bgmSource").objectReferenceValue = source;
        serialized.FindProperty("initialState").enumValueIndex = (int)initialState;
        serialized.FindProperty("titleClip").objectReferenceValue = titleClip;
        serialized.FindProperty("stageClip").objectReferenceValue = stageClip;
        serialized.FindProperty("goalClip").objectReferenceValue = goalClip;
        serialized.FindProperty("titleVolume").floatValue = titleVolume;
        serialized.FindProperty("stageVolume").floatValue = stageVolume;
        serialized.FindProperty("goalVolume").floatValue = goalVolume;
        serialized.FindProperty("switchFadeDuration").floatValue = 0.5f;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(controller);
    }

    private static void ValidateSingleController(Scene scene, GameBgmController expected)
    {
        GameBgmController[] controllers = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<GameBgmController>(true))
            .ToArray();
        if (controllers.Length != 1 || controllers[0] != expected)
            throw new InvalidOperationException($"{scene.name}のGameBgmController数が1ではありません: {controllers.Length}");
    }

    private static T FindInScene<T>(Scene scene) where T : Component
    {
        return scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<T>(true))
            .FirstOrDefault();
    }

    private static AudioClip LoadClip(string path)
    {
        AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        if (clip == null)
            throw new FileNotFoundException("BGM AudioClipを読み込めません。", path);
        return clip;
    }

    private static void ConfigureScene(string path, Action<Scene> configure)
    {
        Scene scene = SceneManager.GetSceneByPath(path);
        bool openedHere = !scene.IsValid() || !scene.isLoaded;
        if (openedHere)
            scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);

        try
        {
            configure(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, path))
                throw new IOException($"Sceneを保存できませんでした: {path}");
        }
        finally
        {
            if (openedHere && scene.IsValid() && scene.isLoaded)
                EditorSceneManager.CloseScene(scene, true);
        }
    }
}

[InitializeOnLoad]
public static class BgmSwitchingSetupRequestRunner
{
    static BgmSwitchingSetupRequestRunner()
    {
        EditorApplication.delayCall += TryApply;
    }

    private static void TryApply()
    {
        string requestPath = Path.GetFullPath(BgmSwitchingSetup.RequestPath);
        if (!File.Exists(requestPath))
            return;

        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += TryApply;
            return;
        }

        try
        {
            BgmSwitchingSetup.Apply();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            return;
        }

        File.Delete(requestPath);
    }
}
