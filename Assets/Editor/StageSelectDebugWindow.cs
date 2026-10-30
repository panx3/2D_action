using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Manual unlock tests use their own save namespace, including across domain reload.</summary>
[InitializeOnLoad]
public sealed class StageSelectDebugWindow : EditorWindow
{
    private const string Prefix = "IronBallGirl.StageSelect.";
    private const string DebugNamespace = "ManualDebug.";
    private const string Active = "StageSelectManualDebug.Active";
    private const string PreviousStart = "StageSelectManualDebug.PreviousStart";
    private const string RestoreStart = "StageSelectManualDebug.RestoreStart";
    private bool showRealSave;

    static StageSelectDebugWindow()
    {
        if (SessionState.GetBool(Active, false)) StageSelectProgress.TestNamespace = DebugNamespace;
        EditorApplication.playModeStateChanged += OnPlayStateChanged;
        SceneManager.sceneLoaded += OnDebugSceneLoaded;
    }

    [MenuItem("鉄球少女/Stage Select/解放デバッグ")]
    public static void Open()
    {
        var window = GetWindow<StageSelectDebugWindow>("ステージ解放デバッグ");
        window.minSize = new Vector2(480, 280);
    }

    private static string Key(string suffix) => Prefix + DebugNamespace + suffix;
    private static bool Flag(string key) => PlayerPrefs.GetInt(key, 0) != 0;

    private void OnGUI()
    {
        EditorGUILayout.HelpBox("チェックやボタンを押すと、セレクト画面へすぐ反映します。本番のセーブは変更しません。", MessageType.Info);
        bool cleared = Flag(Key("Clear.Stage1")), revealed = Flag(Key("Reveal.Stage2"));
        bool blocked = EditorApplication.isCompiling || (EditorApplication.isPlayingOrWillChangePlaymode &&
            (!EditorApplication.isPlaying || !SessionState.GetBool(Active, false) || TitleStageTransition.IsTransitioning || StageSelectController.IsEnteringStage));
        using (new EditorGUI.DisabledScope(blocked))
        {
            bool unlocked = EditorGUILayout.ToggleLeft("ステージ2を解放する", cleared);
            if (unlocked != cleared) ApplyUnlocked(unlocked);
            EditorGUILayout.LabelField("現在：" + (!cleared ? "未解放" : revealed ? "解放済み" : "解放演出待ち"));
            if (GUILayout.Button("ステージ1をクリアしたことにする（解放演出を再生）", GUILayout.Height(34))) SimulateStage1Clear();
            EditorGUILayout.Space();
            if (GUILayout.Button("セレクト画面を開く")) ShowDebugSelect();
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("ステージ1から遊んで確認する")) StartDebug("Assets/Scenes/Stage1.unity");
        }
        if (blocked) EditorGUILayout.HelpBox("通常のPlay中は一度停止してください。画面遷移中は完了までお待ちください。", MessageType.None);
        EditorGUILayout.Space();
        showRealSave = EditorGUILayout.Foldout(showRealSave, "本番の保存状態を見る");
        if (showRealSave)
        {
            EditorGUILayout.LabelField("ステージ2", Flag(Prefix + "Clear.Stage1") ? "解放済み" : "未解放");
            EditorGUILayout.LabelField("出現演出", Flag(Prefix + "Reveal.Stage2") ? "再生済み" : "未再生");
        }
    }

    public static void ApplyUnlocked(bool unlocked) { SetDebugState(unlocked, unlocked); ShowDebugSelect(); }
    public static void SimulateStage1Clear() { SetDebugState(true, false); ShowDebugSelect(); }

    private static void ShowDebugSelect()
    {
        if (!EditorApplication.isPlayingOrWillChangePlaymode) StartDebug("Assets/Scenes/StageSelectScene.unity");
        else if (EditorApplication.isPlaying && SessionState.GetBool(Active, false) && !StageSelectController.IsEnteringStage && !TitleStageTransition.IsTransitioning)
        {
            Time.timeScale = 1;
            SceneManager.LoadScene("StageSelectScene");
        }
    }

    public static void SetDebugState(bool cleared, bool revealed)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode && (!EditorApplication.isPlaying || !SessionState.GetBool(Active, false) || StageSelectController.IsEnteringStage || TitleStageTransition.IsTransitioning)) return;
        PlayerPrefs.SetInt(Key("Clear.Stage1"), cleared ? 1 : 0);
        PlayerPrefs.SetInt(Key("Reveal.Stage2"), cleared && revealed ? 1 : 0);
        PlayerPrefs.SetInt(Key("Clear.Stage2"), 0);
        PlayerPrefs.Save();
    }

    private static void StartDebug(string scenePath)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorUtility.scriptCompilationFailed) return;
        SessionState.SetString(PreviousStart, AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetBool(RestoreStart, true);
        SessionState.SetBool(Active, true);
        StageSelectProgress.TestNamespace = DebugNamespace;
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
        EditorApplication.isPlaying = true;
    }

    private static void OnPlayStateChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(RestoreStart, false)) return;
        if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.EnteredEditMode)
        {
            StageSelectProgress.TestNamespace = null;
            SessionState.SetBool(Active, false);
            EditorApplication.update -= FocusStage2;
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(PreviousStart, ""));
            SessionState.SetBool(RestoreStart, false);
        }
    }

    private static void OnDebugSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "StageSelectScene" || !SessionState.GetBool(Active, false)) return;
        EditorApplication.update -= FocusStage2;
        EditorApplication.update += FocusStage2;
    }

    private static void FocusStage2()
    {
        var controller = Object.FindAnyObjectByType<StageSelectController>();
        if (controller == null || !controller.InputReady) return;
        EditorApplication.update -= FocusStage2;
        if (controller.SelectedIndex == 0) controller.Move(1);
    }
}
