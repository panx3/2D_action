using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Live, isolated debug progress with one persistent, latest-request-wins operation.</summary>
[InitializeOnLoad]
public sealed class StageSelectDebugWindow : EditorWindow
{
    private const string Prefix = "IronBallGirl.StageSelect.";
    private const string DebugNamespace = "ManualDebug.";
    private const string Active = "StageSelectManualDebug.Active";
    private const string PreviousStart = "StageSelectManualDebug.PreviousStart";
    private const string RestoreStart = "StageSelectManualDebug.RestoreStart";
    private const string PreviousNamespace = "StageSelectManualDebug.PreviousNamespace";
    private const string PreviousNamespaceNull = "StageSelectManualDebug.PreviousNamespaceNull";
    private const string PendingKey = "StageSelectManualDebug.Pending";
    private const string StatusKey = "StageSelectManualDebug.Status";
    private enum Request { None, Select, Stage1, Goal }
    private bool showRealSave;
    public static bool HasPendingRequest => Pending != Request.None;
    public static string Status
    {
        get
        {
            if(EditorApplication.isPlaying && !HasPendingRequest && SessionState.GetBool(Active,false)
                && SceneManager.GetActiveScene().name=="StageSelectScene")
            {
                var select=UnityEngine.Object.FindAnyObjectByType<StageSelectController>();
                if(select!=null && select.IsUnlockPresentation) return "ステージ2の解放演出を再生中";
                if(select!=null && select.InputReady) return "反映済み：ステージ2 "+(!Flag(Key("Clear.Stage1")) ? "未解放" : "解放済み");
            }
            return SessionState.GetString(StatusKey,"デバッグ待機中");
        }
    }
    private static Request Pending
    {
        get => (Request)SessionState.GetInt(PendingKey, 0);
        set => SessionState.SetInt(PendingKey, (int)value);
    }

    static StageSelectDebugWindow()
    {
        EditorApplication.playModeStateChanged += OnPlayStateChanged;
        EditorApplication.update += ProcessRequest;
    }

    // An EditorWindow's static constructor can run during deserialization, where SessionState is forbidden.
    [InitializeOnLoadMethod]
    private static void RestoreDebugNamespace()
    {
        if (SessionState.GetBool(Active, false)) StageSelectProgress.TestNamespace = DebugNamespace;
    }

    [MenuItem("鉄球少女/Stage Select/解放デバッグ")]
    public static void Open()
    {
        var window = GetWindow<StageSelectDebugWindow>("ステージ解放デバッグ");
        window.minSize = new Vector2(480, 370);
        if (EditorApplication.isPlaying && SceneManager.GetActiveScene().name == "StageSelectScene")
            Queue(Request.Select);
    }

    private static string Key(string suffix) => Prefix + DebugNamespace + suffix;
    private static bool Flag(string key) => PlayerPrefs.GetInt(key, 0) != 0;
    private static void SetStatus(string value) => SessionState.SetString(StatusKey, value);
    private void OnInspectorUpdate() => Repaint();

