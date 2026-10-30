using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>保存Sceneを変更せず、遠方の実Player/鉄球で接続を検証する。</summary>
[InitializeOnLoad]
public static class FallingPressPlayModeChecks
{
    private const string Key = "FallingPressChecks";
    private static FallingPressController press, second;
    private static MorningStarLauncher launcher;
    private static Rigidbody2D playerBody, ball;
    private static PlayerHealth hp;
    private static Gamepad pad;
    private static float began, phaseTime, mass, gravity, damping;
    private static double deadline;
    private static int phase, errors, initialHp;
    private static bool warningSeen, bottomSeen, differentSeen, wasBackground, launchSeen, launchRequested;
    private static RigidbodyConstraints2D constraints;
    private static readonly System.Collections.Generic.List<string> passed = new System.Collections.Generic.List<string>();

    static FallingPressPlayModeChecks() { EditorApplication.playModeStateChanged += OnState; }
    [MenuItem("Tools/Falling Press/Run Play Mode Checks")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        FallingPressSetup.Validate();
        SessionState.SetBool(Key, true); SessionState.SetString(Key + ".Result", "Running");
        EditorApplication.isPlaying = true;
    }
    public static string Result() => SessionState.GetString(Key + ".Result", "Not run");
    private static void OnState(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            wasBackground = Application.runInBackground; Application.runInBackground = true;
            began = phaseTime = Time.time; phase = errors = 0; passed.Clear();
            warningSeen = bottomSeen = differentSeen = launchSeen = launchRequested = false;
            deadline = EditorApplication.timeSinceStartup + 150d;
            Application.logMessageReceived += Log; EditorApplication.update += Tick;
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
            SessionState.SetBool(Key, false); Debug.Log("[FallingPressChecks] " + Result());
        }
    }
    private static void Log(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors++;
    }
    private static void Tick()
    {
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Timed out; phase=" + phase + ", time=" + Time.time);
            float t = Time.time - phaseTime;
            if (phase == 0)
            {
                if (Time.time - began < 6f) return;
                Setup(); Next();
            }
            else if (phase == 1)
            {
                warningSeen |= press.State == FallingPressController.CycleState.Warning;
                bottomSeen |= press.State == FallingPressController.CycleState.BottomWait && press.CurrentClearance < 0.1f;
                differentSeen |= second.State != press.State;
                if (!bottomSeen || press.State != FallingPressController.CycleState.TopWait) return;
                Require(warningSeen && differentSeen, "warning or independent timing missing");
                Pass("empty cycle/warning/ascent + two independent pairs");
                PlaceBall(); Next();
            }
            else if (phase == 2)
            {
                if (!press.IsBallBlocked) { Require(t < 8f, "ball on stand did not block; occupied=" + press.BallStand.IsOccupied + " ball=" + ball.position); return; }
                Require(press.HasSafeClearance && press.CurrentClearance >= 3.5f, "insufficient clearance");
                Require(ball.GetComponent<Collider2D>().bounds.min.y >= 1.78f, "ball crushed below support");
                Require(ball.mass == mass && ball.gravityScale == gravity && ball.linearDamping == damping && ball.constraints == constraints,
                    "Rest physics changed");
                Require(!press.BallStand.TryClaim(second), "another press claimed the same stand");
                Pass("correct stand blocks at >=3.5; ball intact; Rest settings unchanged; exclusive pairing");
                initialHp = hp.CurrentHp;
                Set(press, "bottomWait", 4f);
                playerBody.position = new Vector2(996.05f, 2.06f); playerBody.linearVelocity = Vector2.zero;
                InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = Vector2.right }); Next();
            }
            else if (phase == 3)
            {
                Require(hp.CurrentHp == initialHp, "damage during safe passage at " + playerBody.position);
                if (playerBody.position.x < 1004f) { Require(t < 3f, "Player could not pass through stand/head; x=" + playerBody.position.x); return; }
                Pass("actual Player runs left-to-right safely under supported press");
                InputSystem.QueueStateEvent(pad, new GamepadState());
                ball.position = new Vector2(1005f, 0.85f); ball.linearVelocity = Vector2.zero;
                playerBody.position = new Vector2(1004f, 2.06f); playerBody.linearVelocity = Vector2.zero;
                Next();
            }
            else if (phase == 4)
            {
                if (press.IsBallBlocked) { Require(t < 1f, "did not resume after ball left stand"); return; }
                Require(press.State == FallingPressController.CycleState.Descending || press.State == FallingPressController.CycleState.BottomWait,
                    "unexpected resume state " + press.State);
                Pass("removing ball resumes descent");
                playerBody.position = new Vector2(1000f, press.UndersideY + 1f); playerBody.linearVelocity = Vector2.zero;
                Next();
            }
            else if (phase == 5)
            {
                if (hp.CurrentHp == initialHp) { Require(t < 1f, "press contact did not damage Player"); return; }
                Require(hp.CurrentHp == initialHp - 1, "damage did not use existing invulnerability");
                Pass("Player contact uses existing HP/invulnerability");
                launcher.ResetForRespawn(); playerBody.position = new Vector2(990f, 2.06f);
                ball.position = new Vector2(989f, 0.85f); ball.linearVelocity = Vector2.zero;
                Physics2D.SyncTransforms(); Next();
            }
            else if (phase == 6)
            {
                if (!launchRequested && t > 0.3f)
                {
                    launchRequested = true;
                    InputSystem.QueueStateEvent(pad, new GamepadState { rightStick = Vector2.right });
                }
                if (!launchSeen) { Require(t < 3f, "right-stick launch did not fire; state=" + launcher.State); return; }
                InputSystem.QueueStateEvent(pad, new GamepadState());
                launcher.RequestReturn();
                Require(launcher.State == MorningStarLauncher.MorningStarState.Returning, "Recall did not enter Returning"); Next();
            }
            else if (phase == 7)
            {
                if (launcher.State != MorningStarLauncher.MorningStarState.Dragging) { Require(t < 3f, "Recall did not reach Rest"); return; }
                Require(Mathf.Abs(ball.mass - launcher.RestBallMass) < 0.001f && Mathf.Abs(ball.gravityScale - launcher.RestBallGravityScale) < 0.001f,
                    "Recall Rest settings mismatch");
                Pass("right-stick launch -> Recall -> Dragging and Rest physics");
                var magnet = new GameObject("__PressCheckMagnet").AddComponent<MagnetPoint>();
                Vector2 anchor = launcher.RopeAnchorWorld + Vector2.up * 4f;
                magnet.transform.position = anchor;
                Require(launcher.TryAttachToMagnet(magnet, ball, anchor) && launcher.State == MorningStarLauncher.MorningStarState.Swinging,
                    "existing Magnet/Swing attach failed");
                Pass("existing Magnet/Swing transition");
                launcher.RequestReturn(); launcher.ResetForRespawn();
                ball.position = new Vector2(1001.3f, 2.76f); Physics2D.SyncTransforms();
                Require(!press.BallStand.TryGetSupport(out _), "off-center ball accepted as support");
                PlaceBall();
                var settings = new SerializedObject(press); settings.FindProperty("ballStopEnabled").boolValue = false;
                settings.ApplyModifiedPropertiesWithoutUndo(); Next();
            }
            else if (phase == 8)
            {
                Require(!press.IsBallBlocked, "disabled ball stop still blocked");
                if (press.State != FallingPressController.CycleState.BottomWait) { Require(t < 9f, "disabled press did not fully descend"); return; }
                Pass("off-center ball rejected; disabled ball-stop reaches bottom");
                Require(errors == 0, "new runtime errors=" + errors);
                Finish("PASS: " + string.Join("; ", passed) + "; runtime errors=0");
            }
        }
        catch (Exception e) { Finish("FAIL phase " + phase + ": " + e.Message + "; passed=" + string.Join("; ", passed)); }
    }
    private static void Setup()
    {
        launcher = UnityEngine.Object.FindAnyObjectByType<MorningStarLauncher>();
        hp = launcher.GetComponent<PlayerHealth>(); playerBody = launcher.GetComponent<Rigidbody2D>(); ball = launcher.BallBody;
        foreach (var p in UnityEngine.Object.FindObjectsByType<FallingPressController>(FindObjectsSortMode.None)) p.gameObject.SetActive(false);
        var floor = new GameObject("__PressCheckFloor"); floor.transform.position = new Vector2(1005f, -0.5f);
        floor.AddComponent<BoxCollider2D>().size = new Vector2(50f, 1f);
        press = Fixture(1000f); second = Fixture(1020f); Set(second, "topWait", 1.1f);
        Require(press.BallStand != second.BallStand, "pairs share stand reference");
        launcher.ResetForRespawn(); hp.ResetToFullHp();
        playerBody.position = new Vector2(990f, 2.06f); playerBody.linearVelocity = Vector2.zero;
        ball.position = new Vector2(989f, 0.85f); ball.linearVelocity = Vector2.zero;
        mass = ball.mass; gravity = ball.gravityScale; damping = ball.linearDamping; constraints = ball.constraints;
        pad = InputSystem.AddDevice<Gamepad>(); launcher.LaunchCommitted += OnLaunch;
        Physics2D.SyncTransforms();
    }
    private static FallingPressController Fixture(float x)
    {
        var go = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(FallingPressSetup.PressPath), new Vector3(x, 0f, 0f), Quaternion.identity);
        var stand = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(FallingPressSetup.StandPath), new Vector3(x, 0f, 0f), Quaternion.identity);
        var p = go.GetComponent<FallingPressController>(); Set(p, "topWait", 0.3f); Set(p, "warningTime", 0.25f);
        var so = new SerializedObject(p); so.FindProperty("ballStand").objectReferenceValue = stand.GetComponent<PressBallStandController>();
        so.ApplyModifiedPropertiesWithoutUndo();
        Set(p, "bottomWait", 0.8f); Set(p, "ascentSpeed", 10f); return p;
    }
    private static void PlaceBall()
    {
        launcher.ResetForRespawn(); playerBody.position = new Vector2(998f, 2.06f); playerBody.linearVelocity = Vector2.zero;
        ball.position = new Vector2(1000f, 2.76f); ball.linearVelocity = Vector2.zero; Physics2D.SyncTransforms();
    }
    private static void Set(UnityEngine.Object target, string name, float value)
    {
        var so = new SerializedObject(target); so.FindProperty(name).floatValue = value; so.ApplyModifiedPropertiesWithoutUndo();
    }
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static void OnLaunch(float charge) { launchSeen = true; }
    private static void Pass(string message) { passed.Add(message); Debug.Log("[FallingPressChecks] " + message); }
    private static void Next() { phase++; phaseTime = Time.time; }
    private static void Finish(string result)
    {
        EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
        if (launcher != null) launcher.LaunchCommitted -= OnLaunch;
        if (pad != null && pad.added) InputSystem.RemoveDevice(pad);
        Application.runInBackground = wasBackground;
        SessionState.SetString(Key + ".Result", result); EditorApplication.isPlaying = false;
    }
}
