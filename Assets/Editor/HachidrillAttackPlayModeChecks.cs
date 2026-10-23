using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>本番Sceneを保存せず、固定視認/斜め滑空/二段判定/帰還/死亡を確認する。</summary>
[InitializeOnLoad]
public static class HachidrillAttackPlayModeChecks
{
    private const string Key = "HachidrillAttackChecks";
    private static readonly List<Probe> probes = new List<Probe>();
    private static Probe main, left, immune, interrupted, dying, occluded, boundary;
    private static HachidrillController terrain, patrol;
    private static Scene scene;
    private static float started, interruptedAt;
    private static double realStarted;
    private static int errors, boundaryStage, deathCount;
    private static bool background, deathChecked, interruptChecked, reacquired;
    private sealed class Probe
    {
        public HachidrillAttackController AI;
        public HachidrillController Flight;
        public PlayerHealth Player;
        public BoxCollider2D Hitbox;
        public Vector2 Center;
        public Bounds Detection;
        public bool Diagonal, ComboDone, Returned;
        public int HpAfterCombo;
        public readonly HashSet<int> Frames = new HashSet<int>();
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
            probes.Clear(); errors = boundaryStage = deathCount = 0;
            deathChecked = interruptChecked = reacquired = false;
            background = Application.runInBackground; Application.runInBackground = true;
            Application.logMessageReceived += Log;
            try
            {
                CreateWorld(); started = Time.time; realStarted = EditorApplication.timeSinceStartup;
                EditorApplication.update += Tick;
            }
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
        // 本番側のPlayer/敵/Scene遷移が検証へ介入しないようPlay中だけ停止。終了時にUnityが復元する。
        for (int i = 0; i < SceneManager.sceneCount; i++)
            foreach (var root in SceneManager.GetSceneAt(i).GetRootGameObjects()) root.SetActive(false);
        scene = SceneManager.CreateScene("HachidrillAttackValidation");
        main = Fixture(1000f, false, false); left = Fixture(1020f, true, false);
        immune = Fixture(1040f, false, true); interrupted = Fixture(1060f, false, false);
        dying = Fixture(1080f, false, false); occluded = Fixture(1100f, false, false);
        Set(occluded.Flight, "moveSpeed", 0f);
        Box("SightWall", new Vector2(1100.85f, 5f), new Vector2(0.1f, 8f));
        boundary = Fixture(1120f, false, false); Set(boundary.AI, "detectionWidth", 6f);
        boundary.Detection = boundary.AI.DetectionBounds;
        boundary.Player.transform.position = new Vector2(1122.99f, 4f);
        Set(boundary.Flight, "moveSpeed", 0f);
        terrain = Enemy(new Vector2(1140f, 8f)); terrain.GetComponent<HachidrillAttackController>().enabled = false;
        terrain.SetFlightTarget(new Vector2(1142f, 6f), 15f, 300f);
        Box("GlideWall", new Vector2(1141f, 7f), new Vector2(0.2f, 8f));
        patrol = Enemy(new Vector2(1160f, 8f)); patrol.GetComponent<HachidrillAttackController>().enabled = false;
        Set(patrol, "patrolHalfRange", 0.6f);
        Physics2D.SyncTransforms();
    }

