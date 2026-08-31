using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class HpCameraFeaturePlayModeTest
{
    private const string RunningKey = "HpCameraFeaturePlayModeTest.Running";
    private const string ResultKey = "HpCameraFeaturePlayModeTest.Result";

    private static SegmentHpBarUI hpUi;
    private static PlayerHealth health;
    private static Player player;
    private static Rigidbody2D playerBody;
    private static MorningStarLauncher launcher;
    private static Rigidbody2D ballBody;
    private static CameraFollow cameraFollow;
    private static CameraShake2D cameraShake;
    private static GimmickRespawnController respawn;
    private static Vector2 initialPlayerPosition;
    private static double phaseStartedAt;
    private static int phase;
    private static int warnings;
    private static int errors;
    private static bool finished;

    static HpCameraFeaturePlayModeTest()
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
            finished = false;
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
            Debug.Log("[HpCameraFeatureTest] " + result);
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
        if (finished)
            return;

        try
        {
            double elapsed = EditorApplication.timeSinceStartup - phaseStartedAt;
            switch (phase)
            {
                case 0:
                    if (elapsed < 1d)
                        return;
                    Setup();
                    VerifyHierarchyAndDefaults();
                    health.TakeDamage(Mathf.Max(1, health.MaxHp / 2));
                    Require(hpUi.TargetHpNormalized < 1f, "HP target did not follow real damage");
                    Require(hpUi.DisplayHpNormalized > hpUi.TargetHpNormalized, "HP fill did not start smooth reduction");
                    Require(hpUi.DamageHpNormalized >= hpUi.DisplayHpNormalized,
                        "DamageFill shrank before its configured delay");
                    Require(hpUi.IsShaking, "accepted damage did not start HP shake");
                    NextPhase();
                    break;

                case 1:
                    if (elapsed < 0.12d)
                        return;
                    Require(hpUi.DisplayHpNormalized > hpUi.TargetHpNormalized,
                        "HP fill reached target too early instead of smoothing for about 0.25s");
                    Require(hpUi.DamageHpNormalized > hpUi.DisplayHpNormalized,
                        "DamageFill did not stay behind the primary HP fill");
                    NextPhase();
                    break;

                case 2:
                    if (elapsed < 0.20d)
                        return;
                    Require(Mathf.Abs(hpUi.DisplayHpNormalized - hpUi.TargetHpNormalized) < 0.002f,
                        "HP fill did not reach the target in about 0.25s");
                    Require(!hpUi.IsShaking, "HP shake did not finish");
                    RectTransform root = hpUi.transform.Find("HpCanvas/HPBarRoot") as RectTransform;
                    Require(root != null && Vector2.Distance(root.anchoredPosition, hpUi.InitialAnchoredPosition) < 0.01f,
                        "HP shake did not restore its original position");
                    int hpBeforeBlockedDamage = health.CurrentHp;
                    health.TakeDamage(1);
                    Require(health.CurrentHp == hpBeforeBlockedDamage, "invincibility did not block repeated damage");
                    Require(!hpUi.IsShaking, "blocked damage incorrectly restarted HP shake");
                    NextPhase();
                    break;

                case 3:
                    if (elapsed < 0.18d)
                        return;
                    Require(Mathf.Abs(hpUi.DamageHpNormalized - hpUi.TargetHpNormalized) < 0.002f,
                        "DamageFill did not finish delayed follow");
                    health.ResetToFullHp();
                    health.TakeDamage(health.MaxHp);
                    NextPhase();
                    break;

                case 4:
                    if (elapsed < 0.30d)
                        return;
                    Require(health.CurrentHp == 0, "0 HP setup failed");
                    Require(hpUi.TargetHpNormalized == 0f && hpUi.DisplayHpNormalized < 0.002f,
                        "0 HP did not produce an empty primary fill");
                    Require(hpUi.HpMaskWidth < 0.1f, "0 HP mask still exposed the fill sprite");
                    health.ResetToFullHp();
                    BeginRightLookAhead();
                    NextPhase();
                    break;

                case 5:
                    if (elapsed < 0.40d)
                        return;
                    Require(cameraFollow.CurrentLookAheadOffset > 1f,
                        "right movement did not create positive look ahead");
                    playerBody.linearVelocity = new Vector2(-5f, 0f);
                    NextPhase();
                    break;

                case 6:
                    if (elapsed < 0.05d)
                        return;
                    Require(cameraFollow.CurrentLookAheadOffset > -1f,
                        "look ahead snapped instantly when direction changed");
                    NextPhase();
                    break;

                case 7:
                    if (elapsed < 0.42d)
                        return;
                    Require(cameraFollow.CurrentLookAheadOffset < -1f,
                        "left movement did not create negative look ahead");
                    playerBody.linearVelocity = Vector2.zero;
                    NextPhase();
                    break;

                case 8:
                    if (elapsed < 0.50d)
                        return;
                    Require(Mathf.Abs(cameraFollow.CurrentLookAheadOffset) < 0.15f,
                        "stopping did not return look ahead near zero");
                    BeginSwingLookAhead();
                    NextPhase();
                    break;

                case 9:
                    if (elapsed < 0.22d)
                        return;
                    Require(launcher.IsHookedState, "Magnet Swing test did not remain attached");
                    float swingLimit = cameraFollow.HorizontalLookAhead * cameraFollow.SwingLookAheadMultiplier + 0.12f;
                    Require(Mathf.Abs(cameraFollow.CurrentLookAheadOffset) <= swingLimit,
                        "Magnet Swing look ahead was not reduced");
                    BeginRespawnCameraTest();
                    NextPhase();
                    break;

                case 10:
                    if (elapsed < 0.05d)
                        return;
                    Require(cameraShake.IsShaking, "existing CameraShake did not start");
                    respawn.RespawnAt(initialPlayerPosition, true);
                    Vector3 expected = new Vector3(
                        initialPlayerPosition.x + cameraFollow.FollowOffset.x,
                        initialPlayerPosition.y + cameraFollow.FollowOffset.y,
                        -10f);
                    Require(Vector3.Distance(cameraFollow.transform.position, expected) < 0.02f,
                        "Respawn did not snap Camera to Player immediately");
                    Require(Mathf.Abs(cameraFollow.CurrentLookAheadOffset) < 0.001f,
                        "Respawn did not reset look ahead");
                    Require(!cameraShake.IsShaking, "Respawn did not clear residual CameraShake");
                    Require(cameraFollow.enabled, "CameraFollow was disabled");
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

    private static void Setup()
    {
        hpUi = UnityEngine.Object.FindAnyObjectByType<SegmentHpBarUI>();
        health = UnityEngine.Object.FindAnyObjectByType<PlayerHealth>();
        player = UnityEngine.Object.FindAnyObjectByType<Player>();
        launcher = UnityEngine.Object.FindAnyObjectByType<MorningStarLauncher>();
        cameraFollow = UnityEngine.Object.FindAnyObjectByType<CameraFollow>();
        cameraShake = UnityEngine.Object.FindAnyObjectByType<CameraShake2D>();
        respawn = UnityEngine.Object.FindAnyObjectByType<GimmickRespawnController>();
        GameObject ball = GameObject.FindGameObjectWithTag("morningstar");
        ballBody = ball != null ? ball.GetComponent<Rigidbody2D>() : null;
        playerBody = player != null ? player.Rigidbody2D : null;
        Require(hpUi != null && health != null && player != null && playerBody != null
                && launcher != null && ballBody != null && cameraFollow != null
                && cameraShake != null && respawn != null,
            "required HP/Player/Camera/Respawn references are missing");

        DeathRespawnManager death = UnityEngine.Object.FindAnyObjectByType<DeathRespawnManager>();
        if (death != null)
            death.enabled = false;
        foreach (RespawnZone zone in UnityEngine.Object.FindObjectsByType<RespawnZone>(FindObjectsInactive.Exclude))
            zone.enabled = false;

        initialPlayerPosition = playerBody.position;
        player.enabled = false;
        playerBody.gravityScale = 0f;
        playerBody.linearVelocity = Vector2.zero;
        health.ResetToFullHp();
        launcher.ResetForRespawn();
        cameraFollow.SnapToTarget();
    }

    private static void VerifyHierarchyAndDefaults()
    {
        Transform root = hpUi.transform.Find("HpCanvas/HPBarRoot");
        Require(root != null, "HPBarRoot hierarchy is missing");
        RectTransform frame = root.Find("Frame") as RectTransform;
        RectTransform emptyBar = root.Find("EmptyBar") as RectTransform;
        RectTransform damageMask = root.Find("DamageMask") as RectTransform;
        RectTransform damageFill = root.Find("DamageMask/DamageFill") as RectTransform;
        RectTransform hpMask = root.Find("HpMask") as RectTransform;
        RectTransform hpFill = root.Find("HpMask/HpFill") as RectTransform;
        Require(frame != null && emptyBar != null,
            "Frame or EmptyBar is missing");
        Require(damageMask != null && damageFill != null && hpMask != null && hpFill != null,
            "continuous HP mask hierarchy is missing");

        Vector2 frameAnchorMin = frame.anchorMin;
        Vector2 frameAnchorMax = frame.anchorMax;
        Vector2 frameAnchoredPosition = frame.anchoredPosition;
        Vector2 frameSizeDelta = frame.sizeDelta;
        Vector2 framePivot = frame.pivot;
        Vector3 frameScale = frame.localScale;
        float framePositionZ = frame.localPosition.z;

        hpUi.AlignVisualsToFrame();

        Require(frame.anchorMin == frameAnchorMin && frame.anchorMax == frameAnchorMax
                && frame.anchoredPosition == frameAnchoredPosition && frame.sizeDelta == frameSizeDelta
                && frame.pivot == framePivot && frame.localScale == frameScale
                && Mathf.Approximately(frame.localPosition.z, framePositionZ),
            "AlignVisualsToFrame changed the reference Frame RectTransform");
        Require(emptyBar.localScale == Vector3.one && damageMask.localScale == Vector3.one
                && damageFill.localScale == Vector3.one && hpMask.localScale == Vector3.one
                && hpFill.localScale == Vector3.one,
            "an HP visual uses transform scaling instead of RectTransform sizing");

        Rect frameBounds = GetBounds(frame, root);
        Rect expectedGaugeBounds = Rect.MinMaxRect(
            frameBounds.xMin + frameBounds.width * (62f / 260f),
            frameBounds.yMax - frameBounds.height * ((13f + 7f) / 35f),
            frameBounds.xMin + frameBounds.width * ((62f + 144f) / 260f),
            frameBounds.yMax - frameBounds.height * (13f / 35f));
        Require(BoundsApproximately(GetBounds(emptyBar, root), expectedGaugeBounds)
                && BoundsApproximately(GetBounds(damageMask, root), expectedGaugeBounds)
                && BoundsApproximately(GetBounds(hpMask, root), expectedGaugeBounds),
            "EmptyBar or masks are not aligned to the Frame's central gauge region");
        Require(BoundsApproximately(GetBounds(damageFill, root), frameBounds)
                && BoundsApproximately(GetBounds(hpFill, root), frameBounds),
            "fill sprites do not preserve the Frame sprite alignment");
        Require(Mathf.Abs(hpUi.HpSmoothDuration - 0.25f) < 0.001f, "hpSmoothDuration is not 0.25");
        Require(Mathf.Abs(hpUi.DamageDelay - 0.10f) < 0.001f, "DamageFill delay is not 0.10");
        Require(Mathf.Abs(hpUi.DamageShakeDuration - 0.14f) < 0.001f, "shake duration is not 0.14");
        Require(Vector2.Distance(hpUi.DamageShakeAmount, new Vector2(4f, 1.5f)) < 0.001f,
            "shake amount is not (4, 1.5)");
        Require(hpUi.FullMaskWidth > 100f && Mathf.Abs(hpUi.HpMaskWidth - hpUi.FullMaskWidth) < 0.01f,
            "full HP did not expose the full mask width");
        Require(Mathf.Abs(cameraFollow.HorizontalLookAhead - 2.2f) < 0.001f,
            "horizontalLookAhead is not 2.2");
        Require(Mathf.Abs(cameraFollow.LookAheadSmoothTime - 0.15f) < 0.001f,
            "lookAheadSmoothTime is not 0.15");
    }

    private static Rect GetBounds(RectTransform rect, Transform relativeTo)
    {
        Vector3[] corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        Vector3 bottomLeft = relativeTo.InverseTransformPoint(corners[0]);
        Vector3 topRight = relativeTo.InverseTransformPoint(corners[2]);
        return Rect.MinMaxRect(bottomLeft.x, bottomLeft.y, topRight.x, topRight.y);
    }

    private static bool BoundsApproximately(Rect actual, Rect expected)
    {
        const float tolerance = 0.05f;
        return Mathf.Abs(actual.xMin - expected.xMin) <= tolerance
               && Mathf.Abs(actual.xMax - expected.xMax) <= tolerance
               && Mathf.Abs(actual.yMin - expected.yMin) <= tolerance
               && Mathf.Abs(actual.yMax - expected.yMax) <= tolerance;
    }

    private static void BeginRightLookAhead()
    {
        launcher.ResetForRespawn();
        cameraFollow.SnapToTarget();
        playerBody.linearVelocity = new Vector2(5f, 0f);
    }

    private static void BeginSwingLookAhead()
    {
        launcher.ResetForRespawn();
        cameraFollow.ResetLookAhead();
        GameObject magnetObject = new GameObject("__HpCameraTestMagnet");
        MagnetPoint magnet = magnetObject.AddComponent<MagnetPoint>();
        Vector2 anchor = launcher.RopeAnchorWorld + Vector2.up * launcher.BaseMaxRopeLength;
        magnetObject.transform.position = anchor;
        Require(launcher.TryAttachToMagnet(magnet, ballBody, anchor), "Magnet attach failed");
        playerBody.linearVelocity = new Vector2(8f, 0f);
    }

    private static void BeginRespawnCameraTest()
    {
        launcher.ResetForRespawn();
        playerBody.position = initialPlayerPosition + new Vector2(40f, 20f);
        playerBody.linearVelocity = new Vector2(6f, 0f);
        cameraFollow.transform.position = new Vector3(-40f, -20f, -10f);
        cameraShake.Shake(0.5f, 0.5f);
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
        finished = true;
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= CountLog;
        SessionState.SetString(ResultKey,
            $"PASS frameUnchanged+frameAligned+scaleOne+continuousHp+smooth+damageDelay+shake+invincibility+zeroHp+lookAhead+stop+switch+swing+respawnSnap+cameraShake; warnings={warnings}; errors={errors}");
        EditorApplication.isPlaying = false;
    }

    private static void Fail(Exception exception)
    {
        if (finished)
            return;
        finished = true;
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= CountLog;
        errors++;
        SessionState.SetString(ResultKey,
            $"FAILED: {exception.Message}; phase={phase}; warnings={warnings}; errors={errors}");
        Debug.LogException(exception);
        EditorApplication.isPlaying = false;
    }
}
