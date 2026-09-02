using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class MorningStarRecallFacingPlayModeTest
{
    private const string RunningKey = "MorningStarRecallFacingPlayModeTest.Running";
    private const string ResultKey = "MorningStarRecallFacingPlayModeTest.Result";

    private static MorningStarLauncher launcher;
    private static Player player;
    private static Rigidbody2D ballBody;
    private static ChainConstraint2D constraint;
    private static SpriteRenderer bodySprite;
    private static Vector3 playerRootScale;
    private static double phaseStartedAt;
    private static int phase;
    private static int warnings;
    private static int errors;
    private static bool finished;

    static MorningStarRecallFacingPlayModeTest()
    {
        if (SessionState.GetBool(RunningKey, false))
            Subscribe();
    }

    public static void Run()
    {
        SessionState.SetBool(RunningKey, true);
        SessionState.EraseString(ResultKey);
        EditorSceneManager.OpenScene("Assets/Scenes/CompletScene.unity");
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
            Application.runInBackground = true;
            phase = 0;
            phaseStartedAt = EditorApplication.timeSinceStartup;
            warnings = 0;
            errors = 0;
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
            Debug.Log("[MorningStarRecallFacingTest] " + result);
            EditorApplication.Exit(result.StartsWith("PASS", StringComparison.Ordinal) ? 0 : 1);
        }
    }

    private static void CountLog(string message, string stack, LogType type)
    {
        if (stack != null && stack.Contains("UnityEditor.Search.SearchDatabase"))
            return;
        if (message != null && message.StartsWith("No graphic device is available", StringComparison.Ordinal))
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
            double elapsed = EditorApplication.timeSinceStartup - phaseStartedAt;
            switch (phase)
            {
                case 0:
                    if (elapsed < 1d)
                        return;
                    Setup();
                    BeginLaunch(Vector2.right);
                    NextPhase();
                    break;

                case 1:
                    if (!WaitForState(MorningStarLauncher.MorningStarState.Thrown, elapsed, 1.2d))
                        return;
                    CheckFacing(true, "right launch");
                    Require(launcher.IsLaunchPoseActive, "right Launch Animation pose was not active");
                    NextPhase();
                    break;

                case 2:
                    if (elapsed < 1.1d)
                        return;
                    Require(launcher.CurrentState == MorningStarLauncher.MorningStarState.Dragging,
                        $"finished flight remained in {launcher.CurrentState} instead of Rest/Dragging");
                    Require(!launcher.IsLaunchRopeLengthActive,
                        "finished flight retained the launch rope length");
                    Require(Mathf.Abs(launcher.MaxRopeLength - launcher.BaseMaxRopeLength) < 0.001f,
                        "Rest did not restore the base rope length");
                    Require(Mathf.Abs(constraint.MaxRopeLength - launcher.MaxRopeLength) < 0.001f,
                        "ChainConstraint did not keep the effective maximum length");
                    launcher.RequestReturn();
                    Require(launcher.CurrentState == MorningStarLauncher.MorningStarState.Returning,
                        "explicit Recall did not enter Returning");
                    NextPhase();
                    break;

                case 3:
                    if (!WaitForState(MorningStarLauncher.MorningStarState.Dragging, elapsed, 1.5d))
                        return;
                    BeginLaunch(Vector2.left);
                    NextPhase();
                    break;

                case 4:
                    if (!WaitForState(MorningStarLauncher.MorningStarState.Thrown, elapsed, 1.2d))
                        return;
                    CheckFacing(false, "left launch");
                    Require(launcher.IsLaunchPoseActive, "left Launch Animation pose was not active");
                    launcher.RequestReturn();
                    NextPhase();
                    break;

                case 5:
                    if (!WaitForState(MorningStarLauncher.MorningStarState.Dragging, elapsed, 1.5d))
                        return;
                    launcher.ResetForRespawn();
                    player.SetLaunchFacing(-1f, 0f);
                    BeginLaunch(Vector2.up);
                    NextPhase();
                    break;

                case 6:
                    if (!WaitForState(MorningStarLauncher.MorningStarState.Thrown, elapsed, 1.2d))
                        return;
                    CheckFacing(false, "vertical launch");
                    Require(launcher.IsLaunchPoseActive, "vertical Launch Animation pose was not active");
                    launcher.RequestReturn();
                    NextPhase();
                    break;

                case 7:
                    if (!WaitForState(MorningStarLauncher.MorningStarState.Dragging, elapsed, 1.5d))
                        return;
                    BeginMagnetTest();
                    NextPhase();
                    break;

                case 8:
                    if (elapsed < 0.8d)
                        return;
                    Require(launcher.CurrentState == MorningStarLauncher.MorningStarState.Swinging,
                        $"Magnet state auto-transitioned to {launcher.CurrentState}");
                    Require(launcher.IsHookedState, "Magnet Swing detached without explicit input");
                    Require(player.transform.localScale == playerRootScale,
                        "Player Root scale changed during facing tests");
                    Require(errors == 0, $"runtime errors={errors}");
                    Complete();
                    break;
            }
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private static void Setup()
    {
        launcher = UnityEngine.Object.FindAnyObjectByType<MorningStarLauncher>();
        player = UnityEngine.Object.FindAnyObjectByType<Player>();
        constraint = UnityEngine.Object.FindAnyObjectByType<ChainConstraint2D>();
        GameObject ball = GameObject.FindGameObjectWithTag("morningstar");
        ballBody = ball != null ? ball.GetComponent<Rigidbody2D>() : null;
        bodySprite = player != null ? GetBodySprite(player) : null;
        Require(launcher != null && player != null && constraint != null && ballBody != null && bodySprite != null,
            "required Player/MorningStar/Chain references are missing");

        DeathRespawnManager respawn = UnityEngine.Object.FindAnyObjectByType<DeathRespawnManager>();
        if (respawn != null)
            respawn.enabled = false;
        foreach (RespawnZone zone in UnityEngine.Object.FindObjectsByType<RespawnZone>())
            zone.enabled = false;

        SetMoveInput(Vector2.zero);
        playerRootScale = player.transform.localScale;
        launcher.ResetForRespawn();
    }

    private static void BeginLaunch(Vector2 direction)
    {
        launcher.ResetForRespawn();
        SetMoveInput(Vector2.zero);
        launcher.ApplyRecallThenLaunch(direction);
    }

    private static void BeginMagnetTest()
    {
        launcher.ResetForRespawn();
        GameObject magnetObject = new GameObject("__RecallFacingMagnet");
        MagnetPoint magnet = magnetObject.AddComponent<MagnetPoint>();
        Vector2 anchor = launcher.RopeAnchorWorld + Vector2.up * launcher.BaseMaxRopeLength;
        magnetObject.transform.position = anchor;
        Require(launcher.TryAttachToMagnet(magnet, ballBody, anchor), "Magnet attach failed");
        Require(launcher.CurrentState == MorningStarLauncher.MorningStarState.Swinging,
            "Magnet attach did not enter Swinging");
    }

    private static void CheckFacing(bool expectedRight, string context)
    {
        Require(player.FacingRight == expectedRight, $"{context}: Player facing state is wrong");
        Require(bodySprite.flipX == !expectedRight, $"{context}: SpriteRenderer.flipX is wrong");
        Require(player.transform.localScale == playerRootScale, $"{context}: Player Root scale changed");
    }

    private static bool WaitForState(MorningStarLauncher.MorningStarState expected, double elapsed, double timeout)
    {
        if (launcher.CurrentState == expected)
            return true;
        Require(elapsed < timeout, $"timed out waiting for {expected}; current={launcher.CurrentState}");
        return false;
    }

    private static SpriteRenderer GetBodySprite(Player target)
    {
        FieldInfo field = typeof(Player).GetField("_bodySprite", BindingFlags.Instance | BindingFlags.NonPublic);
        return field != null ? field.GetValue(target) as SpriteRenderer : null;
    }

    private static void SetMoveInput(Vector2 value)
    {
        FieldInfo field = typeof(Player).GetField("_moveInput", BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null)
            throw new MissingFieldException(typeof(Player).Name, "_moveInput");
        field.SetValue(player, value);
    }

    private static void NextPhase()
    {
        phase++;
        phaseStartedAt = EditorApplication.timeSinceStartup;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void Complete()
    {
        finished = true;
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= CountLog;
        SessionState.SetString(ResultKey,
            $"PASS flightToRest+manualRecall+rightLeftVerticalFacing+launchPose+magnetPersistence+rootScale; "
            + $"warnings={warnings}; errors={errors}");
        EditorApplication.isPlaying = false;
    }

    private static void Fail(Exception exception)
    {
        if (finished)
            return;
        finished = true;
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= CountLog;
        errors++;
        SessionState.SetString(ResultKey,
            $"FAILED: {exception.Message}; phase={phase}; warnings={warnings}; errors={errors}");
        Debug.LogException(exception);
        EditorApplication.isPlaying = false;
    }
}
