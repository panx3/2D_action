using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>本番Sceneを保存せず、一時Sceneで巡回・接触・共通被弾/撃破を検証する。</summary>
[InitializeOnLoad]
public static class HachidrillPlayModeChecks
{
    private const string Key = "HachidrillChecks";
    private static readonly List<Probe> probes = new List<Probe>();
    private static HachidrillController contact, disabledContact, damageThree, survivor;
    private static PlayerHealth player, safePlayer, threePlayer;
    private static Scene scene;
    private static float started;
    private static int stage, errors, lethalNotifications;
    private static bool background;
    private sealed class Probe
    {
        public HachidrillController Enemy;
        public Vector2 Center;
        public bool Left, Right, Hover, Limit, Wall;
        public readonly HashSet<Sprite> Frames = new HashSet<Sprite>();
    }

    static HachidrillPlayModeChecks() => EditorApplication.playModeStateChanged += OnPlayState;

    [MenuItem("Tools/Hachidrill/Run Play Mode Checks")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        HachidrillSetup.Validate();
        SessionState.SetString(Key + ".DirtyScenes", string.Join("\n", Enumerable.Range(0, SceneManager.sceneCount)
            .Select(i => SceneManager.GetSceneAt(i)).Where(s => s.isDirty).Select(s => s.path)));
        SessionState.SetBool(Key, true); SessionState.SetString(Key + ".Result", "Running");
        EditorApplication.isPlaying = true;
    }

    public static string Result() => SessionState.GetString(Key + ".Result", "Not run");

