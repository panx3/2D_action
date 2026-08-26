using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class MorningStarRollingBreakablePlayTest
{
    private const string RunningKey = "MorningStarRollingBreakablePlayTest.Running";
    private const string ResultKey = "MorningStarRollingBreakablePlayTest.Result";
    private const string WarningKey = "MorningStarRollingBreakablePlayTest.Warnings";
    private const string ErrorKey = "MorningStarRollingBreakablePlayTest.Errors";

    private static double enteredAt;
    private static int phase;
    private static int warningCount;
    private static int errorCount;
    private static bool testFailed;

    private static MorningStarRollingVisual rolling;
    private static MorningStarLauncher launcher;
    private static Rigidbody2D ballBody;
    private static Transform ballRoot;
    private static Transform visual;
    private static Quaternion rootRotation;
    private static float phaseAngle;
    private static GameObject ground;
    private static GameObject magnet;
    private static BreakableWall wall;
    private static GameObject playerProbe;

    static MorningStarRollingBreakablePlayTest()
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
            testFailed = false;
            Application.logMessageReceived -= CountRuntimeLog;
            Application.logMessageReceived += CountRuntimeLog;
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
            Debug.Log($"[MorningStarFeatureTest] {result} warnings={warnings}, errors={errors}");
            EditorApplication.Exit(result.StartsWith("PASS") ? 0 : 1);
        }
    }

    private static void CountRuntimeLog(string condition, string stackTrace, LogType type)
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
                case 0 when elapsed >= 0.6d:
                    SetupRollingTest();
                    phase = 1;
                    break;

                case 1 when elapsed >= 1.2d:
                    phaseAngle = GetVisualAngle();
                    ballBody.linearVelocity = new Vector2(1.5f, 0f);
                    phase = 2;
                    break;

                case 2 when elapsed >= 1.45d:
                    float rightAngle = GetVisualAngle();
                    Require(Mathf.DeltaAngle(phaseAngle, rightAngle) < -1f, "right movement did not rotate clockwise");
                    Require(Quaternion.Angle(rootRotation, ballRoot.rotation) < 0.01f, "physical root rotated");
                    phaseAngle = rightAngle;
                    ballBody.linearVelocity = new Vector2(-1.5f, 0f);
                    phase = 3;
                    break;

                case 3 when elapsed >= 1.7d:
                    float leftAngle = GetVisualAngle();
                    Require(Mathf.DeltaAngle(phaseAngle, leftAngle) > 1f, "left movement did not rotate counterclockwise");
                    phaseAngle = leftAngle;
                    ballBody.linearVelocity = new Vector2(0.05f, 0f);
                    phase = 4;
                    break;

                case 4 when elapsed >= 2.0d:
                    Require(AngleUnchanged(phaseAngle, GetVisualAngle()), "below-minimum movement changed visual angle");
                    phaseAngle = GetVisualAngle();
                    ballBody.linearVelocity = new Vector2(4f, 0f);
                    phase = 5;
                    break;

                case 5 when elapsed >= 2.2d:
                    Require(!AngleUnchanged(phaseAngle, GetVisualAngle()), "high-speed movement did not advance visual angle");
                    ballBody.linearVelocity = Vector2.zero;
                    phaseAngle = GetVisualAngle();
                    phase = 6;
                    break;

                case 6 when elapsed >= 2.5d:
                    Require(AngleUnchanged(phaseAngle, GetVisualAngle()), "stopping reset or changed the last angle");
                    ballBody.position += Vector2.up * 2f;
                    ballBody.linearVelocity = new Vector2(3f, 0f);
                    phase = 7;
                    break;

                case 7 when elapsed >= 2.7d:
                    phaseAngle = GetVisualAngle();
                    phase = 8;
                    break;

                case 8 when elapsed >= 3.0d:
                    Require(AngleUnchanged(phaseAngle, GetVisualAngle()), "airborne movement advanced ground rolling");
                    SetupMagnetPauseTest();
                    phase = 9;
                    break;

                case 9 when elapsed >= 3.5d:
                    phaseAngle = GetVisualAngle();
                    ballBody.linearVelocity = new Vector2(2f, 0f);
                    phase = 10;
                    break;

                case 10 when elapsed >= 3.8d:
                    Require(AngleUnchanged(phaseAngle, GetVisualAngle()), "movement inside MagnetPoint advanced rolling");
                    SetupWallTest();
                    phase = 11;
                    break;

                case 11 when elapsed >= 4.35d:
                    Require(wall != null && !wall.IsBroken, "Player contact broke BreakableWall");
                    Object.Destroy(playerProbe);
                    LaunchBallAtWall(wall.BreakSpeedThreshold - 1f);
                    phase = 12;
                    break;

                case 12 when elapsed >= 4.9d:
                    Require(wall != null && !wall.IsBroken, "below-threshold morningstar broke BreakableWall");
                    LaunchBallAtWall(wall.BreakSpeedThreshold + 4f);
                    phase = 13;
                    break;

                case 13 when elapsed >= 5.35d:
                    Require(wall != null && wall.IsBroken, "above-threshold morningstar did not break BreakableWall");
                    Collider2D wallCollider = wall.GetComponent<Collider2D>();
                    Require(wallCollider != null && !wallCollider.enabled, "broken wall collider remained enabled");
                    VerifyRepeatedHitAndRespawn();
                    Complete();
                    break;
            }
        }
        catch (System.Exception exception)
        {
            Fail(exception);
        }
    }

    private static void SetupRollingTest()
    {
        rolling = Object.FindAnyObjectByType<MorningStarRollingVisual>();
        Require(rolling != null, "MorningStarRollingVisual missing");

        ballRoot = rolling.transform;
        ballBody = ballRoot.GetComponent<Rigidbody2D>();
        visual = rolling.Visual;
        launcher = Object.FindAnyObjectByType<MorningStarLauncher>();
        Require(ballBody != null && visual != null && launcher != null, "rolling references missing");
        Require(ballRoot.GetComponent<SpriteRenderer>() == null, "SpriteRenderer still exists on physical root");
        Require(visual.GetComponent<SpriteRenderer>() != null, "Visual SpriteRenderer missing");
        Require(Mathf.Approximately(rolling.VisualRadius, 0.395f), "unexpected visualRadius");
        Require(Mathf.Approximately(rolling.RotationStep, 30f), "unexpected rotationStep");
        Require(Mathf.Approximately(rolling.MinimumRollSpeed, 0.1f), "unexpected minimumRollSpeed");

        launcher.enabled = false;
        ChainConstraint2D constraint = ballRoot.GetComponent<ChainConstraint2D>();
        if (constraint != null)
            constraint.enabled = false;
        HingeJoint2D hinge = ballRoot.GetComponent<HingeJoint2D>();
        if (hinge != null)
            hinge.enabled = false;

        rootRotation = ballRoot.rotation;
        ground = new GameObject("__MorningStarFeatureTestGround");
        ground.transform.position = new Vector3(-1000f, -1000f, 0f);
        BoxCollider2D groundCollider = ground.AddComponent<BoxCollider2D>();
        groundCollider.size = new Vector2(40f, 1f);

        ballBody.gravityScale = 1f;
        ballBody.position = new Vector2(-1000f, -999.1f);
        ballBody.linearVelocity = Vector2.zero;

        MorningStarCollisionReporter reporter = ballRoot.GetComponent<MorningStarCollisionReporter>();
        if (reporter == null)
            reporter = ballRoot.gameObject.AddComponent<MorningStarCollisionReporter>();
        reporter.Initialize(launcher);
    }

    private static void SetupMagnetPauseTest()
    {
        ballBody.linearVelocity = Vector2.zero;
        ballBody.position = new Vector2(-1000f, -999.1f);

        magnet = new GameObject("__MorningStarFeatureTestMagnet");
        magnet.transform.position = ballBody.position;
        MagnetPoint magnetPoint = magnet.AddComponent<MagnetPoint>();
        magnetPoint.enabled = false;
        CircleCollider2D trigger = magnet.AddComponent<CircleCollider2D>();
        trigger.isTrigger = true;
        trigger.radius = 2f;
    }

    private static void SetupWallTest()
    {
        if (magnet != null)
            Object.Destroy(magnet);

        GameObject wallPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Gimmicks/BreakableWall.prefab");
        Require(wallPrefab != null, "BreakableWall prefab missing");
        GameObject wallObject = Object.Instantiate(wallPrefab);
        wallObject.name = "__MorningStarFeatureTestWall";
        wallObject.transform.position = new Vector3(-990f, -999.1f, 0f);
        wall = wallObject.GetComponent<BreakableWall>();
        Require(wall != null && Mathf.Approximately(wall.BreakSpeedThreshold, 6f), "BreakableWall threshold mismatch");

        playerProbe = new GameObject("__MorningStarFeatureTestPlayerProbe");
        playerProbe.tag = "Player";
        playerProbe.transform.position = new Vector3(-993f, -999.1f, 0f);
        Rigidbody2D probeBody = playerProbe.AddComponent<Rigidbody2D>();
        probeBody.gravityScale = 0f;
        playerProbe.AddComponent<BoxCollider2D>().size = Vector2.one * 0.5f;
        probeBody.linearVelocity = new Vector2(10f, 0f);

        ballBody.linearVelocity = Vector2.zero;
        ballBody.position = new Vector2(-1005f, -997f);
    }

    private static void LaunchBallAtWall(float speed)
    {
        ballBody.position = new Vector2(-993f, -999.1f);
        ballBody.linearVelocity = new Vector2(Mathf.Max(0f, speed), 0f);
    }

    private static void VerifyRepeatedHitAndRespawn()
    {
        MorningStarHitContext highSpeedContext = new MorningStarHitContext(
            1,
            Vector2.zero,
            0f,
            wall.transform.position,
            Vector2.right,
            wall.BreakSpeedThreshold + 1f,
            1f);
        wall.OnMorningStarHit(highSpeedContext);
        wall.OnMorningStarHit(highSpeedContext);

        GimmickRespawnController respawn = Object.FindAnyObjectByType<GimmickRespawnController>();
        if (respawn != null)
            respawn.Respawn();

        Require(wall != null && wall.IsBroken, "respawn unexpectedly restored BreakableWall");
    }

    private static float GetVisualAngle()
    {
        return visual.localEulerAngles.z;
    }

    private static bool AngleUnchanged(float before, float after)
    {
        return Mathf.Abs(Mathf.DeltaAngle(before, after)) < 0.1f;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new System.InvalidOperationException(message);
    }

    private static void Complete()
    {
        if (testFailed)
            return;

        EditorApplication.update -= Tick;
        Application.logMessageReceived -= CountRuntimeLog;
        SessionState.SetString(ResultKey, "PASS: rolling directions/speed/stop/air/magnet and wall player/low/high/collider/repeat/respawn checks passed.");
        SessionState.SetInt(WarningKey, warningCount);
        SessionState.SetInt(ErrorKey, errorCount);
        Debug.Log($"[MorningStarFeatureTest] PASS runtimeWarnings={warningCount}, runtimeErrors={errorCount}");
        EditorApplication.isPlaying = false;
    }

    private static void Fail(System.Exception exception)
    {
        if (testFailed)
            return;

        testFailed = true;
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= CountRuntimeLog;
        SessionState.SetString(ResultKey, "FAILED: " + exception.Message);
        SessionState.SetInt(WarningKey, warningCount);
        SessionState.SetInt(ErrorKey, errorCount + 1);
        Debug.LogException(exception);
        EditorApplication.isPlaying = false;
    }
}
