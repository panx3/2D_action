using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Deterministic physics checks in a disposable Play Mode scene.</summary>
[InitializeOnLoad]
public static class RisingPlatformValidation
{
    private const string Key = "RisingPlatformValidation.";
    private const string ScenePath = "Assets/Editor/__RisingPlatformValidation.unity";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const float Dt = 0.02f;
    private static GameObject root;
    private static RisingPlatform lift;
    private static Player player;
    private static Rigidbody2D playerBody;
    private static BoxCollider2D playerCollider;

    static RisingPlatformValidation()
    {
        EditorApplication.playModeStateChanged += OnModeChanged;
    }

    [MenuItem("Tools/Validation/Rising Platform")]
    public static void Run()
    {
        Start(false);
    }

    [MenuItem("Tools/Validation/Platforms (Rising + One Way)")]
    public static void RunAll()
    {
        Start(true);
    }

    [MenuItem("Tools/Validation/One Way Platform")]
    public static void RunOneWay()
    {
        Start(true, true);
    }

    private static void Start(bool includeOneWay, bool onlyOneWay = false)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorUtility.scriptCompilationFailed)
            throw new InvalidOperationException("Run from Edit Mode with no compilation errors.");
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            throw new InvalidOperationException("Temporary test scene already exists; refusing to overwrite it.");
        SessionState.SetString(Key + "StartScene", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        Scene active = SceneManager.GetActiveScene();
        Scene temporary = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        EditorSceneManager.SaveScene(temporary, ScenePath);
        EditorSceneManager.CloseScene(temporary, true);
        SceneManager.SetActiveScene(active);
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
        SessionState.SetBool(Key + "Running", true);
        SessionState.SetBool(Key + "IncludeOneWay", includeOneWay);
        SessionState.SetBool(Key + "OnlyOneWay", onlyOneWay);
        SessionState.SetString(Key + "Result", "Pending");
        EditorApplication.isPlaying = true;
    }

    private static void OnModeChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key + "Running", false))
            return;
        if (state == PlayModeStateChange.EnteredPlayMode)
            EditorApplication.delayCall += Execute;
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(
                SessionState.GetString(Key + "StartScene", ""));
            AssetDatabase.DeleteAsset(ScenePath);
            SessionState.SetBool(Key + "Running", false);
            Debug.Log("[RisingPlatformValidation] " + SessionState.GetString(Key + "Result", "Interrupted"));
        }
    }

    private static void Execute()
    {
        SimulationMode2D previousSimulation = Physics2D.simulationMode;
        float previousDt = Time.fixedDeltaTime;
        try
        {
            Time.fixedDeltaTime = Dt;
            Physics2D.simulationMode = SimulationMode2D.Script;
            bool onlyOneWay = SessionState.GetBool(Key + "OnlyOneWay", false);
            if (!onlyOneWay)
            {
                ValidatePrefab();
                ValidateAutomatic();
                ValidateDetection();
                ValidateCarryAndJump();
                ValidateDistances();
                ValidateDirectionsAndRideStart();
            }
            bool includeOneWay = SessionState.GetBool(Key + "IncludeOneWay", false);
            if (includeOneWay)
                OneWayPlatformValidation.Validate();
            SessionState.SetString(Key + "Result", "PASS: "
                + (onlyOneWay ? "" : "Rising distance 2/3/5 in both modes, prefab, automatic waits/repeat/independence, detection, reboarding, carry, movement, jump and upward impulse; four directions, ride-start latch, endpoint waits and carriage. ")
                + (includeOneWay ? "OneWay: Player/ball passage and landing; chain underside length, top support, shared render route, edge release, reset and ordinary terrain." : ""));
        }
        catch (Exception exception)
        {
            Exception cause = exception is TargetInvocationException invocation ? invocation.InnerException : exception;
            SessionState.SetString(Key + "Result", "FAIL: " + cause);
            Debug.LogException(cause);
        }
        finally
        {
            Cleanup();
            Physics2D.simulationMode = previousSimulation;
            Time.fixedDeltaTime = previousDt;
            EditorApplication.isPlaying = false;
        }
    }

    private static void ValidatePrefab()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Gimmicks/RisingPlatform.prefab");
        var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Gimmicks/FallingPlatform.prefab");
        Require(prefab != null && prefab.GetComponent<RisingPlatform>() != null, "Rising prefab missing");
        Require(prefab.GetComponent<FallingPlatform>() == null, "Falling script remained on prefab");
        Require(prefab.GetComponent<SpriteRenderer>().sprite != null
            && prefab.GetComponent<SpriteRenderer>().sharedMaterial == source.GetComponent<SpriteRenderer>().sharedMaterial,
            "Platform sprite or source material missing");
        Require(prefab.GetComponent<Rigidbody2D>().bodyType == RigidbodyType2D.Kinematic
            && !prefab.GetComponent<BoxCollider2D>().isTrigger && prefab.CompareTag("Floor"), "Invalid prefab physics");
    }

    private static void ValidateAutomatic()
    {
        Setup(true);
        Tick(9);
        Require(Y < 0.001f, "Initial bottom wait skipped");
        UntilState("Top");
        Require(Mathf.Abs(Y - 0.8f) < 0.001f, "Automatic ascent failed");
        Tick(8);
        Require(Mathf.Abs(Y - 0.8f) < 0.001f, "Top wait skipped");
        UntilState("Bottom");
        Require(Mathf.Abs(Y) < 0.001f, "Automatic descent failed");
        Tick(8);
        Require(Mathf.Abs(Y) < 0.001f, "Bottom wait skipped");
        var second = UnityEngine.Object.Instantiate(lift.gameObject, new Vector3(10f, 0f, 0f), Quaternion.identity, root.transform).GetComponent<RisingPlatform>();
        Set(second, "bottomWaitTime", 2f);
        Tick(35);
        Require(Y > 0.1f && second.GetComponent<Rigidbody2D>().position.y == 0f, "Instances not independent");
        UnityEngine.Object.DestroyImmediate(second.gameObject);
        Cleanup();
    }

    private static void ValidateDetection()
    {
        Setup(false);
        // Tagged ball/enemy stand-ins have no Player component; neither may activate.
        var impostor = new GameObject("NonPlayerContact");
        impostor.transform.SetParent(root.transform);
        impostor.tag = "Player";
        impostor.AddComponent<Rigidbody2D>();
        impostor.AddComponent<BoxCollider2D>();
        impostor.transform.position = new Vector3(0f, 0.7f, 0f);
        Tick(30);
        Require(Y == 0f, "Non-player activated lift");
        UnityEngine.Object.DestroyImmediate(impostor);
        AddPlayer(new Vector2(lift.GetComponent<BoxCollider2D>().bounds.max.x + 0.4f, 0f));
        playerBody.gravityScale = 0f;
        Tick(20);
        Require(Y == 0f, "Side contact activated lift");
        Board();
        Tick(5);
        Leave();
        Tick(20);
        Require(Y == 0f, "Activation was not canceled by departure");
        Board();
        Tick(18);
        Require(Y > 0.01f && Y < 0.8f, "Player did not start ascent");
        Leave();
        Tick(35);
        Require(Mathf.Abs(Y - 0.8f) < 0.001f, "Early departure interrupted ascent");
        Board();
        Tick(20);
        Require(Mathf.Abs(Y - 0.8f) < 0.001f, "Top did not wait for rider");
        Leave();
        Tick(5);
        Board();
        Tick(20);
        Require(Mathf.Abs(Y - 0.8f) < 0.001f, "Reboarding did not cancel return delay");
        Leave();
        Tick(22);
        Require(Y > 0f && Y < 0.8f, "Return did not begin");
        Board();
        UntilState("Bottom");
        Require(Mathf.Abs(Y) < 0.001f, "Reboarding reversed descent");
        Tick(5);
        Require(Mathf.Abs(Y) < 0.001f, "Reboarding skipped bottom activation delay");
        Tick(20);
        Require(Y > 0.1f, "Next ride did not activate after reaching bottom");
        Cleanup();
    }

    private static void ValidateCarryAndJump()
    {
        Setup(true);
        Set(lift, "moveDistance", 3f);
        Set(lift, "moveSpeed", 2f);
        AddPlayer(Vector2.zero);
        Board();
        for (int i = 0; i < 210; i++)
        {
            Tick(1);
            float gap = playerCollider.bounds.min.y - lift.GetComponent<BoxCollider2D>().bounds.max.y;
            Require(Mathf.Abs(gap) < 0.04f, "Unstable carriage: gap=" + gap + " step=" + i);
            Require(player.IsGrounded, "Ground detection lost during carriage at step=" + i);
        }
        Set(player, "_moveInput", Vector2.right);
        Tick(8);
        Require(playerBody.position.x > 0.05f, "Player horizontal movement blocked");
        Set(player, "_moveInput", Vector2.zero);
        Invoke(player, "PerformJump", false);
        float beforeJump = playerBody.position.y;
        Tick(5);
        Require(playerBody.position.y > beforeJump + 0.05f && !player.IsGrounded, "Jump was captured by lift");
        Board();
        Tick(5);
        player.ApplyExternalImpulse(Vector2.up * 10f);
        float beforePull = playerBody.position.y;
        Tick(4);
        Require(playerBody.position.y > beforePull + 0.05f, "Upward external impulse was canceled");
        Cleanup();
    }

    private static float Y => lift.GetComponent<Rigidbody2D>().position.y;

    private static void ValidateDistances()
    {
        foreach (bool automatic in new[] { true, false })
        foreach (float distance in new[] { 2f, 3f, 5f })
        {
            Vector2 origin = new Vector2(7f, -4f);
            Setup(automatic, origin);
            Set(lift, "moveDistance", distance);
            Set(lift, "moveSpeed", 2f);
            if (!automatic)
            {
                AddPlayer(Vector2.zero);
                Board();
            }
            UntilState("Top");
            Require(Mathf.Abs(Y - origin.y - distance) < 0.001f,
                "Incorrect distance " + distance + " auto=" + automatic);
            Require(Mathf.Abs(lift.GetComponent<Rigidbody2D>().position.x - origin.x) < 0.001f,
                "Vertical lift changed X position");
            if (!automatic)
            {
                Require(player.IsGrounded, "Rider lost grounding at distance " + distance);
                Tick(20);
                Require(Mathf.Abs(Y - origin.y - distance) < 0.001f, "Top did not hold rider");
                Leave();
            }
            UntilState("Bottom");
            Require(Mathf.Abs(Y - origin.y) < 0.001f, "Did not return to placed lower endpoint");
            Set(lift, "moveDistance", 0f);
            Invoke(lift, "OnValidate");
            Require((float)typeof(RisingPlatform).GetField("moveDistance", Private).GetValue(lift) > 0f,
                "Zero distance was accepted");
            Set(lift, "moveDistance", -2f);
            Invoke(lift, "OnValidate");
            Require((float)typeof(RisingPlatform).GetField("moveDistance", Private).GetValue(lift) > 0f,
                "Negative distance was accepted");
            Cleanup();
        }
    }

    private static void ValidateDirectionsAndRideStart()
    {
        Vector2[] directions = { Vector2.up, Vector2.down, Vector2.left, Vector2.right };
        for (int direction = 0; direction < directions.Length; direction++)
        {
            Setup(true, new Vector2(7f, -4f));
            var directionField = typeof(RisingPlatform).GetField("moveDirection", Private);
            directionField.SetValue(lift, Enum.ToObject(directionField.FieldType, direction));
            Set(lift, "startOnPlayerRide", true);
            Vector2 origin = lift.GetComponent<Rigidbody2D>().position;
            Vector2 endpoint = origin + directions[direction] * 0.8f;
            Tick(30);
            Require(lift.GetComponent<Rigidbody2D>().position == origin, "Ride-start skipped initial wait");

            AddPlayer(new Vector2(lift.GetComponent<BoxCollider2D>().bounds.max.x + 0.4f, origin.y));
            playerBody.gravityScale = 0f;
            Tick(20);
            Require(lift.GetComponent<Rigidbody2D>().position == origin, "Side contact started lift");
            Board();
            Tick(5);
            Leave();
            Tick(20);
            Require(lift.GetComponent<Rigidbody2D>().position == origin, "Brief contact bypassed activation delay");

            Board();
            Tick(20);
            Require(Vector2.Distance(lift.GetComponent<Rigidbody2D>().position, origin) > 0.05f,
                "Boarding did not start direction " + direction);
            // Stay aboard through two cycles, including direction changes and waits.
            for (int i = 0; i < 210; i++)
            {
                Tick(1);
                Bounds platformBounds = lift.GetComponent<BoxCollider2D>().bounds;
                Require(Mathf.Abs(playerCollider.bounds.min.y - platformBounds.max.y) < 0.04f,
                    "Carriage gap in direction " + direction + " step=" + i);
                Require(Mathf.Abs(playerBody.position.x - lift.GetComponent<Rigidbody2D>().position.x) < 0.15f,
                    "Horizontal carriage drift in direction " + direction + " step=" + i);
                Require(player.IsGrounded, "Grounding lost in direction " + direction + " step=" + i);
            }

            Leave();
            UntilState("Top");
            Require(Vector2.Distance(lift.GetComponent<Rigidbody2D>().position, endpoint) < 0.001f,
                "Incorrect endpoint in direction " + direction);
            Tick(8);
            Require(Vector2.Distance(lift.GetComponent<Rigidbody2D>().position, endpoint) < 0.001f,
                "Far endpoint wait skipped");
            UntilState("Bottom");
            Require(Vector2.Distance(lift.GetComponent<Rigidbody2D>().position, origin) < 0.001f,
                "Did not return to origin in direction " + direction);
            Tick(8);
            Require(Vector2.Distance(lift.GetComponent<Rigidbody2D>().position, origin) < 0.001f,
                "Starting endpoint wait skipped");
            Tick(15);
            Require(Vector2.Distance(lift.GetComponent<Rigidbody2D>().position, origin) > 0.05f,
                "Lift stopped after rider left in direction " + direction);
            Cleanup();
        }
    }

    private static void Setup(bool automatic, Vector2 origin = default)
    {
        root = new GameObject("RisingPlatformValidationFixture");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Gimmicks/RisingPlatform.prefab");
        lift = UnityEngine.Object.Instantiate(prefab, origin, Quaternion.identity, root.transform).GetComponent<RisingPlatform>();
        Set(lift, "autoMove", automatic);
        Set(lift, "moveDistance", 0.8f);
        Set(lift, "moveSpeed", 1f);
        Set(lift, "topWaitTime", 0.2f);
        Set(lift, "bottomWaitTime", 0.2f);
        Set(lift, "activationDelay", 0.2f);
        Set(lift, "returnDelay", 0.2f);
    }

    private static void AddPlayer(Vector2 position)
    {
        var go = new GameObject("ValidationPlayer");
        go.transform.SetParent(root.transform);
        go.tag = "Player";
        playerBody = go.AddComponent<Rigidbody2D>();
        playerBody.freezeRotation = true;
        playerCollider = go.AddComponent<BoxCollider2D>();
        player = go.AddComponent<Player>();
        player.enabled = false; // Execute actual movement/grounding explicitly for each physics step.
        Set(player, "_jumpVoiceClips", new AudioClip[0]);
        playerBody.position = position;
        Physics2D.SyncTransforms();
    }

    private static void Board()
    {
        float footOffset = playerBody.position.y - playerCollider.bounds.min.y;
        playerBody.position = new Vector2(lift.GetComponent<Rigidbody2D>().position.x,
            lift.GetComponent<BoxCollider2D>().bounds.max.y + footOffset + 0.01f);
        playerBody.linearVelocity = Vector2.zero;
        playerBody.gravityScale = 1f;
        Set(player, "_bjump", false);
        Physics2D.SyncTransforms();
    }

    private static void Leave()
    {
        playerBody.position = new Vector2(20f, 5f);
        playerBody.linearVelocity = Vector2.zero;
        playerBody.gravityScale = 0f;
        Physics2D.SyncTransforms();
    }

    private static void Tick(int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (player != null)
                Invoke(player, "FixedUpdate");
            foreach (RisingPlatform platform in root.GetComponentsInChildren<RisingPlatform>())
                Invoke(platform, "FixedUpdate");
            Physics2D.Simulate(Dt);
        }
    }

    private static void UntilState(string expected)
    {
        for (int i = 0; i < 400; i++)
        {
            if (typeof(RisingPlatform).GetField("state", Private).GetValue(lift).ToString() == expected)
                return;
            Tick(1);
        }
        throw new InvalidOperationException("Did not reach " + expected);
    }

    private static void Cleanup()
    {
        if (root != null)
            UnityEngine.Object.DestroyImmediate(root);
        root = null;
        player = null;
        playerBody = null;
        playerCollider = null;
        lift = null;
    }

    private static void Set(object target, string name, object value)
    {
        FieldInfo field = target.GetType().GetField(name, Private);
        if (field == null)
            throw new MissingFieldException(target.GetType().Name, name);
        field.SetValue(target, value);
    }

    private static void Invoke(object target, string name, params object[] arguments)
    {
        target.GetType().GetMethod(name, Private).Invoke(target, arguments);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
