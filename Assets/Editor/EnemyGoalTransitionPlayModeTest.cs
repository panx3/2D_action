using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class EnemyGoalTransitionPlayModeTest
{
    public const string RequestPath = "Temp/EnemyGoalTransitionPlayModeTest.request";
    private const string ScenePath = "Assets/Scenes/CompletScene.unity";
    private const string RunningKey = "EnemyGoalTransitionPlayModeTest.Running";
    private const string ResultKey = "EnemyGoalTransitionPlayModeTest.Result";

    private static double phaseStartedAt;
    private static int phase;
    private static int warnings;
    private static int errors;
    private static int fragmentBaseline;
    private static GameObject enemyInstance;
    private static GoalMenuController goalMenu;
    private static GoalPoint goalPoint;
    private static PauseMenuController pauseMenu;
    private static Checkpoint checkpoint;
    private static Rigidbody2D playerBody;
    private static Sprite previousGoalSprite;
    private static bool finished;
    private static bool celebrationVerified;

    static EnemyGoalTransitionPlayModeTest()
    {
        if (SessionState.GetBool(RunningKey, false))
            Subscribe();
    }

    [InitializeOnLoadMethod]
    private static void QueueRequestedRun()
    {
        if (File.Exists(RequestPath))
            EditorApplication.delayCall += RunRequested;
    }

    private static void RunRequested()
    {
        if (!File.Exists(RequestPath) || EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        File.Delete(RequestPath);
        Run();
    }

    public static void Run()
    {
        SessionState.SetBool(RunningKey, true);
        SessionState.EraseString(ResultKey);
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath);
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
            phase = 0;
            phaseStartedAt = EditorApplication.timeSinceStartup;
            warnings = 0;
            errors = 0;
            finished = false;
            celebrationVerified = false;
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
            Debug.Log("[EnemyGoalTransitionTest] " + result);
            if (Application.isBatchMode)
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
                    if (elapsed < 0.8d)
                        return;
                    SetupAndKillEnemy();
                    NextPhase();
                    break;

                case 1:
                    if (elapsed < 0.15d)
                        return;
                    Require(enemyInstance == null, "Enemy body was not destroyed after HP reached zero");
                    Require(CountFragments() >= fragmentBaseline + 10, "EnemyFragment burst was not generated");
                    Require(checkpoint.IsActivated, "Checkpoint did not activate from Player body contact");
                    NextPhase();
                    break;

                case 2:
                    if (elapsed < 2.15d)
                        return;
                    Require(CountFragments() <= fragmentBaseline, "Enemy fragments did not expire");
                    VerifyCheckpointGlow();
                    MovePlayerIntoGoal();
                    NextPhase();
                    break;

                case 3:
                    if (elapsed < 0.2d)
                        return;
                    Require(!goalPoint.IsCleared && goalPoint.HitCount == 0,
                        "Player contact incorrectly counted as a Goal crystal hit");
                    ApplyGoalHit(1);
                    NextPhase();
                    break;

                case 4:
                    if (elapsed < 0.2d)
                        return;
                    ApplyGoalHit(2);
                    NextPhase();
                    break;

                case 5:
                    if (elapsed < 0.2d)
                        return;
                    ApplyGoalHit(3);
                    NextPhase();
                    break;

                case 6:
                    if (!celebrationVerified && elapsed >= 0.3d)
                    {
                        VerifyCelebrationInProgress();
                        celebrationVerified = true;
                    }
                    if (elapsed < 2.15d)
                        return;
                    VerifyGoalState();
                    Finish(true, "enemyDeath+fragments+fragmentLifetime+playerContactIgnored+goal3Hits+goalUI+pauseLock+timeScale; warnings="
                        + warnings + "; errors=" + errors);
                    break;
            }
        }
        catch (Exception exception)
        {
            Finish(false, exception.Message + "; phase=" + phase + "; warnings=" + warnings + "; errors=" + errors);
        }
    }

    private static void SetupAndKillEnemy()
    {
        goalMenu = UnityEngine.Object.FindAnyObjectByType<GoalMenuController>(FindObjectsInactive.Include);
        goalPoint = UnityEngine.Object.FindAnyObjectByType<GoalPoint>(FindObjectsInactive.Exclude);
        pauseMenu = UnityEngine.Object.FindAnyObjectByType<PauseMenuController>(FindObjectsInactive.Include);
        checkpoint = UnityEngine.Object.FindAnyObjectByType<Checkpoint>(FindObjectsInactive.Exclude);
        Player player = UnityEngine.Object.FindAnyObjectByType<Player>(FindObjectsInactive.Exclude);
        playerBody = player != null ? player.GetComponent<Rigidbody2D>() : null;

        Require(goalMenu != null, "GoalMenuController missing");
        Require(goalPoint != null, "GoalPoint missing");
        Require(pauseMenu != null, "PauseMenuController missing");
        Require(checkpoint != null, "Checkpoint missing");
        Require(playerBody != null, "Player Rigidbody2D missing");
        VerifyGoalUiText();
        VerifyHpLayout();
        Require(goalPoint.transform.Find("GoalCelebration") == null,
            "Goal celebration must belong to Goal UI, not the world crystal");

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/TekkyuEnemy.prefab");
        Require(prefab != null, "TekkyuEnemy prefab missing");
        fragmentBaseline = CountFragments();
        enemyInstance = UnityEngine.Object.Instantiate(prefab, new Vector3(-30f, 20f, 0f), Quaternion.identity);
        Enemy movement = enemyInstance.GetComponent<Enemy>();
        if (movement != null)
            movement.enabled = false;

        EnemyHealth health = enemyInstance.GetComponent<EnemyHealth>();
        Require(health != null, "EnemyHealth missing on TekkyuEnemy");
        Collider2D[] colliders = enemyInstance.GetComponentsInChildren<Collider2D>(true);
        health.OnMorningStarHit(new MorningStarHitContext(
            health.MaxHp,
            Vector2.right * 2f,
            0f,
            enemyInstance.transform.position,
            Vector2.right,
            8f,
            1f));

        Require(health.IsDeathHandled, "Enemy death guard was not set");
        Require(colliders.All(collider => collider == null || !collider.enabled),
            "Enemy collider remained enabled during death frame");

        Collider2D checkpointCollider = checkpoint.GetComponent<Collider2D>();
        Require(checkpointCollider != null && checkpointCollider.isTrigger,
            "Checkpoint trigger collider missing");
        playerBody.linearVelocity = Vector2.zero;
        playerBody.angularVelocity = 0f;
        playerBody.position = checkpointCollider.bounds.center;
        Physics2D.SyncTransforms();
    }

    private static void VerifyCheckpointGlow()
    {
        SpriteRenderer rootRenderer = checkpoint.GetComponent<SpriteRenderer>();
        SpriteRenderer[] renderers = checkpoint.GetComponentsInChildren<SpriteRenderer>(true);
        Require(rootRenderer != null && rootRenderer.sprite != null
                && rootRenderer.sprite.name == "CheckpointMonument_Base",
            "Checkpoint monument base sprite missing");
        Require(renderers.Length == 3, "Checkpoint must contain base plus two glow renderers");
        Require(renderers.Where(renderer => renderer != rootRenderer)
                .All(renderer => renderer.color.a > 0.5f),
            "Checkpoint crystal/emblem glow did not remain lit");
    }

    private static void VerifyHpLayout()
    {
        SegmentHpBarUI hpUi = UnityEngine.Object.FindAnyObjectByType<SegmentHpBarUI>(FindObjectsInactive.Exclude);
        Require(hpUi != null, "SegmentHpBarUI missing");
        RectTransform frame = hpUi.GetComponentsInChildren<RectTransform>(true)
            .FirstOrDefault(rect => rect.name == "Frame");
        Require(frame != null, "HP Frame missing");
        Require((frame.anchoredPosition - new Vector2(20f, -254f)).sqrMagnitude < 0.0001f
                && (frame.sizeDelta - new Vector2(408.476f, 74.2252f)).sqrMagnitude < 0.0001f
                && frame.anchorMin == Vector2.zero && frame.anchorMax == Vector2.one
                && frame.pivot == new Vector2(0.5f, 0.5f)
                && frame.localScale == Vector3.one,
            "HP Frame immutable RectTransform changed");
        Require(Mathf.Abs(hpUi.FullMaskWidth - 498.72516f) < 0.02f,
            "HP left-origin mask is not aligned to the Frame gauge region");
    }

    private static void MovePlayerIntoGoal()
    {
        Time.timeScale = 1f;
        Collider2D goalCollider = goalPoint.GetComponent<Collider2D>();
        Require(goalCollider != null && !goalCollider.isTrigger, "Goal crystal must use a collision collider");
        playerBody.linearVelocity = Vector2.zero;
        playerBody.angularVelocity = 0f;
        playerBody.position = goalCollider.bounds.center;
        Physics2D.SyncTransforms();
    }

    private static void ApplyGoalHit(int expectedHitCount)
    {
        SpriteRenderer renderer = goalPoint.GetComponent<SpriteRenderer>();
        Require(renderer != null, "Goal crystal SpriteRenderer missing");
        previousGoalSprite = renderer.sprite;
        goalPoint.OnMorningStarHit(new MorningStarHitContext(
            1,
            Vector2.right,
            0f,
            goalPoint.transform.position,
            Vector2.right,
            8f,
            1f));
        Require(goalPoint.HitCount == expectedHitCount,
            $"Goal crystal hit count expected {expectedHitCount}, actual {goalPoint.HitCount}");
        Require(renderer.sprite != previousGoalSprite, $"Goal crystal stage did not change at hit {expectedHitCount}");
    }

    private static void VerifyGoalState()
    {
        Require(goalPoint.IsCleared, "GoalPoint did not reach cleared state");
        Require(goalPoint.IsBroken && goalPoint.HitCount == 3, "Goal crystal did not finish at exactly three hits");
        Require(!goalPoint.GetComponent<Collider2D>().enabled, "Broken Goal crystal collider remained enabled");
        Require(goalMenu.IsGoalReached, "Goal UI did not open from GoalPoint");
        Require(Mathf.Approximately(Time.timeScale, 0f), "Goal did not pause gameplay");
        Require(pauseMenu.IsExternallyBlocked, "Pause was not blocked by Goal UI");
        Require(!pauseMenu.IsPaused, "Pause UI remained open with Goal UI");
        pauseMenu.OpenPause();
        Require(!pauseMenu.IsPaused, "Pause opened while Goal UI was active");
        Transform celebration = goalMenu.transform.Find("GoalPresentation/GoalStonePanel/GoalCelebration");
        Require(celebration != null && celebration.childCount == 14,
            "Goal celebration hierarchy does not contain fourteen editable sparkle elements");
        Require(UnityEngine.Object.FindObjectsByType<CrystalFragment>(FindObjectsInactive.Exclude).Length >= 8,
            "Final Goal crystal fragment burst was not generated");
    }

    private static void VerifyCelebrationInProgress()
    {
        CrystalAcquiredUI celebration = UnityEngine.Object.FindAnyObjectByType<CrystalAcquiredUI>(
            FindObjectsInactive.Include);
        Require(celebration != null && celebration.HasPlayed && celebration.IsPlaying,
            "Goal celebration did not start after the final crystal hit");
        Require(!goalMenu.IsGoalReached,
            "Goal menu opened before the celebration finished");
        Require(pauseMenu.IsExternallyBlocked,
            "Pause was not blocked during the goal celebration");
        Transform logo = celebration.transform.Find("GoalCelebrationPresentation/CongratulationLogo");
        Require(logo != null && logo.gameObject.activeInHierarchy,
            "CONGRATULATION logo is not active during the goal celebration");
        Require(celebration.GetComponentsInChildren<TextMeshProUGUI>(true).Length == 0,
            "Legacy crystal-acquired text remains in the celebration UI");
    }

    private static void VerifyGoalUiText()
    {
        string[] texts = goalMenu.GetComponentsInChildren<TextMeshProUGUI>(true)
            .Select(text => text.text)
            .ToArray();
        Require(texts.Contains("GOAL"), "GOAL title missing");
        Require(texts.Contains("ステージをクリアしました"), "clear message missing");
        Require(texts.Contains("つぎへ") && texts.Contains("もういちど") && texts.Contains("タイトルへ戻る"),
            "Goal menu buttons missing");
        string combined = string.Join(" ", texts);
        Require(!combined.Contains("クリアタイム") && !combined.Contains("回収率")
                && !combined.Contains("ミス回数") && !combined.Contains("スコア")
                && !combined.Contains("ランキング"),
            "forbidden result text is displayed");
    }

    private static int CountFragments()
    {
        return UnityEngine.Object.FindObjectsByType<EnemyFragment>(FindObjectsInactive.Exclude).Length;
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

    private static void Finish(bool pass, string detail)
    {
        if (finished)
            return;
        finished = true;
        Application.logMessageReceived -= CountLog;
        EditorApplication.update -= Tick;
        Time.timeScale = 1f;
        SessionState.SetString(ResultKey, (pass ? "PASS " : "FAILED: ") + detail);
        EditorApplication.isPlaying = false;
    }
}
