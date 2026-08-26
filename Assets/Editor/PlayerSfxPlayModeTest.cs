using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class PlayerSfxPlayModeTest
{
    private const string RunningKey = "PlayerSfxPlayModeTest.Running";
    private const string ResultKey = "PlayerSfxPlayModeTest.Result";
    private const string WarningKey = "PlayerSfxPlayModeTest.Warnings";
    private const string ErrorKey = "PlayerSfxPlayModeTest.Errors";

    private static Player player;
    private static PlayerHealth health;
    private static MorningStarLauncher launcher;
    private static Rigidbody2D playerBody;
    private static AudioSource sfxSource;
    private static AudioSource footstepSource;
    private static AudioClip jumpClip;
    private static AudioClip launchClip;
    private static double enteredAt;
    private static double phaseDeadline;
    private static int phase;
    private static int warningCount;
    private static int errorCount;
    private static bool failed;

    static PlayerSfxPlayModeTest()
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
            Debug.Log($"[PlayerSfxTest] {result} warnings={warnings}, errors={errors}");
            EditorApplication.Exit(result.StartsWith("PASS") ? 0 : 1);
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
            switch (phase)
            {
                case 0 when elapsed >= 0.7d:
                    Setup();
                    RunFootstepChecks();
                    RunJumpCheck();
                    phaseDeadline = elapsed + jumpClip.length + 0.3d;
                    phase = 1;
                    break;

                case 1 when elapsed >= phaseDeadline:
                    Require(!sfxSource.isPlaying, "Jump SFX restarted or did not finish after one shot");
                    RunInvalidLaunchCheck();
                    phaseDeadline = elapsed + 0.15d;
                    phase = 2;
                    break;

                case 2 when elapsed >= phaseDeadline:
                    Require(!sfxSource.isPlaying, "invalid launch request played SFX");
                    launcher.ApplyRecallThenLaunch(Vector2.right);
                    phaseDeadline = elapsed + 1d;
                    phase = 3;
                    break;

                case 3:
                    if (sfxSource.isPlaying
                        && (launcher.CurrentState == MorningStarLauncher.MorningStarState.Thrown
                            || launcher.CurrentState == MorningStarLauncher.MorningStarState.Dropping))
                    {
                        phaseDeadline = elapsed + launchClip.length + 0.35d;
                        phase = 4;
                    }
                    else if (elapsed >= phaseDeadline)
                    {
                        throw new InvalidOperationException("actual morningstar launch did not play SFX");
                    }
                    break;

                case 4 when elapsed >= phaseDeadline:
                    Require(!sfxSource.isPlaying, "launch SFX repeated while no new launch occurred");
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
        player = UnityEngine.Object.FindAnyObjectByType<Player>();
        health = UnityEngine.Object.FindAnyObjectByType<PlayerHealth>();
        launcher = UnityEngine.Object.FindAnyObjectByType<MorningStarLauncher>();
        Require(player != null && health != null && launcher != null, "Player SFX test references missing");

        playerBody = player.Rigidbody2D;
        sfxSource = player.transform.Find("SfxAudioSource")?.GetComponent<AudioSource>();
        footstepSource = player.transform.Find("FootstepAudioSource")?.GetComponent<AudioSource>();
        jumpClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SFX/jump_realistic.wav");
        launchClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SFX/tekkyu_launch.wav");
        AudioClip footstepClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SFX/footstep_grass.wav");

        Require(playerBody != null && sfxSource != null && footstepSource != null, "AudioSource/Rigidbody2D missing");
        Require(jumpClip != null && launchClip != null && footstepClip != null, "AudioClip import missing");
        Require(!sfxSource.loop && !sfxSource.playOnAwake, "SfxAudioSource settings invalid");
        Require(footstepSource.clip == footstepClip, "Footstep clip reference invalid");
        Require(footstepSource.loop && !footstepSource.playOnAwake, "FootstepAudioSource settings invalid");
        Require(Mathf.Approximately(sfxSource.volume, 1f) && Mathf.Approximately(footstepSource.volume, 0.55f), "initial AudioSource volume balance is invalid");

        DeathRespawnManager deathRespawn = UnityEngine.Object.FindAnyObjectByType<DeathRespawnManager>();
        if (deathRespawn != null)
            deathRespawn.enabled = false;

        Collider2D playerCollider = player.GetComponent<Collider2D>();
        if (playerCollider != null)
            playerCollider.enabled = false;
    }

    private static void RunFootstepChecks()
    {
        SetPlayerField("_bjump", false);

        playerBody.linearVelocity = Vector2.zero;
        InvokePlayer("UpdateFootstepAudio", true);
        Require(!footstepSource.isPlaying, "footstep played while stationary");

        playerBody.linearVelocity = new Vector2(2f, 0f);
        InvokePlayer("UpdateFootstepAudio", true);
        Require(footstepSource.isPlaying, "footstep did not start while moving right on ground");

        playerBody.linearVelocity = new Vector2(-2f, 0f);
        InvokePlayer("UpdateFootstepAudio", true);
        Require(footstepSource.isPlaying, "footstep did not continue while moving left on ground");

        playerBody.linearVelocity = Vector2.zero;
        InvokePlayer("UpdateFootstepAudio", true);
        Require(!footstepSource.isPlaying, "footstep did not stop when movement stopped");

        playerBody.linearVelocity = new Vector2(2f, 0f);
        InvokePlayer("UpdateFootstepAudio", false);
        Require(!footstepSource.isPlaying, "footstep played while airborne");

        InvokePlayer("UpdateFootstepAudio", true);
        Require(footstepSource.isPlaying, "footstep did not restart after grounded movement");
        health.TakeDamage(health.MaxHp);
        Require(health.IsDead && !footstepSource.isPlaying, "death did not immediately stop footstep");

        health.ResetToFullHp();
        playerBody.linearVelocity = new Vector2(2f, 0f);
        InvokePlayer("UpdateFootstepAudio", true);
        Require(footstepSource.isPlaying, "footstep did not recover after revive and movement");

        GimmickRespawnController gimmickRespawn = UnityEngine.Object.FindAnyObjectByType<GimmickRespawnController>();
        if (gimmickRespawn != null)
            gimmickRespawn.Respawn();
        InvokePlayer("UpdateFootstepAudio", true);
        Require(!footstepSource.isPlaying, "respawn at zero speed left footstep playing");

        playerBody.linearVelocity = new Vector2(2f, 0f);
        InvokePlayer("UpdateFootstepAudio", true);
        Require(footstepSource.isPlaying, "footstep setup before disable failed");
        playerBody.linearVelocity = Vector2.zero;
        player.enabled = false;
        Require(!footstepSource.isPlaying, "Player disable/Scene exit path did not stop footstep");
        player.enabled = true;
    }

    private static void RunJumpCheck()
    {
        sfxSource.Stop();
        footstepSource.Play();
        playerBody.gravityScale = 0f;
        playerBody.linearVelocity = Vector2.zero;
        SetPlayerField("_floorContactCount", 1);
        SetPlayerField("_bjump", false);
        SetPlayerField("_coyoteTimer", 0f);
        SetPlayerField("_jumpBufferTimer", 1f);
        InvokePlayer("FixedUpdate");

        Require(playerBody.linearVelocity.y > 0f, "jump impulse was not applied");
        Require(sfxSource.isPlaying, "actual jump did not play Jump SFX");
        Require(!footstepSource.isPlaying, "actual jump did not stop footstep");

        // 地面判定が1フレーム残っても_bjumpが二重ジャンプ・二重SEを防ぐ。
        SetPlayerField("_jumpBufferTimer", 1f);
    }

    private static void RunInvalidLaunchCheck()
    {
        sfxSource.Stop();
        launcher.ApplyRecallThenLaunch(Vector2.zero);
    }

    private static void SetPlayerField(string fieldName, object value)
    {
        FieldInfo field = typeof(Player).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null)
            throw new MissingFieldException(typeof(Player).Name, fieldName);
        field.SetValue(player, value);
    }

    private static void InvokePlayer(string methodName, params object[] arguments)
    {
        MethodInfo method = typeof(Player).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (method == null)
            throw new MissingMethodException(typeof(Player).Name, methodName);
        method.Invoke(player, arguments);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void Complete()
    {
        if (failed)
            return;

        EditorApplication.update -= Tick;
        Application.logMessageReceived -= CountLog;
        SessionState.SetString(ResultKey, "PASS: footstep stop/right/left/air/death/respawn/disable, actual jump, invalid launch, actual launch and no-repeat checks passed.");
        SessionState.SetInt(WarningKey, warningCount);
        SessionState.SetInt(ErrorKey, errorCount);
        Debug.Log($"[PlayerSfxTest] PASS runtimeWarnings={warningCount}, runtimeErrors={errorCount}");
        EditorApplication.isPlaying = false;
    }

    private static void Fail(Exception exception)
    {
        if (failed)
            return;

        failed = true;
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= CountLog;
        Exception reported = exception is TargetInvocationException invocation && invocation.InnerException != null
            ? invocation.InnerException
            : exception;
        SessionState.SetString(ResultKey, "FAILED: " + reported.Message);
        SessionState.SetInt(WarningKey, warningCount);
        SessionState.SetInt(ErrorKey, errorCount + 1);
        Debug.LogException(reported);
        EditorApplication.isPlaying = false;
    }
}
