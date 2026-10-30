using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>実Player/鉄球と独立Prefabを使い、物理的な支持・衝突・表示を確認する。</summary>
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
    private static bool warningSeen, bottomSeen, differentSeen, standSeen, occupiedSeen, wasBackground, launchSeen, launchRequested;
    private static RigidbodyConstraints2D constraints;
    private static readonly List<string> passed = new List<string>();

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
            warningSeen = bottomSeen = differentSeen = standSeen = occupiedSeen = launchSeen = launchRequested = false;
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
            switch (phase)
            {
                case 0:
                    if (Time.time - began < 6f) return;
                    Setup(); Next(); break;
                case 1:
                    warningSeen |= press.State == FallingPressController.CycleState.Warning;
                    bottomSeen |= press.State == FallingPressController.CycleState.BottomWait && press.CurrentClearance < 0.1f;
                    differentSeen |= second.State != press.State;
                    if (!bottomSeen || press.State != FallingPressController.CycleState.TopWait) return;
                    Require(warningSeen && differentSeen, "warning or independent timing missing");
                    Pass("no obstacle: full cycle/warning/ascent; two independent presses");
                    press.BallStand.gameObject.SetActive(true); Next(); break;
                case 2:
                    if (press.IsStandBlocked)
                    {
                        Require(press.CurrentClearance >= 1.88f && press.CurrentClearance < 2.05f && !press.HasSafeClearance,
                            "stand-only contact height incorrect: " + press.CurrentClearance);
                        if (!standSeen) Pass("empty stand physically stops press; insufficient clearance still stops");
                        standSeen = true;
                    }
                    if (!standSeen || press.State != FallingPressController.CycleState.Rising || press.CurrentClearance < 4.5f)
                    { Require(t < 8f, "press did not hit standalone stand and rise"); return; }
                    PlaceBall(true); Next(); break;
                case 3:
                    occupiedSeen |= press.BallStand.IsOccupied && press.BallStand.IsReadyMarkVisible;
                    if (!press.IsBallBlocked) { Require(t < 8f, "ball on stand did not block; ball=" + ball.position); return; }
                    Require(occupiedSeen && press.HasSafeClearance, "contact-based ready mark or safe clearance missing");
                    Require(ball.GetComponent<Collider2D>().bounds.min.y >= 1.78f, "ball crushed below stand");
                    CheckRest();
                    Pass("actual stand contact lights frame/check mark; ball stops press at safe clearance; Rest unchanged");
                    initialHp = hp.CurrentHp; Set(press, "bottomWait", 4f);
                    playerBody.position = new Vector2(996.05f, 2.06f); playerBody.linearVelocity = Vector2.zero;
                    InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = Vector2.right }); Next(); break;
                case 4:
                    Require(hp.CurrentHp == initialHp, "damage during safe passage at " + playerBody.position);
                    if (playerBody.position.x < 1004f) { Require(t < 3f, "Player could not pass; x=" + playerBody.position.x); return; }
                    Pass("actual Player safely runs across supported press/stand");
                    InputSystem.QueueStateEvent(pad, new GamepadState());
                    ball.position = new Vector2(1008f, 0.85f); ball.linearVelocity = Vector2.zero;
                    playerBody.position = new Vector2(1004f, 2.06f); playerBody.linearVelocity = Vector2.zero;
                    Next(); break;
                case 5:
                    if (!press.IsStandBlocked) { Require(t < 2f, "removing ball did not resume descent onto stand"); return; }
                    Require(!press.BallStand.IsOccupied && !press.BallStand.IsReadyMarkVisible, "ready feedback did not clear");
                    Pass("ball removal clears feedback and press descends until actual stand contact");
                    playerBody.position = new Vector2(1000f, press.UndersideY + 1f); playerBody.linearVelocity = Vector2.zero;
                    Next(); break;
                case 6:
                    if (hp.CurrentHp == initialHp) { Require(t < 1f, "press contact did not damage Player"); return; }
                    Require(hp.CurrentHp == initialHp - 1, "damage did not use existing invulnerability");
                    Pass("Player contact uses existing HP/invulnerability");
                    launcher.ResetForRespawn(); playerBody.position = new Vector2(990f, 2.06f); playerBody.linearVelocity = Vector2.zero;
                    ball.position = new Vector2(989f, 0.85f); ball.linearVelocity = Vector2.zero;
                    Physics2D.SyncTransforms(); Next(); break;
                case 7:
                    if (!launchRequested && t > 0.3f)
                    {
                        launchRequested = true;
                        InputSystem.QueueStateEvent(pad, new GamepadState { rightStick = Vector2.right });
                    }
                    if (!launchSeen) { Require(t < 3f, "right-stick launch did not fire; state=" + launcher.State); return; }
                    InputSystem.QueueStateEvent(pad, new GamepadState()); launcher.RequestReturn();
                    Require(launcher.State == MorningStarLauncher.MorningStarState.Returning, "Recall did not enter Returning"); Next(); break;
                case 8:
                    if (launcher.State != MorningStarLauncher.MorningStarState.Dragging) { Require(t < 3f, "Recall did not reach Rest"); return; }
                    CheckRest(); Pass("right-stick launch -> Recall -> Dragging; Rest physics unchanged");
                    var magnet = new GameObject("__PressCheckMagnet").AddComponent<MagnetPoint>();
                    Vector2 anchor = launcher.RopeAnchorWorld + Vector2.up * 4f; magnet.transform.position = anchor;
                    Require(launcher.TryAttachToMagnet(magnet, ball, anchor) && launcher.State == MorningStarLauncher.MorningStarState.Swinging,
                        "existing Magnet/Swing attach failed");
                    Pass("existing Magnet/Swing transition"); launcher.ResetForRespawn();
                    // 補間Transform/鎖アンカーが移動先に追従してから床の鉄球を置く。
                    playerBody.position = new Vector2(995.9f, 2.06f);
                    playerBody.transform.position = new Vector3(995.9f, 2.06f, playerBody.transform.position.z);
                    playerBody.linearVelocity = Vector2.zero;
                    press.BallStand.gameObject.SetActive(false); Next(); break;
                case 9:
                    if (press.CurrentClearance < 4.5f) { Require(t < 8f, "press did not rise for floor-ball check"); return; }
                    PlaceBall(false); Next(); break;
                case 10:
                    if (!press.IsBallBlocked) { Require(t < 8f, "floor-ball stop missing; head=" + press.State + "/" + press.CurrentClearance + " ball=" + ball.position + " state=" + launcher.State + " player=" + playerBody.position + " simulated=" + ball.simulated); return; }
                    Require(press.CurrentClearance > 1.6f && press.CurrentClearance < 1.9f && !press.HasSafeClearance,
                        "floor-ball stop height incorrect " + press.CurrentClearance);
                    Require(ball.GetComponent<Collider2D>().bounds.min.y > -0.12f, "floor ball crushed");
                    CheckRest(); Pass("ball without stand physically stops press despite insufficient Player clearance");
                    press.enabled = false;
                    launcher.ResetForRespawn(); playerBody.position = new Vector2(994f, 2.06f); playerBody.linearVelocity = Vector2.zero;
                    ball.position = new Vector2(995f, 2.6f); ball.linearVelocity = new Vector2(20f, 0f);
                    Physics2D.SyncTransforms(); Next(); break;
                case 11:
                    Require(ball.position.x < 995.8f, "ball passed through solid press side: " + ball.position);
                    if (t < 0.3f) return;
                    Pass("real solid side collision blocks moving ball"); CheckRest();
                    Require(errors == 0, "new runtime errors=" + errors);
                    Finish("PASS: " + string.Join("; ", passed) + "; runtime errors=0"); break;
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
        press.BallStand.gameObject.SetActive(false);
        launcher.ResetForRespawn(); hp.ResetToFullHp();
        playerBody.position = new Vector2(990f, 2.06f); playerBody.linearVelocity = Vector2.zero;
        ball.position = new Vector2(989f, 0.85f); ball.linearVelocity = Vector2.zero;
        mass = ball.mass; gravity = ball.gravityScale; damping = ball.linearDamping; constraints = ball.constraints;
        pad = InputSystem.AddDevice<Gamepad>(); launcher.LaunchCommitted += OnLaunch; Physics2D.SyncTransforms();
    }
    private static FallingPressController Fixture(float x)
    {
        var go = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(FallingPressSetup.PressPath), new Vector3(x, 0f, 0f), Quaternion.identity);
        var stand = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(FallingPressSetup.StandPath), new Vector3(x, 0f, 0f), Quaternion.identity);
        var p = go.GetComponent<FallingPressController>(); Set(p, "topWait", 0.3f); Set(p, "warningTime", 0.25f);
        var so = new SerializedObject(p); so.FindProperty("ballStand").objectReferenceValue = stand.GetComponent<PressBallStandController>(); so.ApplyModifiedPropertiesWithoutUndo();
        Set(p, "bottomWait", 0.8f); Set(p, "ascentSpeed", 10f); return p;
    }
    private static void PlaceBall(bool onStand)
    {
        // 床の鉄球では通路高が足りない。Playerをプレス外へ置き、被弾ノックバックで鉄球を引き抜かない。
        launcher.ResetForRespawn(); playerBody.position = new Vector2(onStand ? 998f : 995.9f, 2.06f); playerBody.linearVelocity = Vector2.zero;
        playerBody.transform.position = new Vector3(playerBody.position.x, playerBody.position.y, playerBody.transform.position.z);
        ball.position = new Vector2(1000f, onStand ? 2.76f : 0.86f);
        ball.transform.position = new Vector3(ball.position.x, ball.position.y, ball.transform.position.z);
        ball.linearVelocity = Vector2.zero; Physics2D.SyncTransforms();
    }
    private static void CheckRest()
    {
        Require(ball.mass == mass && ball.gravityScale == gravity && ball.linearDamping == damping && ball.constraints == constraints,
            "Rest physics changed");
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
