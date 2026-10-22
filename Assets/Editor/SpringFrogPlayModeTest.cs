using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class SpringFrogPlayModeTest
{
    private const string RunningKey = "SpringFrogTest.Running";
    private const string ResultKey = "SpringFrogTest.Result";
    private static readonly List<PatrolProbe> Probes = new List<PatrolProbe>();
    private static EnemyHealth contactHealth;
    private static SpringFrog contactFrog;
    private static PlayerHealth playerHealth;
    private static float startedAt;
    private static int errors;
    private static int damageAfterDeath;
    private static bool contactChecked;
    private static bool deathChecked;
    private static bool originalRunInBackground;

    private sealed class PatrolProbe
    {
        public SpringFrog Frog;
        public Vector2 Start;
        public float PreviousVerticalSpeed;
        public int Jumps;
        public bool MovedLeft;
        public bool MovedRight;
        public bool HeardJump;
        public readonly HashSet<Sprite> Frames = new HashSet<Sprite>();
    }

    static SpringFrogPlayModeTest()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    [MenuItem("Tools/Spring Frog/Run Play Mode Validation")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play Mode before running SpringFrog validation.");
        SpringFrogSetup.Validate();
        SessionState.SetString(ResultKey, "Running");
        SessionState.SetBool(RunningKey, true);
        EditorApplication.isPlaying = true;
    }

    public static string GetResult() => SessionState.GetString(ResultKey, "Not run");

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(RunningKey, false))
            return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            originalRunInBackground = Application.runInBackground;
            Application.runInBackground = true;
            errors = 0;
            contactChecked = false;
            deathChecked = false;
            Probes.Clear();
            Application.logMessageReceived += CountError;
            try
            {
                CreateTestWorld();
                startedAt = Time.time;
                EditorApplication.update += Tick;
            }
            catch (Exception exception)
            {
                Finish("FAIL: " + exception.Message);
            }
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= CountError;
            SessionState.SetBool(RunningKey, false);
            Debug.Log("[SpringFrogTest] " + GetResult());
        }
    }

    private static void CreateTestWorld()
    {
        Scene scene = SceneManager.CreateScene("SpringFrogValidation");
        for (int probeIndex = 0; probeIndex < 3; probeIndex++)
        {
            float origin = 1000f + probeIndex * 10f;
            CreateBox(scene, "Floor", new Vector2(origin, -0.5f), new Vector2(6f, 1f));
            SpringFrog frog = CreateFrog(scene, new Vector2(origin, 0.5f));
            if (probeIndex == 1)
            {
                frog.gameObject.SetActive(false);
                SerializedObject settings = new SerializedObject(frog);
                settings.FindProperty("_initialDirection").intValue = -1;
                settings.ApplyModifiedPropertiesWithoutUndo();
                frog.gameObject.SetActive(true);
            }
            if (probeIndex == 2)
                CreateBox(scene, "Wall", new Vector2(origin + 1.7f, 1.5f), new Vector2(0.5f, 3f));
            Probes.Add(new PatrolProbe { Frog = frog, Start = frog.transform.position });
        }

        contactFrog = CreateFrog(scene, new Vector2(1040f, 1f));
        contactFrog.enabled = false;
        Rigidbody2D frogBody = contactFrog.GetComponent<Rigidbody2D>();
        frogBody.constraints = RigidbodyConstraints2D.FreezeAll;
        frogBody.gravityScale = 0f;
        contactHealth = contactFrog.GetComponent<EnemyHealth>();
        SerializedObject health = new SerializedObject(contactHealth);
        health.FindProperty("destroyOnDeath").boolValue = false;
        health.ApplyModifiedPropertiesWithoutUndo();

        GameObject player = CreateBox(scene, "ContactPlayer", new Vector2(1040.93f, 1f), new Vector2(0.8f, 1f));
        player.tag = "Player";
        player.layer = LayerMask.NameToLayer("player");
        Rigidbody2D playerBody = player.AddComponent<Rigidbody2D>();
        playerBody.bodyType = RigidbodyType2D.Kinematic;
        Player movement = player.AddComponent<Player>();
        movement.enabled = false;
        playerHealth = player.AddComponent<PlayerHealth>();
        Physics2D.SyncTransforms();
    }

    private static SpringFrog CreateFrog(Scene scene, Vector2 position)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SpringFrogSetup.PrefabPath);
        GameObject frog = UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity);
        SceneManager.MoveGameObjectToScene(frog, scene);
        return frog.GetComponent<SpringFrog>();
    }

    private static GameObject CreateBox(Scene scene, string name, Vector2 position, Vector2 size)
    {
        GameObject box = new GameObject(name);
        SceneManager.MoveGameObjectToScene(box, scene);
        box.transform.position = position;
        box.AddComponent<BoxCollider2D>().size = size;
        return box;
    }

    private static void Tick()
    {
        try
        {
            float elapsed = Time.time - startedAt;
            foreach (PatrolProbe probe in Probes)
            {
                Rigidbody2D body = probe.Frog.GetComponent<Rigidbody2D>();
                float verticalSpeed = body.linearVelocity.y;
                if (verticalSpeed > 2f && probe.PreviousVerticalSpeed <= 0.1f)
                {
                    probe.Jumps++;
                    probe.HeardJump |= probe.Frog.GetComponent<AudioSource>().isPlaying;
                }
                probe.PreviousVerticalSpeed = verticalSpeed;
                probe.MovedLeft |= body.linearVelocity.x < -0.5f;
                probe.MovedRight |= body.linearVelocity.x > 0.5f;
                probe.Frames.Add(probe.Frog.GetComponentInChildren<SpriteRenderer>().sprite);
                Check(body.position.y > 0.35f, "Patrol fell off its platform.");
                Check(body.position.y < 1.15f, "Jump exceeded the small-hop height.");
                Check(Mathf.Abs(body.position.x - probe.Start.x) < 2.5f, "Patrol escaped the platform.");
            }
            if (elapsed > 0.3f && !contactChecked)
            {
                Check(playerHealth.CurrentHp == playerHealth.MaxHp - 1 && playerHealth.IsInvincible,
                    "Initial body contact did not use PlayerHealth invincibility.");
                contactChecked = true;
            }
            if (elapsed > 1.6f && !deathChecked)
            {
                Check(playerHealth.CurrentHp == playerHealth.MaxHp - 2,
                    "Continuous contact did not respect invincibility/cooldown.");
                IMorningStarHitReceiver receiver = contactFrog.GetComponent<IMorningStarHitReceiver>();
                Check(receiver != null, "Existing MorningStar receiver is missing.");
                receiver.OnMorningStarHit(new MorningStarHitContext(1, Vector2.right, 0f,
                    contactFrog.transform.position, Vector2.right, 3f, 1f));
                Check(contactHealth.IsDeathHandled && contactHealth.CurrentHp == 0,
                    "A single MorningStar hit did not kill SpringFrog.");
                Check(!contactFrog.GetComponent<Collider2D>().enabled && !contactFrog.GetComponent<Rigidbody2D>().simulated
                    && !contactFrog.enabled && !contactFrog.gameObject.activeSelf, "Death did not disable movement/contact.");
                damageAfterDeath = playerHealth.CurrentHp;
                deathChecked = true;
            }
            if (elapsed < 8f)
                return;
            foreach (PatrolProbe probe in Probes)
            {
                Check(probe.Jumps >= 5, "Patrol did not repeat jumps.");
                Check(probe.MovedLeft && probe.MovedRight, "Wall/edge patrol did not reverse.");
                Check(probe.HeardJump, "Jump AudioSource did not play.");
                Check(probe.Frames.Count == 6, "Not all six animation frames were displayed.");
            }
            Check(deathChecked && playerHealth.CurrentHp == damageAfterDeath, "Dead frog continued contact damage.");
            Check(errors == 0, "Console errors occurred during validation.");
            Finish("PASS: 3 independent patrols, left/right, repeated small hops, edges/wall, six frames, jump audio, body damage/invincibility, one-hit death and disabled contact. Errors=0.");
        }
        catch (Exception exception)
        {
            Finish("FAIL: " + exception.Message);
        }
    }

    private static void CountError(string message, string trace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            errors++;
    }

    private static void Finish(string result)
    {
        SessionState.SetString(ResultKey, result);
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= CountError;
        Application.runInBackground = originalRunInBackground;
        EditorApplication.isPlaying = false;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
