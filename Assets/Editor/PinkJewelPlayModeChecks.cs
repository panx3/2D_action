using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Explicit, one-pass production-scene checks. Leaves subjective VFX/game-feel tuning to the user.</summary>
[InitializeOnLoad]
public static class PinkJewelPlayModeChecks
{
    private const string Key = "Tekkyu.PinkJewelChecks";
    private static readonly string Output = Path.Combine(Path.GetTempPath(), "TekkyuPinkJewelChecks");
    private static readonly StringBuilder Report = new StringBuilder();
    private static int phase, phaseFrame, errors, oldHandle;
    private static double started, phaseStarted, phaseUnscaledTime;
    private static bool previousBackground;
    private static Player player;
    private static PlayerHealth health;
    private static MorningStarLauncher launcher;
    private static Rigidbody2D body;
    private static float gravity;
    private static RigidbodyConstraints2D constraints;
    private static PinkJewelCollection collection;
    private static PinkJewelPickup[] pickups;
    private static GameObject neutral, chain;
    private static DeathRespawnManager respawn;
    private static int hpBefore;

    static PinkJewelPlayModeChecks()
    {
        if (SessionState.GetBool(Key, false)) Subscribe();
    }

    [MenuItem("Tools/Tekkyu/Pink Jewels/Verify Play Mode")]
    public static void Run()
    {
        if (EditorApplication.isPlaying || EditorUtility.scriptCompilationFailed
            || SceneManager.GetActiveScene().path != "Assets/Scenes/CompletScene.unity")
            throw new InvalidOperationException("Open CompletScene in Edit Mode with clean compilation first.");
        Directory.CreateDirectory(Output);
        SessionState.SetBool(Key, true);
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
            Report.Clear(); phase = errors = 0;
            started = phaseStarted = EditorApplication.timeSinceStartup;
            previousBackground = Application.runInBackground; Application.runInBackground = true;
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
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
        { errors++; Report.AppendLine("ERROR " + message); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Advance(int next)
    {
        phase = next; phaseStarted = EditorApplication.timeSinceStartup; phaseFrame = Time.frameCount;
        phaseUnscaledTime = Time.unscaledTimeAsDouble;
    }

    private static bool Frames(int count = 5) => Time.frameCount - phaseFrame >= count
        && Time.unscaledTimeAsDouble - phaseUnscaledTime >= count * 0.02d;

    private static bool BindStage()
    {
        var intro = Object.FindAnyObjectByType<PlayerStartIntro>();
        if (intro != null && intro.IsPlaying) return false;
        player = Object.FindAnyObjectByType<Player>();
        if (player == null || !player.InputEnabled) return false;
        health = player.GetComponent<PlayerHealth>(); body = player.GetComponent<Rigidbody2D>();
        launcher = player.GetComponent<MorningStarLauncher>();
        collection = PinkJewelCollection.Current;
        pickups = Object.FindObjectsByType<PinkJewelPickup>(FindObjectsInactive.Include).OrderBy(p => p.Slot).ToArray();
        Require(collection != null && pickups.Length == 3 && pickups.Select(p => p.Slot).SequenceEqual(new[] { 1, 2, 3 }),
            "Missing manager, three instances, or unique placement identities");
        gravity = body.gravityScale; constraints = body.constraints;
        return true;
    }

    private static void Freeze()
    {
        player.SetInputEnabled(false); launcher.enabled = false;
        body.gravityScale = 0f; body.linearVelocity = Vector2.zero; body.constraints = RigidbodyConstraints2D.FreezeAll;
    }

    private static void Move(Vector3 position)
    {
        player.transform.position = position; body.position = position; body.linearVelocity = Vector2.zero;
        // Keep equipment and the rope path at the same destination, as production respawn does.
        launcher.ResetForRespawn();
        Physics2D.SyncTransforms();
    }

    private static void Touch(int slot)
    {
        // The production capsule's center is below its Transform origin.
        Vector3 centerOffset = player.GetComponent<Collider2D>().bounds.center - player.transform.position;
        Move(pickups[slot - 1].GetComponent<Collider2D>().bounds.center - centerOffset);
        Object.FindAnyObjectByType<CameraFollow>().SnapToTarget();
    }

    private static PinkJewelIconRow Hud() => Object.FindObjectsByType<PinkJewelIconRow>(FindObjectsInactive.Include)
        .Single(r => r.name.StartsWith("PinkJewelHUD", StringComparison.Ordinal));

    private static void VerifyFresh()
    {
        Require(collection.CollectedMask == 0 && Hud().DisplayedMask == 0, "Fresh stage retained collection state");
        Require(pickups.All(p => !p.IsCollected && p.GetComponent<Collider2D>().enabled
            && p.transform.Find("Visual").gameObject.activeSelf), "A fresh stage did not restore all three jewels");
    }

    private static void Capture(string name) => ScreenCapture.CaptureScreenshot(Path.Combine(Output, name + ".png"));

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isPaused) return;
        EditorApplication.QueuePlayerLoopUpdate();
        try
        {
            double age = EditorApplication.timeSinceStartup - phaseStarted;
            Require(EditorApplication.timeSinceStartup - started < 120d, "Timed out at phase " + phase);
            switch (phase)
            {
                case 0:
                    if (age < 0.5d || !BindStage()) return;
                    VerifyFresh(); hpBefore = health.CurrentHp; Freeze();
                    // Test real physics contacts, including an equipment collider inheriting the Player body.
                    neutral = new GameObject("JewelNeutralProbe", typeof(BoxCollider2D), typeof(Rigidbody2D));
                    neutral.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
                    neutral.transform.position = pickups[0].transform.position;
                    chain = new GameObject("JewelChainProbe", typeof(CircleCollider2D));
                    chain.transform.SetParent(player.transform, false); chain.transform.position = pickups[2].transform.position;
                    // Use the production ball collider as callback input; its chain/Rest owner
                    // may legitimately reposition it after a teleport, so do not fight that owner.
                    pickups[1].gameObject.SendMessage("OnTriggerEnter2D", launcher.BallBody.GetComponent<Collider2D>(),
                        SendMessageOptions.RequireReceiver);
                    Physics2D.SyncTransforms(); Advance(1); break;
                case 1:
                    if (!Frames(8)) return;
                    Require(collection.CollectedMask == 0, "Ball, chain, or neutral object collected a jewel");
                    Report.AppendLine("PASS production ball collider callback / inherited-body chain contact / neutral contact rejected");
                    Object.Destroy(neutral); Object.Destroy(chain); launcher.BallBody.simulated = false;
                    Touch(3); Advance(2); break;
                case 2:
                    if (!Frames(4)) return;
                    Require(collection.CollectedMask == 4 && pickups[2].IsCollected && Hud().DisplayedMask == 4,
                        "Actual Player contact did not update slot 3 and HUD");
                    Require(!collection.TryCollect(3), "Duplicate collection was accepted");
                    Require(Object.FindObjectsByType<PinkJewelPickupVfx>().Length == 1, "Pickup effect was not detached");
                    Require(Object.FindObjectsByType<PinkJewelPickupVfx>()[0].GetComponentsInChildren<ParticleSystem>().Any(p => p.particleCount > 0),
                        "Pickup particle burst was empty");
                    Capture("pickup"); pickups[2].gameObject.SetActive(false); Move(new Vector3(-16f, -0.65f, 0f));
                    Advance(3); break;
                case 3:
                    if (age < 0.15d || !Frames(2)) return;
                    Require(Object.FindObjectsByType<PinkJewelPickupVfx>().Length == 1, "Disabling pickup truncated VFX tail");
                    Require(health.CurrentHp == hpBefore, "Collectible altered HP");
                    Advance(4); break;
                case 4:
                    if (age < 1.2d || !Frames(5)) return;
                    Require(Object.FindObjectsByType<PinkJewelPickupVfx>().Length == 0, "Pickup effect did not clean up");
                    Require(!pickups[2].transform.Find("Visual").gameObject.activeSelf
                        && !pickups[2].GetComponent<Collider2D>().enabled, "Collected visual/trigger remained active");
                    pickups[2].gameObject.SetActive(true);
                    Report.AppendLine("PASS out-of-order slot 3 / duplicate guard / HUD / detached full particle tail / HP unchanged");
                    var checkpoint = Object.FindAnyObjectByType<Checkpoint>();
                    Move(checkpoint.GetComponent<Collider2D>().bounds.center); Advance(5); break;
                case 5:
                    if (!Frames(6)) return;
                    Require(Object.FindAnyObjectByType<Checkpoint>().IsActivated && collection.CollectedMask == 4, "Checkpoint lost state");
                    body.gravityScale = gravity; body.constraints = constraints;
                    launcher.enabled = true; launcher.BallBody.simulated = true;
                    launcher.BallBody.constraints = RigidbodyConstraints2D.FreezeRotation;
                    launcher.ResetForRespawn(); player.SetInputEnabled(true);
                    var gimmick = Object.FindAnyObjectByType<GimmickRespawnController>();
                    Move(gimmick.CurrentRespawnPosition + Vector3.right * 8f);
                    respawn = Object.FindAnyObjectByType<DeathRespawnManager>();
                    respawn.RespawnFromFall(gimmick.CurrentRespawnPosition); Advance(6); break;
                case 6:
                    if (!Frames(8) || respawn.IsRespawning) return;
                    Require(collection.CollectedMask == 4 && Hud().DisplayedMask == 4 && pickups[2].IsCollected,
                        "Ordinary respawn lost collected state");
                    Require(!health.IsDead && health.CurrentHp == hpBefore - 1, "Ordinary respawn HP behavior regressed");
                    Report.AppendLine("PASS actual checkpoint and fall-respawn lifecycle retains collected jewels");
                    Freeze(); health.BeginRespawnProtection(); Touch(1); Advance(7); break;
                case 7:
                    if (!Frames(5)) return;
                    Require(collection.CollectedMask == 4, "Respawn-protected body collected a jewel");
                    health.EndRespawnProtection(); Advance(8); break;
                case 8:
                    if (!Frames(5)) return;
                    Require(collection.CollectedMask == 5 && Hud().DisplayedMask == 5, "TriggerStay did not collect slot 1 after protection");
                    Object.FindAnyObjectByType<CameraFollow>().SnapToTarget(); Capture("hud");
                    var goal = Object.FindAnyObjectByType<GoalMenuController>();
                    goal.CaptureClearTime(); goal.ShowGoal(); Advance(9); break;
                case 9:
                    var goalMenu = Object.FindAnyObjectByType<GoalMenuController>();
                    if (age < 0.5d || !Frames(5) || !goalMenu.GetComponent<CRTOverlayController>().IsReady) return;
                    var result = Object.FindObjectsByType<PinkJewelIconRow>().Single(r => r.name.StartsWith("PinkJewelResult", StringComparison.Ordinal));
                    Require(collection.IsFinalized && collection.ResultMask == 5 && result.DisplayedMask == 5,
                        "Result did not show the stable placement-ordered snapshot");
                    Require(!collection.TryCollect(2) && collection.ResultMask == 5, "Collection changed after clear");
                    Require(result.GetComponentsInChildren<TMPro.TMP_Text>(true).Length == 0, "Jewel UI contains notification text");
                    Capture("result"); Report.AppendLine("PASS 2/3 result snapshot, clear freeze, paused CRT, no jewel text");
                    // ScreenCapture writes at end-of-frame; retain this scene until it completes.
                    Advance(90); break;
                case 90:
                    if (!Frames(2)) return;
                    oldHandle = SceneManager.GetActiveScene().handle; Advance(10);
                    Object.FindAnyObjectByType<GoalMenuController>().RetryStage(); break;
                case 10:
                    if (SceneManager.GetActiveScene().handle == oldHandle || !BindStage()) return;
                    VerifyFresh(); Report.AppendLine("PASS actual goal Retry starts with all jewels restored");
                    Require(collection.TryCollect(2), "Slot 2 setup failed");
                    PinkJewelCollection.PrepareForRespawnReload();
                    oldHandle = SceneManager.GetActiveScene().handle; Advance(11);
                    SceneManager.LoadScene("CompletScene"); break;
                case 11:
                    if (SceneManager.GetActiveScene().handle == oldHandle || !BindStage()) return;
                    Require(collection.CollectedMask == 2 && pickups[1].IsCollected && Hud().DisplayedMask == 2,
                        "Explicit ordinary-respawn scene reload lost state or revived collected jewel");
                    Report.AppendLine("PASS explicit scene-reload respawn preserves state and hides collected instance");
                    health.ResetToFullHp(); health.TakeDamage(health.MaxHp);
                    Require(health.IsDead, "HP0 did not enter game over"); Advance(12); break;
                case 12:
                    // Avoid a compile-time dependency on the in-progress GameOver menu implementation.
                    var gameOver = Object.FindObjectsByType<MonoBehaviour>().FirstOrDefault(c => c.GetType().Name == "GameOverMenuController");
                    if (gameOver == null || !gameOver.GetComponent<CRTOverlayController>().IsReady || !Frames(5)) return;
                    Require(collection.CollectedMask == 2, "Game over erased the attempt before Retry");
                    oldHandle = SceneManager.GetActiveScene().handle; Advance(13);
                    gameOver.SendMessage("RetryStage", SendMessageOptions.RequireReceiver); break;
                case 13:
                    if (SceneManager.GetActiveScene().handle == oldHandle || !BindStage()) return;
                    VerifyFresh(); Report.AppendLine("PASS actual HP0 game-over Retry resets every jewel");
                    Require(collection.TryCollect(1), "New-start setup failed");
                    Advance(14); SceneManager.LoadScene("TitleScene"); break;
                case 14:
                    if (SceneManager.GetActiveScene().name != "TitleScene" || !Frames(5)) return;
                    Advance(15); SceneManager.LoadScene("CompletScene"); break;
                case 15:
                    if (SceneManager.GetActiveScene().name != "CompletScene" || !BindStage()) return;
                    VerifyFresh(); Report.AppendLine("PASS Title -> new stage starts fresh");
                    Require(errors == 0, "New runtime errors: " + errors);
                    Finish(true, "All integration checks passed; final visual/game-feel evaluation remains manual."); break;
            }
        }
        catch (Exception exception) { Finish(false, "phase=" + phase + " " + exception.Message); }
    }

    private static void Finish(bool success, string message)
    {
        Report.AppendLine((success ? "PASS " : "FAIL ") + message);
        if (!success && player != null)
        {
            Report.AppendLine($"diagnostic player={player.transform.position} body={body.position} simulated={body.simulated} dead={health.IsDead} respawning={health.IsRespawning} goalBlocked={player.IsGoalInputBlocked} scale={Time.timeScale}");
            if (collection != null && pickups != null)
                Report.AppendLine($"diagnostic mask={collection.CollectedMask} hud={Hud().DisplayedMask} pickups=" +
                    string.Join(";", pickups.Select(p => $"{p.Slot}:collected={p.IsCollected},enabled={p.enabled},trigger={p.GetComponent<Collider2D>().enabled},bounds={p.GetComponent<Collider2D>().bounds}")));
        }
        Report.AppendLine("runtimeErrors=" + errors);
        File.WriteAllText(Path.Combine(Output, "report.txt"), Report.ToString());
        SessionState.SetString(Key + ".Result", Report.ToString());
        Application.runInBackground = previousBackground;
        Time.timeScale = 1f;
        EditorApplication.update -= Tick;
        EditorApplication.isPlaying = false;
    }
}
