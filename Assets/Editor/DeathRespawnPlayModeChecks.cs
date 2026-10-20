using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>CompletScene上で演出ライフサイクルを確認する手動実行の回帰チェック。</summary>
[InitializeOnLoad]
public static class DeathRespawnPlayModeChecks
{
    private const string Key = "Tekkyu.RespawnChecks";
    private static readonly string Output = Path.Combine(Path.GetTempPath(), "TekkyuRespawnChecks");
    private static readonly StringBuilder Report = new StringBuilder();
    private static DeathRespawnManager manager;
    private static GimmickRespawnController respawn;
    private static DeathRespawnFeedback feedback;
    private static Player player;
    private static PlayerHealth health;
    private static MorningStarLauncher launcher;
    private static CameraFollow follow;
    private static Checkpoint checkpoint;
    private static RespawnZone zone;
    private static Vector3 initial, cameraStart, expected, pausePosition;
    private static int phase, frame, cycles, gameErrors;
    private static double started, cycleStarted, phaseStarted;
    private static float travelStartSpeed, travelMiddleSpeed, travelEndSpeed, lastProgress;
    private static Vector3 lastCamera;
    private static bool deathCapture, rebuildCapture, wasBackground;
    private static double rebuildSeenAt;
    private static float lastSampleTime;
    private static float ballMass, ballGravity, ballDamping;

    static DeathRespawnPlayModeChecks()
    {
        if (SessionState.GetBool(Key, false)) Subscribe();
    }

    [MenuItem("Tools/Tekkyu/Verify Death and Respawn")]
    public static void Run()
    {
        if (EditorApplication.isPlaying || EditorUtility.scriptCompilationFailed
            || UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != "Assets/Scenes/CompletScene.unity")
            throw new InvalidOperationException("Open CompletScene in Edit Mode with a clean compilation first.");
        SessionState.SetBool(Key, true);
        SessionState.EraseString(Key + ".Result");
        Subscribe();
        EditorApplication.isPlaying = true;
    }

    private static void Subscribe()
    {
        EditorApplication.playModeStateChanged -= StateChanged;
        EditorApplication.playModeStateChanged += StateChanged;
    }

