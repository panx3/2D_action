using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Manual unlock tests use their own save namespace, including across domain reload.</summary>
[InitializeOnLoad]
public sealed class StageSelectDebugWindow : EditorWindow
{
    private const string Prefix = "IronBallGirl.StageSelect.";
    private const string DebugNamespace = "ManualDebug.";
    private const string Active = "StageSelectManualDebug.Active";
    private const string PreviousStart = "StageSelectManualDebug.PreviousStart";
    private const string RestoreStart = "StageSelectManualDebug.RestoreStart";

    static StageSelectDebugWindow()
    {
        if (SessionState.GetBool(Active, false)) StageSelectProgress.TestNamespace = DebugNamespace;
        EditorApplication.playModeStateChanged += OnPlayStateChanged;
    }

    [MenuItem("鉄球少女/Stage Select/解放デバッグ")]
    public static void Open()
    {
        var window = GetWindow<StageSelectDebugWindow>("ステージ解放デバッグ");
        window.minSize = new Vector2(480, 400);
    }

    private static string Key(string suffix) => Prefix + DebugNamespace + suffix;
    private static bool Flag(string key) => PlayerPrefs.GetInt(key, 0) != 0;

    private void OnGUI()
    {
        EditorGUILayout.LabelField("本番の保存状態（表示のみ）", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Stage1クリア / Stage2解放", Flag(Prefix + "Clear.Stage1") ? "済み" : "未クリア");
        EditorGUILayout.LabelField("Stage2出現演出", Flag(Prefix + "Reveal.Stage2") ? "再生済み" : "未再生");
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox("下の操作は専用のデバッグデータだけを変更します。本番のステージ解放記録は変更しません。テスト終了時に通常の保存先と起動Sceneへ戻ります。", MessageType.Info);
        EditorGUILayout.LabelField("デバッグ用状態", EditorStyles.boldLabel);
        bool cleared = Flag(Key("Clear.Stage1")), revealed = Flag(Key("Reveal.Stage2"));
        EditorGUILayout.LabelField("現在", !cleared ? "未解放" : revealed ? "解放済み・演出再生済み" : "Stage1クリア直後・出現演出待ち");
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            if (GUILayout.Button("① 未解放にする（Stage1クリアから確認）")) SetDebugState(false, false);
            if (GUILayout.Button("② クリア直後にする（結晶 → カセットを確認）")) SetDebugState(true, false);
            if (GUILayout.Button("③ 解放済みにする（Stage2への挿入を確認）")) SetDebugState(true, true);
            EditorGUILayout.Space();
            if (GUILayout.Button("この状態でセレクト画面からテスト")) StartDebug("Assets/Scenes/StageSelectScene.unity");
            if (GUILayout.Button("この状態でStage1からテスト")) StartDebug("Assets/Scenes/Stage1.unity");
        }
        if (SessionState.GetBool(Active, false))
            EditorGUILayout.HelpBox("専用データでテスト中。Stage1をクリアすると、このデータでStage2が解放されます。", MessageType.Info);
        EditorGUILayout.Space();
        if (GUILayout.Button("解放条件のInspectorを開く"))
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<StageSelectStage>("Assets/Data/StageSelect/MachineCity.asset");
    }

    public static void SetDebugState(bool cleared, bool revealed)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
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
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(PreviousStart, ""));
            SessionState.SetBool(RestoreStart, false);
        }
    }
}
