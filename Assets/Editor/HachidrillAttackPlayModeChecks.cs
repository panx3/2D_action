using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>本番Sceneを保存せず、急降下の命中/回避/地形/帰還/中断を検証する。</summary>
[InitializeOnLoad]
public static class HachidrillAttackPlayModeChecks
{
    private const string Key = "HachidrillAttackChecks";
    private static readonly List<Probe> probes = new List<Probe>();
    private static Scene scene;
    private static float started;
    private static double realStarted;
    private static int errors;
    private static bool background;
    private sealed class Probe
    {
        public string Name;
        public HachidrillAttackController AI;
        public PlayerHealth Player;
        public Vector2 Start;
        public bool Dodge, NoFloor, Blocked, Interrupt, Kill;
        public bool Acted, Windup, Dive, Embedded, Extract, Returned;
        public Vector2 LockedDirection;
        public float EmbeddedAt;
        public Bounds Detection;
    }
    static HachidrillAttackPlayModeChecks() => EditorApplication.playModeStateChanged += PlayState;
    [MenuItem("Tools/Hachidrill/Run Attack Play Mode Checks")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        HachidrillSetup.Validate();
        SessionState.SetString(Key + ".Dirty", string.Join("\n", Enumerable.Range(0, SceneManager.sceneCount)
            .Select(i => SceneManager.GetSceneAt(i)).Where(s => s.isDirty).Select(s => s.path)));
        SessionState.SetString(Key + ".Result", "Running"); SessionState.SetBool(Key, true);
        EditorApplication.isPlaying = true;
    }
    public static string Result() => SessionState.GetString(Key + ".Result", "Not run");
    private static void PlayState(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            probes.Clear(); errors = 0;
            background = Application.runInBackground; Application.runInBackground = true;
            Application.logMessageReceived += Log;
            try { CreateWorld(); started = Time.time; realStarted = EditorApplication.timeSinceStartup; EditorApplication.update += Tick; }
            catch (Exception e) { Finish("FAIL setup: " + e); }
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
            foreach (string path in SessionState.GetString(Key + ".Dirty", "").Split('\n'))
            {
                if (string.IsNullOrEmpty(path)) continue;
                var original = SceneManager.GetSceneByPath(path);
                if (original.IsValid() && original.isLoaded) EditorSceneManager.MarkSceneDirty(original);
            }
            SessionState.SetBool(Key, false); Debug.Log("[HachidrillAttackChecks] " + Result());
        }
    }
    private static void CreateWorld()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
            foreach (var root in SceneManager.GetSceneAt(i).GetRootGameObjects()) root.SetActive(false);
        Time.timeScale = 1f;
        scene = SceneManager.CreateScene("HachidrillDiveValidation");
        var listener = new GameObject("ValidationAudioListener");
        SceneManager.MoveGameObjectToScene(listener, scene); listener.AddComponent<AudioListener>();
        Fixture("hit right", 1000f, 1.5f);
        Fixture("hit left", 1040f, -1.5f);
        Fixture("dodge", 1080f, 1.5f).Dodge = true;
        var noFloor = Fixture("no floor", 1120f, 1.5f, false); noFloor.NoFloor = true;
        Set(noFloor.AI, "maximumDiveTime", 0.5f);
        var blocked = Fixture("occluded", 1160f, 1.5f); blocked.Blocked = true;
        Box("SightWall", new Vector2(1160.8f, 3f), new Vector2(0.1f, 8f));
        Fixture("interrupt", 1200f, 1.5f).Interrupt = true;
        Fixture("death", 1240f, 1.5f).Kill = true;
        Physics2D.SyncTransforms();
    }
    private static Probe Fixture(string name, float x, float offset, bool floor = true)
    {
        var root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(HachidrillSetup.PrefabPath),
            new Vector3(x, 6f), Quaternion.identity);
        SceneManager.MoveGameObjectToScene(root, scene);
        var ai = root.GetComponent<HachidrillAttackController>();
        Set(root.GetComponent<HachidrillController>(), "moveSpeed", 0f);
        Set(ai, "attackKnockback", 0f);
        var health = root.GetComponent<EnemyHealth>(); Set(health, "maxHp", 2); Set(health, "destroyOnDeath", false); health.ResetHealth();
        var target = Box("DrillTestPlayer", new Vector2(x + offset, 1f), new Vector2(0.42f, 1f));
        target.tag = "Player"; target.layer = LayerMask.NameToLayer("player");
        target.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
        target.AddComponent<Player>().enabled = false;
        var hp = target.AddComponent<PlayerHealth>(); Set(hp, "_invincibleDuration", 0f);
        Set(ai, "player", hp); Set(ai, "playerCollider", target.GetComponent<Collider2D>());
        if (floor) Box("Floor", new Vector2(x, -0.25f), new Vector2(28f, 0.5f));
        var probe = new Probe { Name = name, AI = ai, Player = hp, Start = new Vector2(x, 6f), Detection = ai.DetectionBounds };
        probes.Add(probe); return probe;
    }
    private static GameObject Box(string name, Vector2 position, Vector2 size)
    {
        var root = new GameObject(name); SceneManager.MoveGameObjectToScene(root, scene);
        root.transform.position = position; root.AddComponent<BoxCollider2D>().size = size; return root;
    }
    private static void Tick()
    {
        try
        {
            foreach (var p in probes)
            {
                var ai = p.AI; var flight = ai.GetComponent<HachidrillController>(); var body = ai.GetComponent<Rigidbody2D>();
                Require(ai.DetectionBounds.center == p.Detection.center, p.Name + ": territory moved.");
                Require(!((BoxCollider2D)Get(ai, "attackHitbox")).enabled, p.Name + ": legacy trigger remained active.");
                if (p.Blocked) { Require(ai.State == HachidrillAttackController.ActionState.Patrol, "Detected through wall."); continue; }
                if (p.Kill && p.Acted)
                {
                    Require(ai.State == HachidrillAttackController.ActionState.Dead && !body.simulated, "Death did not stop dive.");
                    continue;
                }
                p.Windup |= ai.State == HachidrillAttackController.ActionState.Windup;
                if (ai.State == HachidrillAttackController.ActionState.Dive)
                {
                    if (!p.Dive) p.LockedDirection = ai.DiveDirection;
                    p.Dive = true;
                    Require(Vector2.Distance(p.LockedDirection, ai.DiveDirection) < 0.0001f, "Dive homed after launch.");
                    Require(ai.DiveDirection.y < -0.5f && flight.SuppressContactDamage, "Downward dive/contact ownership.");
                    if (!p.Acted && (p.Dodge || p.Interrupt || p.Kill))
                    {
                        p.Acted = true;
                        if (p.Dodge) p.Player.transform.position += Vector3.right * 10f;
                        else
                        {
                            var hp = ai.GetComponent<EnemyHealth>();
                            Hit(hp); if (p.Kill) Hit(hp, true);
                            if (p.Interrupt) Require(ai.State == HachidrillAttackController.ActionState.Return, "Hit did not interrupt.");
                        }
                    }
                }
                if (ai.State == HachidrillAttackController.ActionState.Embedded)
                {
                    if (!p.Embedded) p.EmbeddedAt = Time.time;
                    p.Embedded = true;
                    Require(ai.GetComponent<BoxCollider2D>().bounds.min.y >= -0.04f, "Body penetrated floor.");
                    Require(ai.ImpactNormal.y > 0.45f && Mathf.Abs(ai.ImpactPoint.y) < 0.05f, "Incorrect impact point.");
                    // 着地フレームだけは掃引で求めた接地位置まで最後の安全な移動を行う。
                    Require(flight.SuppressContactDamage && (ai.StateElapsed <= Time.fixedDeltaTime * 1.5f
                        || body.linearVelocity.magnitude < 0.1f), "Embedded did not stop safely.");
                    Require(p.Player.CurrentHp == p.Player.MaxHp - (p.Dodge ? 0 : 1), p.Name + ": swept hit or dodge failed.");
                }
                if (ai.State == HachidrillAttackController.ActionState.Extract)
                { p.Extract = true; Require(Time.time - p.EmbeddedAt >= 1.35f, "Embedded vulnerability too short."); }
                if (p.Dive && ai.State == HachidrillAttackController.ActionState.Patrol)
                {
                    p.Returned = true;
                    Require(Mathf.Abs(body.position.y - p.Start.y) < 0.065f, "Did not return to initial altitude.");
                    Require(Mathf.Abs(flight.AttackVisualAngle) < 0.001f && flight.AttackVisualOffset == Vector3.zero, "Pose not restored.");
                    p.Player.transform.position += Vector3.right * 40f;
                }
            }
            if (probes.Where(p => !p.Kill && !p.Blocked).All(p => p.Returned))
            {
                Require(probes.Where(p => !p.NoFloor && !p.Interrupt && !p.Kill && !p.Blocked).All(p => p.Embedded && p.Extract), "Missing embed/pop cycle.");
                Require(probes.All(p => p.Blocked || p.Windup && p.Dive), "Missing windup/dive.");
                Require(errors == 0, "Runtime Error/Exception.");
                Finish("PASS: left/right swept hits once, dodge with locked aim, terrain stop, embedded vulnerability, extraction, return altitude/pose, no-floor timeout, LOS wall, hit interruption/death; 0 runtime errors.");
            }
            if (Time.time - started > 14f || EditorApplication.timeSinceStartup - realStarted > 45f)
                Finish("FAIL timeout: " + string.Join("; ", probes.Select(p => p.Name + "=" + p.AI.State + " return=" + p.Returned)));
        }
        catch (Exception e) { Finish("FAIL: " + e); }
    }
    private static object Get(object target, string field) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static void Hit(EnemyHealth hp, bool lethal = false)
    {
        if (lethal) Set(hp, "_lastDamageTime", float.NegativeInfinity);
        hp.OnMorningStarHit(new MorningStarHitContext(1, Vector2.zero, 0f, hp.transform.position, Vector2.right, 3f, 1f));
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Log(string message, string trace, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors++; }
    private static void Finish(string result)
    {
        EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
        Application.runInBackground = background;
        SessionState.SetString(Key + ".Result", result); EditorApplication.isPlaying = false;
    }
}
