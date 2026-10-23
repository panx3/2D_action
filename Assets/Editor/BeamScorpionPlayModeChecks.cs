using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>本番Sceneを保存せず、遠方の一時Sceneで巡回・遮蔽・被弾・撃破を検証する。</summary>
[InitializeOnLoad]
public static class BeamScorpionPlayModeChecks
{
    private const string Key = "BeamScorpionChecks";
    private static readonly List<Probe> probes = new List<Probe>();
    private static readonly HashSet<BeamScorpionController.ActionState> states = new HashSet<BeamScorpionController.ActionState>();
    private static BeamScorpionController attacker, leftAttacker, contact, rangeEnemy, cadenceEnemy;
    private static PlayerHealth target, leftTarget, contactTarget, rangeTarget;
    private static GameObject wall;
    private static float started, wallStart;
    private static int stage, checks, runtimeErrors, hpAtDeath;
    private static bool wasBackground;
    private sealed class Probe
    {
        public BeamScorpionController Enemy;
        public Vector2 Start;
        public bool Left, Right;
        public bool WideFloor, ReachedPatrolLimit;
        public HashSet<Sprite> Frames = new HashSet<Sprite>();
    }
    static BeamScorpionPlayModeChecks() { EditorApplication.playModeStateChanged += OnPlayState; }
    [MenuItem("Tools/Beam Scorpion/Run Play Mode Checks")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        BeamScorpionSetup.Validate();
        SessionState.SetBool(Key, true); SessionState.SetString(Key + ".Result", "Running");
        EditorApplication.isPlaying = true;
    }
    public static string Result() => SessionState.GetString(Key + ".Result", "Not run");
    private static void OnPlayState(PlayModeStateChange change)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            checks = stage = runtimeErrors = 0; probes.Clear(); states.Clear();
            wasBackground = Application.runInBackground; Application.runInBackground = true;
            Application.logMessageReceived += Log;
            try { CreateWorld(); started = Time.time; EditorApplication.update += Tick; }
            catch (Exception e) { Finish("FAIL setup: " + e); }
        }
        if (change == PlayModeStateChange.EnteredEditMode)
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
            SessionState.SetBool(Key, false); Debug.Log("[BeamScorpionChecks] " + Result());
        }
    }
    private static void CreateWorld()
    {
        Scene scene = SceneManager.CreateScene("BeamScorpionValidation");
        for (int i = 0; i < 4; i++)
        {
            float x = 1000f + i * 20f;
            Box(scene, "PatrolFloor", new Vector2(x, -0.5f), new Vector2(i == 3 ? 20f : 6f, 1f));
            var enemy = Enemy(scene, new Vector2(x, 0.06f), i == 1);
            Set(enemy, "detectionRange", 0f);
            if (i == 2) Box(scene, "PatrolWall", new Vector2(x - 2.1f, 2f), new Vector2(0.3f, 4f));
            probes.Add(new Probe { Enemy = enemy, Start = enemy.transform.position, WideFloor = i == 3 });
        }
        attacker = AttackFixture(scene, 1080f, true, out target);
        leftAttacker = AttackFixture(scene, 1100f, false, out leftTarget);
        rangeEnemy = AttackFixture(scene, 1120f, true, out rangeTarget);
        rangeTarget.transform.position = new Vector2(1132f, 1.65f);
        PlayerHealth cadenceTarget;
        cadenceEnemy = AttackFixture(scene, 1160f, true, out cadenceTarget);
        Set(cadenceEnemy, "attackInterval", 1.2f); Set(cadenceEnemy, "attackCooldown", 0.55f);
        Set(cadenceEnemy, "windupTime", 0.5f); Set(cadenceEnemy, "nextAttack", Time.time + 1.2f);
        wall = Box(scene, "BeamBlocker", new Vector2(1082f, 2f), new Vector2(0.3f, 4f)); wall.SetActive(false);
        Box(scene, "ContactFloor", new Vector2(1140f, -0.5f), new Vector2(10f, 1f));
        contact = Enemy(scene, new Vector2(1140f, 0.06f), true);
        Set(contact, "moveSpeed", 0f); Set(contact, "detectionRange", 0f);
        contactTarget = PlayerFixture(scene, new Vector2(1141.45f, 1.65f));
        Physics2D.SyncTransforms();
    }
    private static BeamScorpionController AttackFixture(Scene scene, float x, bool right, out PlayerHealth hp)
    {
        Box(scene, "AttackFloor", new Vector2(x, -0.5f), new Vector2(14f, 1f));
        var enemy = Enemy(scene, new Vector2(x, 0.06f), right);
        // 最初はPlayerと逆を向かせ、射程検知時の向き直りも検証。
        Set(enemy, "direction", right ? -1 : 1);
        Set(enemy, "moveSpeed", 0f); Set(enemy, "attackInterval", 0.25f); Set(enemy, "windupTime", 0.2f);
        Set(enemy, "attackCooldown", 0.25f); Set(enemy, "nextAttack", Time.time + 0.25f);
        hp = PlayerFixture(scene, new Vector2(x + (right ? 4f : -4f), 1.65f));
        Set(enemy, "player", hp); Set(enemy, "playerCollider", hp.GetComponent<Collider2D>());
        return enemy;
    }
    private static BeamScorpionController Enemy(Scene scene, Vector2 position, bool right)
    {
        var root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(BeamScorpionSetup.PrefabPath), position, Quaternion.identity);
        SceneManager.MoveGameObjectToScene(root, scene);
        root.SetActive(false); var enemy = root.GetComponent<BeamScorpionController>();
        Set(enemy, "startFacingRight", right); root.SetActive(true); return enemy;
    }
    private static PlayerHealth PlayerFixture(Scene scene, Vector2 position)
    {
        var root = Box(scene, "BeamTestPlayer", position, new Vector2(0.8f, 3.3f));
        root.tag = "Player"; root.layer = LayerMask.NameToLayer("player");
        var body = root.AddComponent<Rigidbody2D>(); body.bodyType = RigidbodyType2D.Kinematic;
        var player = root.AddComponent<Player>(); player.enabled = false;
        return root.AddComponent<PlayerHealth>();
    }
    private static GameObject Box(Scene scene, string name, Vector2 position, Vector2 size)
    {
        var item = new GameObject(name); SceneManager.MoveGameObjectToScene(item, scene);
        item.transform.position = position; item.AddComponent<BoxCollider2D>().size = size; return item;
    }
    private static void Tick()
    {
        try
        {
            float elapsed = Time.time - started;
            foreach (var probe in probes)
            {
                var body = probe.Enemy.GetComponent<Rigidbody2D>();
                probe.Left |= body.linearVelocity.x < -0.1f; probe.Right |= body.linearVelocity.x > 0.1f;
                probe.Frames.Add(probe.Enemy.GetComponentInChildren<SpriteRenderer>().sprite);
                float distance = Mathf.Abs(body.position.x - probe.Start.x);
                probe.ReachedPatrolLimit |= distance > 2.45f;
                Require(body.position.y > -0.05f && distance < (probe.WideFloor ? 2.51f : 2.1f), "Patrol escaped its platform/range.");
            }
            if (attacker != null && attacker.isActiveAndEnabled)
            {
                states.Add(attacker.State);
                if (attacker.State != BeamScorpionController.ActionState.Patrol)
                    Require(Mathf.Abs(attacker.GetComponent<Rigidbody2D>().linearVelocity.x) < 0.01f, "Attack moved horizontally.");
            }
            if (stage == 0 && elapsed > 1.1f)
            {
                Check(target.CurrentHp == target.MaxHp - 1 && target.IsInvincible, "Beam damage/invincibility.");
                Check(leftTarget.CurrentHp == leftTarget.MaxHp - 1, "Left-facing beam damage.");
                Check(attacker.PlayerImpactCount > 0 && leftAttacker.PlayerImpactCount > 0, "Player-hit VFX both directions.");
                Check(attacker.FireSoundPlayCount > 0 && leftAttacker.FireSoundPlayCount > 0, "Fire SFX.");
                Check(contactTarget.CurrentHp == contactTarget.MaxHp, "Body contact causes no damage.");
                Check(rangeEnemy.State == BeamScorpionController.ActionState.Patrol && rangeEnemy.FireSoundPlayCount == 0,
                    "Player outside range does not trigger shooting.");
                rangeTarget.transform.position = new Vector2(1124f, 1.65f);
                var origin = (Transform)Get(attacker, "beamOrigin");
                var leftOrigin = (Transform)Get(leftAttacker, "beamOrigin");
                Check(origin.position.x > attacker.transform.position.x && leftOrigin.position.x < leftAttacker.transform.position.x, "Mirrored tail origin.");
                Check(states.Contains(BeamScorpionController.ActionState.Windup) && states.Contains(BeamScorpionController.ActionState.Firing)
                    && states.Contains(BeamScorpionController.ActionState.Recovery), "Windup / fire / recovery states.");
                // 予備動作へ入った後に壁を出す。検知遮蔽とは独立してビーム遮蔽を検証する。
                target.ResetToFullHp(); wall.SetActive(true); Physics2D.SyncTransforms();
                State(attacker, BeamScorpionController.ActionState.Windup); wallStart = Time.time; stage = 1;
            }
            if (stage == 1 && Time.time - wallStart > 0.4f)
            {
                Check(attacker.State == BeamScorpionController.ActionState.Firing && attacker.BeamActive, "Beam firing behind new wall.");
                Check(attacker.BeamEnd.x <= wall.GetComponent<Collider2D>().bounds.min.x + 0.02f, "Wall clips beam before Player.");
                Check(target.CurrentHp == target.MaxHp, "Wall prevents Player damage.");
                Check(attacker.EnvironmentImpactCount > 0, "Environment-hit VFX.");
                Check(rangeEnemy.FireSoundPlayCount > 0 && rangeTarget.CurrentHp == rangeTarget.MaxHp - 1,
                    "Entering range starts shooting without waiting for a patrol turn.");
                var health = attacker.GetComponent<EnemyHealth>();
                Set(health, "destroyOnDeath", false);
                // HP3の被弾表示は専用チェックで検証。ここは撃破時のビーム停止だけを確認する。
                Set(health, "maxDamagePerHit", 0);
                health.OnMorningStarHit(new MorningStarHitContext(health.CurrentHp, Vector2.zero, 0f, attacker.transform.position, Vector2.right, 1f, 1f));
                Check(health.CurrentHp == 0 && health.IsDeathHandled && !attacker.BeamActive && !attacker.enabled, "Lethal hit cancels beam/controller.");
                Check(!attacker.GetComponent<Collider2D>().enabled && !attacker.GetComponent<Rigidbody2D>().simulated, "Death disables collider/physics.");
                hpAtDeath = target.CurrentHp;
                var contactHealth = contact.GetComponent<EnemyHealth>(); Set(contactHealth, "destroyOnDeath", false);
                Set(contactHealth, "maxDamagePerHit", 0);
                contactHealth.OnMorningStarHit(new MorningStarHitContext(contactHealth.CurrentHp, Vector2.zero, 0f, contact.transform.position, Vector2.right, 1f, 1f));
                contactTarget.ResetToFullHp(); stage = 2;
            }
            if (stage == 2 && elapsed > 2.5f)
            {
                Check(target.CurrentHp == hpAtDeath && contactTarget.CurrentHp == contactTarget.MaxHp, "No beam/contact damage after death.");
                var health = attacker.GetComponent<EnemyHealth>(); health.ResetHealth(); attacker.gameObject.SetActive(true);
                Check(health.CurrentHp == 3 && attacker.enabled && attacker.State == BeamScorpionController.ActionState.Patrol && !attacker.BeamActive,
                    "ResetHealth restores clean patrol.");
                wall.SetActive(false);
                // Inspectorの色・長さ・太さを判定と描画へ反映する。
                target.transform.position = new Vector2(1086f, 1.65f); Physics2D.SyncTransforms();
                Set(attacker, "beamLength", 2f); Set(attacker, "beamThickness", 0.22f); Set(attacker, "beamColor", Color.cyan);
                State(attacker, BeamScorpionController.ActionState.Firing);
                var origin = (Transform)Get(attacker, "beamOrigin");
                Check(Mathf.Abs(Vector2.Distance(origin.position, attacker.BeamEnd) - 2f) < 0.02f, "Configurable maximum length.");
                var beam = (BeamScorpionBeamVfx)Get(attacker, "beam");
                var shell = (SpriteRenderer)Get(beam, "shell");
                Check(Mathf.Abs(shell.bounds.size.y - 0.22f) < 0.01f && shell.color.g > 0.9f, "Configurable thickness and color.");
                CheckBeamCancellation(attacker);
                stage = 3;
            }
            if (stage == 3 && elapsed > 7.5f)
            {
                foreach (var probe in probes)
                {
                    Check(probe.Left && probe.Right, "Wall/edge patrol reversal.");
                    Check(probe.Frames.Count >= 3, "Slow walking sprite animation.");
                    if (probe.WideFloor) Check(probe.ReachedPatrolLimit, "Patrol reverses at configured bounds on a wide floor.");
                }
                Check(cadenceEnemy.FireSoundPlayCount >= 2, "Faster shooting cadence.");
                Check(runtimeErrors == 0, "No new runtime Error/Exception.");
                Finish("PASS (" + checks + "), runtimeErrors=" + runtimeErrors);
            }
            if (elapsed > 12f) Finish("FAIL: timed out at stage " + stage);
        }
        catch (Exception e) { Finish("FAIL: " + e.Message); }
    }
    private static void CheckBeamCancellation(BeamScorpionController enemy)
    {
        Scene scene = enemy.gameObject.scene;
        Vector2 origin = ((Transform)Get(enemy, "beamOrigin")).position;
        var fake = Box(scene, "NonWeaponOnBallLayer", origin + Vector2.right, new Vector2(0.2f, 0.2f));
        fake.layer = LayerMask.NameToLayer("morningstar"); Physics2D.SyncTransforms();
        State(enemy, BeamScorpionController.ActionState.Firing);
        Check(enemy.BeamActive && enemy.State == BeamScorpionController.ActionState.Firing, "Only the real ball cancels beams.");

        var ball = new GameObject("DeflectingMorningStar"); SceneManager.MoveGameObjectToScene(ball, scene);
        ball.layer = LayerMask.NameToLayer("morningstar"); ball.tag = "morningstar";
        ball.transform.position = origin + Vector2.right * 1.5f;
        var body = ball.AddComponent<Rigidbody2D>(); body.bodyType = RigidbodyType2D.Kinematic;
        body.gravityScale = 2.3f; body.linearVelocity = new Vector2(0.4f, -0.3f);
        ball.AddComponent<CircleCollider2D>().radius = 0.3f;
        var owner = new GameObject("DeflectTestLauncher"); SceneManager.MoveGameObjectToScene(owner, scene); owner.SetActive(false);
        var launcher = owner.AddComponent<MorningStarLauncher>(); launcher.enabled = false;
        Set(launcher, "morningStarRb", body); owner.SetActive(true);
        ball.AddComponent<MorningStarCollisionReporter>().Initialize(launcher);
        Physics2D.SyncTransforms();
        int hp = target.CurrentHp;
        State(enemy, BeamScorpionController.ActionState.Firing);
        Check(!enemy.BeamActive && enemy.State == BeamScorpionController.ActionState.Recovery, "Morning Star extinguishes the entire shot.");
        Check(target.CurrentHp == hp, "Cancelled beam cannot damage Player.");
        Check(body.bodyType == RigidbodyType2D.Kinematic && Mathf.Abs(body.gravityScale - 2.3f) < 0.001f
            && (body.linearVelocity - new Vector2(0.4f, -0.3f)).sqrMagnitude < 0.0001f, "Deflection preserves ball physics.");

        // 壁の奥にある鉄球へは反応しない。
        body.position = origin + Vector2.right * 3f; wall.SetActive(true);
        Set(enemy, "beamLength", 8f); Physics2D.SyncTransforms();
        State(enemy, BeamScorpionController.ActionState.Firing);
        Check(enemy.BeamActive && enemy.State == BeamScorpionController.ActionState.Firing,
            "A ball behind a wall does not cancel the shot.");
        Check(enemy.BeamEnd.x <= wall.GetComponent<Collider2D>().bounds.min.x + 0.02f, "Wall remains the nearest beam blocker.");
        wall.SetActive(false); ball.SetActive(false); fake.SetActive(false); Set(enemy, "beamLength", 2f);
        Physics2D.SyncTransforms(); State(enemy, BeamScorpionController.ActionState.Firing);
        Check(enemy.BeamActive, "A later shot works after cancellation.");
        var audio = enemy.GetComponent<AudioSource>();
        var clip = (AudioClip)Get(enemy, "beamFireSE");
        Check(clip != null && AssetDatabase.GetAssetPath(clip) == BeamScorpionSetup.FireSoundPath && clip.length > 0.3f && audio.isPlaying,
            "Dedicated beam shot clip is assigned and AudioSource plays it.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Check(bool condition, string message) { Require(condition, message); checks++; }
    private static void Set(object owner, string field, object value) => owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, value);
    private static object Get(object owner, string field) => owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
    private static void State(BeamScorpionController enemy, BeamScorpionController.ActionState state) =>
        typeof(BeamScorpionController).GetMethod("SetState", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(enemy, new object[] { state });
    private static void Log(string message, string trace, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) runtimeErrors++; }
    private static void Finish(string result)
    {
        EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
        SessionState.SetString(Key + ".Result", result); Application.runInBackground = wasBackground;
        EditorApplication.isPlaying = false;
    }
}
