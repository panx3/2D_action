using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

[InitializeOnLoad]
public static class MorningStarSlopeSnagChecks
{
    private const string Key = "MorningStarSlopeSnag";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static MorningStarSlopeSnagChecks() => EditorApplication.playModeStateChanged += State;
    public static string Result() => SessionState.GetString(Key + ".Result", "Not run");
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        if (SceneManager.GetActiveScene().name != "Stage1") throw new InvalidOperationException("Open Stage1 first.");
        SessionState.SetString(Key + ".Dirty", string.Join("\n", Enumerable.Range(0, SceneManager.sceneCount)
            .Select(i => SceneManager.GetSceneAt(i)).Where(s => s.isDirty).Select(s => s.path)));
        SessionState.SetBool(Key, true); SessionState.SetString(Key + ".Result", "Running");
        EditorApplication.isPlaying = true;
    }
    private static void State(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            var oldMode = Physics2D.simulationMode;
            int errors = 0;
            Application.LogCallback capture = (message, stack, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors++;
            };
            Application.logMessageReceived += capture;
            try
            {
                var maps = UnityEngine.Object.FindObjectsByType<Tilemap>(FindObjectsInactive.Exclude)
                    .Where(m => m.name == "Slope" || m.name == "Ground1" || m.name == "Ground2").ToArray();
                foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                    if (!maps.Any(m => m.transform.IsChildOf(root.transform))) root.SetActive(false);
                Physics2D.simulationMode = SimulationMode2D.Script;
                var scene = SceneManager.CreateScene("SlopeSnagValidation");
                // 本番地形をPlay中だけ使用する。変更はPlay終了時に破棄され、Scene保存は行わない。
                var slope = maps.First(m => m.name == "Slope").GetComponent<TilemapCollider2D>();
                Physics2D.SyncTransforms();
                var report = "";
                for (int variant = 0; variant < 2; variant++)
                {
                    if (variant == 1)
                    {
                        slope.compositeOperation = Collider2D.CompositeOperation.Merge;
                        slope.ProcessTilemapChanges(); slope.GetComponent<CompositeCollider2D>().GenerateGeometry(); Physics2D.SyncTransforms();
                    }
                    foreach (float x in new[] { 36.2f, 172.0f, 172.6f, 173.3f, 174.2f })
                        report += Probe(scene, x, variant == 0 ? "current" : "merged") + "\n";
                }
                CheckWallWrap(scene);
                Require(errors == 0, "Unexpected Console errors: " + errors);
                SessionState.SetString(Key + ".Result", "PASS: 10 slope probes, solid-wall wrap/unwrap, Console errors=0\n" + report);
            }
            catch (Exception e) { SessionState.SetString(Key + ".Result", "FAIL: " + e); }
            finally
            {
                Application.logMessageReceived -= capture;
                Physics2D.simulationMode = oldMode;
                EditorApplication.isPlaying = false;
            }
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            // UnityのPlay終了処理がdirtyを解除するため、その完了後に元の状態を戻す。
            EditorApplication.delayCall += RestoreDirtyScenes;
            SessionState.SetBool(Key, false); Debug.Log("[SlopeSnagChecks] " + Result());
        }
    }
    private static void RestoreDirtyScenes()
    {
        foreach (string path in SessionState.GetString(Key + ".Dirty", "").Split('\n'))
        {
            if (string.IsNullOrEmpty(path)) continue;
            var scene = SceneManager.GetSceneByPath(path);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.MarkSceneDirty(scene);
        }
    }
    private static float Ground(float x)
    {
        return Physics2D.RaycastAll(new Vector2(x, 60f), Vector2.down, 100f, 65)
            .Where(h => h.collider.GetComponent<Tilemap>() != null).Max(h => h.point.y);
    }
    private static string Probe(Scene scene, float startX, string variant)
    {
        var fixture = new GameObject("SnagProbe"); SceneManager.MoveGameObjectToScene(fixture, scene);
        try
        {
            var hand = new GameObject("Hand"); hand.transform.SetParent(fixture.transform); hand.layer = 3;
            var player = hand.AddComponent<Rigidbody2D>(); player.bodyType = RigidbodyType2D.Kinematic;
            var controller = hand.AddComponent<Player>(); controller.enabled = false;
            Set(controller, "_isGrounded", true);
            float handX = startX - 3.5f; player.position = new Vector2(handX, Ground(handX) + 0.5f);
            var ball = new GameObject("MorningStar"); ball.layer = 7; ball.transform.SetParent(fixture.transform);
            var body = ball.AddComponent<Rigidbody2D>(); body.mass = 0.8f;
            body.gravityScale = 2f; body.linearDamping = 0.08f;
            body.constraints = RigidbodyConstraints2D.FreezeRotation; body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            ball.AddComponent<CircleCollider2D>().radius = 0.85f;
            body.position = new Vector2(startX, Ground(startX) + 0.86f);
            var chain = ball.AddComponent<ChainConstraint2D>();
            Set(chain, "playerRb", player); Set(chain, "morningStarRb", body); Set(chain, "handAnchor", hand.transform);
            chain.ConfigureTerrainPath(65, 0.14f, 0.01f);
            chain.SetMaxRopeLength(5f);
            chain.ConfigureTension(0.82f, 42f, 5f, 65f, 1.8f, 0.45f, 0.1f, 4f, 0.3f, 0.25f, 0.08f);
            Physics2D.SyncTransforms();
            for (int i = 0; i < 20; i++) Physics2D.Simulate(0.02f);
            Vector2 start = body.position;
            float maximumRope = 0f; int maximumContacts = 0;
            for (int i = 0; i < 160; i++)
            {
                handX -= 0.025f; Vector2 next = new Vector2(handX, Ground(handX) + 0.5f);
                player.linearVelocity = (next - player.position) / 0.02f; player.position = next;
                typeof(ChainConstraint2D).GetMethod("FixedUpdate", Private).Invoke(chain, null);
                maximumRope = Mathf.Max(maximumRope, chain.CurrentRopeLength);
                maximumContacts = Mathf.Max(maximumContacts, chain.RopeContactPointCount);
                Physics2D.Simulate(0.02f);
            }
            Require(body.position.x < start.x - 2.5f, variant + " ball stuck at x=" + startX);
            Require(maximumRope <= 5.03f, variant + " false detour at x=" + startX + ": " + maximumRope);
            return variant + " x=" + startX + " ball=" + start + "->" + body.position
                + " moved=" + (body.position.x - start.x) + " ropeMax=" + maximumRope + " contacts=" + maximumContacts;
        }
        finally { UnityEngine.Object.DestroyImmediate(fixture); }
    }
    private static void CheckWallWrap(Scene scene)
    {
        var fixture = new GameObject("WallWrapProbe"); SceneManager.MoveGameObjectToScene(fixture, scene);
        try
        {
            var wall = new GameObject("Wall"); wall.transform.SetParent(fixture.transform);
            wall.transform.position = new Vector3(1000f, 1000f);
            var collider = wall.AddComponent<BoxCollider2D>(); collider.size = new Vector2(1f, 4f);
            var hand = new GameObject("Hand"); hand.transform.SetParent(fixture.transform);
            var player = hand.AddComponent<Rigidbody2D>(); player.bodyType = RigidbodyType2D.Kinematic;
            var ball = new GameObject("Ball"); ball.transform.SetParent(fixture.transform);
            var body = ball.AddComponent<Rigidbody2D>(); body.bodyType = RigidbodyType2D.Kinematic;
            var chain = ball.AddComponent<ChainConstraint2D>(); chain.enabled = false;
            Set(chain, "playerRb", player); Set(chain, "morningStarRb", body); Set(chain, "handAnchor", hand.transform);
            chain.ConfigureTerrainPath(1, 0.14f, 0.01f);
            chain.SetMaxRopeLength(20f);
            Physics2D.SyncTransforms();
            var rebuild = typeof(ChainConstraint2D).GetMethod("RebuildRopePath", Private);
            rebuild.Invoke(chain, new object[] { new Vector2(997f, 1000f), new Vector2(1003f, 1000f) });
            Require(chain.RopeContactPointCount > 0, "Solid wall no longer wraps.");
            for (int i = 0; i < chain.RopeContactPointCount; i++)
                Require(!collider.OverlapPoint(chain.GetRopeContactPoint(i)), "Wrap point inside wall.");
            for (int i = 0; i < 8; i++)
                rebuild.Invoke(chain, new object[] { new Vector2(997f, 1000f), new Vector2(998f, 1000f) });
            Require(chain.RopeContactPointCount == 0, "Wall contacts failed to unwrap.");
        }
        finally { UnityEngine.Object.DestroyImmediate(fixture); }
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
}
