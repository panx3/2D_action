using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class EnemyGoalTransitionPlayModeTest
{
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
    private static Rigidbody2D playerBody;
    private static bool finished;

    static EnemyGoalTransitionPlayModeTest()
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
            Debug.Log("[EnemyGoalTransitionTest] " + result);
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
                    NextPhase();
                    break;

                case 2:
                    if (elapsed < 2.15d)
                        return;
                    Require(CountFragments() <= fragmentBaseline, "Enemy fragments did not expire");
                    MovePlayerIntoGoal();
                    NextPhase();
                    break;

                case 3:
                    if (elapsed < 0.5d)
                        return;
                    VerifyGoalState();
                    Finish(true, "enemyDeath+fragments+fragmentLifetime+goalTrigger+goalUI+pauseLock+timeScale; warnings="
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
        Player player = UnityEngine.Object.FindAnyObjectByType<Player>(FindObjectsInactive.Exclude);
        playerBody = player != null ? player.GetComponent<Rigidbody2D>() : null;

        Require(goalMenu != null, "GoalMenuController missing");
        Require(goalPoint != null, "GoalPoint missing");
        Require(pauseMenu != null, "PauseMenuController missing");
        Require(playerBody != null, "Player Rigidbody2D missing");
        VerifyGoalUiText();

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
    }

    private static void MovePlayerIntoGoal()
    {
        Time.timeScale = 1f;
        Collider2D goalCollider = goalPoint.GetComponent<Collider2D>();
        Require(goalCollider != null && goalCollider.isTrigger, "GoalPoint trigger collider missing");
        playerBody.linearVelocity = Vector2.zero;
        playerBody.angularVelocity = 0f;
        playerBody.position = goalCollider.bounds.center;
        Physics2D.SyncTransforms();
    }

    private static void VerifyGoalState()
    {
        Require(goalPoint.IsCleared, "GoalPoint did not reach cleared state");
        Require(goalMenu.IsGoalReached, "Goal UI did not open from GoalPoint");
        Require(Mathf.Approximately(Time.timeScale, 0f), "Goal did not pause gameplay");
        Require(pauseMenu.IsExternallyBlocked, "Pause was not blocked by Goal UI");
        Require(!pauseMenu.IsPaused, "Pause UI remained open with Goal UI");
        pauseMenu.OpenPause();
        Require(!pauseMenu.IsPaused, "Pause opened while Goal UI was active");
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
