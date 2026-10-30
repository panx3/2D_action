using System;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

/// <summary>One bounded smoke pass through real scenes and Input System events; preserves the editor scene setup.</summary>
[InitializeOnLoad]
public static class StageSelectFlowProbe
{
    private const string Key = "StageSelectFlowProbe.";
    private static double started, changedAt;
    private static int step, errors, warnings;
    private static bool capturing, sawOpening, sawZoom, sawLoading, sawReveal, sawIntro;
    private static Gamepad gamepad;
    private static Keyboard keyboard;
    private static string output;
    private static double openingVisibleAt;
    private static bool capturedBirth;
    static StageSelectFlowProbe() { if (SessionState.GetBool(Key + "Running", false)) Subscribe(); }

    public static string Run(string outputDirectory)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorUtility.scriptCompilationFailed)
            throw new InvalidOperationException("Run in Edit Mode with a clean compilation.");
        SessionState.SetString(Key + "PreviousStart", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetString(Key + "PreviousScene", SceneManager.GetActiveScene().path);
        SessionState.SetBool(Key + "PreviousDirty", SceneManager.GetActiveScene().isDirty);
        SessionState.SetString(Key + "Output", outputDirectory);
        SessionState.SetString(Key + "Result", "RUNNING"); SessionState.SetBool(Key + "Running", true);
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/TitleScene.unity");
        EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor")).Focus();
        Subscribe(); EditorApplication.isPlaying = true;
        return "Started bounded Title → Select → Title → Select → Stage1 probe; original edit scenes are not saved.";
    }
    public static string Result() => SessionState.GetString(Key + "Result", "NOT RUN");
    private static void Subscribe()
    { EditorApplication.playModeStateChanged -= State; EditorApplication.playModeStateChanged += State; }
    private static void State(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            output = SessionState.GetString(Key + "Output", ""); Directory.CreateDirectory(output);
            started = changedAt = EditorApplication.timeSinceStartup; step = errors = warnings = 0;
            sawOpening = sawZoom = sawLoading = sawReveal = sawIntro = capturing = false;
            openingVisibleAt = -1; capturedBirth = false;
            gamepad = InputSystem.AddDevice<Gamepad>(); keyboard = InputSystem.AddDevice<Keyboard>();
            Application.logMessageReceived += Log; EditorApplication.update += Tick;
        }
        else if (state == PlayModeStateChange.ExitingPlayMode)
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
            if (gamepad != null && gamepad.added) InputSystem.RemoveDevice(gamepad);
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Key + "PreviousStart", ""));
            SessionState.SetBool(Key + "Running", false); EditorApplication.playModeStateChanged -= State;
            bool preserved = SceneManager.GetActiveScene().path == SessionState.GetString(Key + "PreviousScene", "")
                && SceneManager.GetActiveScene().isDirty == SessionState.GetBool(Key + "PreviousDirty", false);
            string result = Result() + "; original edit scene/dirty state preserved=" + preserved;
            if (!preserved) result = "FAIL: " + result;
            SessionState.SetString(Key + "Result", result); Debug.Log("[StageSelectProbe] " + result);
        }
    }
    private static void Log(string message, string stack, LogType type)
    {
        if (stack != null && stack.Contains("UnityEditor.Search.SearchDatabase")) return;
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors++;
        if (type == LogType.Warning) warnings++;
    }
    private static void Next() { step++; changedAt = EditorApplication.timeSinceStartup; }
    private static double Elapsed => EditorApplication.timeSinceStartup - changedAt;
    private static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        try
        {
            Require(EditorApplication.timeSinceStartup - started < 100, "flow timed out at step " + step);
            Require(errors == 0, "runtime Error/Exception detected");
            if (capturing) return;
            var title = UnityEngine.Object.FindAnyObjectByType<TitleScreenController>();
            var select = UnityEngine.Object.FindAnyObjectByType<StageSelectController>();
            if (select != null)
            {
                sawOpening |= select.Phase == StageSelectController.SelectPhase.Opening;
                sawZoom |= select.Phase == StageSelectController.SelectPhase.Zooming;
                sawLoading |= select.Phase == StageSelectController.SelectPhase.Loading;
                sawReveal |= select.Phase == StageSelectController.SelectPhase.Revealing;
                if (step == 1 && select.Phase == StageSelectController.SelectPhase.Opening && !TitleStageTransition.IsTransitioning)
                {
                    if (openingVisibleAt < 0) openingVisibleAt = EditorApplication.timeSinceStartup;
                    if (!capturedBirth && EditorApplication.timeSinceStartup - openingVisibleAt > .64)
                    { capturedBirth = true; Capture(select, "00-crystal-birth"); return; }
                }
            }
            switch (step)
            {
                case 0:
                    if (title == null || !title.InputReady) return;
                    Require(title.StageSceneName == "StageSelectScene", "title route");
                    title.OnStartPressed(); title.OnStartPressed(); Next(); break;
                case 1:
                    if (select == null || !select.InputReady) return;
                    Require(sawOpening && select.SelectedStage.sceneName == "Stage1" && select.SelectedStage.CanLaunch, "opening/Stage1 cartridge");
                    Require(UnityEngine.Object.FindObjectsByType<CRTFilterController>(FindObjectsSortMode.None).Length == 0, "selection must not have fullscreen CRT");
                    Capture(select, "01-select-ready"); Next(); break;
                case 2:
                    InputSystem.QueueStateEvent(gamepad, new GamepadState { leftStick = Vector2.right }); Next(); break;
                case 3:
                    if (Elapsed < .09) return;
                    InputSystem.QueueStateEvent(gamepad, new GamepadState()); Next(); break;
                case 4:
                    if (!select.InputReady) return;
                    Require(select.SelectedIndex == 1 && !select.SelectedStage.CanLaunch, "stick/unavailable cartridge");
                    select.Confirm(); select.Confirm(); Require(select.InputReady, "unavailable confirm guard");
                    Capture(select, "02-machine-city"); Next(); break;
                case 5:
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(UnityEngine.InputSystem.Key.LeftArrow)); Next(); break;
                case 6:
                    if (Elapsed < .09) return;
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState()); Next(); break;
                case 7:
                    if (!select.InputReady) return;
                    Require(select.SelectedIndex == 0, "keyboard left");
                    InputSystem.QueueStateEvent(gamepad, new GamepadState().WithButton(GamepadButton.DpadLeft)); Next(); break;
                case 8:
                    if (Elapsed < .09) return;
                    InputSystem.QueueStateEvent(gamepad, new GamepadState()); Next(); break;
                case 9:
                    if (!select.InputReady) return;
                    Require(select.SelectedIndex == 1, "dpad wrap");
                    select.Move(-1); select.Move(1); select.Move(-1); Next(); break;
                case 10:
                    if (!select.InputReady) return;
                    Require(select.SelectedIndex == 1, "bounded last-direction queue");
                    select.Move(1); select.Back(); select.Back(); Next(); break;
                case 11:
                    if (title == null || !title.InputReady) return;
                    title.OnStartPressed(); Next(); break;
                case 12:
                    if (select == null || !select.InputReady) return;
                    if (select.SelectedIndex != 0) { select.Move(-1); return; }
                    select.Confirm(); select.Confirm();
                    Require(StageSelectController.IsEnteringStage && TitleStageTransition.IsTransitioning, "single entry/shared intro gate");
                    Next(); break;
                case 13:
                    if (select.Phase != StageSelectController.SelectPhase.Zooming) return;
                    if (Elapsed < 1.35) return;
                    Capture(select, "03-crt-zoom"); Next(); break;
                case 14:
                    if (SceneManager.GetActiveScene().name != "Stage1" || TitleStageTransition.IsTransitioning) return;
                    Require(sawZoom && sawLoading && sawReveal, "CRT zoom/load/reveal phases");
                    Require(!StageSelectController.IsEnteringStage && Mathf.Approximately(Time.timeScale, 1), "gate/time cleanup");
                    Require(UnityEngine.Object.FindObjectsByType<StageSelectController>(FindObjectsSortMode.None).Length == 0, "persistent theatre cleanup");
                    Next(); break;
                case 15:
                    var intro = UnityEngine.Object.FindAnyObjectByType<PlayerStartIntro>();
                    if (intro == null || intro.Phase != PlayerStartIntro.IntroPhase.Complete) return;
                    sawIntro = true;
                    Require(GameBgmController.Instance != null && GameBgmController.Instance.CurrentState == GameBgmController.BgmState.Stage, "stage BGM");
                    Require(UnityEngine.Object.FindObjectsByType<CRTFilterController>(FindObjectsSortMode.None).Length == 1, "one original gameplay CRT");
                    capturing = true; intro.StartCoroutine(Snapshot("04-stage1-start")); Next(); break;
                case 16:
                    SessionState.SetString(Key + "Result", "PASS: title/intro/stick/dpad/keyboard/wrap/rapid input/unavailable/back/insert/CRT entry/Stage1 intro/BGM/cleanup; errors=" + errors + "; warnings=" + warnings + "; entrance=" + sawIntro);
                    EditorApplication.isPlaying = false; break;
            }
        }
        catch (Exception exception)
        {
            SessionState.SetString(Key + "Result", "FAIL step " + step + ": " + exception.Message);
            Debug.LogException(exception); EditorApplication.isPlaying = false;
        }
    }
    private static void Capture(StageSelectController owner, string name)
    { capturing = true; owner.StartCoroutine(Snapshot(name)); }
    private static IEnumerator Snapshot(string name)
    {
        yield return new WaitForEndOfFrame();
        Texture2D texture = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(Path.Combine(output, name + ".png"), texture.EncodeToPNG());
        UnityEngine.Object.Destroy(texture); capturing = false;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
