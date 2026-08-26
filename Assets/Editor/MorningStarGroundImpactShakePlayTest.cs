using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class MorningStarGroundImpactShakePlayTest
{
    private const string RunningKey = "MorningStarGroundImpactShakePlayTest.Running";
    private const string ResultKey = "MorningStarGroundImpactShakePlayTest.Result";
    private const string WarningKey = "MorningStarGroundImpactShakePlayTest.Warnings";
    private const string ErrorKey = "MorningStarGroundImpactShakePlayTest.Errors";

    private static readonly List<GameObject> TestObjects = new List<GameObject>();

    private static MorningStarLauncher launcher;
    private static Player player;
    private static CameraShake2D cameraShake;
    private static CameraFollow cameraFollow;
    private static GameObject floor;
    private static double enteredAt;
    private static double phaseDeadline;
    private static int phase;
    private static int expectedShakeCount;
    private static int expectedImpactSoundCount;
    private static int warningCount;
    private static int errorCount;
    private static bool failed;
    private static string cameraFollowSnapshot;

    static MorningStarGroundImpactShakePlayTest()
    {
        if (SessionState.GetBool(RunningKey, false))
            Subscribe();
    }

    public static void Run()
    {
        SessionState.SetBool(RunningKey, true);
        SessionState.EraseString(ResultKey);
        SessionState.SetInt(WarningKey, 0);
        SessionState.SetInt(ErrorKey, 0);
        EditorSceneManager.OpenScene("Assets/Scenes/CompletScene.unity");
        Subscribe();
        EditorApplication.isPlaying = true;
    }

    private static void Subscribe()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            enteredAt = EditorApplication.timeSinceStartup;
            phase = 0;
            warningCount = 0;
            errorCount = 0;
            failed = false;
            TestObjects.Clear();
            Application.logMessageReceived -= CountLog;
            Application.logMessageReceived += CountLog;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }
        else if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(RunningKey, false))
        {
            SessionState.SetBool(RunningKey, false);
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            string result = SessionState.GetString(ResultKey, "FAILED: Play Mode ended before completion.");
            int warnings = SessionState.GetInt(WarningKey, warningCount);
            int errors = SessionState.GetInt(ErrorKey, errorCount);
            Debug.Log($"[GroundImpactShakePlayTest] {result} warnings={warnings}, errors={errors}");
            EditorApplication.Exit(result.StartsWith("PASS") ? 0 : 1);
        }
    }

    private static void CountLog(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Warning)
            warningCount++;
        else if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            errorCount++;
    }

    private static void Tick()
    {
        try
        {
            double elapsed = EditorApplication.timeSinceStartup - enteredAt;
            switch (phase)
            {
                case 0 when elapsed >= 0.7d:
                    Setup();
                    SpawnImpactBall("__ShakeTestBelowThreshold", -2006f, -5f);
                    phaseDeadline = elapsed + 0.20d;
                    phase = 1;
                    break;

                case 1 when elapsed >= phaseDeadline:
                    Require(launcher.GroundImpactShakeCount == expectedShakeCount,
                        "below-threshold floor contact triggered shake");
                    Require(launcher.GroundImpactSoundCount == expectedImpactSoundCount,
                        "below-threshold floor contact triggered impact SFX");
                    SpawnImpactBall("__ShakeTestOrdinaryImpact", -2003f, -8.5f);
                    phaseDeadline = elapsed + 0.07d;
                    phase = 2;
                    break;

                case 2 when elapsed >= phaseDeadline:
                    expectedShakeCount++;
                    Require(launcher.GroundImpactShakeCount == expectedShakeCount,
                        "ordinary ground impact did not trigger exactly one shake");
                    expectedImpactSoundCount++;
                    Require(launcher.GroundImpactSoundCount == expectedImpactSoundCount,
                        "ordinary ground impact did not trigger exactly one impact SFX");
                    Require(launcher.LastGroundImpactSpeed >= 7f && launcher.LastGroundImpactSpeed < 12f,
                        "ordinary impact speed was not measured along the ground normal");
                    Require(launcher.LastGroundImpactShakeStrength >= 0.06f
                        && launcher.LastGroundImpactShakeStrength < 0.10f,
                        "ordinary impact did not map to a small shake strength");
                    Require(cameraShake.IsShaking, "CameraShake2D did not receive the ordinary impact request");
                    phaseDeadline = elapsed + 0.13d;
                    phase = 3;
                    break;

                case 3 when elapsed >= phaseDeadline:
                    Require(!cameraShake.IsShaking, "0.10 second shake did not finish");
                    SpawnImpactBall("__ShakeTestHighImpact", -2000f, -20f);
                    phaseDeadline = elapsed + 0.07d;
                    phase = 4;
                    break;

                case 4 when elapsed >= phaseDeadline:
                    expectedShakeCount++;
                    Require(launcher.GroundImpactShakeCount == expectedShakeCount,
                        "high-speed ground impact did not trigger exactly one shake");
                    expectedImpactSoundCount++;
                    Require(launcher.GroundImpactSoundCount == expectedImpactSoundCount,
                        "high-speed ground impact did not trigger exactly one impact SFX");
                    Require(launcher.LastGroundImpactShakeStrength >= 0.15f
                        && launcher.LastGroundImpactShakeStrength <= 0.1601f,
                        "high-speed impact did not map near maximum strength");
                    phaseDeadline = elapsed + 0.15d;
                    phase = 5;
                    break;

                case 5 when elapsed >= phaseDeadline:
                    SpawnImpactBall("__ShakeTestCooldownA", -1997f, -15f);
                    SpawnImpactBall("__ShakeTestCooldownB", -1994f, -15f);
                    phaseDeadline = elapsed + 0.07d;
                    phase = 6;
                    break;

                case 6 when elapsed >= phaseDeadline:
                    expectedShakeCount++;
                    Require(launcher.GroundImpactShakeCount == expectedShakeCount,
                        "simultaneous bounce contacts bypassed the 0.10 second cooldown");
                    expectedImpactSoundCount++;
                    Require(launcher.GroundImpactSoundCount == expectedImpactSoundCount,
                        "simultaneous contacts bypassed the impact SFX cooldown");
                    phaseDeadline = elapsed + 0.15d;
                    phase = 7;
                    break;

                case 7 when elapsed >= phaseDeadline:
                    SpawnWallAndImpactBall();
                    phaseDeadline = elapsed + 0.10d;
                    phase = 8;
                    break;

                case 8 when elapsed >= phaseDeadline:
                    Require(launcher.GroundImpactShakeCount == expectedShakeCount,
                        "Walls-tag collision triggered the ground impact shake");
                    Require(launcher.GroundImpactSoundCount == expectedImpactSoundCount,
                        "Walls-tag collision triggered ground impact SFX");
                    SpawnRollingContactBall();
                    phaseDeadline = elapsed + 0.25d;
                    phase = 9;
                    break;

                case 9 when elapsed >= phaseDeadline:
                    Require(launcher.GroundImpactShakeCount == expectedShakeCount,
                        "horizontal rolling contact triggered shake");
                    Require(launcher.GroundImpactSoundCount == expectedImpactSoundCount,
                        "horizontal rolling contact triggered impact SFX");
                    RunPlayerJumpAndLandingCheck();
                    phaseDeadline = elapsed + 1.60d;
                    phase = 10;
                    break;

                case 10 when elapsed >= phaseDeadline:
                    Require(player.IsGrounded, "Player did not land on the isolated Floor test surface");
                    Require(launcher.GroundImpactShakeCount == expectedShakeCount,
                        "Player jump/landing triggered the morningstar ground impact shake");
                    Require(!cameraShake.IsShaking, "camera shook after Player jump/landing");
                    RunLaunchOnlyCheck();
                    phaseDeadline = elapsed + 0.45d;
                    phase = 11;
                    break;

                case 11 when elapsed >= phaseDeadline:
                    Require(launcher.GroundImpactShakeCount == expectedShakeCount,
                        "launch/tension path triggered camera shake without a ground impact");
                    Require(!cameraShake.IsShaking, "camera kept shaking after launch-only check");
                    Require(cameraFollow != null && cameraFollow.enabled,
                        "CameraFollow was disabled during the shake test");
                    Require(EditorJsonUtility.ToJson(cameraFollow) == cameraFollowSnapshot,
                        "CameraFollow parameters changed during Play Mode");
                    Complete();
                    break;
            }
        }
        catch (System.Exception exception)
        {
            Fail(exception);
        }
    }

    private static void Setup()
    {
        launcher = Object.FindAnyObjectByType<MorningStarLauncher>();
        player = Object.FindAnyObjectByType<Player>();
        Camera mainCamera = Camera.main;
        cameraShake = mainCamera != null ? mainCamera.GetComponent<CameraShake2D>() : null;
        cameraFollow = mainCamera != null ? mainCamera.GetComponent<CameraFollow>() : null;

        Require(launcher != null, "MorningStarLauncher missing");
        Require(player != null, "Player missing");
        Require(cameraShake != null && cameraShake.enabled, "enabled CameraShake2D missing from Main Camera");
        Require(cameraFollow != null && cameraFollow.enabled, "CameraFollow missing or disabled");
        cameraFollowSnapshot = EditorJsonUtility.ToJson(cameraFollow);

        SerializedObject serializedLauncher = new SerializedObject(launcher);
        Require(Mathf.Approximately(serializedLauncher.FindProperty("minimumGroundImpactSpeed").floatValue, 7f),
            "minimumGroundImpactSpeed is not 7");
        Require(Mathf.Approximately(serializedLauncher.FindProperty("shakeDuration").floatValue, 0.10f),
            "shakeDuration is not 0.10");
        Require(Mathf.Approximately(serializedLauncher.FindProperty("minimumShakeStrength").floatValue, 0.06f),
            "minimumShakeStrength is not 0.06");
        Require(Mathf.Approximately(serializedLauncher.FindProperty("maximumShakeStrength").floatValue, 0.16f),
            "maximumShakeStrength is not 0.16");
        Require(Mathf.Approximately(serializedLauncher.FindProperty("maxImpactSpeed").floatValue, 20f),
            "maxImpactSpeed is not 20");
        Require(Mathf.Approximately(serializedLauncher.FindProperty("shakeCooldown").floatValue, 0.10f),
            "shakeCooldown is not 0.10");

        foreach (MorningStarCollisionReporter existingReporter in
                 Object.FindObjectsByType<MorningStarCollisionReporter>(FindObjectsInactive.Include))
        {
            // Collision callbacks are delivered to disabled MonoBehaviours as well, so
            // remove the scene reporter during this isolated test instead of disabling it.
            Object.Destroy(existingReporter);
        }

        expectedShakeCount = launcher.GroundImpactShakeCount;
        expectedImpactSoundCount = launcher.GroundImpactSoundCount;
        floor = new GameObject("__GroundImpactShakeTestFloor");
        TestObjects.Add(floor);
        floor.tag = "Floor";
        floor.transform.position = new Vector3(-2000f, -2000f, 0f);
        BoxCollider2D floorCollider = floor.AddComponent<BoxCollider2D>();
        floorCollider.size = new Vector2(30f, 1f);
    }

    private static void SpawnImpactBall(string name, float x, float downwardSpeed)
    {
        GameObject ball = CreateReporterBall(name);
        ball.transform.position = new Vector3(x, -1999.02f, 0f);
        Rigidbody2D body = ball.GetComponent<Rigidbody2D>();
        body.linearVelocity = new Vector2(0f, downwardSpeed);
    }

    private static GameObject CreateReporterBall(string name)
    {
        GameObject ball = new GameObject(name);
        TestObjects.Add(ball);
        Rigidbody2D body = ball.AddComponent<Rigidbody2D>();
        body.gravityScale = 0f;
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        CircleCollider2D collider = ball.AddComponent<CircleCollider2D>();
        collider.radius = 0.4f;
        MorningStarCollisionReporter reporter = ball.AddComponent<MorningStarCollisionReporter>();
        reporter.Initialize(launcher);
        return ball;
    }

    private static void SpawnWallAndImpactBall()
    {
        GameObject wall = new GameObject("__GroundImpactShakeTestWall");
        TestObjects.Add(wall);
        wall.tag = "Walls";
        wall.transform.position = new Vector3(-1970f, -1998f, 0f);
        BoxCollider2D wallCollider = wall.AddComponent<BoxCollider2D>();
        wallCollider.size = new Vector2(1f, 6f);

        GameObject ball = CreateReporterBall("__ShakeTestWallImpact");
        ball.transform.position = new Vector3(-1970.92f, -1998f, 0f);
        ball.GetComponent<Rigidbody2D>().linearVelocity = new Vector2(20f, 0f);
    }

    private static void SpawnRollingContactBall()
    {
        GameObject ball = CreateReporterBall("__ShakeTestRollingContact");
        ball.transform.position = new Vector3(-1988f, -1999.02f, 0f);
        ball.GetComponent<Rigidbody2D>().linearVelocity = new Vector2(10f, -1f);
    }

    private static void RunLaunchOnlyCheck()
    {
        launcher.enabled = true;
        launcher.ApplyRecallThenLaunch(Vector2.right);
    }

    private static void RunPlayerJumpAndLandingCheck()
    {
        launcher.enabled = false;
        foreach (ChainConstraint2D constraint in Object.FindObjectsByType<ChainConstraint2D>(FindObjectsInactive.Include))
            constraint.enabled = false;

        DeathRespawnManager deathRespawn = Object.FindAnyObjectByType<DeathRespawnManager>(FindObjectsInactive.Include);
        if (deathRespawn != null)
            deathRespawn.enabled = false;

        GameObject playerFloor = new GameObject("__GroundImpactShakePlayerFloor");
        TestObjects.Add(playerFloor);
        playerFloor.tag = "Floor";
        int wallsLayer = LayerMask.NameToLayer("Walls");
        if (wallsLayer >= 0)
            playerFloor.layer = wallsLayer;
        playerFloor.transform.position = new Vector3(-2000f, 20f, 0f);
        BoxCollider2D floorCollider = playerFloor.AddComponent<BoxCollider2D>();
        floorCollider.size = new Vector2(20f, 1f);

        Rigidbody2D playerBody = player.Rigidbody2D;
        Require(playerBody != null, "Player Rigidbody2D missing");
        playerBody.position = new Vector2(-2000f, 21.05f);
        playerBody.linearVelocity = new Vector2(0f, 7f);
        playerBody.WakeUp();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new System.InvalidOperationException(message);
    }

    private static void Complete()
    {
        if (failed)
            return;

        Cleanup();
        Application.logMessageReceived -= CountLog;
        EditorApplication.update -= Tick;
        SessionState.SetInt(WarningKey, warningCount);
        SessionState.SetInt(ErrorKey, errorCount);
        SessionState.SetString(ResultKey,
            $"PASS: threshold, normal/high-speed mapping, cooldown, wall/rolling exclusion, Player jump/landing exclusion, launch-only exclusion and CameraFollow preservation verified. impacts={expectedShakeCount}, warnings={warningCount}, errors={errorCount}");
        EditorApplication.isPlaying = false;
    }

    private static void Fail(System.Exception exception)
    {
        if (failed)
            return;

        failed = true;
        Cleanup();
        Application.logMessageReceived -= CountLog;
        EditorApplication.update -= Tick;
        errorCount++;
        SessionState.SetInt(WarningKey, warningCount);
        SessionState.SetInt(ErrorKey, errorCount);
        SessionState.SetString(ResultKey, "FAILED: " + exception.Message);
        Debug.LogException(exception);
        EditorApplication.isPlaying = false;
    }

    private static void Cleanup()
    {
        foreach (GameObject testObject in TestObjects)
        {
            if (testObject != null)
                Object.Destroy(testObject);
        }

        TestObjects.Clear();
    }
}
