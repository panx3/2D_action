using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>時間倍率の復帰と実際の被弾・破壊経路を確認する手動実行チェック。</summary>
[InitializeOnLoad]
public static class CombatHitStopPlayModeChecks
{
    private const string Key = "Tekkyu.CombatHitStopChecks";
    private static IEnumerator routine;
    private static MorningStarLauncher launcher;
    private static bool wasBackground;
    private static int errors;
    private static double started;

    static CombatHitStopPlayModeChecks()
    {
        if (SessionState.GetBool(Key, false)) Subscribe();
    }

    [MenuItem("Tools/Tekkyu/Verify Combat Hit Stop")]
    public static void Run()
    {
        if (EditorApplication.isPlaying || EditorUtility.scriptCompilationFailed
            || SceneManager.GetActiveScene().path != "Assets/Scenes/CompletScene.unity")
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
            errors = 0;
            started = EditorApplication.timeSinceStartup;
            wasBackground = Application.runInBackground;
            Application.runInBackground = true;
            Application.logMessageReceived += CountError;
            routine = Check();
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
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors++;
    }

    private static void Tick()
    {
        try
        {
            if (EditorApplication.timeSinceStartup - started > 25d)
                throw new InvalidOperationException("Hit stop check timed out.");
            if (routine.MoveNext()) return;
            Require(errors == 0, "No runtime Errors/Exceptions");
            Finish("PASS: accepted hits, enemy defeat, block context/physical destruction, overlap cap, slow-time restore, pause/resume, disable, respawn and goal handoff; runtimeErrors=0");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Finish("FAIL: " + exception.Message);
        }
    }

