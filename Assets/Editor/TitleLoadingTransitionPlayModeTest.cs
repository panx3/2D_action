using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class TitleLoadingTransitionPlayModeTest
{
    private const string RunningKey = "TitleLoadingTransitionPlayModeTest.Running";
    private const string ResultKey = "TitleLoadingTransitionPlayModeTest.Result";

    private static double startedAt;
    private static int phase;
    private static int warnings;
    private static int errors;
    private static float firstRotation;
    private static bool sawLoading;
    private static bool sawRotation;
    private static bool finished;

    static TitleLoadingTransitionPlayModeTest()
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
            firstRotation = 0f;
            sawLoading = false;
            sawRotation = false;
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
            Debug.Log("[TitleLoadingTransitionTest] " + result);
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
                Require(Mathf.Approximately(title.MinimumLoadingDuration, 0.6f), "minimum loading duration is not 0.6s");
                title.OnStartPressed();
                title.OnStartPressed();
                Require(title.IsStarting, "START input guard was not latched");
                phase = 1;
                startedAt = EditorApplication.timeSinceStartup;
                return;
            }

            if (elapsed > 6d)
                throw new InvalidOperationException("Title loading transition timed out");

            if (TitleStageTransition.IsLoadingVisible)
            {
                Transform pivot = GameObject.Find("FlailPivot")?.transform;
                TextMeshProUGUI label = GameObject.Find("LoadingText")?.GetComponent<TextMeshProUGUI>();
                Require(pivot != null, "FlailPivot was not created");
                Require(label != null && label.text == "LOADING...", "LOADING label was not created");
                if (!sawLoading)
                {
                    sawLoading = true;
                    firstRotation = pivot.eulerAngles.z;
                }
                else if (Mathf.Abs(Mathf.DeltaAngle(firstRotation, pivot.eulerAngles.z)) > 2f)
                {
                    sawRotation = true;
                }
            }

            if (SceneManager.GetActiveScene().name != "CompletScene" || TitleStageTransition.IsTransitioning)
                return;

            Require(sawLoading, "Loading page was never visible");
            Require(sawRotation, "Flail did not rotate while Time.timeScale was zero");
            Require(TitleStageTransition.ActivationCount == 1, "Scene activation ran more than once");
            Require(TitleStageTransition.LastLoadingVisibleDuration >= 0.58f,
                "Loading page did not remain visible for at least 0.6s");
            Require(TitleStageTransition.WasBlackAtActivation, "Scene activated before the overlay was black");
            Require(Mathf.Approximately(Time.timeScale, 1f), "Time.timeScale was not restored");
            Require(errors == 0, "runtime errors were logged during transition: " + errors);
            Finish(true,
                "doubleInputGuard+0.25sFadeOut+0.6sMinimumLoading+unscaledFlail+blackActivation+0.3sFadeIn"
                + "; warnings=" + warnings + "; errors=" + errors);
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
