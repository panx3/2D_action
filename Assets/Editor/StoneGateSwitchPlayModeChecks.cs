using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

/// <summary>Generated artwork, membrane passage and one-shot pressure-switch regression checks.</summary>
[InitializeOnLoad]
public static partial class StoneGateSwitchPlayModeChecks
{
    private const string Key = "StoneGateSwitchChecks";
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly List<string> checks = new List<string>();
    private static Scene probeScene;
    private static PhysicsScene2D physics;
    private static GimmickDoor door;
    private static WeightSwitch floorSwitch;
    private static Rigidbody2D body, ball;
    private static AudioSource audio;
    private static int phase, presses, releases, runtimeErrors, firstFrame;
    private static double started;
    private static bool oldBackground;
    private static float oldTimeScale;

    static StoneGateSwitchPlayModeChecks() => EditorApplication.playModeStateChanged += StateChanged;

    public static string Result() => SessionState.GetString(Key + ".Result", "Not run");

    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorUtility.scriptCompilationFailed)
            throw new InvalidOperationException("Run in compiled Edit Mode.");
        SessionState.SetBool(Key, true);
        SessionState.SetString(Key + ".Result", "Running");
        EditorApplication.isPlaying = true;
    }

    private static void StateChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            checks.Clear(); presses = releases = runtimeErrors = 0; phase = 0;
            oldBackground = Application.runInBackground;
            oldTimeScale = Time.timeScale;
            Application.runInBackground = true;
            Time.timeScale = 1f;
            started = EditorApplication.timeSinceStartup;
            firstFrame = Time.frameCount;
            Application.logMessageReceived += Log;
            EditorApplication.update += Tick;
        }
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= Log;
            Application.runInBackground = oldBackground;
            Time.timeScale = oldTimeScale;
        }
        if (state == PlayModeStateChange.EnteredEditMode)
            SessionState.SetBool(Key, false);
    }

    private static void Tick()
    {
        try
        {
            if (EditorApplication.timeSinceStartup - started > 25)
                throw new Exception("Timed out waiting for Play Mode advancement.");
            if (phase == 0)
            {
                if (Time.frameCount < firstFrame + 3) return;
                Setup();
                phase = 1;
            }
            else if (phase == 1)
            {
                body.linearVelocity = new Vector2(6f, 0f);
                for (int i = 0; i < 100; i++) physics.Simulate(0.02f);
                Require(Mathf.Abs(body.position.x + 0.56f) < 0.08f, "Closed membrane blocks at its center plane, accounting for the Player capsule radius");
                float stoppedOnLeft = body.position.x;
                body.position = new Vector2(4f, 1.65f);
                body.linearVelocity = new Vector2(-50f, 0f);
                for (int i = 0; i < 20; i++) physics.Simulate(0.02f);
                Require(Mathf.Abs(body.position.x - 0.56f) < 0.08f,
                    "Central membrane also blocks a fast approach from the right using the Player collision mode");
                body.position = new Vector2(-4f, 1.65f);
                body.linearVelocity = new Vector2(50f, 0f);
                for (int i = 0; i < 20; i++) physics.Simulate(0.02f);
                Require(Mathf.Abs(body.position.x - stoppedOnLeft) < 0.08f,
                    "Central membrane blocks a fast approach from the left");
                Capture("GateClosed.png", new Vector3(0f, 3.0f, -10f), 4.0f);
                body.linearVelocity = Vector2.zero;
                ball.gameObject.tag = "Untagged";
                ball.position = (Vector2)floorSwitch.GetComponent<Collider2D>().bounds.center;
                physics.Simulate(0.02f);
                Require(!floorSwitch.IsPressed, "Non-Morning-Star contact is ignored");
                ball.position += Vector2.left * 10f;
                physics.Simulate(0.02f);
                ball.gameObject.tag = "morningstar";
                ball.position = (Vector2)floorSwitch.GetComponent<Collider2D>().bounds.center;
                physics.Simulate(0.02f);
                Require(floorSwitch.IsPressed && presses == 1 && releases == 0, "First Morning Star contact latches ON and fires one event");
                Require(door.IsOpen && !door.GetComponent<Collider2D>().enabled, "Switch opens door and immediately removes its blocking collider");
                Require(!floorSwitch.GetComponent<Collider2D>().enabled, "Pressed switch stops accepting contacts");
                started = EditorApplication.timeSinceStartup;
                phase = 2;
            }
            else if (phase == 2)
            {
                if (EditorApplication.timeSinceStartup - started < 0.4) return;
                Require(audio.isPlaying, "Supplied press sound plays on the first contact");
                SpriteRenderer membrane = door.transform.Find("Door_Barrier").GetComponent<SpriteRenderer>();
                Require(membrane.color.a < 0.001f && door.TransitionHadIntermediateFrame, "Only membrane fades out, including intermediate opacity");
                Require(floorSwitch.transform.Find("Visual").GetComponent<SpriteRenderer>().sprite.name.StartsWith("Switch_Pressed"), "Switch finishes on the depressed sprite");
                Require(!floorSwitch.transform.Find("Visual/PressureCap").GetComponent<SpriteRenderer>().enabled, "Moving cap is hidden after settling");
                body.position = new Vector2(-4f, 1.65f);
                body.linearVelocity = new Vector2(6f, 0f);
                for (int i = 0; i < 100; i++) physics.Simulate(0.02f);
                Require(body.position.x > 4f, "Player-sized capsule passes completely through the opened gate");
                body.linearVelocity = Vector2.zero;
                body.position = new Vector2(1.05f, 1.65f);
                body.transform.position = new Vector3(1.05f, 1.65f, 0f);
                physics.Simulate(0.02f);
                Capture("GateOpen.png", new Vector3(0f, 3.0f, -10f), 4.0f);
                Capture("SwitchPressed.png", new Vector3(-6f, 0.7f, -10f), 1.7f);
                phase = 3;
            }
            else if (phase == 3)
            {
                if (EditorApplication.timeSinceStartup - started < 1.6) return;
                Require(!audio.isPlaying, "Press sound ends normally");
                // Re-enable the detector to exercise the latch itself, independently of collider cleanup.
                floorSwitch.GetComponent<Collider2D>().enabled = true;
                ball.position += Vector2.left * 10f;
                physics.Simulate(0.02f);
                ball.position = (Vector2)floorSwitch.GetComponent<Collider2D>().bounds.center;
                physics.Simulate(0.02f);
                floorSwitch.gameObject.SetActive(false);
                floorSwitch.gameObject.SetActive(true);
                floorSwitch.SendMessage("OnTriggerEnter2D", ball.GetComponent<Collider2D>());
                Require(floorSwitch.IsPressed && door.IsOpen && presses == 1 && releases == 0 && !audio.isPlaying,
                    "Recontact and disable/enable preserve ON, with no second event or sound");
                Require(runtimeErrors == 0, "No runtime Error/Exception during the checks");
                phase = 4;
                started = EditorApplication.timeSinceStartup;
                revealCase = 0;
            }
            else if (phase == 4) TickDoorRevealChecks();
        }
        catch (Exception ex) { Finish("FAIL\n" + ex + "\n" + string.Join("\n", checks)); }
    }

    private static void Setup()
    {
        probeScene = SceneManager.CreateScene("StoneGateSwitchProbe", new CreateSceneParameters(LocalPhysicsMode.Physics2D));
        physics = probeScene.GetPhysicsScene2D();
        door = Spawn<GimmickDoor>("Assets/Prefabs/Gimmicks/GimmickDoor.prefab", new Vector3(0f, 2.2f, 0f));
        floorSwitch = Spawn<WeightSwitch>("Assets/Prefabs/Gimmicks/WeightSwitch.prefab", new Vector3(-6f, 0.15f, 0f));
        Require(door.GetComponentsInChildren<Collider2D>().Length == 1, "Door pillars have no hidden blocking colliders");
        var back = door.transform.Find("Door_Back").GetComponent<SpriteRenderer>();
        var front = door.transform.Find("Door_Front").GetComponent<SpriteRenderer>();
        Require(back.sortingOrder < 2 && front.sortingOrder > 2, "Player sorts in front of the back pillar and behind the foreground pillar");
        Require(door.transform.Find("Door_Barrier").GetComponent<SpriteRenderer>().sortingOrder < 2,
            "Player draws in front of the blue membrane");
        Require(back.transform.localPosition == front.transform.localPosition, "Frame layers remain aligned despite legacy scene offsets");
        var evt = (UnityEvent)typeof(WeightSwitch).GetField("onPressed", Fields).GetValue(floorSwitch);
        evt.AddListener(door.Open);
        floorSwitch.Pressed += () => presses++;
        ((UnityEvent)typeof(WeightSwitch).GetField("onReleased", Fields).GetValue(floorSwitch)).AddListener(() => releases++);
        audio = floorSwitch.gameObject.AddComponent<AudioSource>();
        audio.playOnAwake = false;
        typeof(WeightSwitch).GetField("pressAudioSource", Fields).SetValue(floorSwitch, audio);

        var actor = new GameObject("PlayerSizedPassageProbe");
        SceneManager.MoveGameObjectToScene(actor, probeScene);
        actor.layer = 30;
        actor.transform.position = new Vector3(-4f, 1.65f, 0f);
        var capsule = actor.AddComponent<CapsuleCollider2D>();
        capsule.size = new Vector2(1f, 3.3f);
        body = actor.AddComponent<Rigidbody2D>();
        body.gravityScale = 0f;
        body.constraints = RigidbodyConstraints2D.FreezeRotation | RigidbodyConstraints2D.FreezePositionY;
        Player player = UnityEngine.Object.FindAnyObjectByType<Player>();
        if (player != null)
        {
            body.collisionDetectionMode = player.GetComponent<Rigidbody2D>().collisionDetectionMode;
            Require(body.collisionDetectionMode == CollisionDetectionMode2D.Continuous,
                "Production Player uses continuous collision detection for the thin central barrier");
            var art = new GameObject("PlayerArtwork");
            art.layer = 30;
            art.transform.SetParent(actor.transform, false);
            art.transform.localPosition = new Vector3(0f, 0.4f, 0f);
            art.transform.localScale = player.transform.lossyScale;
            var sprite = art.AddComponent<SpriteRenderer>();
            sprite.sprite = player.GetComponent<SpriteRenderer>().sprite;
            sprite.sortingOrder = 2;
        }
        var projectile = new GameObject("MorningStarContactProbe");
        SceneManager.MoveGameObjectToScene(projectile, probeScene);
        projectile.transform.position = new Vector3(-20f, 0f, 0f);
        projectile.AddComponent<CircleCollider2D>().radius = 0.2f;
        projectile.AddComponent<CircleCollider2D>().radius = 0.1f;
        ball = projectile.AddComponent<Rigidbody2D>();
        ball.gravityScale = 0f;
        Capture("SwitchRaised.png", new Vector3(-6f, 0.7f, -10f), 1.7f);
    }

    private static T Spawn<T>(string path, Vector3 position) where T : Component
    {
        var root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
        SceneManager.MoveGameObjectToScene(root, probeScene);
        root.transform.position = position;
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 30;
        return root.GetComponent<T>();
    }

    private static void Capture(string name, Vector3 position, float size)
    {
        SpriteRenderer[] actorSprites = body.GetComponentsInChildren<SpriteRenderer>();
        bool showActor = !name.StartsWith("Switch");
        if (!showActor) foreach (SpriteRenderer sprite in actorSprites) sprite.enabled = false;
        var root = new GameObject("GateArtworkProbeCamera");
        var camera = root.AddComponent<Camera>();
        camera.transform.position = position;
        camera.orthographic = true;
        camera.orthographicSize = size;
        camera.cullingMask = 1 << 30;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.14f, 0.18f, 0.23f);
        var target = new RenderTexture(960, 960, 24);
        var old = RenderTexture.active;
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;
        var texture = new Texture2D(960, 960, TextureFormat.RGBA32, false);
        texture.ReadPixels(new Rect(0, 0, 960, 960), 0, 0);
        texture.Apply();
        Directory.CreateDirectory("output/gate-switch-integration");
        File.WriteAllBytes("output/gate-switch-integration/" + name, texture.EncodeToPNG());
        RenderTexture.active = old;
        camera.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(texture);
        UnityEngine.Object.DestroyImmediate(target);
        UnityEngine.Object.DestroyImmediate(root);
        if (!showActor) foreach (SpriteRenderer sprite in actorSprites) sprite.enabled = true;
    }

    private static void Require(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        checks.Add(label);
    }

    private static void Log(string message, string trace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) runtimeErrors++;
    }

    private static void Finish(string result)
    {
        EditorApplication.update -= Tick;
        SessionState.SetString(Key + ".Result", result);
        Directory.CreateDirectory("output/gate-switch-integration");
        File.WriteAllText("output/gate-switch-integration/validation.txt", result);
        EditorApplication.isPlaying = false;
    }
}