    private static void StateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            Directory.CreateDirectory(Output);
            Report.Clear();
            phase = cycles = gameErrors = 0;
            started = phaseStarted = EditorApplication.timeSinceStartup;
            wasBackground = Application.runInBackground;
            Application.runInBackground = true;
            Application.logMessageReceived += CountError;
            EditorApplication.update += Tick;
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= CountError;
            EditorApplication.playModeStateChanged -= StateChanged;
            SessionState.SetBool(Key, false);
        }
    }

    private static void CountError(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) gameErrors++;
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void MovePlayer(Vector3 position)
    {
        player.transform.position = position;
        player.Rigidbody2D.position = position;
        player.Rigidbody2D.linearVelocity = Vector2.zero;
        Physics2D.SyncTransforms();
        launcher.ResetForRespawn();
    }

    private static void BeginCycle(bool fall)
    {
        health.ResetToFullHp();
        cameraStart = follow.transform.position;
        expected = respawn.CurrentRespawnPosition + (Vector3)follow.FollowOffset;
        expected.z = -10f;
        var cameraSettings = new SerializedObject(follow);
        if (cameraSettings.FindProperty("_useBounds").boolValue)
        {
            expected.x = Mathf.Clamp(expected.x, cameraSettings.FindProperty("_minX").floatValue, cameraSettings.FindProperty("_maxX").floatValue);
            expected.y = Mathf.Clamp(expected.y, cameraSettings.FindProperty("_minY").floatValue, cameraSettings.FindProperty("_maxY").floatValue);
        }
        if (fall)
            zone.SendMessage("OnTriggerEnter2D", player.GetComponent<Collider2D>(), SendMessageOptions.RequireReceiver);
        else
            health.TakeDamage(health.MaxHp);
        Require(manager.IsRespawning && health.IsRespawning, "Death/fall did not enter the shared sequence");
        Require(!player.InputEnabled && !player.Rigidbody2D.simulated && !launcher.BallBody.simulated, "Gameplay was not suspended");
        Require(launcher.State == MorningStarLauncher.MorningStarState.Respawning, "Launcher did not suspend through its state authority");
        Require(Vector3.Distance(follow.transform.position, cameraStart) < .12f, "Camera snapped on death");
        // 同じフレームに死亡・落下・被弾が重なっても受付は一つ。
        manager.Respawn(); respawn.Respawn(); health.TakeDamage(999);
        launcher.ApplyRecallThenLaunch(Vector2.right);
        Require(launcher.State == MorningStarLauncher.MorningStarState.Respawning, "Input escaped the respawn state");
        cycleStarted = EditorApplication.timeSinceStartup;
        deathCapture = rebuildCapture = false;
        rebuildSeenAt = -1d;
        travelStartSpeed = travelMiddleSpeed = travelEndSpeed = lastProgress = 0f;
        lastCamera = cameraStart;
        lastSampleTime = Time.time;
        frame = Time.frameCount;
    }

    private static void SampleCycle()
    {
        float age = (float)(EditorApplication.timeSinceStartup - cycleStarted);
        if (age < .18f) Require(Vector3.Distance(follow.transform.position, cameraStart) < .12f, "Camera followed the death position during dissolve");
        if (!deathCapture && age > .06f)
        {
            Require(feedback.ActivePixelCount > 0, "Electronic pixel mesh was empty during dissolve");
            ScreenCapture.CaptureScreenshot(Path.Combine(Output, cycles == 1 ? "fall.png" : "death-" + cycles + ".png"));
            deathCapture = true;
        }
        if (feedback.IsRebuilding && rebuildSeenAt < 0d) rebuildSeenAt = EditorApplication.timeSinceStartup;
        if (!rebuildCapture && feedback.IsRebuilding && EditorApplication.timeSinceStartup - rebuildSeenAt > .08d)
        {
            ScreenCapture.CaptureScreenshot(Path.Combine(Output, "rebuild-" + cycles + ".png"));
            rebuildCapture = true;
        }
        if (follow.IsRespawnTravelling)
        {
            float progress = follow.RespawnTravelProgress;
            Require(progress >= lastProgress, "Camera travel restarted or moved backwards");
            float speed = Vector3.Distance(follow.transform.position, lastCamera) / Mathf.Max(Time.time - lastSampleTime, .0001f);
            if (progress > .03f && progress < .17f) travelStartSpeed = Mathf.Max(travelStartSpeed, speed);
            if (progress > .4f && progress < .6f) travelMiddleSpeed = Mathf.Max(travelMiddleSpeed, speed);
            if (progress > .85f) travelEndSpeed = Mathf.Max(travelEndSpeed, speed);
            lastProgress = progress;
        }
        lastCamera = follow.transform.position;
        lastSampleTime = Time.time;
        if (manager.IsRespawning) Require(!player.InputEnabled, "Input resumed before the sequence finished");
    }

    private static void VerifyCompleted()
    {
        Require(!manager.IsRespawning && !follow.IsRespawnTravelling && !feedback.IsPresenting, "Sequence did not finish");
        Require(player.InputEnabled && player.Rigidbody2D.simulated && launcher.BallBody.simulated, "Input/physics were not restored");
        Require(!health.IsDead && health.CurrentHp == health.MaxHp && health.IsInvincible, "HP or respawn invincibility was not restored");
        Require(launcher.State == MorningStarLauncher.MorningStarState.Dragging, "Launcher did not return to common Rest");
        Require(Mathf.Approximately(launcher.BallBody.mass, ballMass)
            && Mathf.Approximately(launcher.BallBody.gravityScale, ballGravity)
            && Mathf.Approximately(launcher.BallBody.linearDamping, ballDamping), "Rest physics retained launch/death history");
        Require(Vector2.Distance(player.transform.position, respawn.CurrentRespawnPosition) < .35f, "Wrong checkpoint position");
        Require(Vector3.Distance(follow.transform.position, expected) < .15f, "Camera did not converge at the respawn destination");
        Require(!player.GetComponent<SpriteRenderer>().forceRenderingOff, "Player stayed hidden");
        Require(!launcher.BallBody.GetComponentInChildren<SpriteRenderer>().forceRenderingOff, "Ball stayed hidden");
        Require(Vector2.Distance(launcher.BallBody.position, launcher.RopeAnchorWorld) < launcher.BaseMaxRopeLength, "Ball/chain position was not reset");
        Require(travelMiddleSpeed > travelStartSpeed * 1.3f && travelMiddleSpeed > travelEndSpeed * 1.3f,
            $"Travel did not accelerate and decelerate: {travelStartSpeed:F2}/{travelMiddleSpeed:F2}/{travelEndSpeed:F2}");
        float seconds = (float)(EditorApplication.timeSinceStartup - cycleStarted);
        Require(seconds < 1.3f, "Retry took too long");
        int hp = health.CurrentHp;
        health.TakeDamage(1);
        Require(health.CurrentHp == hp, "Immediate post-respawn damage bypassed invincibility");
        Report.AppendLine($"PASS cycle={cycles} duration={seconds:F3}s cameraSpeeds={travelStartSpeed:F2}/{travelMiddleSpeed:F2}/{travelEndSpeed:F2} destination={player.transform.position}");
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isPaused) return;
        try
        {
            double now = EditorApplication.timeSinceStartup;
            Require(now - started < 40d, "Checks timed out");
            if (phase == 0)
            {
                var intro = Object.FindAnyObjectByType<PlayerStartIntro>();
                if (now - started < 1d || (intro != null && intro.IsPlaying)) return;
                manager = Object.FindAnyObjectByType<DeathRespawnManager>();
                player = Object.FindAnyObjectByType<Player>();
                health = player.GetComponent<PlayerHealth>();
                launcher = player.GetComponent<MorningStarLauncher>();
                respawn = Object.FindAnyObjectByType<GimmickRespawnController>();
                follow = Object.FindAnyObjectByType<CameraFollow>();
                feedback = manager.GetComponent<DeathRespawnFeedback>();
                checkpoint = Object.FindAnyObjectByType<Checkpoint>();
                zone = Object.FindAnyObjectByType<RespawnZone>();
                initial = respawn.CurrentRespawnPosition;
                ballMass = launcher.BallBody.mass; ballGravity = launcher.BallBody.gravityScale; ballDamping = launcher.BallBody.linearDamping;
                MovePlayer(initial + Vector3.right * 15f + Vector3.up * 2f);
                follow.SnapToTarget();
                launcher.ApplyRecallThenLaunch(Vector2.right);
                phase = 1; phaseStarted = now;
            }
            else if (phase == 1 && now - phaseStarted > .3d)
            {
                BeginCycle(false); phase = 2;
            }
            else if (phase == 2 && Time.frameCount != frame)
            {
                frame = Time.frameCount;
                SampleCycle();
                if (manager.IsRespawning) return;
                VerifyCompleted();
                // 実際のCheckpoint Triggerを物理接触させて登録更新を確認する。
                MovePlayer(checkpoint.GetComponent<Collider2D>().bounds.center);
                phase = 3; phaseStarted = now;
            }
            else if (phase == 3 && now - phaseStarted > .18d)
            {
                Require(checkpoint.IsActivated && Vector2.Distance(initial, respawn.CurrentRespawnPosition) > 5f, "Checkpoint trigger did not update the destination");
                cycles++;
                MovePlayer(respawn.CurrentRespawnPosition + Vector3.right * 38f + Vector3.up * 2f);
                follow.SnapToTarget();
                Vector3 offscreen = player.transform.position + Vector3.down * 30f;
                MovePlayer(offscreen);
                BeginCycle(true); phase = 4;
            }
            else if (phase == 4 && Time.frameCount != frame)
            {
                frame = Time.frameCount;
                SampleCycle();
                if (manager.IsRespawning) return;
                VerifyCompleted();
                cycles++;
                MovePlayer(respawn.CurrentRespawnPosition + Vector3.right * 1.5f);
                follow.transform.position = expected + Vector3.right * 1.5f;
                BeginCycle(false); phase = 5; phaseStarted = now;
            }
            else if (phase == 5 && follow.IsRespawnTravelling && follow.RespawnTravelProgress > .25f)
            {
                Time.timeScale = 0f;
                pausePosition = follow.transform.position;
                phase = 6; phaseStarted = now;
            }
            else if (phase == 6 && now - phaseStarted > .15d)
            {
                Require(Vector3.Distance(pausePosition, follow.transform.position) < .0001f && manager.IsRespawning, "Paused sequence advanced");
                Time.timeScale = 1f;
                manager.enabled = false;
                Require(player.InputEnabled && !health.IsRespawning && !feedback.IsPresenting && feedback.ActivePixelCount == 0, "Cancellation left presentation/input locked");
                Require(launcher.State == MorningStarLauncher.MorningStarState.Dragging && launcher.BallBody.simulated, "Cancellation left the ball suspended");
                manager.enabled = true;
                Report.AppendLine("PASS short-distance travel, pause and mid-travel cancellation cleanup");
                var bounds = new SerializedObject(follow);
                bounds.FindProperty("_useBounds").boolValue = true;
                bounds.FindProperty("_minX").floatValue = respawn.CurrentRespawnPosition.x + follow.FollowOffset.x + 1f;
                bounds.FindProperty("_maxX").floatValue = respawn.CurrentRespawnPosition.x + follow.FollowOffset.x + 20f;
                bounds.ApplyModifiedPropertiesWithoutUndo();
                MovePlayer(respawn.CurrentRespawnPosition + Vector3.right * 9f);
                follow.SnapToTarget();
                BeginCycle(false); phase = 7;
            }
            else if (phase == 7 && Time.frameCount != frame)
            {
                frame = Time.frameCount;
                SampleCycle();
                if (manager.IsRespawning) return;
                VerifyCompleted();
                // 通常追従が再開し、先読みと移動にも反応すること。
                MovePlayer(player.transform.position + Vector3.right * 7f);
                cameraStart = follow.transform.position;
                phase = 8; phaseStarted = now;
            }
            else if (phase == 8 && now - phaseStarted > .25d)
            {
                Require(follow.transform.position.x > cameraStart.x + .1f, "Normal camera follow did not resume");
                Require(feedback.ActivePixelCount == 0, "Particles remained after their lifetime");
                Require(gameErrors == 0, "Runtime errors/exceptions: " + gameErrors);
                Report.AppendLine("PASS camera bounds and normal follow resumed; no residual particles; runtime Errors/Exceptions=0");
                Finish(true, "");
            }
        }
        catch (Exception e) { Finish(false, e.ToString()); }
    }

    private static void Finish(bool pass, string detail)
    {
        Time.timeScale = 1f;
        Application.runInBackground = wasBackground;
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= CountError;
        string result = (pass ? "PASS\n" : "FAIL\n") + Report + detail;
        File.WriteAllText(Path.Combine(Output, "result.txt"), result);
        SessionState.SetString(Key + ".Result", result);
        Debug.Log("[DeathRespawnChecks] " + result);
        EditorApplication.isPlaying = false;
    }
}