    private static HachidrillController Enemy(Vector2 center)
    {
        var root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(HachidrillSetup.PrefabPath), center, Quaternion.identity);
        SceneManager.MoveGameObjectToScene(root, scene); return root.GetComponent<HachidrillController>();
    }
    private static Probe Fixture(float x, bool faceLeft, bool invincible)
    {
        Vector2 center = new Vector2(x, 5f);
        var flight = Enemy(center); var ai = flight.GetComponent<HachidrillAttackController>();
        Set(flight, "enableContactDamage", false); Set(ai, "attackKnockback", 0f);
        var target = Box("DrillTestPlayer", center + new Vector2(faceLeft ? -1.15f : 1.15f, -1.6f), new Vector2(0.4f, 0.6f));
        target.tag = "Player"; target.layer = LayerMask.NameToLayer("player");
        target.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
        target.AddComponent<Player>().enabled = false;
        var hp = target.AddComponent<PlayerHealth>();
        if (!invincible) Set(hp, "_invincibleDuration", 0f);
        Set(ai, "player", hp); Set(ai, "playerCollider", target.GetComponent<Collider2D>());
        var probe = new Probe { AI = ai, Flight = flight, Player = hp, Center = center, Detection = ai.DetectionBounds,
            Hitbox = (BoxCollider2D)Get(ai, "attackHitbox") };
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
            float elapsed = Time.time - started;
            foreach (var p in probes)
            {
                Require(p.AI.DetectionBounds.center == p.Detection.center && p.AI.DetectionBounds.size == p.Detection.size,
                    "Detection moved away from its initial patrol center.");
                Vector2 position = p.Flight.GetComponent<Rigidbody2D>().position;
                p.Diagonal |= Mathf.Abs(position.x - p.Center.x) > 0.15f && Mathf.Abs(position.y - p.Center.y) > 0.15f;
                if (p.AI.State == HachidrillAttackController.ActionState.Attack)
                {
                    p.Frames.Add(p.AI.AttackFrame);
                    int thrust = p.AI.AttackFrame == 2 ? 1 : p.AI.AttackFrame == 4 ? 2 : 0;
                    Require(p.AI.ActiveThrust == thrust && p.Hitbox.enabled == (thrust != 0), "Hitbox differs from the displayed thrust frame.");
                    var frames = (Sprite[])Get(p.AI, "attackFrames");
                    Require(p.Flight.VisualRenderer.sprite == frames[p.AI.AttackFrame],
                        "Animator differs from hitbox frame " + p.AI.AttackFrame + ": " + p.Flight.VisualRenderer.sprite.name);
                    Require(p.Flight.SuppressContactDamage, "Body contact overlaps a drill attack.");
                    Require(p.Flight.GetComponent<Rigidbody2D>().linearVelocity.sqrMagnitude < 0.001f, "Attack did not stop glide.");
                }
                else Require(!p.Hitbox.enabled, "Hitbox remains enabled outside Attack.");
            }
            Require(terrain.GetComponent<Rigidbody2D>().position.x <= 1140.215f, "Diagonal glide crossed a wall.");
            Require(Mathf.Abs(patrol.GetComponent<Rigidbody2D>().position.y - 8f) < 0.001f
                && Mathf.Abs(patrol.GetComponent<Rigidbody2D>().position.x - 1160f) <= 0.605f, "Existing patrol regressed.");
            Require(occluded.AI.ThrustCount == 0 && occluded.AI.State == HachidrillAttackController.ActionState.Patrol, "Detected through terrain.");

            foreach (var p in new[] { main, left, immune })
            {
                if (!p.ComboDone && p.AI.ThrustCount == 2 && p.AI.State == HachidrillAttackController.ActionState.Cooldown)
                {
                    p.HpAfterCombo = p.Player.CurrentHp;
                    Require(p.Diagonal && p.Frames.Count == 6, "Diagonal approach or complete six-frame animation missing.");
                    Require(p.HpAfterCombo == p.Player.MaxHp - (p == immune ? 1 : 2), "Two independent hits / per-thrust repeat gate / existing invincibility.");
                    Require(p.Flight.VisualAnimator.GetCurrentAnimatorStateInfo(0).IsName("Hover"), "Attack did not return to Hover.");
                    p.ComboDone = true;
                    p.Player.transform.position = new Vector2(p.Center.x + 7.2f, p.Center.y - 1f);
                }
                if (p.ComboDone && p.AI.State == HachidrillAttackController.ActionState.Patrol)
                {
                    p.Returned = true;
                    Require(Mathf.Abs(p.Flight.transform.position.y - p.Center.y) < 0.04f, "Returned to the wrong patrol altitude.");
                    Require(p.Player.CurrentHp == p.HpAfterCombo && p.AI.ThrustCount == 2, "Continued attacking outside fixed detection.");
                }
            }
            if (!deathChecked && dying.AI.ActiveThrust == 1)
            {
                var hp = dying.Flight.GetComponent<EnemyHealth>(); Set(hp, "destroyOnDeath", false);
                hp.HitConfirmed += (context, lethal) => { if (lethal) deathCount++; };
                Hit(hp); Hit(hp);
                Require(hp.IsDeathHandled && deathCount == 1 && dying.AI.State == HachidrillAttackController.ActionState.Dead
                    && !dying.Hitbox.enabled && !dying.AI.enabled && !dying.Flight.GetComponent<Rigidbody2D>().simulated, "Death left attack/flight active.");
                deathChecked = true;
            }
            if (!interruptChecked && interrupted.AI.ActiveThrust == 1)
            {
                interrupted.Player.transform.position = new Vector2(interrupted.Center.x + 7.2f, 4f);
                interruptedAt = Time.time; interruptChecked = true;
            }
            if (interruptChecked && !reacquired && Time.time - interruptedAt > 0.15f)
            {
                Require(!interrupted.Hitbox.enabled && interrupted.AI.ThrustCount == 1
                    && interrupted.AI.State == HachidrillAttackController.ActionState.Return, "Leaving detection did not cancel the second thrust.");
                interrupted.Player.transform.position = interrupted.Center + new Vector2(1.1f, -1.6f);
                reacquired = true;
            }
            if (reacquired && Time.time - interruptedAt > 0.25f && Time.time - interruptedAt < 0.5f)
                Require(interrupted.AI.State == HachidrillAttackController.ActionState.Cooldown
                    && interrupted.AI.ThrustCount == 1, "Reentry bypassed attack cooldown.");

            if (boundaryStage == 0 && elapsed > 0.3f)
            {
                Require(boundary.AI.State == HachidrillAttackController.ActionState.Patrol, "Boundary reentry margin ignored.");
                boundary.Player.transform.position = new Vector2(1122.7f, 4f); boundaryStage = 1;
            }
            else if (boundaryStage == 1 && elapsed > 0.6f)
            {
                Require(boundary.AI.State == HachidrillAttackController.ActionState.Chase, "Inside territory did not detect.");
                boundary.Player.transform.position = new Vector2(1123.01f, 4f); boundaryStage = 2;
            }
            else if (boundaryStage == 2 && elapsed > 0.8f)
            {
                Require(boundary.AI.State == HachidrillAttackController.ActionState.Return
                    || boundary.AI.State == HachidrillAttackController.ActionState.Patrol, "Outside territory still chases.");
                boundary.Player.transform.position = new Vector2(1122.99f, 4f); boundaryStage = 3;
            }
            else if (boundaryStage == 3 && elapsed > 1.1f)
            {
                Require(boundary.AI.State != HachidrillAttackController.ActionState.Chase, "Boundary jitter reacquired too early.");
                boundaryStage = 4;
            }
            if (elapsed > 7f && main.Returned && left.Returned && immune.Returned && deathChecked && reacquired && boundaryStage == 4)
            {
                Require(interrupted.AI.ThrustCount >= 3, "Did not attack again after reentry/cooldown.");
                Require(errors == 0, "New runtime Error/Exception.");
                Finish("PASS: fixed independent vision, diagonal glide left/right, wall/LOS blocking, two sprite-synced thrusts, no per-thrust repeats with invincibility disabled, standard invincibility preserved, Hover return, loss interruption, cooldown-preserving reentry, original patrol altitude/range, return/restart and death cleanup; 0 runtime errors.");
            }
            if (elapsed > 18f || EditorApplication.timeSinceStartup - realStarted > 45f) Finish("FAIL: checks timed out.");
        }
        catch (Exception e) { Finish("FAIL: " + e); }
    }

    private static object Get(object target, string field) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static void Hit(EnemyHealth hp) => hp.OnMorningStarHit(new MorningStarHitContext(1, Vector2.zero, 0f, hp.transform.position, Vector2.right, 3f, 1f));
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Log(string message, string trace, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors++; }
    private static void Finish(string result)
    {
        EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
        Application.runInBackground = background;
        SessionState.SetString(Key + ".Result", result); EditorApplication.isPlaying = false;
    }
}
