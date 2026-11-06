using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>実Playerがプレスの実体を通れず、下降で挟まれた場合だけ被弾することを確認。</summary>
[InitializeOnLoad]
public static class FallingPressCrushChecks
{
    private const string Key = "FallingPressCrushChecks";
    private static readonly string[] Names = { "upper underside collision", "closed gap side collision", "press top grounding", "floor pinch", "stand pinch", "floor ball stops press", "almost tall enough gap" };
    private static readonly List<string> passed = new List<string>();
    private static MorningStarLauncher launcher;
    private static Player player;
    private static PlayerHealth health;
    private static Rigidbody2D playerBody, ball;
    private static Collider2D playerCollider;
    private static FallingPressController press;
    private static GameObject fixture;
    private static Gamepad pad;
    private static float began, minimumFeet, maximumHead;
    private static Vector2 originalSize;
    private static Vector3 originalScale;
    private static int phase, testCase, hpBefore, errors;
    private static bool background, squashSeen;
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
                playerBody = player.Rigidbody2D; ball = launcher.BallBody; playerCollider = player.GetComponent<Collider2D>();
                originalSize = ((CapsuleCollider2D)playerCollider).size; originalScale = player.transform.localScale;
                pad = InputSystem.AddDevice<Gamepad>();
                foreach (var existing in UnityEngine.Object.FindObjectsByType<FallingPressController>(FindObjectsSortMode.None)) existing.gameObject.SetActive(false);
                BeginCase(); phase = 1; return;
            }
            minimumFeet = Mathf.Min(minimumFeet, playerCollider.bounds.min.y);
            maximumHead = Mathf.Max(maximumHead, playerCollider.bounds.max.y);
            var squash = player.GetComponent<PressPlayerSquash>();
            squashSeen |= squash != null && squash.VerticalScale < 0.9f && squash.HorizontalScale > 1f;
            bool pinch = testCase == 3 || testCase == 4;
            if (!pinch) Require(health.CurrentHp == hpBefore && !squashSeen, "unexpected damage/squash");
            Require(((CapsuleCollider2D)playerCollider).size == originalSize && player.transform.localScale == originalScale, "Player collider/root resized");
            float duration = pinch || testCase == 5 ? 1.15f : testCase == 0 ? 0.3f : 0.8f;
            if (Time.time - began < duration) return;
            if (testCase == 0) Require(maximumHead <= press.UndersideY + 0.06f && maximumHead > press.UndersideY - 0.12f, "jump passed through/missed underside: " + maximumHead);
            if (testCase == 1 || testCase == 6) Require(playerBody.position.x < 996.4f && playerBody.position.x > 995.7f, "Player entered already closed gap: " + playerBody.position);
            if (testCase == 2) Require(player.IsGrounded && Mathf.Abs(playerCollider.bounds.min.y - (press.UndersideY + 2f)) < 0.08f, "not grounded on press top");
            if (pinch)
            {
                Require(health.CurrentHp == hpBefore - 1 && health.IsInvincible && squashSeen, "no protected crush damage/squash; state=" + press.State + " head=" + press.UndersideY + " player=" + playerBody.position);
                Require(minimumFeet > (testCase == 4 ? 1.82f : -0.08f), "Player embedded in support");
                Require(testCase == 4 ? press.IsStandBlocked && press.CurrentClearance < 2.05f : press.State == FallingPressController.CycleState.BottomWait && press.CurrentClearance < 0.08f,
                    "Player stopped press before bottom: " + press.State + "/" + press.CurrentClearance);
                float spriteHeight = player.BodySprite.sprite.bounds.size.y * Mathf.Abs(player.BodySprite.transform.lossyScale.y);
                float ballHeight = ball.GetComponent<Collider2D>().bounds.size.y;
                Require(squash != null && Mathf.Abs(squash.VerticalScale * spriteHeight - ballHeight) < 0.08f,
                    "squash height does not match ball diameter");
            }
            if (testCase == 5) Require(press.IsBallBlocked && press.CurrentClearance > 1.6f && press.CurrentClearance < 1.9f && ball.GetComponent<Collider2D>().bounds.min.y > -0.1f, "floor ball did not stop press or embedded in floor");
            passed.Add(Names[testCase] + ": PASS");
            if (++testCase == Names.Length)
            {
                Require(errors == 0, "runtime errors=" + errors);
                Finish("PASS: " + string.Join("; ", passed) + "; no collider resize/embedding; runtime errors=0"); return;
            }
            BeginCase();
        }
        catch (Exception e) { Finish("FAIL " + Names[Mathf.Min(testCase, Names.Length - 1)] + ": " + e.Message + "; passed=" + string.Join("; ", passed)); }
    }
    private static void BeginCase()
    {
        InputSystem.QueueStateEvent(pad, new GamepadState());
        if (fixture != null) UnityEngine.Object.DestroyImmediate(fixture);
        var squash = player.GetComponent<PressPlayerSquash>(); if (squash != null) squash.ResetEffect();
        health.ResetToFullHp(); hpBefore = health.CurrentHp; squashSeen = false;
        fixture = new GameObject("__PressCrushFixture");
        var floor = new GameObject("Floor"); floor.transform.SetParent(fixture.transform, false);
        floor.transform.localPosition = new Vector2(1000f, -0.5f); floor.AddComponent<BoxCollider2D>().size = new Vector2(24f, 1f);
        var pressRoot = new GameObject("PressRoot"); pressRoot.transform.SetParent(fixture.transform, false); pressRoot.SetActive(false);
        var go = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(FallingPressSetup.PressPath), pressRoot.transform, false);
        go.transform.localPosition = new Vector2(1000f, testCase == 6 ? 3.2f : 0f); press = go.GetComponent<FallingPressController>();
        var settings = new SerializedObject(press);
        settings.FindProperty("topWait").floatValue = testCase == 0 || testCase == 2 ? 10f : 0.4f;
        settings.FindProperty("warningTime").floatValue = 0.2f;
        settings.FindProperty("bottomWait").floatValue = 5f; settings.FindProperty("ascentSpeed").floatValue = 6f;
        settings.FindProperty("knockback").vector2Value = Vector2.zero;
        settings.FindProperty("startPhase").floatValue = testCase == 1 || testCase == 6 ? 0.3f : 0f;
        settings.ApplyModifiedPropertiesWithoutUndo();
        if (testCase == 1 || testCase == 4)
        {
            var stand = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(FallingPressSetup.StandPath), fixture.transform, false);
            stand.transform.localPosition = new Vector2(1000f, 0f);
        }
        Vector2 position = testCase == 0 ? new Vector2(1000f, 4.75f) : testCase == 2 ? new Vector2(1000f, 10.1f) :
            testCase == 4 ? new Vector2(1000f, 3.96f) : testCase == 5 ? new Vector2(1004.5f, 2.06f) :
            testCase == 1 || testCase == 6 ? new Vector2(995.5f, 2.06f) : new Vector2(1000f, 2.06f);
        Move(playerBody, position); launcher.ResetForRespawn();
        Move(ball, testCase == 5 ? new Vector2(1000f, 0.86f) : new Vector2(position.x - 2f, position.y - 1.2f));
        // 鉄球はPlayerとは反対側へ。落下経路の支点として使うのは専用のケースのみ。
        if (testCase == 3 || testCase == 4) Move(ball, new Vector2(995f, 0.86f));
        Physics2D.SyncTransforms(); pressRoot.SetActive(true); Physics2D.SyncTransforms();
        if (testCase == 0)
        {
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.South));
            playerBody.linearVelocity = Vector2.up * 10f;
        }
        if (testCase == 1 || testCase == 6)
        { press.enabled = false; InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = Vector2.right }); }
        began = Time.time; minimumFeet = playerCollider.bounds.min.y; maximumHead = playerCollider.bounds.max.y;
    }
    private static void Move(Rigidbody2D body, Vector2 position)
    { body.position = position; body.transform.position = new Vector3(position.x, position.y, body.transform.position.z); body.linearVelocity = Vector2.zero; }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Finish(string result)
    {
        EditorApplication.update -= Tick; Application.logMessageReceived -= Log; Application.runInBackground = background;
        if (pad != null && pad.added) InputSystem.RemoveDevice(pad);
        SessionState.SetString(Key + ".Result", result); EditorApplication.isPlaying = false;
    }
}