    private void OnGUI()
    {
        EditorGUILayout.HelpBox("通常のPlay中も変更できます。チェックはその場で反映し、画面遷移中の操作は完了直後に反映します。本番の保存データは変更しません。", MessageType.Info);
        using (new EditorGUI.DisabledScope(EditorApplication.isCompiling))
        {
            bool cleared = Flag(Key("Clear.Stage1"));
            bool unlocked = EditorGUILayout.ToggleLeft("ステージ2を解放する", cleared);
            if (unlocked != cleared) ApplyUnlocked(unlocked);
            EditorGUILayout.LabelField("保存状態", !Flag(Key("Clear.Stage1")) ? "未解放" : Flag(Key("Reveal.Stage2")) ? "解放済み" : "解放演出待ち");
            if (GUILayout.Button("ステージ1をクリアしたことにする（解放演出を再生）", GUILayout.Height(32))) SimulateStage1Clear();
            if (GUILayout.Button("セレクト画面を表示")) ShowDebugSelect();
            EditorGUILayout.Space();
            if (GUILayout.Button("ステージ1から遊んで確認する")) StartStage1();
            if (GUILayout.Button(new GUIContent("ステージ1のゴール手前へテレポート", "ゴール手前の床へ、全回復したPlayer・鉄球・鎖・カメラを移動します。通常の復帰地点は維持します。"), GUILayout.Height(32))) TeleportToStage1Goal();
        }
        EditorGUILayout.HelpBox(Status, MessageType.None);
        if (HasPendingRequest && GUILayout.Button("保留中の操作を取り消す"))
        { Pending = Request.None; SetStatus("保留中の操作を取り消しました"); }
        EditorGUILayout.LabelField("クリアから開放まで試す場合は、未解放にしてからゴール手前へ移動してください。", EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.Space();
        showRealSave = EditorGUILayout.Foldout(showRealSave, "本番の保存状態を見る");
        if (showRealSave)
        {
            EditorGUILayout.LabelField("ステージ2", Flag(Prefix + "Clear.Stage1") ? "解放済み" : "未解放");
            EditorGUILayout.LabelField("出現演出", Flag(Prefix + "Reveal.Stage2") ? "再生済み" : "未再生");
        }
    }

    public static void ApplyUnlocked(bool unlocked) { SetDebugState(unlocked, unlocked); Queue(Request.Select); }
    public static void SimulateStage1Clear() { SetDebugState(true, false); Queue(Request.Select); }
    public static void ShowDebugSelect() => Queue(Request.Select);
    public static void StartStage1() => Queue(Request.Stage1);
    public static void TeleportToStage1Goal() => Queue(Request.Goal);

    // Persist intent even during transitions; it never writes the current/production namespace.
    public static void SetDebugState(bool cleared, bool revealed)
    {
        PlayerPrefs.SetInt(Key("Clear.Stage1"), cleared ? 1 : 0);
        PlayerPrefs.SetInt(Key("Reveal.Stage2"), cleared && revealed ? 1 : 0);
        PlayerPrefs.SetInt(Key("Clear.Stage2"), 0);
        PlayerPrefs.Save();
    }

    private static void Queue(Request request)
    {
        Pending = request;
        SetStatus("反映待ち…（最後に指定した操作を反映します）");
        if (!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling)
            StartDebug(request == Request.Select ? "Assets/Scenes/StageSelectScene.unity" : "Assets/Scenes/Stage1.unity");
    }

    private static bool ActivateSession()
    {
        string current = StageSelectProgress.TestNamespace;
        if (!string.IsNullOrEmpty(current) && current != DebugNamespace)
        { SetStatus("別の自動検証の終了を待っています"); return false; }
        if (!SessionState.GetBool(Active, false))
        {
            SessionState.SetString(PreviousNamespace, current ?? "");
            SessionState.SetBool(PreviousNamespaceNull, current == null);
            SessionState.SetBool(Active, true);
        }
        StageSelectProgress.TestNamespace = DebugNamespace;
        return true;
    }

    private static void StartDebug(string scenePath)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorUtility.scriptCompilationFailed) return;
        var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
        if (scene == null) { Pending = Request.None; SetStatus("対象Sceneが見つかりません: " + scenePath); return; }
        if (!ActivateSession()) return;
        if (!SessionState.GetBool(RestoreStart, false))
        {
            SessionState.SetString(PreviousStart, AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            SessionState.SetBool(RestoreStart, true);
        }
        EditorSceneManager.playModeStartScene = scene;
        EditorApplication.isPlaying = true;
    }

