using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>本番Sceneを保存せず、独立した2D物理Sceneで実際の衝突を検証する。</summary>
[InitializeOnLoad]
public static class RollingIronBallPlayModeChecks
{
    private const string Key = "RollingIronBallChecks";
    private const float Dt = 0.02f;
    private static Scene scene;
    private static PhysicsScene2D physics;
    private static readonly List<RollingIronBall> balls = new List<RollingIronBall>();
    private static int errors;
    private static readonly MethodInfo move = typeof(Player).GetMethod("ApplyHorizontalMovement", BindingFlags.Instance | BindingFlags.NonPublic);
    static RollingIronBallPlayModeChecks() => EditorApplication.playModeStateChanged += PlayState;

    [MenuItem("Tools/Gimmicks/Run Rolling Iron Ball Checks")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        var player = UnityEngine.Object.FindFirstObjectByType<Player>();
        var launcher = player.GetComponent<MorningStarLauncher>();
        var data = new SerializedObject(launcher);
        var star = (Rigidbody2D)data.FindProperty("morningStarRb").objectReferenceValue;
        SessionState.SetFloat(Key + ".PlayerMass", player.GetComponent<Rigidbody2D>().mass);
        SessionState.SetFloat(Key + ".PlayerHeight", player.GetComponent<Collider2D>().bounds.size.y);
        SessionState.SetFloat(Key + ".MoveForce", new SerializedObject(player).FindProperty("_groundMoveForce").floatValue);
        SessionState.SetFloat(Key + ".MoveDrag", new SerializedObject(player).FindProperty("_groundLinearDragX").floatValue);
        SessionState.SetFloat(Key + ".StarMass", data.FindProperty("ballMass").floatValue);
        SessionState.SetFloat(Key + ".StarRadius", star.GetComponent<CircleCollider2D>().bounds.extents.x);
        SessionState.SetFloat(Key + ".ThrowSpeed", data.FindProperty("throwSpeed").floatValue);
        SessionState.SetString(Key + ".Dirty", string.Join("\n", Enumerable.Range(0, SceneManager.sceneCount)
            .Select(i => SceneManager.GetSceneAt(i)).Where(s => s.isDirty).Select(s => s.path)));
        SessionState.SetBool(Key, true); SessionState.SetString(Key + ".Result", "Running");
        EditorApplication.isPlaying = true;
    }
    public static string Result() => SessionState.GetString(Key + ".Result", "Not run");

