using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class HpRopeFeaturePlayModeTest
{
    private const string RunningKey = "HpRopeFeaturePlayModeTest.Running";
    private const string ResultKey = "HpRopeFeaturePlayModeTest.Result";

    private static PlayerHealth health;
    private static SegmentHpBarUI hpUi;
    private static MorningStarLauncher launcher;
    private static ChainLineController chainLine;
    private static ChainConstraint2D chainConstraint;
    private static Rigidbody2D ballBody;
    private static DistanceJoint2D hookJoint;
    private static Vector2 hpBasePosition;
    private static Vector2 returnStartPosition;
    private static double phaseStartedAt;
    private static int phase;
    private static int warnings;
    private static int errors;
    private static bool failed;

    static HpRopeFeaturePlayModeTest()
    {
        if (SessionState.GetBool(RunningKey, false))
            Subscribe();
    }

    public static void Run()
    {
        SessionState.SetBool(RunningKey, true);
        SessionState.EraseString(ResultKey);
        EditorSceneManager.OpenScene("Assets/Scenes/CompletScene.unity");
        Subscribe();
        EditorApplication.isPlaying = true;
    }

    private static void Subscribe()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            Time.timeScale = 1f;
            Application.runInBackground = true;
            phase = 0;
            phaseStartedAt = EditorApplication.timeSinceStartup;
            warnings = 0;
            errors = 0;
            failed = false;
            Application.logMessageReceived -= CountLog;
            Application.logMessageReceived += CountLog;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }
        else if (state == PlayModeStateChange.EnteredEditMode
                 && SessionState.GetBool(RunningKey, false))
        {
            SessionState.SetBool(RunningKey, false);
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            string result = SessionState.GetString(ResultKey, "FAILED: Play Mode ended early");
            Debug.Log("[HpRopeFeatureTest] " + result);
            EditorApplication.Exit(result.StartsWith("PASS", StringComparison.Ordinal) ? 0 : 1);
        }
    }

    private static void CountLog(string message, string stack, LogType type)
    {
        if (stack != null && stack.Contains("UnityEditor.Search.SearchDatabase"))
            return;
        if (message != null && message.StartsWith("No graphic device is available", StringComparison.Ordinal))
            return;

        if (type == LogType.Warning)
            warnings++;
        else if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            errors++;
    }

    private static void Tick()
    {
        try
        {
            double elapsed = EditorApplication.timeSinceStartup - phaseStartedAt;
            switch (phase)
            {
                case 0:
                    if (elapsed < 1d)
                        return;
                    SetupReferences();
                    BeginHpTest();
                    NextPhase();
                    break;

                case 1:
                    if (elapsed < 0.36d)
                        return;

                    Require(Mathf.Abs(hpUi.DisplayHpNormalized - hpUi.TargetHpNormalized) < 0.001f,
                        "HP display did not reach target in about 0.25 seconds");
                    Require(hpUi.MaxShakeOffsetObserved > 0.1f,
                        "Damage shake never moved the HP RectTransform");
                    Require(!hpUi.IsShaking, "Damage shake did not finish");
                    Require(Vector2.Distance(GetHpRect().anchoredPosition, hpBasePosition) < 0.001f,
                        "HP RectTransform did not return to its exact initial position");

                    int hpAfterAcceptedDamage = health.CurrentHp;
                    health.TakeDamage(1);
                    Require(health.CurrentHp == hpAfterAcceptedDamage,
                        "Invincible damage unexpectedly reduced HP");
                    Require(!hpUi.IsShaking, "Invincible damage restarted HP shake");

                    BeginHorizontalSagTest();
                    NextPhase();
                    break;

                case 2:
                    if (elapsed < 0.12d)
                        return;
                    CheckSagAgainstStraightLine("horizontal");
                    ballBody.position = launcher.RopeAnchorWorld + Vector2.up * 2f;
                    ballBody.linearVelocity = Vector2.zero;
                    Physics2D.SyncTransforms();
                    NextPhase();
                    break;

                case 3:
                    if (elapsed < 0.12d)
                        return;
                    CheckSagAgainstStraightLine("vertical");
                    launcher.ResetForRespawn();
                    launcher.ApplyRecallThenLaunch(Vector2.right);
                    NextPhase();
                    break;

                case 4:
                    if (!launcher.IsLaunchRopeLengthActive)
                    {
                        Require(elapsed < 1.5d, "Launch never activated expanded rope length");
                        return;
                    }

                    Require(Mathf.Abs(launcher.MaxRopeLength
                                      - launcher.BaseMaxRopeLength * launcher.LaunchRopeLengthMultiplier) < 0.001f,
                        "Launch effective rope length is not base x multiplier");
                    Require(Mathf.Abs(chainConstraint.MaxRopeLength - launcher.MaxRopeLength) < 0.001f,
                        "Physical ChainConstraint length did not follow launch effective length");
                    Require(chainLine.GetComponent<LineRenderer>().textureMode == LineTextureMode.Tile,
                        "LineRenderer Texture Mode is not Tile");
                    Require(chainLine.GetComponent<LineRenderer>().positionCount == 16,
                        "Sag LineRenderer does not use 16 points");

                    ballBody.position = launcher.RopeAnchorWorld + Vector2.right * launcher.MaxRopeLength;
                    ballBody.linearVelocity = Vector2.zero;
                    Physics2D.SyncTransforms();
                    Require(chainLine.CalculateSagForDistance(launcher.MaxRopeLength) < 0.001f,
                        "Taut chain sag calculation is not zero");

                    returnStartPosition = ballBody.position;
                    launcher.RequestReturn();
                    Require(!launcher.IsLaunchRopeLengthActive,
                        "Recall/Return did not restore base rope length immediately");
                    Require(Mathf.Abs(launcher.MaxRopeLength - launcher.BaseMaxRopeLength) < 0.001f,
                        "Recall/Return effective rope length is not base length");
                    Require(!chainConstraint.enabled, "Return left ChainConstraint enabled outside base length");
                    Require(Vector2.Distance(ballBody.position, returnStartPosition) < 0.01f,
                        "Restoring base length warped the MorningStar");
                    phase = 6;
                    phaseStartedAt = EditorApplication.timeSinceStartup;
                    break;

                case 6:
                    if (launcher.CurrentState != MorningStarLauncher.MorningStarState.Dragging)
                    {
                        Require(elapsed < 1.5d, "MorningStar did not finish Return");
                        return;
                    }
                    BeginMagnetTest();
                    NextPhase();
                    break;

                case 7:
                    if (elapsed < 0.12d)
                        return;
                    Require(!launcher.IsLaunchRopeLengthActive,
                        "Magnet attach retained launch rope multiplier");
                    Require(Mathf.Abs(launcher.MaxRopeLength - launcher.BaseMaxRopeLength) < 0.001f,
                        "Magnet Swing is not using base rope length");
                    Require(hookJoint.enabled && hookJoint.maxDistanceOnly,
                        "Magnet Swing DistanceJoint is not active");
                    Require(Mathf.Abs(hookJoint.distance - launcher.BaseMaxRopeLength) < 0.001f,
                        "Magnet Swing joint radius was multiplied");
                    Require(chainLine.LastSagAmount < 0.12f,
                        "Taut Magnet chain has excessive sag");
                    launcher.RequestReturn();
                    NextPhase();
                    break;

                case 8:
                    if (launcher.CurrentState != MorningStarLauncher.MorningStarState.Dragging)
                    {
                        Require(elapsed < 1.5d, "MorningStar did not return after Magnet release");
                        return;
                    }
                    launcher.ApplyRecallThenLaunch(Vector2.right);
                    NextPhase();
                    break;

                case 9:
                    if (!launcher.IsLaunchRopeLengthActive)
                    {
                        Require(elapsed < 1.5d, "Second launch did not activate rope multiplier");
                        return;
                    }
                    launcher.ResetForRespawn();
                    Require(!launcher.IsLaunchRopeLengthActive
                            && Mathf.Abs(launcher.MaxRopeLength - launcher.BaseMaxRopeLength) < 0.001f,
                        "Respawn reset did not restore base rope length");
                    health.ResetToFullHp();
                    launcher.ApplyRecallThenLaunch(Vector2.right);
                    NextPhase();
                    break;

                case 10:
                    if (!launcher.IsLaunchRopeLengthActive)
                    {
                        Require(elapsed < 1.5d, "Death test launch did not activate rope multiplier");
                        return;
                    }
                    health.TakeDamage(health.MaxHp);
                    Require(health.IsDead, "Lethal damage did not enter death state");
                    Require(!launcher.IsLaunchRopeLengthActive
                            && Mathf.Abs(launcher.MaxRopeLength - launcher.BaseMaxRopeLength) < 0.001f,
                        "Death did not restore base rope length");
                    Require(errors == 0, $"runtime errors={errors}");
                    Complete();
                    break;
            }
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private static void SetupReferences()
    {
        health = UnityEngine.Object.FindAnyObjectByType<PlayerHealth>();
        hpUi = UnityEngine.Object.FindAnyObjectByType<SegmentHpBarUI>();
        launcher = UnityEngine.Object.FindAnyObjectByType<MorningStarLauncher>();
        chainLine = UnityEngine.Object.FindAnyObjectByType<ChainLineController>();
        chainConstraint = UnityEngine.Object.FindAnyObjectByType<ChainConstraint2D>();
        GameObject ball = GameObject.FindGameObjectWithTag("morningstar");
        ballBody = ball != null ? ball.GetComponent<Rigidbody2D>() : null;
        hookJoint = launcher != null ? launcher.GetComponent<DistanceJoint2D>() : null;
        Require(health != null && hpUi != null && launcher != null && chainLine != null
                && chainConstraint != null && ballBody != null && hookJoint != null,
            "Required HP/Launcher/Chain references are missing");

        DeathRespawnManager deathRespawn = UnityEngine.Object.FindAnyObjectByType<DeathRespawnManager>();
        if (deathRespawn != null)
            deathRespawn.enabled = false;
        GimmickRespawnController gimmickRespawn = UnityEngine.Object.FindAnyObjectByType<GimmickRespawnController>();
        if (gimmickRespawn != null)
            gimmickRespawn.enabled = false;
        foreach (RespawnZone zone in UnityEngine.Object.FindObjectsByType<RespawnZone>())
            zone.enabled = false;
    }

    private static void BeginHpTest()
    {
        health.ResetToFullHp();
        hpBasePosition = GetHpRect().anchoredPosition;
        int before = health.CurrentHp;
        health.TakeDamage(1);
        Require(health.CurrentHp == before - 1, "Accepted damage did not reduce actual HP");
        Require(Mathf.Abs(hpUi.TargetHpNormalized - health.CurrentHp / (float)health.MaxHp) < 0.001f,
            "HP target is not connected to PlayerHealth");
        Require(hpUi.DisplayHpNormalized > hpUi.TargetHpNormalized,
            "HP display changed instantly instead of smoothing");
        Require(Mathf.Abs(hpUi.HpSmoothDuration - 0.25f) < 0.001f,
            "HP smooth duration is not 0.25 seconds");
        Require(hpUi.IsShaking, "Accepted damage did not start HP shake");
    }

    private static void BeginHorizontalSagTest()
    {
        launcher.ResetForRespawn();
        ballBody.position = launcher.RopeAnchorWorld + Vector2.right * 2f;
        ballBody.linearVelocity = Vector2.zero;
        Physics2D.SyncTransforms();
    }

    private static void CheckSagAgainstStraightLine(string orientation)
    {
        LineRenderer line = chainLine.GetComponent<LineRenderer>();
        Require(line.positionCount == 16, $"{orientation} sag does not have 16 points");
        Require(chainLine.LastSagAmount > 0.1f, $"{orientation} slack chain did not sag");
        int midIndex = line.positionCount / 2;
        float t = midIndex / (float)(line.positionCount - 1);
        float straightY = Mathf.Lerp(line.GetPosition(0).y, line.GetPosition(line.positionCount - 1).y, t);
        Require(line.GetPosition(midIndex).y < straightY - 0.05f,
            $"{orientation} chain did not sag toward World Down");
    }

    private static void BeginMagnetTest()
    {
        launcher.ResetForRespawn();
        GameObject magnetObject = new GameObject("__HpRopeFeatureMagnet");
        MagnetPoint magnet = magnetObject.AddComponent<MagnetPoint>();
        Vector2 anchor = launcher.RopeAnchorWorld + Vector2.up * launcher.BaseMaxRopeLength;
        magnetObject.transform.position = anchor;
        Require(launcher.TryAttachToMagnet(magnet, ballBody, anchor), "Magnet attach failed");
    }

    private static RectTransform GetHpRect()
    {
        RectTransform[] rects = hpUi.GetComponentsInChildren<RectTransform>(true);
        foreach (RectTransform rect in rects)
        {
            if (rect.name == "HPBarRoot")
                return rect;
        }

        // 旧Sceneとの互換用。
        foreach (RectTransform rect in rects)
        {
            if (rect.name == "HpBarImage")
                return rect;
        }

        throw new InvalidOperationException("HPBarRoot RectTransform missing");
    }

    private static void NextPhase()
    {
        phase++;
        phaseStartedAt = EditorApplication.timeSinceStartup;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void Complete()
    {
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= CountLog;
        SessionState.SetString(ResultKey,
            $"PASS hpSmooth+damageShake+invincibleGuard+launchLength+safeReturn+respawn+death+sag2D+tile+magnetBase; "
            + $"base={launcher.BaseMaxRopeLength:F2}; launch={launcher.BaseMaxRopeLength * launcher.LaunchRopeLengthMultiplier:F2}; "
            + $"warnings={warnings}; errors={errors}");
        EditorApplication.isPlaying = false;
    }

    private static void Fail(Exception exception)
    {
        if (failed)
            return;
        failed = true;
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= CountLog;
        errors++;
        SessionState.SetString(ResultKey,
            $"FAILED: {exception.Message}; phase={phase}; warnings={warnings}; errors={errors}");
        Debug.LogException(exception);
        EditorApplication.isPlaying = false;
    }
}
