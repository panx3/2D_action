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
            ValidatePrefab();
            ValidateAutomatic();
            ValidateDetection();
            ValidateCarryAndJump();
            SessionState.SetString(Key + "Result", "PASS: prefab, automatic waits/repeat/independence, player-only top detection, activation cancellation, early departure, return cancellation, descent reboarding, carry, movement, jump and upward impulse.");
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
        Require(prefab.GetComponent<SpriteRenderer>().sprite == source.GetComponent<SpriteRenderer>().sprite
            && prefab.GetComponent<SpriteRenderer>().sharedMaterial == source.GetComponent<SpriteRenderer>().sharedMaterial
            && prefab.transform.localScale == source.transform.localScale, "Source appearance changed");
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
        AddPlayer(new Vector2(2.9f, 0f));
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
        AddPlayer(new Vector2(0f, 0.66f));
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

    private static void Setup(bool automatic)
    {
        root = new GameObject("RisingPlatformValidationFixture");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Gimmicks/RisingPlatform.prefab");
        lift = UnityEngine.Object.Instantiate(prefab, Vector3.zero, Quaternion.identity, root.transform).GetComponent<RisingPlatform>();
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
        playerBody.position = new Vector2(0f, Y + 0.66f);
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
        for (int i = 0; i < 100; i++)
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