    private static void PlayState(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            // Play中だけ本番側の入力・開始演出を止める。終了時にはUnityが元のSceneを復元する。
            for (int i = 0; i < SceneManager.sceneCount; i++)
                foreach (var root in SceneManager.GetSceneAt(i).GetRootGameObjects()) root.SetActive(false);
            errors = 0; Application.logMessageReceived += Log;
            try
            {
                scene = SceneManager.CreateScene("RollingIronBallValidation", new CreateSceneParameters(LocalPhysicsMode.Physics2D));
                physics = scene.GetPhysicsScene2D();
                string result = Checks();
                Require(errors == 0, "New runtime Error/Exception.");
                SessionState.SetString(Key + ".Result", "PASS: " + result + "; runtime errors=0.");
            }
            catch (Exception e) { SessionState.SetString(Key + ".Result", "FAIL: " + e); }
            finally { Application.logMessageReceived -= Log; EditorApplication.isPlaying = false; }
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            foreach (string path in SessionState.GetString(Key + ".Dirty", "").Split('\n'))
            {
                if (string.IsNullOrEmpty(path)) continue;
                var original = SceneManager.GetSceneByPath(path);
                if (original.IsValid() && original.isLoaded) EditorSceneManager.MarkSceneDirty(original);
            }
            SessionState.SetBool(Key, false); Debug.Log("[RollingIronBallChecks] " + Result());
        }
    }

    private static string Checks()
    {
        Clear(); Floor();
        var ball = Ball(new Vector2(0f, Radius + 0.02f));
        var body = ball.GetComponent<Rigidbody2D>();
        Require(body.bodyType == RigidbodyType2D.Dynamic && body.constraints == RigidbodyConstraints2D.None
            && body.mass >= SessionState.GetFloat(Key + ".PlayerMass", 1f) * 6f, "Dynamic/free-rotation/heavy setup.");
        Require(Mathf.Abs(Radius * 2f / SessionState.GetFloat(Key + ".PlayerHeight", 3.3f) - 1.5f) < 0.01f, "Player size ratio.");
        var driver = Driver(new Vector2(-Radius - 0.52f, SessionState.GetFloat(Key + ".PlayerHeight", 3.3f) * 0.5f));
        Step(60, () => move.Invoke(driver, new object[] { true }));
        float pushed = body.position.x, pushedSpeed = body.linearVelocity.x;
        Require(pushed > 0.25f && Mathf.Abs(body.rotation) > 5f, "Player force did not push/rotate the ball.");
        Clear(); Floor(); driver = Driver(new Vector2(0f, SessionState.GetFloat(Key + ".PlayerHeight", 3.3f) * 0.5f));
        Step(60, () => move.Invoke(driver, new object[] { true }));
        Require(pushedSpeed < driver.GetComponent<Rigidbody2D>().linearVelocity.x * 0.85f, "Ball felt weightless against Player drive.");

        Clear(); Floor(); ball = Ball(new Vector2(0f, Radius + 0.02f)); body = ball.GetComponent<Rigidbody2D>(); Step(10);
        float radius = SessionState.GetFloat(Key + ".StarRadius", 0.85f);
        var star = New("ThrownMorningStar"); star.tag = "morningstar"; star.layer = LayerMask.NameToLayer("morningstar");
        star.transform.position = new Vector2(-Radius - radius - 0.8f, Radius);
        var starBody = star.AddComponent<Rigidbody2D>(); starBody.mass = SessionState.GetFloat(Key + ".StarMass", 0.8f);
        starBody.gravityScale = 0f; starBody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        star.AddComponent<CircleCollider2D>().radius = radius;
        starBody.linearVelocity = Vector2.right * SessionState.GetFloat(Key + ".ThrowSpeed", 18f);
        Step(35);
        Require(body.position.x > 0.25f && Mathf.Abs(body.rotation) > 2f, "Morning Star mass/speed did not transfer physical momentum.");
        float struck = body.position.x;
        float transferredSpeed = body.linearVelocity.x;
        var thrownTarget = Enemy(new Vector2(body.position.x + Radius + 0.12f, body.position.y), true);
        Step(30);
        Require(thrownTarget.IsDeathHandled, "Normal thrown impact could not crush a nearby enemy; speed=" + transferredSpeed);

        float gentle = Slope(8f), steep = Slope(25f);
        Require(gentle > 0.35f && steep > gentle * 1.6f, "Downhill rolling/acceleration did not follow slope.");

        // 低速は生存。敵側から静止鉄球へ走り込んでも圧殺しない。
        Clear(); Floor(); ball = Ball(new Vector2(0f, Radius + 0.02f));
        var enemy = Enemy(new Vector2(Radius + 0.22f, Radius), true);
        Roll(ball, 0.4f); Step(25); Require(enemy.CurrentHp == 5, "Below threshold killed enemy.");
        Clear(); Floor(); ball = Ball(new Vector2(0f, Radius + 0.02f));
        enemy = Enemy(new Vector2(-Radius - 0.6f, Radius), true);
        enemy.GetComponent<Rigidbody2D>().linearVelocity = Vector2.right * 4f;
        Step(30); Require(enemy.CurrentHp == 5, "Enemy running into stationary ball was crushed.");

        foreach (float speed in new[] { -1.5f, 1.5f, 18f })
        {
            Clear(); Floor(); ball = Ball(new Vector2(0f, Radius + 0.02f));
            enemy = Enemy(new Vector2(Mathf.Sign(speed) * (Radius + 0.25f), Radius), true);
            int events = 0; enemy.HitConfirmed += (context, lethal) => { Require(lethal && context.Source == null, "Environmental defeat context."); events++; };
            Roll(ball, speed); Step(30);
            Require(enemy.IsDeathHandled && enemy.CurrentHp == 0 && events == 1 && !enemy.gameObject.activeSelf,
                "Slow/fast left-right crush or one-time cleanup failed: " + speed);
            Require(!enemy.TryCrush(Vector2.zero, Vector2.right), "Death repeated.");
        }
        Clear(); Floor(); ball = Ball(new Vector2(0f, Radius + 0.02f));
        var first = Enemy(new Vector2(Radius + 0.25f, Radius), false);
        var second = Enemy(new Vector2(Radius + 1.1f, Radius), false);
        Roll(ball, 2.5f); Step(60);
        Require(first.IsDeathHandled && second.IsDeathHandled, "Rolling through consecutive enemies failed.");

        foreach (string name in new[] { "TekkyuEnemy", "WrenchCrab", "SpringFrog", "Hachidrill" })
        {
            Clear();
            var root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/" + name + ".prefab"));
            SceneManager.MoveGameObjectToScene(root, scene);
            var health = root.GetComponent<EnemyHealth>();
            Require(health != null && health.TryCrush(root.transform.position, Vector2.right * 2f)
                && health.IsDeathHandled, name + ": production death/AI/feedback path failed.");
        }

        // 通常武器のダメージ上限と被弾無敵を維持し、圧殺だけがその制限を越える。
        Clear(); enemy = Enemy(Vector2.zero, true);
        var hit = new MorningStarHitContext(20, Vector2.zero, 0f, Vector2.zero, Vector2.right, 5f, 1f);
        enemy.OnMorningStarHit(hit); enemy.OnMorningStarHit(hit);
        Require(enemy.CurrentHp == 4, "Normal hit cap/invincibility regressed.");
        Require(enemy.TryCrush(Vector2.zero, Vector2.right * 2f) && enemy.IsDeathHandled, "Crush failed after recent normal hit.");
        return $"size 1.5x; Player push {pushed:F2}u / {pushedSpeed:F2}u/s; thrown impact {struck:F2}u at {transferredSpeed:F2}u/s then crush; slope speeds {gentle:F2}/{steep:F2}; slow-safe, stationary-safe, slow/fast left-right crush, consecutive kills, four production enemies, normal damage cap/invincibility preserved";
    }

    private static float Radius => AssetDatabase.LoadAssetAtPath<GameObject>(RollingIronBallSetup.PrefabPath).GetComponent<CircleCollider2D>().radius;
    private static float Slope(float degrees)
    {
        Clear(); var floor = Floor(); floor.transform.rotation = Quaternion.Euler(0f, 0f, -degrees);
        Vector2 normal = Quaternion.Euler(0f, 0f, -degrees) * Vector2.up;
        var ball = Ball(normal * (Radius + 0.55f)); Step(80);
        Require(Mathf.Abs(ball.GetComponent<Rigidbody2D>().rotation) > 5f, "Slope slid without rotation.");
        return ball.GetComponent<Rigidbody2D>().linearVelocity.x;
    }
    private static GameObject New(string name)
    {
        var root = new GameObject(name); SceneManager.MoveGameObjectToScene(root, scene); return root;
    }
    private static GameObject Floor()
    {
        var floor = New("Floor"); floor.transform.position = new Vector2(0f, -0.5f);
        floor.AddComponent<BoxCollider2D>().size = new Vector2(60f, 1f); return floor;
    }
    private static RollingIronBall Ball(Vector2 position)
    {
        var root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(RollingIronBallSetup.PrefabPath), position, Quaternion.identity);
        SceneManager.MoveGameObjectToScene(root, scene);
        var ball = root.GetComponent<RollingIronBall>(); balls.Add(ball); return ball;
    }
    private static Player Driver(Vector2 position)
    {
        var root = New("PlayerDrive"); root.transform.position = position; root.layer = LayerMask.NameToLayer("player");
        var body = root.AddComponent<Rigidbody2D>(); body.mass = SessionState.GetFloat(Key + ".PlayerMass", 1f);
        body.constraints = RigidbodyConstraints2D.FreezeRotation;
        root.AddComponent<CapsuleCollider2D>().size = new Vector2(1f, SessionState.GetFloat(Key + ".PlayerHeight", 3.3f));
        var player = root.AddComponent<Player>(); player.enabled = false;
        Set(player, "_moveInput", Vector2.right);
        Set(player, "_groundMoveForce", SessionState.GetFloat(Key + ".MoveForce", 70f));
        Set(player, "_groundLinearDragX", SessionState.GetFloat(Key + ".MoveDrag", 8f));
        return player;
    }
    private static EnemyHealth Enemy(Vector2 position, bool childCollider)
    {
        var root = New("CrushTarget"); root.transform.position = position; root.layer = LayerMask.NameToLayer("Enemy");
        var body = root.AddComponent<Rigidbody2D>(); body.gravityScale = 0f;
        body.constraints = RigidbodyConstraints2D.FreezeRotation; body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        var colliderHost = childCollider ? new GameObject("Body") : root;
        if (childCollider) { colliderHost.transform.SetParent(root.transform, false); colliderHost.layer = root.layer; }
        colliderHost.AddComponent<BoxCollider2D>().size = new Vector2(0.2f, 0.8f);
        var health = root.AddComponent<EnemyHealth>();
        Set(health, "maxHp", 5); Set(health, "maxDamagePerHit", 1); Set(health, "hitInvincibleDuration", 2f);
        Set(health, "destroyOnDeath", false); health.ResetHealth(); return health;
    }
    private static void Roll(RollingIronBall ball, float speed)
    {
        var body = ball.GetComponent<Rigidbody2D>(); body.linearVelocity = Vector2.right * speed;
        body.angularVelocity = -speed / Radius * Mathf.Rad2Deg;
    }
    private static void Step(int count, Action before = null)
    {
        Physics2D.SyncTransforms();
        for (int i = 0; i < count; i++)
        {
            before?.Invoke();
            foreach (var ball in balls) ball.SendMessage("FixedUpdate");
            Require(physics.Simulate(Dt), "Local physics simulation failed.");
        }
    }
    private static void Clear()
    {
        balls.Clear();
        foreach (var root in scene.GetRootGameObjects()) UnityEngine.Object.DestroyImmediate(root);
    }
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Log(string message, string trace, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors++; }
}
