using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>配置・破片生成・トランポリン受付・街灯・背景ループの限定的な回帰チェック。</summary>
[InitializeOnLoad]
public static class FieldPolishPlayModeChecks
{
    private const string Key = "FieldPolishChecks";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly List<string> checks = new List<string>();
    private static StreetlampFlicker lamp;
    private static ScreenLocalLight2D[] lights;
    private static bool sawOff, sawOnAfter, oldRunInBackground;
    private static float started;
    private static double realStarted;
    private static int phase, errors, startFrame;

    static FieldPolishPlayModeChecks() => EditorApplication.playModeStateChanged += OnState;

    public static string Result() => SessionState.GetString(Key + ".Result", "Not run");

    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().isDirty)
            throw new InvalidOperationException("Run from saved Stage1 in Edit Mode.");
        foreach (var crab in UnityEngine.Object.FindObjectsByType<WrenchCrab>())
            SessionState.SetFloat(Key + ".Y." + crab.name, crab.transform.position.y);
        SessionState.SetBool(Key, true);
        SessionState.SetString(Key + ".Result", "Running");
        EditorApplication.isPlaying = true;
    }

    private static void OnState(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            checks.Clear(); phase = errors = 0; sawOff = sawOnAfter = false;
            started = Time.time; startFrame = Time.frameCount; realStarted = EditorApplication.timeSinceStartup;
            oldRunInBackground = Application.runInBackground; Application.runInBackground = true;
            Application.logMessageReceived += OnLog;
            try
            {
                foreach (var crab in UnityEngine.Object.FindObjectsByType<WrenchCrab>())
                {
                    var so = new SerializedObject(crab);
                    so.FindProperty("_moveSpeed").floatValue = 0f;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Environment/Streetlamp.prefab");
                lamp = UnityEngine.Object.Instantiate(prefab, new Vector3(-100, 100, 0), Quaternion.identity).GetComponent<StreetlampFlicker>();
                lamp.enabled = false;
                var settings = new SerializedObject(lamp);
                settings.FindProperty("flickerInterval").vector2Value = new Vector2(0.5f, 0.5f);
                settings.ApplyModifiedPropertiesWithoutUndo();
                lamp.enabled = true;
                lights = lamp.GetComponentsInChildren<ScreenLocalLight2D>();
                Require(lights.Length == 2 && lights.All(l => l.lightEnabled && l.emissionSource != null), "Streetlamp: two lit bulbs owned by its sprite");
                EditorApplication.update += Tick;
            }
            catch (Exception e) { Finish("FAIL setup: " + e); }
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            SessionState.SetBool(Key, false);
            Debug.Log("[FieldPolishChecks] " + Result());
        }
    }

    private static void OnLog(string message, string stack, LogType type)
    {
        if (stack != null && stack.Contains("UnityEditor.Search.SearchDatabase")) return;
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors++;
    }

    private static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        try
        {
            if (EditorApplication.timeSinceStartup - realStarted > 18) throw new Exception("Runtime did not finish in 18s");
            if (lights.Any(l => !l.lightEnabled)) sawOff = true;
            if (sawOff && lights.All(l => l.lightEnabled)) sawOnAfter = true;
            if (Time.frameCount - startFrame < 10 || Time.time - started < 0.6f) return;
            if (phase == 0)
            {
                CheckCrabs(); CheckJumpWindow(); CheckBackground(); phase++;
            }
            if (Time.time - started < 1.6f) return;
            Require(sawOff && sawOnAfter, "Streetlamp: occasional off pulse returns to steady light");
            Require(errors == 0, "No runtime Errors / Exceptions");
            Finish("PASS (" + checks.Count + ")");
        }
        catch (Exception e) { Finish("FAIL: " + e); }
    }

    private static void CheckCrabs()
    {
        var crabs = UnityEngine.Object.FindObjectsByType<WrenchCrab>();
        Require(crabs.Length == 2, "Stage1 has two crabs");
        foreach (var crab in crabs)
        {
            float initialY = SessionState.GetFloat(Key + ".Y." + crab.name, float.NaN);
            float drift = Mathf.Abs(crab.transform.position.y - initialY);
            // Placement has a 0.01 gap; solver penetration/slop can settle a few source pixels.
            Require(drift < 0.06f, crab.name + ": initial vertical drift=" + drift.ToString("F4"));
            Require(crab.transform.Find("Visual").localPosition.sqrMagnitude < 0.0001f, crab.name + ": Visual is at prefab origin");
        }
        // Exercise an offset Visual too, so this does not merely pass because placement was repaired.
        var victim = crabs[0];
        var visual = victim.transform.Find("Visual"); visual.localPosition += Vector3.up * 2f;
        var renderer = visual.GetComponent<SpriteRenderer>();
        Vector3 expected = renderer.bounds.center;
        var before = new HashSet<EnemyFragment>(UnityEngine.Object.FindObjectsByType<EnemyFragment>());
        victim.GetComponent<EnemyHealth>().OnMorningStarHit(new MorningStarHitContext(
            100, Vector2.zero, 0f, expected, Vector2.right, 20f, 1f));
        var fragments = UnityEngine.Object.FindObjectsByType<EnemyFragment>().Where(f => !before.Contains(f)).ToArray();
        Require(fragments.Length == 20, "Crab death emits its configured 20 fragments");
        Require(fragments.All(f => Vector2.Distance(f.transform.position, expected) < 0.83f), "Fragments originate around the offset Visual, before its pose is restored");
        Debug.Log("[FieldPolishChecks] Crab fragments=" + fragments.Length + " center=" + expected);
    }

    private static void CheckJumpWindow()
    {
        var player = UnityEngine.Object.FindAnyObjectByType<Player>();
        player.SetStartIntroControl(false); player.SetInputEnabled(true); player.enabled = false;
        var launcher = player.GetComponent<MorningStarLauncher>(); if (launcher != null) launcher.enabled = false;
        foreach (var constraint in UnityEngine.Object.FindObjectsByType<ChainConstraint2D>()) constraint.enabled = false;
        var rb = player.GetComponent<Rigidbody2D>(); rb.position = new Vector2(0, 200); Physics2D.SyncTransforms();
        Require(Mathf.Approximately(new SerializedObject(player).FindProperty("_jumpBufferTime").floatValue, 0.15f), "Normal jump buffer remains 0.15s");
        foreach (var pad in UnityEngine.Object.FindObjectsByType<JumpPad>())
            Require(Mathf.Approximately(new SerializedObject(pad).FindProperty("highJumpInputWindow").floatValue, 0.3f), pad.name + ": trampoline window=0.30s");

        PreparePlayer(player, rb);
        Set(player, "_lastJumpPressedAt", Time.time - 0.25f);
        player.BeginTrampolineJumpInput(0.3f);
        Call(player, "FixedUpdate");
        Require(rb.linearVelocity.y > 20f, "Jump buffered 0.25s before trampoline adds the existing jump impulse");
        float boosted = rb.linearVelocity.y;
        Set(player, "_jumpBufferTimer", 0.1f); Call(player, "FixedUpdate");
        Require(rb.linearVelocity.y <= boosted + 0.01f, "A bounce accepts the bonus only once");

        PreparePlayer(player, rb); player.BeginTrampolineJumpInput(0.3f);
        Set(player, "_trampolineJumpUntil", Time.time + 0.05f);
        Set(player, "_jumpBufferTimer", 0.1f); Call(player, "FixedUpdate");
        Require(rb.linearVelocity.y > 20f, "Jump 0.25s after trampoline also adds the bonus");

        PreparePlayer(player, rb); player.BeginTrampolineJumpInput(0.3f);
        Set(player, "_trampolineJumpUntil", Time.time - 0.01f);
        Set(player, "_jumpBufferTimer", 0.1f); Call(player, "FixedUpdate");
        Require(rb.linearVelocity.y <= 20.01f, "Expired trampoline window cannot add an air jump");
        player.SetInputEnabled(false);
        Require(float.IsNegativeInfinity((float)Get(player, "_trampolineJumpUntil")), "Input lock clears pending trampoline input");
    }

    private static void PreparePlayer(Player player, Rigidbody2D rb)
    {
        rb.linearVelocity = new Vector2(0, 20);
        Set(player, "_bjump", true); Set(player, "_jumpHeld", true);
        Set(player, "_jumpBufferTimer", 0f); Set(player, "_coyoteTimer", 0f);
        Set(player, "_lastJumpPressedAt", float.NegativeInfinity);
        Set(player, "_trampolineJumpUntil", float.NegativeInfinity);
    }

    private static void CheckBackground()
    {
        var background = UnityEngine.Object.FindObjectsByType<ParallaxBackgroundLayer>().Single(p => p.name == "ForeRuins");
        var camera = (Camera)Get(background, "targetCamera"); Vector3 original = camera.transform.position;
        foreach (float x in new[] { original.x, original.x + 180f, original.x - 180f })
        {
            camera.transform.position = new Vector3(x, original.y, original.z); Call(background, "LateUpdate");
            var copies = background.GetComponentsInChildren<SpriteRenderer>().OrderBy(r => r.bounds.center.x).ToArray();
            Require(copies.Length == 3, "Layer2: source plus two loop copies at camera x=" + x.ToString("F1"));
            for (int i = 1; i < copies.Length; i++)
                Require(copies[i - 1].bounds.max.x - copies[i].bounds.min.x > 0.001f && copies[i - 1].flipX != copies[i].flipX,
                    "Layer2: mirrored neighbours overlap across the wrap");
        }
        camera.transform.position = original; Call(background, "LateUpdate");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new Exception(message);
        checks.Add(message);
    }
    private static object Get(object owner, string field) => owner.GetType().GetField(field, Private).GetValue(owner);
    private static void Set(object owner, string field, object value) => owner.GetType().GetField(field, Private).SetValue(owner, value);
    private static void Call(object owner, string method) => owner.GetType().GetMethod(method, Private).Invoke(owner, null);

    private static void Finish(string result)
    {
        EditorApplication.update -= Tick; Application.logMessageReceived -= OnLog;
        Application.runInBackground = oldRunInBackground;
        SessionState.SetString(Key + ".Result", result + "\n" + string.Join("\n", checks));
        EditorApplication.isPlaying = false;
    }
}
