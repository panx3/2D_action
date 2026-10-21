using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.U2D;
using Object = UnityEngine.Object;

/// <summary>Explicit integration checks. Controlled Player velocities are fixtures, not gameplay tuning.</summary>
[InitializeOnLoad]
public static class CameraWorkPlayModeChecks
{
    private const string Key = "Tekkyu.CameraWorkChecks";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly StringBuilder Report = new StringBuilder();
    private static IEnumerator routine;
    private static Player player;
    private static Rigidbody2D body;
    private static Vector2 drive;
    private static bool driving, oldBackground;
    private static int errors;
    private static double started;

    static CameraWorkPlayModeChecks()
    { if (SessionState.GetBool(Key, false)) Subscribe(); }

    [MenuItem("Tools/Tekkyu/Verify Camera Work")]
    public static void Run()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.isDirty
            || scene.path != "Assets/Scenes/CompletScene.unity" || EditorUtility.scriptCompilationFailed)
            throw new InvalidOperationException("Start from saved CompletScene with clean compilation.");
        SessionState.SetBool(Key, true);
        SessionState.SetString(Key + ".Result", "RUNNING");
        Subscribe();
        EditorApplication.isPlaying = true;
    }

    private static void Subscribe()
    {
        EditorApplication.playModeStateChanged -= OnState;
        EditorApplication.playModeStateChanged += OnState;
    }

    private static void OnState(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            Report.Clear(); errors = 0; driving = false;
            started = EditorApplication.timeSinceStartup;
            oldBackground = Application.runInBackground; Application.runInBackground = true;
            Application.logMessageReceived += OnLog;
            routine = Check();
            new GameObject("CameraWorkCheckDriver").AddComponent<CameraWorkCheckDriver>();
            EditorApplication.update += Watchdog;
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            EditorApplication.update -= Watchdog;
            Application.logMessageReceived -= OnLog;
            EditorApplication.playModeStateChanged -= OnState;
            SessionState.SetBool(Key, false);
            routine = null;
        }
    }

    internal static void Tick()
    {
        if (routine == null) return;
        try { if (!routine.MoveNext()) Finish(true, ""); }
        catch (Exception e) { Finish(false, e.ToString()); }
    }

    internal static void Drive()
    { if (driving && body != null) body.linearVelocity = drive; }

    private static IEnumerator Check()
    {
        player = Object.FindAnyObjectByType<Player>(); body = player.GetComponent<Rigidbody2D>();
        var launcher = player.GetComponent<MorningStarLauncher>();
        var camera = Camera.main;
        var follow = camera.GetComponent<CameraFollow>();
        var shake = camera.GetComponent<CameraShake2D>();
        var pixel = camera.GetComponent<PixelPerfectCamera>();
        var intro = player.GetComponent<PlayerStartIntro>();
        Require(camera.transform.Find("GimmickGuide") == null, "Removed gimmick markers must not return");
        Vector3 introCamera = camera.transform.position;
        float introSize = camera.orthographicSize;
        bool capturedIntro = false;
        while (!intro.IsPlaying) yield return null;
        while (intro.IsPlaying)
        {
            Require(follow.enabled && follow.IsEventControlled, "Intro camera ownership");
            Vector3 viewport = camera.WorldToViewportPoint(player.transform.position);
            Require(viewport.x > 0.04f && viewport.x < 0.96f && viewport.y > 0.05f && viewport.y < 0.92f,
                "Entrance Player visibility: " + viewport);
            Require(Vector3.Distance(camera.transform.position, introCamera) < Mathf.Max(0.3f, Time.deltaTime * 13f),
                "Intro camera continuity");
            Require(Mathf.Abs(camera.orthographicSize - introSize) < 0.001f && follow.CurrentMagnetFramingOffset == Vector2.zero,
                "Intro fixed lens / no magnet framing interference");
            introCamera = camera.transform.position;
            if (!capturedIntro && intro.CameraProgress > 0.25f)
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(Path.GetTempPath(), "TekkyuIntroCamera.png"));
                capturedIntro = true;
            }
            yield return null;
        }
        Require(Vector3.Distance(camera.transform.position, introCamera) < 0.08f, "Intro-to-follow position continuity");
        yield return null;
        Require(follow.enabled && !follow.IsEventControlled && player.InputEnabled, "Intro handoff");
        Require(Mathf.Abs(camera.WorldToViewportPoint(player.transform.position).x - 0.4f) < 0.02f,
            "Intro must finish at the normal 40% framing");
        Report.AppendLine("PASS entrance stays visible, smooth intro pan, fixed lens and continuous 40% normal-follow handoff");
        float normalSize = follow.BaseOrthographicSize;
        bool wideView = follow.NormalViewExpansion > 0f;
        Require(normalSize > 0f && pixel.upscaleRT != wideView, "Effective normal-view baseline");

        player.enabled = false; player.SetStartIntroControl(true); player.SetStartIntroMotion(true, Vector2.zero);
        body.gravityScale = 0f; body.linearDamping = 0f;
        foreach (var collider in player.GetComponentsInChildren<Collider2D>()) collider.enabled = false;
        launcher.BeginRespawnPresentation(); launcher.enabled = false;
        driving = true; drive = Vector2.zero;
        follow.SnapToTarget();
        follow.HoldForRespawn(); // Isolate projection changes from grounded body settling.
        yield return null;
        float expandedBottom = camera.transform.position.y - camera.orthographicSize;
        float expandedTop = camera.transform.position.y + camera.orthographicSize;
        float expansion = follow.NormalViewExpansion;
        typeof(CameraFollow).GetField("_normalViewExpansion", Private).SetValue(follow, 0f);
        typeof(CameraFollow).GetField("_zoomInitialized", Private).SetValue(follow, false);
        yield return null; yield return null;
        Require(Mathf.Abs(camera.transform.position.y - camera.orthographicSize - expandedBottom) < 0.02f,
            $"Wider normal view must preserve the previous lower edge: old={camera.transform.position.y - camera.orthographicSize:F4}, expanded={expandedBottom:F4}, size={camera.orthographicSize:F4}, offset={follow.UpperViewOffset:F4}");
        float addedTop = expandedTop - (camera.transform.position.y + camera.orthographicSize);
        Require(!wideView || addedTop > 0.1f, "Wider normal view must add upper visibility");
        typeof(CameraFollow).GetField("_normalViewExpansion", Private).SetValue(follow, expansion);
        typeof(CameraFollow).GetField("_zoomInitialized", Private).SetValue(follow, false);
        yield return null; yield return null;
        Require(Mathf.Abs(camera.orthographicSize - normalSize) < 0.001f
            && Mathf.Abs(camera.transform.position.y - camera.orthographicSize - expandedBottom) < 0.02f,
            "Normal-view expansion must not compound when initialized again");
        Report.AppendLine($"PASS fixed wider view, unchanged lower edge, upper visibility +{addedTop:F3} units");
        follow.EndRespawnTravel();
        drive = Vector2.right * 8f;
        float end = Time.time + 1.8f; while (Time.time < end) yield return null;
        float rightViewportX = camera.WorldToViewportPoint(player.transform.position).x;
        Require(Mathf.Abs(rightViewportX - 0.4f) < 0.03f, "Right movement must frame Player at 40%: " + rightViewportX);
        drive = Vector2.left * 8f;
        float previousX = camera.transform.position.x;
        yield return null;
        Require(Mathf.Abs(camera.transform.position.x - previousX) < 0.7f, "Reversal continuity");
        end = Time.time + 1.8f; while (Time.time < end) yield return null;
        float leftViewportX = camera.WorldToViewportPoint(player.transform.position).x;
        Require(Mathf.Abs(leftViewportX - 0.6f) < 0.03f, "Left movement must frame Player at 60%: " + leftViewportX);
        drive = Vector2.zero;
        end = Time.time + 1.2f; while (Time.time < end) yield return null;
        Require(Mathf.Abs(camera.WorldToViewportPoint(player.transform.position).x - 0.6f) < 0.03f,
            "Stopping must retain the forward view rather than recenter");
        Report.AppendLine($"PASS actual viewport framing right={rightViewportX:F3}, left={leftViewportX:F3}; smooth reversal and stationary direction retention");

        float stepCameraY = camera.transform.position.y;
        drive = Vector2.up * 0.3f;
        end = Time.time + 0.15f; while (Time.time < end) yield return null;
        drive = Vector2.zero;
        Require(follow.CurrentVerticalLookAheadOffset == 0f && Mathf.Abs(camera.transform.position.y - stepCameraY) < 0.01f,
            "Small grounded step must not bob the camera");

        Vector2 jumpOrigin = body.position;
        float jumpCameraY = camera.transform.position.y;
        player.SetStartIntroMotion(false, Vector2.zero);
        float jumpStarted = Time.time;
        while (Time.time - jumpStarted < 1.2f)
        {
            float t = Mathf.Clamp01((Time.time - jumpStarted) / 1.2f);
            body.position = jumpOrigin + Vector2.up * (4f * 3.2f * t * (1f - t));
            body.transform.position = body.position;
            Physics2D.SyncTransforms();
            Require(Mathf.Abs(camera.transform.position.y - jumpCameraY) < 0.02f,
                "Normal-height jump must hold the camera height");
            yield return null;
        }
        body.position = jumpOrigin; body.transform.position = jumpOrigin; Physics2D.SyncTransforms();
        Require(follow.CurrentVerticalLookAheadOffset == 0f, "Normal jump must not add upward lead");
        drive = Vector2.down * 28f;
        end = Time.time + 1.1f; while (Time.time < end) yield return null;
        Require(follow.CurrentVerticalLookAheadOffset < -0.85f && follow.CurrentVerticalLookAheadOffset >= -1.01f, "Restrained fall lead");
        Require(Mathf.Abs(camera.orthographicSize - normalSize) < 0.001f,
            "Automatic zoom must remain off during fast movement");
        Require(pixel.upscaleRT != wideView && pixel.assetsPPU == 32 && pixel.refResolutionX == 640,
            "Normal-view rendering mode and pixel-art reference settings must remain stable");
        float fallingViewportY = camera.WorldToViewportPoint(player.transform.position).y;
        Require(fallingViewportY > 0.3f && fallingViewportY < 0.7f, "Fast fall must preserve usable view above and below Player: " + fallingViewportY);
        Report.AppendLine("PASS normal jump holds camera height, restrained downward lead, fast-fall visibility and constant zoom");

        drive = Vector2.zero; player.SetStartIntroMotion(true, Vector2.zero);
        end = Time.time + 3.5f; while (Time.time < end) yield return null;
        Require(Mathf.Abs(follow.CurrentVerticalLookAheadOffset) < 0.01f, "Landing lead return");
        Require(Mathf.Abs(camera.orthographicSize - normalSize) < 0.001f && pixel.upscaleRT != wideView,
            "Normal lens / rendering mode restore");
        var stablePosition = camera.transform.position;
        // Change actual ball distance/velocity without Player movement: the camera must ignore both.
        launcher.BallBody.position = body.position + Vector2.right * 60f;
        launcher.BallBody.transform.position = launcher.BallBody.position;
        end = Time.time + 0.4f; while (Time.time < end) yield return null;
        Require(Mathf.Abs(camera.WorldToViewportPoint(player.transform.position).x - 0.6f) < 0.03f && camera.orthographicSize == normalSize,
            "Ball-only distance does not pan/zoom");
        Report.AppendLine("PASS landing/idle restore and distant-ball exclusion");

        launcher.enabled = true; launcher.EndRespawnPresentation();
        foreach (var constraint in Object.FindObjectsByType<ChainConstraint2D>()) constraint.enabled = false;
        launcher.ApplyRecallThenLaunch(Vector2.right);
        end = Time.time + 0.5f; while (Time.time < end) yield return null;
        Require(launcher.State == MorningStarLauncher.MorningStarState.Thrown, "Actual ball launch");
        Require(Mathf.Abs(camera.WorldToViewportPoint(player.transform.position).x - 0.6f) < 0.03f && camera.orthographicSize == normalSize,
            "Launch-only must not drive camera lead / zoom");
        launcher.BeginRespawnPresentation(); launcher.enabled = false;
        Report.AppendLine("PASS actual recall/launch with stationary Player; no camera pan or zoom");

        typeof(MorningStarLauncher).GetMethod("TransitionToState", Private).Invoke(launcher,
            new object[] { MorningStarLauncher.MorningStarState.Swinging });
        player.SetStartIntroMotion(false, Vector2.right * 28f); drive = Vector2.right * 28f;
        end = Time.time + 1.8f; while (Time.time < end) yield return null;
        float swingViewportX = camera.WorldToViewportPoint(player.transform.position).x;
        Require(Mathf.Abs(swingViewportX - 0.4f) < 0.03f
            && Mathf.Abs(camera.orthographicSize - normalSize) < 0.001f, "Restrained swing lead / constant zoom");
        Require(camera.transform.eulerAngles == Vector3.zero && Mathf.Approximately(camera.transform.position.z, -10f), "Rotation / depth");
        Report.AppendLine($"PASS high-speed swing viewport X={swingViewportX:F3}, fixed camera rotation/depth, fast-fall viewport Y={fallingViewportY:F3}");

        drive = Vector2.zero;
        var magnet = Object.FindAnyObjectByType<MagnetPoint>();
        Require(magnet != null, "Production magnet missing");
        SpriteRenderer magnetVisual = null;
        foreach (var renderer in magnet.GetComponentsInChildren<SpriteRenderer>())
            if (renderer.enabled && renderer.sprite != null) magnetVisual = renderer;
        Require(magnetVisual != null, "Magnet visual missing");
        Vector2 previousBodyPosition = body.position;
        float previousReferenceSize = (float)typeof(CameraFollow).GetField("_referenceOrthographicSize", Private).GetValue(follow);
        // Exercise the production 640x360 framing even when the Editor Game View is tall.
        camera.aspect = 16f / 9f;
        typeof(CameraFollow).GetField("_referenceOrthographicSize", Private).SetValue(follow, 5.625f);
        typeof(CameraFollow).GetField("_referenceProjectionCaptured", Private).SetValue(follow, true);
        typeof(CameraFollow).GetField("_zoomInitialized", Private).SetValue(follow, false);
        body.position = (Vector2)magnetVisual.bounds.center + new Vector2(-6.5f, -8.5f);
        body.transform.position = body.position; Physics2D.SyncTransforms(); follow.SnapToTarget();
        end = Time.time + 2f; while (Time.time < end) yield return null;
        float magnetSize = camera.orthographicSize;
        Require(follow.CurrentMagnetFramingOffset.y > 0.5f && follow.CurrentMagnetFramingOffset.y <= 2.81f,
            "Near magnet must gently lift the camera");
        Vector3 magnetTop = camera.WorldToViewportPoint(magnetVisual.bounds.max);
        Vector3 playerViewport = camera.WorldToViewportPoint(player.transform.position);
        Require(magnetTop.y <= 1f && magnetTop.x < 1f && playerViewport.y >= 0.2f,
            "Magnet must enter the view while preserving Player / floor visibility: magnet=" + magnetTop + " player=" + playerViewport);
        float settledY = camera.transform.position.y;
        end = Time.time + 0.4f; while (Time.time < end) yield return null;
        Require(Mathf.Abs(camera.transform.position.y - settledY) < 0.03f
            && Mathf.Abs(camera.orthographicSize - magnetSize) < 0.001f, "Magnet framing must settle without swaying or zooming");
        Require(follow.BeginEventControl(player), "Magnet framing event suppression claim");
        yield return null;
        Require(follow.CurrentMagnetFramingOffset == Vector2.zero, "Event presentation takes priority over magnet framing");
        follow.EndEventControl(player);
        body.position = (Vector2)magnetVisual.bounds.center + Vector2.left * 2f;
        body.transform.position = body.position; Physics2D.SyncTransforms(); follow.SnapToTarget();
        end = Time.time + 0.4f; while (Time.time < end) yield return null;
        Require(follow.CurrentMagnetFramingOffset.sqrMagnitude < 0.001f, "Already-visible magnet must not move the camera");
        body.position = (Vector2)magnetVisual.bounds.center + new Vector2(6.5f, -8.5f);
        body.transform.position = body.position; Physics2D.SyncTransforms(); follow.SnapToTarget();
        end = Time.time + 0.4f; while (Time.time < end) yield return null;
        Require(follow.CurrentMagnetFramingOffset.sqrMagnitude < 0.001f, "Passed magnet must not pull the view back");
        body.position = (Vector2)magnetVisual.bounds.center + new Vector2(-6.5f, -12f);
        body.transform.position = body.position; Physics2D.SyncTransforms(); follow.SnapToTarget();
        end = Time.time + 0.4f; while (Time.time < end) yield return null;
        Require(follow.CurrentMagnetFramingOffset.sqrMagnitude < 0.001f,
            "Unreachable upper magnet must not take floor visibility from a lower platform");
        camera.ResetAspect();
        typeof(CameraFollow).GetField("_referenceOrthographicSize", Private).SetValue(follow, previousReferenceSize);
        typeof(CameraFollow).GetField("_zoomInitialized", Private).SetValue(follow, false);
        body.position = previousBodyPosition; body.transform.position = body.position;
        Physics2D.SyncTransforms(); follow.SnapToTarget();
        yield return null; yield return null;
        Require(Mathf.Abs(camera.orthographicSize - normalSize) < 0.001f, "Restore original Game View lens after magnet fixture");
        Report.AppendLine("PASS proximity magnet framing, actual target visibility, player safety, fixed lens, stable settling and event priority; no markers");

        Require(follow.BeginEventControl(player), "Event ownership claim");
        stablePosition = camera.transform.position;
        camera.orthographicSize = normalSize * 1.08f;
        end = Time.time + 0.3f; while (Time.time < end) yield return null;
        Require(camera.transform.position == stablePosition && Mathf.Abs(camera.orthographicSize - normalSize * 1.08f) < 0.001f,
            "Event lens / position priority");
        follow.EndEventControl(player);
        end = Time.time + 0.15f; while (Time.time < end) yield return null;
        Require(!follow.IsEventControlled, "Event release");
        Report.AppendLine("PASS event position/lens priority and release");

        shake.Shake(0.15f, 0.16f);
        float strength = shake.CurrentStrength;
        shake.Shake(0.1f, 0.06f);
        Require(shake.CurrentStrength == strength, "Weak request overwrote strong shake");
        yield return null;
        float amplitude = shake.CurrentAmplitude;
        end = Time.time + 0.07f; while (Time.time < end) yield return null;
        Require(shake.CurrentAmplitude < amplitude, "Shake decay");
        shake.ResetShake();
        var pause = Object.FindAnyObjectByType<PauseMenuController>(); pause.OpenPause();
        stablePosition = camera.transform.position; float pausedSize = camera.orthographicSize;
        double until = EditorApplication.timeSinceStartup + 0.4;
        while (EditorApplication.timeSinceStartup < until) yield return null;
        Require(pause.IsPaused && camera.transform.position == stablePosition && camera.orthographicSize == pausedSize,
            "CRT pause camera freeze");
        pause.ClosePause();
        while (pause.IsPaused) yield return null;
        Report.AppendLine("PASS bounded shake decay/merge and CRT pause/resume");

        var floor = new GameObject("CameraCheckFloor"); floor.tag = "Floor";
        floor.transform.position = new Vector2(-2000f, -2000f);
        floor.AddComponent<BoxCollider2D>().size = new Vector2(30f, 1f);
        for (int i = 0; i < 2; i++)
        {
            int before = launcher.GroundImpactShakeCount;
            var ball = new GameObject("CameraCheckImpactBall");
            ball.transform.position = new Vector2(-2000f, -1999.02f);
            var impactBody = ball.AddComponent<Rigidbody2D>(); impactBody.gravityScale = 0f;
            impactBody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            ball.AddComponent<CircleCollider2D>().radius = 0.4f;
            ball.AddComponent<MorningStarCollisionReporter>().Initialize(launcher);
            impactBody.linearVelocity = Vector2.down * (i == 0 ? 8f : 25f);
            Physics2D.SyncTransforms();
            end = Time.time + 0.16f; while (Time.time < end) yield return null;
            Require(launcher.GroundImpactShakeCount == before + 1, "Actual collision shake count");
            Require(i == 0 ? launcher.LastGroundImpactShakeStrength < 0.1f : launcher.LastGroundImpactShakeStrength >= 0.15f,
                "Impact velocity strength mapping");
            Object.Destroy(ball);
        }
        Object.Destroy(floor);
        Report.AppendLine("PASS actual light/heavy Floor collisions through existing reporter / velocity mapping");

        driving = false; launcher.enabled = true; launcher.EndRespawnPresentation();
        var respawn = Object.FindAnyObjectByType<DeathRespawnManager>();
        var destination = player.transform.position + Vector3.left * 35f;
        stablePosition = camera.transform.position;
        respawn.RespawnFromFall(destination);
        Require(respawn.IsRespawning, "Respawn start");
        yield return null;
        Require(camera.transform.position == stablePosition, "Death hold");
        while (respawn.IsRespawning) yield return null;
        Require(Vector2.Distance(camera.transform.position, destination + (Vector3)follow.FollowOffset) < 0.03f,
            "Respawn endpoint");
        yield return null;
        Require(Vector2.Distance(camera.transform.position, destination + (Vector3)follow.FollowOffset) < 0.08f,
            "Respawn must not jump back to the pre-death follow center");
        Report.AppendLine("PASS fall respawn hold, existing slide and no stale long-distance follow");

        var goal = Object.FindAnyObjectByType<GoalPoint>();
        var hit = new MorningStarHitContext(1, Vector2.zero, 0f, goal.transform.position, Vector2.right, 20f, 1f);
        for (int i = 0; i < 3; i++)
        {
            goal.OnMorningStarHit(hit);
            end = Time.unscaledTime + 0.18f; while (Time.unscaledTime < end) yield return null;
        }
        Require(goal.IsBroken && player.IsGoalInputBlocked, "Goal presentation start");
        shake.ResetShake(); stablePosition = camera.transform.position; float goalSize = camera.orthographicSize;
        end = Time.unscaledTime + 2.7f; while (Time.unscaledTime < end) yield return null;
        Require(camera.transform.position == stablePosition && camera.orthographicSize == goalSize, "Goal camera priority");
        Require(Object.FindAnyObjectByType<GoalMenuController>().IsGoalReached, "CRT result handoff");
        Require(errors == 0, "Runtime Console errors=" + errors);
        Report.AppendLine("PASS crystal -> logo -> CRT result, camera freeze and zero runtime errors");
    }

    private static void Require(bool pass, string message)
    { if (!pass) throw new InvalidOperationException(message); }

    private static void OnLog(string message, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors++; }

    private static void Watchdog()
    { if (routine != null && EditorApplication.timeSinceStartup - started > 90d) Finish(false, "Timed out"); }

    private static void Finish(bool success, string detail)
    {
        EditorApplication.update -= Watchdog; Application.logMessageReceived -= OnLog;
        Time.timeScale = 1f; Application.runInBackground = oldBackground; driving = false;
        string result = (success ? "PASS\n" : "FAIL\n") + Report + detail;
        SessionState.SetString(Key + ".Result", result);
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "TekkyuCameraWorkChecks.txt"), result);
        Debug.Log("[CameraWorkChecks] " + result);
        routine = null; EditorApplication.isPlaying = false;
    }
}

[DefaultExecutionOrder(11000)]
public sealed class CameraWorkCheckDriver : MonoBehaviour
{
    private void FixedUpdate() => CameraWorkPlayModeChecks.Drive();
    private void LateUpdate() => CameraWorkPlayModeChecks.Tick();
}
