using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>未保存の編集Sceneを維持し、開始演出→入力復帰→Respawnを実機Editorで検証する。</summary>
[InitializeOnLoad]
public static class PlayerStartIntroPlayModeTest
{
    private const string Key = "PlayerStartIntroPlayModeTest.";
    private static Gamepad pad;
    private static PlayerStartIntro intro;
    private static Player player;
    private static MorningStarLauncher launcher;
    private static double startedAt;
    private static float jumpStartedAt;
    private static float completedAt;
    private static Vector2 stopPosition;
    private static int step;
    private static int landings;
    private static int errors;
    private static bool titleStarted;
    private static bool sawWalk;
    private static bool sawTitle;
    private static bool sawJump;
    private static readonly HashSet<string> animations = new HashSet<string>();

    static PlayerStartIntroPlayModeTest()
    {
        if (SessionState.GetBool(Key + "Running", false))
            Subscribe();
    }

    public static void Run(bool fromTitle = false)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Edit Modeで実行してください。");
        SessionState.SetString(Key + "PreviousStartScene", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetBool(Key + "FromTitle", fromTitle);
        SessionState.SetBool(Key + "Running", true);
        SessionState.SetString(Key + "Result", "RUNNING");
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(
            fromTitle ? "Assets/Scenes/TitleScene.unity" : "Assets/Scenes/CompletScene.unity");
        Subscribe();
        EditorApplication.isPlaying = true;
    }

    private static void Subscribe()
    {
        EditorApplication.playModeStateChanged -= StateChanged;
        EditorApplication.playModeStateChanged += StateChanged;
    }

