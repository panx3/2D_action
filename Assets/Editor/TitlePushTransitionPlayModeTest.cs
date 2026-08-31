using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class TitlePushTransitionPlayModeTest
{
    private const string RunningKey = "TitlePushTransitionPlayModeTest.Running";
    private const string ResultKey = "TitlePushTransitionPlayModeTest.Result";

    private static double startedAt;
    private static int phase;
    private static int warnings;
    private static int errors;
    private static bool sawTransition;
    private static bool finished;

    static TitlePushTransitionPlayModeTest()
    {
        if (SessionState.GetBool(RunningKey, false))
            Subscribe();
    }

    public static void Run()
    {
        SessionState.SetBool(RunningKey, true);
        SessionState.EraseString(ResultKey);
        EditorSceneManager.OpenScene("Assets/Scenes/TitleScene.unity");
        Subscribe();
        EditorApplication.isPlaying = true;
    }

    private static void Subscribe()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            Time.timeScale = 1f;
            startedAt = EditorApplication.timeSinceStartup;
            phase = 0;
            warnings = 0;
            errors = 0;
            sawTransition = false;
            finished = false;
            Application.logMessageReceived -= CountLog;
            Application.logMessageReceived += CountLog;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }
        else if (state == PlayModeStateChange.EnteredEditMode
                 && SessionState.GetBool(RunningKey, false))
        {
            SessionState.SetBool(RunningKey, false);
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            string result = SessionState.GetString(ResultKey, "FAILED: Play Mode ended early");
            Debug.Log("[TitlePushTransitionTest] " + result);
            EditorApplication.Exit(result.StartsWith("PASS", StringComparison.Ordinal) ? 0 : 1);
        }
    }

    private static void CountLog(string message, string stack, LogType type)
    {
        if (stack != null && stack.Contains("UnityEditor.Search.SearchDatabase"))
            return;
        if (type == LogType.Warning)
            warnings++;
        else if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            errors++;
    }

    private static void Tick()
    {
        if (finished)
            return;

        try
        {
            double elapsed = EditorApplication.timeSinceStartup - startedAt;
            if (phase == 0)
            {
                if (elapsed < 1.8d)
                    return;

                TitleScreenController title = UnityEngine.Object.FindAnyObjectByType<TitleScreenController>();
                Require(title != null && title.InputReady, "Title input did not become ready");
                title.OnStartPressed();
                title.OnStartPressed();
                Require(title.IsStarting, "START input guard was not latched");
                phase = 1;
                startedAt = EditorApplication.timeSinceStartup;
                return;
            }

            if (ScreenPushTransition.IsTransitioning)
                sawTransition = true;

            if (elapsed > 5d)
                throw new InvalidOperationException("Title push transition timed out");

            if (SceneManager.GetActiveScene().name != "CompletScene" || ScreenPushTransition.IsTransitioning)
                return;

            Require(sawTransition, "persistent capture transition was never active");
            Require(Mathf.Approximately(Time.timeScale, 1f), "Time.timeScale was not restored after transition");
            Require(errors == 0, "runtime errors were logged during transition: " + errors);
            Finish(true, "singleStart+captureOverlay+loadFirst+0.8sPush+timeScaleRestore; warnings="
                + warnings + "; errors=" + errors);
        }
        catch (Exception exception)
        {
            Finish(false, exception.Message + "; phase=" + phase + "; warnings=" + warnings + "; errors=" + errors);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void Finish(bool pass, string detail)
    {
        if (finished)
            return;
        finished = true;
        Application.logMessageReceived -= CountLog;
        EditorApplication.update -= Tick;
        Time.timeScale = 1f;
        SessionState.SetString(ResultKey, (pass ? "PASS " : "FAILED: ") + detail);
        EditorApplication.isPlaying = false;
    }
}
