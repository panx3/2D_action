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
    private static StageTitleUI stageTitleUI;
    private static CanvasGroup titleGroup;
    private static RectTransform titleRect;
    private static Vector2 titleScreenPosition;
    private static double startedAt;
    private static float jumpStartedAt;
    private static float completedAt;
    private static float titleStartedAt;
    private static float introCompletedAt;
    private static double pauseStartedAt;
    private static float pausedGameTime;
    private static int pauseStep;
    private static PauseMenuController pauseMenu;
    private static Vector2 stopPosition;
    private static int step;
    private static int landings;
    private static int errors;
    private static bool titleStarted;
    private static bool sawWalk;
    private static bool sawFadeIn;
    private static bool sawHold;
    private static bool sawFadeOut;
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
            jumpStartedAt = completedAt = titleStartedAt = introCompletedAt = -1f;
            step = landings = errors = 0;
            pauseStep = 0;
            titleStarted = sawWalk = sawFadeIn = sawHold = sawFadeOut = sawJump = false;
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
                stageTitleUI = UnityEngine.Object.FindAnyObjectByType<StageTitleUI>();
                Require(stageTitleUI != null, "共通ステージ名UIがない");
                titleGroup = stageTitleUI.GetComponent<CanvasGroup>();
                UnityEngine.UI.Image image = stageTitleUI.GetComponentInChildren<UnityEngine.UI.Image>();
                titleRect = image.rectTransform;
                Canvas canvas = stageTitleUI.GetComponent<Canvas>();
                // CRTがOverlayを一時的にScreenSpaceCameraへ変換する既存仕様にも対応する。
                Require(canvas.renderMode != RenderMode.WorldSpace, "ステージ名が画面固定ではない");
                Require(titleRect.anchorMin == Vector2.one && titleRect.anchorMax == Vector2.one
                    && titleRect.pivot == Vector2.one, "ステージ名の右上アンカーが不正");
                Require(!image.raycastTarget && !titleGroup.blocksRaycasts, "ステージ名UIが入力を遮断する");
                Require(image.sprite != null && image.sprite.name == "ステージ名sprite_0", "指定Spriteを使用していない");
                UnityEngine.UI.Shadow shadow = image.GetComponent<UnityEngine.UI.Shadow>();
                Require(shadow != null && image.GetComponents<UnityEngine.UI.Shadow>().Length == 1
                    && shadow.effectColor == new Color(0f, 0f, 0f, 0.3f)
                    && shadow.effectDistance == new Vector2(2f, -2f) && shadow.useGraphicAlpha,
                    "共通PrefabのShadow設定が不正");
                pauseMenu = UnityEngine.Object.FindAnyObjectByType<PauseMenuController>();
                Require(image.preserveAspect && Vector2.Distance(titleRect.sizeDelta, new Vector2(945f, 211.5f)) < 0.1f,
                    "基準サイズの1.5倍・縦横比維持が不正");
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
                Require(!stageTitleUI.HasPlayed && titleGroup.alpha == 0f, "登場完了前にステージ名が表示された");
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
                return;
            }

            Require(player.InputEnabled && player.Rigidbody2D.simulated, "演出後に通常操作・物理へ戻らない");
            if (step == 0)
            {
                Require(sawJump && sawWalk, "シーケンスが欠落");
                Require(animations.Contains("JumpUp") && animations.Contains("JumpFall")
                    && animations.Contains("Walk") && animations.Contains("Idle"), "既存アニメーションへの遷移が欠落");
                Require(landings == 1, "着地イベントが1回ではない: " + landings);
                Require(Time.time - jumpStartedAt >= 1.1f && Time.time - jumpStartedAt <= 2f, "登場後に余分な操作禁止時間がある");
                Require(launcher.State == MorningStarLauncher.MorningStarState.Dragging, "保持した右スティックで即発射した");
                Require(player.IsGrounded, "停止位置で接地していない");
                Require(stageTitleUI.HasPlayed && !stageTitleUI.IsShowing && titleGroup.alpha == 0f
                    && !titleRect.GetComponent<UnityEngine.UI.Image>().enabled, "完了通知後の2秒待機がない");
                introCompletedAt = Time.time;
                // 登場中は意図的に上下へ移動するため、固定位置は保持フェーズで測る。
                completedAt = Time.time;
                stopPosition = player.transform.position;
                InputSystem.QueueStateEvent(pad, new GamepadState());
                step = 1;
            }
            else if (step == 1 && Time.time - completedAt > 0.12f)
            {
                Require(player.IsGrounded, "演出中のJump入力が復帰後に漏れた");
                Require(!stageTitleUI.IsShowing && titleGroup.alpha == 0f, "待機中に表示された");
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
                // 再通知されても5秒の表示を最初からやり直さない。
                stageTitleUI.ShowOnce();
                Require(pauseMenu != null, "Pause Menuがない");
                pauseMenu.OpenPause();
                Require(pauseMenu.IsPaused, "待機中にポーズできない");
                pauseStartedAt = EditorApplication.timeSinceStartup;
                pausedGameTime = Time.time;
                step = 4;
            }
            else if (step == 4)
            {
                if (pauseStep == 0 || pauseStep == 2)
                {
                    if (EditorApplication.timeSinceStartup - pauseStartedAt < 0.35d)
                        return;
                    Require(Mathf.Abs(Time.time - pausedGameTime) < 0.03f, "ポーズ中に時間が進んだ");
                    Require(pauseStep == 0 ? !stageTitleUI.IsShowing && titleGroup.alpha == 0f
                        : stageTitleUI.IsShowing && Mathf.Approximately(titleGroup.alpha, 1f), "ポーズ中に待機・表示が進んだ");
                    pauseMenu.ClosePause();
                    pauseStep++;
                    return;
                }
                if (titleStartedAt < 0f)
                {
                    float delayElapsed = Time.time - introCompletedAt;
                    if (!stageTitleUI.IsShowing)
                    {
                        Require(delayElapsed < 2.2f && titleGroup.alpha == 0f, "2秒後に表示が開始されない");
                        return;
                    }
                    Require(delayElapsed >= 1.95f && delayElapsed < 2.2f, "待機時間が演出完了から2秒ではない: " + delayElapsed);
                    titleStartedAt = Time.time;
                }
                float titleElapsed = Time.time - titleStartedAt;
                if (titleElapsed > 0.12f && titleElapsed < 0.35f && !sawFadeIn)
                {
                    Require(titleGroup.alpha > 0f && titleGroup.alpha < 1f, "0.4秒フェードインが不正");
                    Require(titleRect.anchoredPosition.y > -48f && titleRect.anchoredPosition.y <= 0f,
                        "登場時の上から下へのスライドが不正");
                    sawFadeIn = true;
                    Capture("fade-in");
                }
                if (titleElapsed > 0.8f && titleElapsed < 3.9f && !sawHold)
                {
                    titleScreenPosition = TitleScreenPosition();
                    Require(Vector2.Distance(titleRect.anchoredPosition, new Vector2(-48f, -48f)) < 0.1f,
                        "保持中に右上の定位置で停止していない");
                    Require(stageTitleUI.IsShowing && Mathf.Approximately(titleGroup.alpha, 1f), "保持中の透明度が不正");
                    sawHold = true;
                    Capture("stage-title");
                    pauseMenu.OpenPause();
                    pauseStartedAt = EditorApplication.timeSinceStartup;
                    pausedGameTime = Time.time;
                    pauseStep = 2;
                }
                if (titleElapsed > 4.3f && titleElapsed < 4.8f && !sawFadeOut)
                {
                    Require(stageTitleUI.IsShowing && titleGroup.alpha > 0f && titleGroup.alpha < 1f, "4秒以降のフェードアウトが不正");
                    Require(titleRect.anchoredPosition.y > -48f && titleRect.anchoredPosition.y <= 0f,
                        "退出時の下から上へのスライドが不正");
                    sawFadeOut = true;
                    Capture("fade-out");
                }
                if (sawHold && titleElapsed > 3.5f && titleElapsed < 3.9f)
                    Require(Vector2.Distance(titleScreenPosition, TitleScreenPosition()) < 0.5f,
                        "保持中の画面上の位置が変化した");
                if (titleElapsed > 5.15f)
                {
                    Require(sawFadeIn && sawHold && sawFadeOut, "フェードの確認フェーズが欠落");
                    Require(!stageTitleUI.IsShowing && titleGroup.alpha == 0f
                        && !titleRect.GetComponent<UnityEngine.UI.Image>().enabled, "約5秒で表示が非表示にならない");
                    stageTitleUI.ShowOnce();
                    stageTitleUI.enabled = false;
                    stageTitleUI.enabled = true;
                    Require(!stageTitleUI.IsShowing && titleGroup.alpha == 0f, "再通知・再有効化で二重表示された");
                    Require(Vector2.Distance(titleRect.sizeDelta, new Vector2(945f, 211.5f)) < 0.1f,
                        "再有効化で拡大が累積した");
                    Require(errors == 0, "新規Console Error/Exception: " + errors);
                    Finish("PASS: 2s delay after intro, input active while waiting; pause freezes delay/display; Shadow; fade/slide 5s; "
                        + "move/jump/right-stick/respawn restored; no duplicate/re-enable replay. Animations=" + string.Join(",", animations));
                }
            }
        }
        catch (Exception exception)
        {
            Finish("FAILED: " + exception.Message);
        }
    }

    private static void CountLanding() => landings++;
    private static Vector2 TitleScreenPosition()
    {
        // Editor.updateは描画前にも呼ばれるため、Camera描画Canvasの更新を確定してから測る。
        Canvas.ForceUpdateCanvases();
        Canvas canvas = stageTitleUI.GetComponent<Canvas>();
        return RectTransformUtility.WorldToScreenPoint(
            canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, titleRect.position);
    }
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