    private static IEnumerator Check()
    {
        var player = Object.FindAnyObjectByType<Player>();
        launcher = Object.FindAnyObjectByType<MorningStarLauncher>();
        var pause = Object.FindAnyObjectByType<PauseMenuController>();
        var goal = Object.FindAnyObjectByType<GoalMenuController>(FindObjectsInactive.Include);
        Require(player != null && launcher != null && pause != null && goal != null, "Production owners exist");
        while (!player.InputEnabled) yield return null;
        Require(Time.timeScale == 1f, "Gameplay started");

        var enemyObject = new GameObject("__HitStopEnemyProbe");
        var enemy = enemyObject.AddComponent<EnemyHealth>();
        var settings = new SerializedObject(enemy);
        settings.FindProperty("hitInvincibleDuration").floatValue = 0.2f;
        settings.FindProperty("destroyOnDeath").boolValue = false;
        settings.ApplyModifiedPropertiesWithoutUndo();
        enemy.OnMorningStarHit(Context(1, 12f));
        Require(enemy.CurrentHp == 2 && launcher.IsHitStopping && Time.timeScale == 0f, "Accepted enemy hit stops");
        launcher.CancelHitStop();
        enemy.OnMorningStarHit(Context(1, 12f));
        Require(enemy.CurrentHp == 2 && !launcher.IsHitStopping, "Invincible hit does not stop");
        double until = Time.realtimeSinceStartupAsDouble + 0.23d;
        while (Time.realtimeSinceStartupAsDouble < until) yield return null;
        enemy.OnMorningStarHit(Context(100, 12f));
        Require(enemy.IsDeathHandled && launcher.IsHitStopping, "Enemy defeat stops before disabling receiver");
        Require(EndTime() - Time.realtimeSinceStartupAsDouble > 0.05d, "Defeat has stronger stop");
        while (launcher.IsHitStopping) yield return null;
        Require(Time.timeScale == 1f, "Defeat restores time after enemy deactivation");
        enemy.OnMorningStarHit(Context(1, 12f));
        Require(!launcher.IsHitStopping, "Dead enemy contact does not stop");

        var wallObject = new GameObject("__HitStopWallProbe");
        wallObject.AddComponent<BoxCollider2D>();
        var wall = wallObject.AddComponent<BreakableWall>();
        wall.OnMorningStarHit(Context(1, 1f));
        Require(!wall.IsBroken && !launcher.IsHitStopping, "Below-threshold block hit does not stop");
        wall.OnMorningStarHit(Context(1, 12f));
        Require(wall.IsBroken && launcher.IsHitStopping, "Context block destruction stops");
        launcher.CancelHitStop();
        wall.OnMorningStarHit(Context(1, 12f));
        Require(!launcher.IsHitStopping, "Broken block does not stop again");
        Object.Destroy(wallObject);

        // 戦闘Layerを外し、BreakableWall自身の衝突処理からの停止を確認する。
        var launcherSettings = new SerializedObject(launcher);
        int enemies = launcherSettings.FindProperty("enemyLayers").intValue;
        int blocks = launcherSettings.FindProperty("breakableLayers").intValue;
        launcherSettings.FindProperty("enemyLayers").intValue = 1 << 31;
        launcherSettings.FindProperty("breakableLayers").intValue = 1 << 31;
        launcherSettings.ApplyModifiedPropertiesWithoutUndo();
        wallObject = new GameObject("__PhysicalHitStopWallProbe");
        wallObject.transform.position = new Vector3(-2000f, 2000f);
        wallObject.AddComponent<BoxCollider2D>();
        wall = wallObject.AddComponent<BreakableWall>();
        var ball = new GameObject("__PhysicalHitStopBallProbe");
        ball.tag = "morningstar";
        ball.transform.position = new Vector3(-2001.1f, 2000f);
        var body = ball.AddComponent<Rigidbody2D>();
        body.gravityScale = 0f;
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        ball.AddComponent<CircleCollider2D>().radius = 0.25f;
        ball.AddComponent<MorningStarCollisionReporter>().Initialize(launcher);
        Physics2D.SyncTransforms();
        body.linearVelocity = Vector2.right * 12f;
        until = Time.realtimeSinceStartupAsDouble + 1d;
        while (!wall.IsBroken && Time.realtimeSinceStartupAsDouble < until) yield return null;
        Require(wall.IsBroken && launcher.IsHitStopping, "Physical block destruction stops without combat Layer");
        Object.Destroy(ball);
        Object.Destroy(wallObject);
        launcher.CancelHitStop();
        launcherSettings.Update();
        launcherSettings.FindProperty("enemyLayers").intValue = enemies;
        launcherSettings.FindProperty("breakableLayers").intValue = blocks;
        launcherSettings.ApplyModifiedPropertiesWithoutUndo();

        Time.timeScale = 0.5f;
        launcher.PlayEnemyHitStop(0.03f, false);
        launcher.PlayBlockBreakHitStop();
        launcher.PlayEnemyHitStop(0.1f, false);
        launcher.PlayEnemyHitStop(1f, false);
        Require(EndTime() - Time.realtimeSinceStartupAsDouble <= 0.121d, "Overlapping requests are capped, never summed");
        while (launcher.IsHitStopping) yield return null;
        Require(Time.timeScale == 0.5f, "Overlap preserves original non-unit scale");
        Time.timeScale = 0f;
        launcher.PlayBlockBreakHitStop();
        Require(!launcher.IsHitStopping && Time.timeScale == 0f, "Existing external stop is preserved");
        Time.timeScale = 1f;

        launcher.PlayBlockBreakHitStop();
        pause.OpenPause();
        Require(pause.IsPaused && !launcher.IsHitStopping, "Pause takes ownership during hit stop");
        until = Time.realtimeSinceStartupAsDouble + 0.3d;
        while (Time.realtimeSinceStartupAsDouble < until) yield return null;
        Require(Time.timeScale == 0f, "Hit stop expiry cannot resume pause");
        pause.ClosePause();
        while (pause.IsPaused) yield return null;
        Require(Time.timeScale == 1f, "Pause resumes the original time scale");

        launcher.PlayBlockBreakHitStop();
        launcher.enabled = false;
        Require(!launcher.IsHitStopping && Time.timeScale == 1f, "Disable restores time");
        launcher.enabled = true;
        launcher.PlayBlockBreakHitStop();
        launcher.BeginRespawnPresentation();
        Require(!launcher.IsHitStopping && Time.timeScale == 1f, "Respawn cancels stop");
        launcher.PlayBlockBreakHitStop();
        Require(!launcher.IsHitStopping, "No new stop during respawn");
        launcher.EndRespawnPresentation();
        launcher.PlayBlockBreakHitStop();
        launcher.ResetForRespawn();
        Require(!launcher.IsHitStopping && Time.timeScale == 1f, "Respawn reset restores time");

        launcher.PlayBlockBreakHitStop();
        goal.ShowGoal();
        Require(goal.IsGoalReached && !launcher.IsHitStopping, "Goal takes time ownership");
        until = Time.realtimeSinceStartupAsDouble + 0.2d;
        while (Time.realtimeSinceStartupAsDouble < until) yield return null;
        Require(Time.timeScale == 0f, "Goal remains stopped after hit stop expiry");
    }

    private static MorningStarHitContext Context(int damage, float speed)
        => new MorningStarHitContext(damage, Vector2.zero, 0.025f, Vector2.zero, Vector2.right, speed, 1f, launcher);

    private static double EndTime()
        => (double)typeof(MorningStarLauncher).GetField("_hitStopEndsAt", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(launcher);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Finish(string result)
    {
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= CountError;
        if (launcher != null) launcher.CancelHitStop();
        Time.timeScale = 1f;
        Application.runInBackground = wasBackground;
        SessionState.SetString(Key + ".Result", result);
        Debug.Log("[CombatHitStopChecks] " + result);
        EditorApplication.isPlaying = false;
    }
}