    private static void StateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            Application.runInBackground = true;
            startedAt = EditorApplication.timeSinceStartup;
            jumpStartedAt = completedAt = -1f;
            step = landings = errors = 0;
            titleStarted = sawWalk = sawTitle = sawJump = false;
            animations.Clear();
            Application.logMessageReceived += CountErrors;
            EditorApplication.update += Tick;
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(
                SessionState.GetString(Key + "PreviousStartScene", ""));
            SessionState.SetBool(Key + "Running", false);
            EditorApplication.playModeStateChanged -= StateChanged;
            Debug.Log("[PlayerStartIntroTest] " + SessionState.GetString(Key + "Result", "FAILED: interrupted"));
        }
    }

    private static void CountErrors(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            errors++;
    }

    private static void Tick()
    {
        try
        {
            EditorApplication.QueuePlayerLoopUpdate();
            Require(EditorApplication.timeSinceStartup - startedAt < 25d, "演出またはロードがタイムアウト");
            if (SessionState.GetBool(Key + "FromTitle", false) && !titleStarted)
            {
                TitleScreenController title = UnityEngine.Object.FindAnyObjectByType<TitleScreenController>();
                if (title == null || !title.InputReady)
                    return;
                title.OnStartPressed();
                titleStarted = true;
            }
            if (intro == null)
            {
                intro = UnityEngine.Object.FindAnyObjectByType<PlayerStartIntro>();
                if (intro == null)
                    return;
                player = intro.GetComponent<Player>();
                launcher = intro.GetComponent<MorningStarLauncher>();
                player.Landed += CountLanding;
                pad = InputSystem.AddDevice<Gamepad>();
                // 演出中に入力を保持しても、Jump/発射バッファへ漏れないことを確認する。
                InputSystem.QueueStateEvent(pad, new GamepadState
                {
                    leftStick = Vector2.right,
                    rightStick = Vector2.right
                }.WithButton(GamepadButton.South));
            }

            Animator animator = player.GetComponent<Animator>();
            foreach (string name in new[] { "JumpUp", "JumpFall", "JumpLand", "Walk", "Idle" })
                if (animator.GetCurrentAnimatorStateInfo(0).IsName(name))
                    animations.Add(name);

            if (intro.Phase != PlayerStartIntro.IntroPhase.Complete)
            {
                Require(!player.InputEnabled, "演出中に入力が解放された");
                Require(!player.Rigidbody2D.simulated, "演出中にPlayer物理が動いている");
                Require(launcher.State == MorningStarLauncher.MorningStarState.StartIntro, "演出中に鉄球状態が変化した");
                if (intro.Phase == PlayerStartIntro.IntroPhase.Jump && !sawJump)
                {
                    sawJump = true;
                    jumpStartedAt = Time.time;
                    Vector3 viewport = Camera.main.WorldToViewportPoint(player.transform.position);
                    Require(viewport.x < 0f && viewport.y > 1f, "開始位置が画面外の左上ではない");
                }
                if (intro.Phase == PlayerStartIntro.IntroPhase.Walk && !sawWalk)
                {
                    sawWalk = true;
                    Capture("walk");
                }
                if (intro.Phase == PlayerStartIntro.IntroPhase.StageTitle)
                {
                    sawTitle = true;
                    SerializedObject settings = new SerializedObject(intro);
                    CanvasGroup group = (CanvasGroup)settings.FindProperty("stageTitleGroup").objectReferenceValue;
                    Require(group != null && group.alpha == 1f, "ステージ名が表示されていない");
                    if (!File.Exists(CapturePath("title")))
                        Capture("title");
                }
                return;
            }

            Require(player.InputEnabled && player.Rigidbody2D.simulated, "演出後に通常操作・物理へ戻らない");
            if (step == 0)
            {
                Require(sawJump && sawWalk && sawTitle, "シーケンスが欠落");
                Require(animations.Contains("JumpUp") && animations.Contains("JumpFall")
                    && animations.Contains("Walk") && animations.Contains("Idle"), "既存アニメーションへの遷移が欠落");
                Require(landings == 1, "着地イベントが1回ではない: " + landings);
                Require(Time.time - jumpStartedAt >= 1.5f && Time.time - jumpStartedAt <= 2.6f, "演出時間が想定外");
                Require(launcher.State == MorningStarLauncher.MorningStarState.Dragging, "保持した右スティックで即発射した");
                Require(player.IsGrounded, "停止位置で接地していない");
                completedAt = Time.time;
                stopPosition = player.transform.position;
                InputSystem.QueueStateEvent(pad, new GamepadState());
                step = 1;
            }
            else if (step == 1 && Time.time - completedAt > 0.12f)
            {
                Require(player.IsGrounded, "演出中のJump入力が復帰後に漏れた");
                Require(launcher.State == MorningStarLauncher.MorningStarState.Dragging, "Rest復帰が不正");
                InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = Vector2.right }.WithButton(GamepadButton.South));
                completedAt = Time.time;
                step = 2;
            }
            else if (step == 2 && Time.time - completedAt > 0.08f)
            {
                Require(player.Rigidbody2D.linearVelocity.y > 0f, "通常ジャンプができない");
                Require(player.Rigidbody2D.linearVelocity.x > 0f, "通常移動ができない");
                InputSystem.QueueStateEvent(pad, new GamepadState { rightStick = Vector2.right });
                completedAt = Time.time;
                step = 3;
            }
            else if (step == 3 && Time.time - completedAt > 0.15f)
            {
                Require(launcher.State != MorningStarLauncher.MorningStarState.Dragging, "通常の右スティック発射ができない");
                InputSystem.QueueStateEvent(pad, new GamepadState());
                UnityEngine.Object.FindAnyObjectByType<GimmickRespawnController>().RespawnAt(stopPosition, true);
                Require(!intro.IsPlaying && intro.Phase == PlayerStartIntro.IntroPhase.Complete, "Respawnで演出が再開した");
                Require(launcher.State == MorningStarLauncher.MorningStarState.Dragging, "RespawnのRest復帰が不正");
                SerializedObject settings = new SerializedObject(launcher);
                Rigidbody2D ball = (Rigidbody2D)settings.FindProperty("morningStarRb").objectReferenceValue;
                Require(ball.simulated && Mathf.Approximately(ball.mass, launcher.RestBallMass)
                    && Mathf.Approximately(ball.gravityScale, launcher.RestBallGravityScale)
                    && Mathf.Approximately(ball.linearDamping, launcher.RestBallLinearDamping), "Rest物理値が漏れた");
                Require(errors == 0, "新規Console Error/Exception: " + errors);
                Finish("PASS: jump→land→walk→idle→title→input; held input blocked; move/jump/right-stick/respawn restored. Animations="
                    + string.Join(",", animations));
            }
        }
        catch (Exception exception)
        {
            Finish("FAILED: " + exception.Message);
        }
    }

    private static void CountLanding() => landings++;
    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
    private static string CapturePath(string phase)
        => Path.Combine(Path.GetTempPath(), "PlayerStartIntro-" + (SessionState.GetBool(Key + "FromTitle", false) ? "title-" : "direct-") + phase + ".png");
    private static void Capture(string phase) => ScreenCapture.CaptureScreenshot(CapturePath(phase));

    private static void Finish(string result)
    {
        SessionState.SetString(Key + "Result", result);
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= CountErrors;
        if (player != null)
            player.Landed -= CountLanding;
        if (pad != null && pad.added)
            InputSystem.RemoveDevice(pad);
        EditorApplication.isPlaying = false;
    }
}
