using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>被弾を「下面と足元の支持面に挟まれた場合」に限定する回帰確認。</summary>
[InitializeOnLoad]
public static class FallingPressCrushChecks
{
    private const string Key = "FallingPressCrushChecks";
    private static readonly string[] Names = { "upper wait contact", "side graze", "light underside touch", "airborne contact", "rising contact", "floor pinch", "stand pinch" };
    private static readonly List<string> passed = new List<string>();
    private static MorningStarLauncher launcher;
    private static Player player;
    private static PlayerHealth health;
    private static Rigidbody2D playerBody, ball;
    private static Collider2D playerCollider;
    private static FallingPressController press;
    private static GameObject fixture;
    private static float began, minimumFeet;
    private static int phase, testCase, hpBefore, errors;
    private static bool background;
    private static double deadline;
    static FallingPressCrushChecks() { EditorApplication.playModeStateChanged += State; }
    [MenuItem("Tools/Falling Press/Run Crush Damage Checks")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        SessionState.SetBool(Key, true); SessionState.SetString(Key + ".Result", "Running"); EditorApplication.isPlaying = true;
    }
    public static string Result() => SessionState.GetString(Key + ".Result", "Not run");
    private static void State(PlayModeStateChange change)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            background = Application.runInBackground; Application.runInBackground = true;
            began = Time.time; phase = testCase = errors = 0; passed.Clear();
            deadline = EditorApplication.timeSinceStartup + 90;
            Application.logMessageReceived += Log; EditorApplication.update += Tick;
        }
        if (change == PlayModeStateChange.EnteredEditMode)
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
            SessionState.SetBool(Key, false); Debug.Log("[PressCrushChecks] " + Result());
        }
    }
    private static void Log(string message, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors++; }
    private static void Tick()
    {
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("timeout");
            if (phase == 0)
            {
                if (Time.time - began < 6f) return;
                launcher = UnityEngine.Object.FindAnyObjectByType<MorningStarLauncher>();
                player = launcher.GetComponent<Player>(); health = player.GetComponent<PlayerHealth>();
                playerBody = player.GetComponent<Rigidbody2D>(); ball = launcher.BallBody; playerCollider = player.GetComponent<Collider2D>();
                foreach (var existing in UnityEngine.Object.FindObjectsByType<FallingPressController>(FindObjectsSortMode.None)) existing.gameObject.SetActive(false);
                BeginCase(); phase = 1; return;
            }
            minimumFeet = Mathf.Min(minimumFeet, playerCollider.bounds.min.y);
            float duration = testCase == 3 ? 0.15f : 0.28f;
            if (testCase < 5 && health.CurrentHp != hpBefore)
                throw new Exception(Names[testCase] + " unexpectedly damaged Player; press=" + press.State + " feet=" + playerCollider.bounds.min.y);
            if (Time.time - began < duration) return;
            var squash = player.GetComponent<PressPlayerSquash>();
            if (testCase < 5)
            {
                Require(squash == null || squash.VerticalScale == 1f, Names[testCase] + " unexpectedly squashed Player");
                if (testCase == 0) Require(press.State == FallingPressController.CycleState.TopWait, "upper wait fixture changed phase");
                if (testCase == 2) Require(press.IsStandBlocked, "light touch fixture did not stop on raised stand");
                if (testCase == 4) Require(press.State == FallingPressController.CycleState.Rising, "rising fixture changed phase");
            }
            else
            {
                Require(health.CurrentHp == hpBefore - 1 && health.IsInvincible, Names[testCase] + " did not apply one protected damage");
                Require(squash != null && squash.VerticalScale < 0.85f, Names[testCase] + " did not squash Player");
                Require(minimumFeet > (testCase == 6 ? 1.82f : -0.08f), "Player embedded in support");
            }
            passed.Add(Names[testCase] + (testCase < 5 ? ": no damage/squash" : ": damage + squash, no embedding"));
            if (++testCase == Names.Length)
            {
                Require(errors == 0, "runtime errors=" + errors);
                Finish("PASS: " + string.Join("; ", passed) + "; runtime errors=0"); return;
            }
            BeginCase();
        }
        catch (Exception e) { Finish("FAIL " + Names[Mathf.Min(testCase, Names.Length - 1)] + ": " + e.Message); }
    }
    private static void BeginCase()
    {
        if (fixture != null) UnityEngine.Object.DestroyImmediate(fixture);
        var squash = player.GetComponent<PressPlayerSquash>(); if (squash != null) squash.ResetEffect();
        health.ResetToFullHp(); hpBefore = health.CurrentHp;
        fixture = new GameObject("__PressCrushFixture");
        var floor = new GameObject("Floor"); floor.transform.SetParent(fixture.transform, false);
        floor.transform.localPosition = new Vector2(1000f, -0.5f); floor.AddComponent<BoxCollider2D>().size = new Vector2(24f, 1f);
        var pressRoot = new GameObject("PressRoot"); pressRoot.transform.SetParent(fixture.transform, false); pressRoot.SetActive(false);
        var go = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(FallingPressSetup.PressPath), pressRoot.transform, false);
        go.transform.localPosition = new Vector2(1000f, 0f); press = go.GetComponent<FallingPressController>();
        var settings = new SerializedObject(press);
        settings.FindProperty("topWait").floatValue = 1f; settings.FindProperty("warningTime").floatValue = 0.2f;
        settings.FindProperty("bottomWait").floatValue = 0.7f; settings.FindProperty("ascentSpeed").floatValue = 6f;
        settings.FindProperty("knockback").vector2Value = Vector2.zero;
        // Cycle=1+.2+.5+.7+1=3.4秒。下降途中/上昇途中から始めて対象状態を直接確認する。
        settings.FindProperty("startPhase").floatValue = testCase == 0 ? 0f : testCase == 4 ? 2.6f / 3.4f : 1.55f / 3.4f;
        settings.ApplyModifiedPropertiesWithoutUndo();
        if (testCase == 2 || testCase == 6)
        {
            var stand = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(FallingPressSetup.StandPath), fixture.transform, false);
            stand.transform.localPosition = new Vector2(1000f, testCase == 2 ? 1.3f : 0f);
        }
        Vector2 position = testCase == 0 ? new Vector2(1000f, 7.06f) : testCase == 1 ? new Vector2(1003.65f, 2.06f) :
            testCase == 2 ? new Vector2(1002.7f, 2.06f) : testCase == 3 ? new Vector2(1000f, 3.06f) :
            testCase == 6 ? new Vector2(1000f, 3.96f) : new Vector2(1000f, 2.06f);
        Move(playerBody, position); launcher.ResetForRespawn();
        Move(ball, new Vector2(position.x - 5f, position.y - 1.2f));
        Physics2D.SyncTransforms(); pressRoot.SetActive(true); Physics2D.SyncTransforms();
        began = Time.time; minimumFeet = playerCollider.bounds.min.y;
    }
    private static void Move(Rigidbody2D body, Vector2 position)
    { body.position = position; body.transform.position = new Vector3(position.x, position.y, body.transform.position.z); body.linearVelocity = Vector2.zero; }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Finish(string result)
    {
        EditorApplication.update -= Tick; Application.logMessageReceived -= Log; Application.runInBackground = background;
        SessionState.SetString(Key + ".Result", result); EditorApplication.isPlaying = false;
    }
}