    private static void ProcessRequest()
    {
        if (Pending == Request.None || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if(EditorUtility.scriptCompilationFailed)
        { SetStatus("コンパイルエラー解消後に反映します"); return; }
        if (!EditorApplication.isPlaying)
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode && !EditorUtility.scriptCompilationFailed)
                StartDebug(Pending == Request.Select ? "Assets/Scenes/StageSelectScene.unity" : "Assets/Scenes/Stage1.unity");
            return;
        }
        if (TitleStageTransition.IsTransitioning || StageSelectController.IsEnteringStage)
        { SetStatus("画面遷移の完了直後に反映します"); return; }
        if (!ActivateSession()) return;
        var pause = UnityEngine.Object.FindAnyObjectByType<PauseMenuController>();
        if (pause != null && pause.IsPaused)
        { pause.ClosePause(); SetStatus("ポーズ解除後に反映します"); return; }
        try
        {
            if (Pending == Request.Select)
            {
                if (SceneManager.GetActiveScene().name != "StageSelectScene") { Load("StageSelectScene"); return; }
                var select = UnityEngine.Object.FindAnyObjectByType<StageSelectController>();
                if (select == null || !select.RefreshDebugSelection("Stage2"))
                { SetStatus("セレクト画面の準備を待っています"); return; }
                Pending = Request.None;
                SetStatus("反映済み：" + (!Flag(Key("Clear.Stage1")) ? "ステージ2 未解放" : Flag(Key("Reveal.Stage2")) ? "ステージ2 解放済み" : "ステージ2 解放演出を再生中"));
                return;
            }
            if (SceneManager.GetActiveScene().name != "Stage1") { Load("Stage1"); return; }
            var player = UnityEngine.Object.FindAnyObjectByType<Player>();
            if (player == null) { SetStatus("ステージ1の準備を待っています"); return; }
            var goal = UnityEngine.Object.FindAnyObjectByType<GoalPoint>();
            var health = player.GetComponent<PlayerHealth>();
            if (goal == null || goal.IsBroken || goal.IsCleared || (health != null && health.IsDead))
            { Load("Stage1"); return; }
            var intro = player.GetComponent<PlayerStartIntro>();
            if (intro != null && intro.isActiveAndEnabled && (!intro.didStart || intro.IsPlaying))
            { SetStatus("開始演出の完了後に移動します"); return; }
            if (Pending == Request.Stage1)
            { Pending = Request.None; SetStatus("ステージ1を開始しました"); return; }
            var death = UnityEngine.Object.FindAnyObjectByType<DeathRespawnManager>();
            var camera = UnityEngine.Object.FindAnyObjectByType<CameraFollow>();
            if ((death != null && death.IsRespawning) || player.IsGimmickInputBlocked || player.IsGoalInputBlocked
                || (camera != null && (camera.IsEventControlled || camera.IsRespawnCameraHeld)))
            { SetStatus("現在の演出・復帰処理が終わり次第、ゴール手前へ移動します"); return; }
            var respawn = UnityEngine.Object.FindAnyObjectByType<GimmickRespawnController>();
            if (respawn == null) { Pending = Request.None; SetStatus("共通の復帰処理が見つかりません"); return; }
            if (!TryFindGoalFloor(player, goal, out Vector3 destination))
            { Pending = Request.None; SetStatus("ゴール手前に安全な床が見つかりません"); return; }
            Vector3 oldCheckpoint = respawn.CurrentRespawnPosition;
            player.SetInputEnabled(false);
            respawn.RespawnAt(destination, true);
            respawn.SetRespawnPoint(oldCheckpoint);
            Physics2D.SyncTransforms();
            player.SetStartIntroControl(false);
            player.SetInputEnabled(true);
            Pending = Request.None;
            SetStatus("ゴール手前へ移動しました（全回復・鉄球とカメラも復元）");
        }
        catch (Exception exception)
        {
            Pending = Request.None;
            SetStatus("反映に失敗しました: " + exception.Message);
            Debug.LogException(exception);
        }
    }

    private static void Load(string sceneName)
    {
        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        { Pending = Request.None; SetStatus("対象Sceneが登録されていません: " + sceneName); return; }
        Time.timeScale = 1;
        SetStatus(sceneName + " を準備しています…");
        SceneManager.LoadScene(sceneName);
    }

    private static bool TryFindGoalFloor(Player player, GoalPoint goal, out Vector3 destination)
    {
        destination = default;
        var collider = player.GetComponent<Collider2D>();
        if (collider == null) return false;
        Physics2D.SyncTransforms();
        Bounds bounds = collider.bounds;
        Vector2 centerOffset = (Vector2)bounds.center - (Vector2)player.transform.position;
        float feetOffset = player.transform.position.y - bounds.min.y;
        int mask = new SerializedObject(player).FindProperty("_groundLayers").intValue;
        foreach (float distance in new[] { 4f, 5.5f, 3f, 7f })
        {
            Vector2 origin = (Vector2)goal.transform.position + new Vector2(-distance, 5);
            foreach (var hit in Physics2D.RaycastAll(origin, Vector2.down, 16, mask))
            {
                if (hit.collider.isTrigger || hit.collider.gameObject.scene != player.gameObject.scene
                    || (hit.rigidbody != null && hit.rigidbody.bodyType != RigidbodyType2D.Static) || hit.normal.y < .7f) continue;
                Vector2 point = new Vector2(origin.x, hit.point.y + feetOffset + .06f);
                bool blocked = false;
                foreach (var other in Physics2D.OverlapCapsuleAll(point + centerOffset, (Vector2)bounds.size * .92f, CapsuleDirection2D.Vertical, 0, mask))
                    if (!other.isTrigger && !other.transform.IsChildOf(player.transform) && other.gameObject.scene == player.gameObject.scene)
                    { blocked = true; break; }
                if (blocked) continue;
                destination = new Vector3(point.x, point.y, player.transform.position.z);
                return true;
            }
        }
        return false;
    }

    private static void OnPlayStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode)
        { Pending = Request.None; return; }
        if (state != PlayModeStateChange.EnteredEditMode) return;
        if (SessionState.GetBool(Active, false))
        {
            StageSelectProgress.TestNamespace = SessionState.GetBool(PreviousNamespaceNull, true)
                ? null : SessionState.GetString(PreviousNamespace, "");
            SessionState.SetBool(Active, false);
        }
        if (SessionState.GetBool(RestoreStart, false))
        {
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(PreviousStart, ""));
            SessionState.SetBool(RestoreStart, false);
        }
        SetStatus("停止しました。デバッグの保存状態は次回も使用できます");
    }
}
