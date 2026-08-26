using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class PlayerHandAnchorFacingPlayModeTest
{
    private const string RunningKey = "PlayerHandAnchorFacingPlayModeTest.Running";
    private const string ResultKey = "PlayerHandAnchorFacingPlayModeTest.Result";
    private const string WarningKey = "PlayerHandAnchorFacingPlayModeTest.Warnings";
    private const string ErrorKey = "PlayerHandAnchorFacingPlayModeTest.Errors";

    private static Player player;
    private static MorningStarLauncher launcher;
    private static ChainLineController chainLine;
    private static Transform handAnchor;
    private static SpriteRenderer bodySprite;
    private static LineRenderer lineRenderer;
    private static Vector3 rightLocalPosition;
    private static Vector3 playerLocalScale;
    private static double enteredAt;
    private static double launchDeadline;
    private static int phase;
    private static int warningCount;
    private static int errorCount;
    private static bool failed;

    static PlayerHandAnchorFacingPlayModeTest()
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
            Debug.Log($"[HandAnchorFacingTest] {result} warnings={warnings}, errors={errors}");
            EditorApplication.Exit(result.StartsWith("PASS", StringComparison.Ordinal) ? 0 : 1);
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
            if (phase == 0 && elapsed >= 1d)
            {
                SetupAndRunFacingChecks();
                launcher.ApplyRecallThenLaunch(Vector2.left);
                launchDeadline = elapsed + 0.7d;
                phase = 1;
                return;
            }

            if (phase == 1 && elapsed >= launchDeadline)
            {
                Require(handAnchor.localPosition == MirrorX(rightLocalPosition),
                    "left-facing HandAnchor changed during morningstar launch");
                Require(GetSerializedTransform(chainLine, "startPoint") == handAnchor,
                    "ChainLine startPoint reference changed during launch");
                if (lineRenderer.enabled && lineRenderer.positionCount > 0)
                {
                    Require(Vector3.Distance(lineRenderer.GetPosition(0), handAnchor.position) < 0.001f,
                        "LineRenderer start position is not the mirrored HandAnchor");
                }

                Complete();
            }
        }
        catch (Exception exception)
        {
            Fail(exception is TargetInvocationException invocation && invocation.InnerException != null
                ? invocation.InnerException
                : exception);
        }
    }

    private static void SetupAndRunFacingChecks()
    {
        player = UnityEngine.Object.FindAnyObjectByType<Player>();
        launcher = UnityEngine.Object.FindAnyObjectByType<MorningStarLauncher>();
        chainLine = UnityEngine.Object.FindAnyObjectByType<ChainLineController>();
        Require(player != null && launcher != null && chainLine != null,
            "Player/MorningStarLauncher/ChainLineController missing");

        DeathRespawnManager respawn = UnityEngine.Object.FindAnyObjectByType<DeathRespawnManager>();
        if (respawn != null)
            respawn.enabled = false;

        handAnchor = player.WeaponHandAnchor;
        bodySprite = player.GetComponentInChildren<SpriteRenderer>();
        lineRenderer = chainLine.GetComponent<LineRenderer>();
        Require(handAnchor != null && handAnchor.name == "HandAnchor", "existing HandAnchor was not resolved");
        Require(handAnchor.parent == player.transform, "HandAnchor is not a direct Player child");
        Require(launcher.HandAnchor == handAnchor, "MorningStarLauncher does not reuse HandAnchor");
        Require(GetSerializedTransform(chainLine, "startPoint") == handAnchor,
            "ChainLine startPoint does not reuse HandAnchor");

        ChainConstraint2D constraint = UnityEngine.Object.FindAnyObjectByType<ChainConstraint2D>();
        Require(constraint != null && GetSerializedTransform(constraint, "handAnchor") == handAnchor,
            "ChainConstraint does not reuse HandAnchor");
        Require(bodySprite != null && lineRenderer != null, "SpriteRenderer/LineRenderer missing");

        rightLocalPosition = player.RightFacingHandAnchorLocalPosition;
        playerLocalScale = player.transform.localScale;
        Require(Mathf.Abs(rightLocalPosition.x) > 0.001f, "right-facing HandAnchor X is zero");

        SetMoveInput(Vector2.right);
        InvokePlayer("UpdateMovementFacing");
        Require(player.FacingRight, "right movement did not set right-facing state");
        Require(!bodySprite.flipX, "right-facing body Sprite flipX is not preserved");
        Require(handAnchor.localPosition == rightLocalPosition, "right-facing HandAnchor baseline changed");
        Require(player.transform.localScale == playerLocalScale, "Player Transform was scaled for right-facing");

        SetMoveInput(Vector2.left);
        InvokePlayer("UpdateMovementFacing");
        Require(!player.FacingRight, "left movement did not set left-facing state");
        Require(bodySprite.flipX, "left-facing body Sprite did not use flipX");
        Require(handAnchor.localPosition == MirrorX(rightLocalPosition), "left-facing HandAnchor X was not mirrored");
        Require(Mathf.Approximately(handAnchor.localPosition.y, rightLocalPosition.y), "HandAnchor Y changed");
        Require(Mathf.Approximately(handAnchor.localPosition.z, rightLocalPosition.z), "HandAnchor Z changed");
        Require(player.transform.localScale == playerLocalScale, "Player Transform was scaled for left-facing");

        for (int i = 0; i < 20; i++)
        {
            bool right = (i & 1) == 0;
            SetMoveInput(right ? Vector2.right : Vector2.left);
            InvokePlayer("UpdateMovementFacing");
            Vector3 expected = right ? rightLocalPosition : MirrorX(rightLocalPosition);
            Require(handAnchor.localPosition == expected, $"continuous facing switch failed at {i}");
            Require(player.transform.localScale == playerLocalScale, $"Player Transform changed at {i}");
        }

        // Walk/Jump animation state does not own this direct child, so the same left offset must remain.
        SetMoveInput(Vector2.left);
        InvokePlayer("UpdateMovementFacing");
        Rigidbody2D body = player.Rigidbody2D;
        if (body != null)
            body.linearVelocity = new Vector2(-2f, 5f);
        InvokePlayer("ApplyMovementFacingVisual");
        Require(handAnchor.localPosition == MirrorX(rightLocalPosition),
            "walking/jumping state changed the left-facing HandAnchor");
    }

    private static Vector3 MirrorX(Vector3 position)
    {
        position.x = -position.x;
        return position;
    }

    private static Transform GetSerializedTransform(UnityEngine.Object target, string propertyName)
    {
        SerializedProperty property = new SerializedObject(target).FindProperty(propertyName);
        if (property == null)
            throw new MissingFieldException(target.GetType().Name, propertyName);
        return property.objectReferenceValue as Transform;
    }

    private static void SetMoveInput(Vector2 value)
    {
        FieldInfo field = typeof(Player).GetField("_moveInput", BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null)
            throw new MissingFieldException(typeof(Player).Name, "_moveInput");
        field.SetValue(player, value);
    }

    private static void InvokePlayer(string methodName)
    {
        MethodInfo method = typeof(Player).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (method == null)
            throw new MissingMethodException(typeof(Player).Name, methodName);
        method.Invoke(player, null);
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
        SessionState.SetInt(WarningKey, warningCount);
        SessionState.SetInt(ErrorKey, errorCount);
        SessionState.SetString(ResultKey,
            $"PASS: existing HandAnchor mirrored from right={rightLocalPosition} to left={MirrorX(rightLocalPosition)}; right/left/repeated/walk/jump/launch and Player Transform preservation verified.");
        EditorApplication.isPlaying = false;
    }

    private static void Fail(Exception exception)
    {
        if (failed)
            return;

        failed = true;
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= CountLog;
        errorCount++;
        SessionState.SetInt(WarningKey, warningCount);
        SessionState.SetInt(ErrorKey, errorCount);
        SessionState.SetString(ResultKey, "FAILED: " + exception.Message);
        Debug.LogException(exception);
        EditorApplication.isPlaying = false;
    }
}