    private static void OnPlayState(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            stage = errors = lethalNotifications = 0; probes.Clear();
            background = Application.runInBackground; Application.runInBackground = true;
            Application.logMessageReceived += Log;
            try { CreateWorld(); started = Time.time; EditorApplication.update += Tick; }
            catch (Exception e) { Finish("FAIL setup: " + e); }
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
            foreach (string path in SessionState.GetString(Key + ".DirtyScenes", "").Split('\n'))
            {
                if (string.IsNullOrEmpty(path)) continue;
                Scene original = SceneManager.GetSceneByPath(path);
                if (original.IsValid() && original.isLoaded) EditorSceneManager.MarkSceneDirty(original);
            }
            SessionState.SetBool(Key, false); Debug.Log("[HachidrillChecks] " + Result());
        }
    }

    private static void CreateWorld()
    {
        scene = SceneManager.CreateScene("HachidrillValidation");
        for (int i = 0; i < 4; i++)
        {
            Vector2 position = new Vector2(1000f + i * 7f, 5f + i);
            var enemy = Enemy(position);
            Set(enemy, "patrolHalfRange", 0.6f);
            Set(enemy, "startDirection", i % 2 == 0 ? HachidrillController.InitialDirection.Left : HachidrillController.InitialDirection.Right);
            enemy.gameObject.SetActive(false); enemy.gameObject.SetActive(true);
            if (i == 2) enemy.transform.localScale = new Vector3(1.5f, 1.5f, 1f);
            if (i == 3)
            {
                Box("FlightWall", position + Vector2.right * 0.95f, new Vector2(0.2f, 4f));
                Set(enemy, "moveSpeed", 12f);
            }
            probes.Add(new Probe { Enemy = enemy, Center = position, Wall = i == 3 });
        }
        contact = ContactFixture(1040f, out player);
        disabledContact = ContactFixture(1050f, out safePlayer); Set(disabledContact, "enableContactDamage", false);
        damageThree = ContactFixture(1060f, out threePlayer); Set(damageThree, "contactDamage", 3);
        survivor = Enemy(new Vector2(1070f, 5f));
        Set(survivor, "moveSpeed", 0f); Set(survivor.GetComponent<EnemyHealth>(), "maxHp", 2);
        survivor.GetComponent<EnemyHealth>().ResetHealth();
        Physics2D.SyncTransforms();
    }

    private static HachidrillController Enemy(Vector2 position)
    {
        var root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(HachidrillSetup.PrefabPath), position, Quaternion.identity);
        SceneManager.MoveGameObjectToScene(root, scene);
        root.GetComponent<HachidrillAttackController>().enabled = false;
        root.transform.localScale = Vector3.one;
        return root.GetComponent<HachidrillController>();
    }

    private static HachidrillController ContactFixture(float x, out PlayerHealth hp)
    {
        var enemy = Enemy(new Vector2(x, 5f)); Set(enemy, "moveSpeed", 0f);
        Set(enemy, "contactKnockback", 0f);
        enemy.GetComponent<Rigidbody2D>().constraints = RigidbodyConstraints2D.FreezeAll;
        var target = Box("HachidrillTestPlayer", new Vector2(x + 0.6f, 4.9f), new Vector2(0.4f, 0.6f));
        target.tag = "Player"; target.layer = LayerMask.NameToLayer("player");
        target.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
        target.AddComponent<Player>().enabled = false;
        hp = target.AddComponent<PlayerHealth>(); return enemy;
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
            foreach (var probe in probes)
            {
                var body = probe.Enemy.GetComponent<Rigidbody2D>();
                Require(Vector2.Distance(probe.Enemy.PatrolCenter, probe.Center) < 0.001f, "Independent placement center.");
                Require(Mathf.Abs(body.position.x - probe.Center.x) < 0.605f, "Patrol exceeded half range.");
                Require(Mathf.Abs(body.position.y - probe.Center.y) < 0.001f, "Body/collider altitude changed.");
                if (probe.Wall) Require(body.position.x <= probe.Center.x + 0.405f, "Crossed flight wall.");
                probe.Left |= body.linearVelocity.x < -0.1f; probe.Right |= body.linearVelocity.x > 0.1f;
                probe.Limit |= Mathf.Abs(body.position.x - probe.Center.x) > 0.58f;
                probe.Frames.Add(probe.Enemy.GetComponentInChildren<Animator>().GetComponent<SpriteRenderer>().sprite);
                var visual = probe.Enemy.transform.Find("VisualRoot");
                probe.Hover |= Mathf.Abs(visual.localPosition.y) > 0.01f;
                Require(Mathf.Abs(visual.localPosition.y) <= 0.041f, "Excessive hovering.");
            }
            if (stage == 0 && elapsed > 0.4f)
            {
                Require(player.CurrentHp == player.MaxHp - 1 && player.IsInvincible, "Contact damage 1 / invincibility.");
                Require(safePlayer.CurrentHp == safePlayer.MaxHp, "Disabled contact damage.");
                Require(threePlayer.CurrentHp == threePlayer.MaxHp - 3, "Inspector contact damage override.");
                Hit(survivor.GetComponent<EnemyHealth>(), 1);
                var feedback = survivor.GetComponent<EnemyDamageFeedback>();
                Require(survivor.GetComponent<EnemyHealth>().CurrentHp == 1 && feedback.GaugeVisible && feedback.FlashActive,
                    "Shared HP / gauge / flash after nonlethal hit.");
                stage = 1;
            }
            if (stage == 1 && elapsed > 0.8f)
            {
                Require(player.CurrentHp == player.MaxHp - 1, "Repeated contact bypassed invincibility.");
                stage = 2;
            }
            if (stage == 2 && elapsed > 1.65f)
            {
                Require(player.CurrentHp == player.MaxHp - 2, "Sustained contact after invincibility.");
                var hp = contact.GetComponent<EnemyHealth>(); Set(hp, "destroyOnDeath", false);
                hp.HitConfirmed += (context, lethal) => { if (lethal) lethalNotifications++; };
                Hit(hp, 1); Hit(hp, 1);
                Require(hp.IsDeathHandled && lethalNotifications == 1 && !contact.gameObject.activeSelf
                    && !contact.enabled && !contact.GetComponent<Collider2D>().enabled
                    && !contact.GetComponent<Rigidbody2D>().simulated, "Idempotent death stops flight/contact/physics.");
                Require(scene.GetRootGameObjects().Count(g => g.name == "Defeat(Clone)") == 1, "Shared defeat VFX exactly once.");
                stage = 3;
            }
            if (stage == 3 && elapsed > 4.5f)
            {
                Require(player.CurrentHp == player.MaxHp - 2, "Dead enemy dealt contact damage.");
                foreach (var probe in probes)
                    Require(probe.Left && probe.Right && probe.Hover && probe.Frames.Count == 6 && (probe.Wall || probe.Limit),
                        "Both directions / patrol limit / hover / six animation frames.");
                Require(errors == 0, "New runtime Error/Exception.");
                Finish("PASS: four independent patrols (including scaled root), exact limits, high-speed wall blocking, stable body Y, visual hover, six frames, contact ON/OFF/value/cooldown/invincibility, shared nonlethal feedback and one-time death; 0 runtime errors.");
            }
            if (elapsed > 15f) Finish("FAIL: timeout.");
        }
        catch (Exception e) { Finish("FAIL: " + e); }
    }

    private static void Hit(EnemyHealth hp, int damage) => hp.OnMorningStarHit(new MorningStarHitContext(
        damage, Vector2.zero, 0f, hp.transform.position, Vector2.right, 3f, 1f));
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Log(string message, string trace, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors++; }
    private static void Finish(string result)
    {
        EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
        Application.runInBackground = background;
        SessionState.SetString(Key + ".Result", result); EditorApplication.isPlaying = false;
    }
}
