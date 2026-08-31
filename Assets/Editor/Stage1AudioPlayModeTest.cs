using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class Stage1AudioPlayModeTest
{
    private const string RunningKey = "Stage1AudioPlayModeTest.Running";
    private const string ResultKey = "Stage1AudioPlayModeTest.Result";
    private const string WarningKey = "Stage1AudioPlayModeTest.Warnings";
    private const string ErrorKey = "Stage1AudioPlayModeTest.Errors";

    private static double enteredAt;
    private static int warningCount;
    private static int errorCount;
    private static bool completed;

    static Stage1AudioPlayModeTest()
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
            warningCount = 0;
            errorCount = 0;
            completed = false;
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
            Debug.Log($"[Stage1AudioPlayModeTest] {result} warnings={warnings}, errors={errors}");
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
        if (completed || EditorApplication.timeSinceStartup - enteredAt < 1.0d)
            return;

        try
        {
            RunChecks();
            Complete();
        }
        catch (Exception exception)
        {
            Fail(exception is TargetInvocationException invocation && invocation.InnerException != null
                ? invocation.InnerException
                : exception);
        }
    }

    private static void RunChecks()
    {
        Player player = UnityEngine.Object.FindAnyObjectByType<Player>();
        PlayerHealth health = UnityEngine.Object.FindAnyObjectByType<PlayerHealth>();
        Require(player != null && health != null, "Player/PlayerHealth missing");

        AudioClip bgmClip = LoadClip("Assets/Audio/BGM/tekkyu_shojo_stage1_theme_v4_european_fantasy.wav");
        AudioClip jumpClip = LoadClip("Assets/Audio/SFX/jump_realistic.wav");
        AudioClip footstepClip = LoadClip("Assets/Audio/SFX/footstep_grass.wav");
        AudioClip landingClip = LoadClip("Assets/Audio/SFX/Imported/ジャンプの着地.mp3");
        AudioClip enemyHitClip = LoadClip("Assets/Audio/SFX/Imported/ロボットを強く殴る2.mp3");
        AudioClip impactClip = LoadClip("Assets/Audio/SFX/Imported/打撃6.mp3");
        AudioClip runningClip = LoadClip("Assets/Audio/SFX/Imported/走る.mp3");
        AudioClip[] voices =
        {
            LoadClip("Assets/Audio/SFX/Voice/yo_03.wav"),
            LoadClip("Assets/Audio/SFX/Voice/ei_03.wav"),
            LoadClip("Assets/Audio/SFX/Voice/fun_01.wav"),
            LoadClip("Assets/Audio/SFX/Voice/ho_01.wav")
        };

        AudioSource bgmSource = null;
        int activeBgmCount = 0;
        foreach (AudioSource source in UnityEngine.Object.FindObjectsByType<AudioSource>())
        {
            if (source.clip == bgmClip && source.loop && source.playOnAwake)
            {
                bgmSource = source;
                activeBgmCount++;
            }
        }

        Require(activeBgmCount == 1, $"formal BGM source count is {activeBgmCount}");
        Require(bgmSource != null && bgmSource.gameObject.name == "StageAudio", "StageAudio BGM source missing");
        Require(bgmSource.isPlaying, "Stage BGM did not start in Play Mode");
        Require(Mathf.Approximately(bgmSource.volume, 0.2f) && Mathf.Approximately(bgmSource.spatialBlend, 0f),
            "Stage BGM volume/spatial settings invalid");

        RequirePlayerFloat(player, "_groundMoveForce", 70f);
        RequirePlayerFloat(player, "_groundLinearDragX", 8f);
        RequirePlayerFloat(player, "_airMoveFactor", 0.1f);
        RequirePlayerFloat(player, "_airLinearDragX", 1.5f);
        RequirePlayerFloat(player, "_jumpSpeed", 8f);
        RequirePlayerFloat(player, "_coyoteTime", 0.1f);
        RequirePlayerFloat(player, "_jumpBufferTime", 0.15f);
        RequirePlayerFloat(player, "_fallGravityMultiplier", 4f);
        RequirePlayerFloat(player, "_jumpCutMultiplier", 2f);
        RequirePlayerFloat(player, "_maxFallSpeed", -50f);

        AudioSource sfxSource = player.transform.Find("SfxAudioSource")?.GetComponent<AudioSource>();
        AudioSource footstepSource = player.transform.Find("FootstepAudioSource")?.GetComponent<AudioSource>();
        AudioSource worldImpactSource = GameObject.Find(OneShotAudioUtility.WorldImpactSourceName)?.GetComponent<AudioSource>();
        Require(sfxSource != null && footstepSource != null && worldImpactSource != null, "role-separated AudioSource missing");
        Require(footstepSource.clip == footstepClip && footstepSource.clip != runningClip,
            "formal footstep clip is not footstep_grass or running clip was double-assigned");
        Require(footstepSource.loop && !footstepSource.playOnAwake && Mathf.Approximately(footstepSource.volume, 0.55f),
            "footstep AudioSource settings invalid");

        DeathRespawnManager respawn = UnityEngine.Object.FindAnyObjectByType<DeathRespawnManager>();
        if (respawn != null)
            respawn.enabled = false;

        Rigidbody2D playerBody = player.Rigidbody2D;
        Require(playerBody != null, "Player Rigidbody2D missing");
        Collider2D playerCollider = player.GetComponent<Collider2D>();
        if (playerCollider != null)
            playerCollider.enabled = false;

        int voiceCountBefore = player.JumpVoicePlayCount;
        playerBody.gravityScale = 0f;
        playerBody.linearVelocity = Vector2.zero;
        SetPlayerField(player, "_isGrounded", true);
        SetPlayerField(player, "_rawGrounded", false);
        SetPlayerField(player, "_groundedGraceTimer", 1f);
        SetPlayerField(player, "_bjump", false);
        SetPlayerField(player, "_coyoteTimer", 0f);
        SetPlayerField(player, "_jumpBufferTimer", 1f);
        InvokePlayer(player, "FixedUpdate");
        Require(playerBody.linearVelocity.y > 0f, "actual jump impulse was not applied");
        Require(player.JumpVoicePlayCount == voiceCountBefore + 1, "actual jump did not play exactly one voice");
        Require(Array.IndexOf(voices, player.LastJumpVoiceClip) >= 0, "jump voice was not selected from the four configured clips");
        SetPlayerField(player, "_jumpBufferTimer", 1f);
        InvokePlayer(player, "FixedUpdate");
        Require(player.JumpVoicePlayCount == voiceCountBefore + 1, "held/duplicate jump replayed the voice");
        Require(GetPlayerClip(player, "_jumpClip") == jumpClip, "existing jump_realistic clip was replaced");

        int landingCountBefore = player.LandingSoundPlayCount;
        SetPlayerField(player, "_groundStateInitialized", true);
        SetPlayerField(player, "_wasGrounded", false);
        SetPlayerField(player, "_isGrounded", true);
        InvokePlayer(player, "Update");
        Require(player.LandingSoundPlayCount == landingCountBefore + 1, "air-to-ground transition did not play landing SFX");
        InvokePlayer(player, "Update");
        Require(player.LandingSoundPlayCount == landingCountBefore + 1, "standing on ground replayed landing SFX");
        Require(GetPlayerClip(player, "_landingClip") == landingClip, "landing clip reference invalid");

        SetPlayerField(player, "_bjump", false);
        playerBody.linearVelocity = new Vector2(2f, 0f);
        InvokePlayer(player, "UpdateFootstepAudio", true);
        Require(footstepSource.isPlaying, "ground movement did not start footstep loop");
        InvokePlayer(player, "UpdateFootstepAudio", false);
        Require(!footstepSource.isPlaying, "airborne state did not stop footstep loop");

        int damageSoundBefore = health.DamageSoundPlayCount;
        health.TakeDamage(1);
        Require(health.DamageSoundPlayCount == damageSoundBefore + 1, "accepted Player damage did not play hit SFX");

        GameObject enemyObject = new GameObject("__Stage1AudioEnemyTest");
        EnemyHealth enemy = enemyObject.AddComponent<EnemyHealth>();
        SetField(enemy, "hitAudioSource", worldImpactSource);
        SetField(enemy, "morningStarHitClip", enemyHitClip);
        SetField(enemy, "morningStarHitVolume", 0.9f);
        enemy.OnMorningStarHit(CreateHitContext(0, 8f));
        Require(enemy.HitSoundPlayCount == 0 && enemy.CurrentHp == enemy.MaxHp,
            "invalid Enemy damage played hit SFX");
        MorningStarHitContext enemyContext = CreateHitContext(1, 8f);
        enemy.OnMorningStarHit(enemyContext);
        Require(enemy.HitSoundPlayCount == 1 && enemy.CurrentHp == enemy.MaxHp - 1,
            "valid Enemy damage did not play exactly one hit SFX");

        GameObject wallObject = new GameObject("__Stage1AudioWallTest");
        wallObject.AddComponent<BoxCollider2D>();
        BreakableWall wall = wallObject.AddComponent<BreakableWall>();
        SetField(wall, "breakAudioSource", worldImpactSource);
        SetField(wall, "breakImpactClip", impactClip);
        SetField(wall, "breakImpactVolume", 0.9f);
        wall.OnMorningStarHit(CreateHitContext(1, 8f));
        Require(wall.IsBroken && wall.BreakSoundPlayCount == 1, "BreakableWall break did not play exactly one impact SFX");

        Require(bgmSource.isPlaying, "one-shot SFX interrupted the BGM AudioSource");
        UnityEngine.Object.Destroy(enemyObject);
        UnityEngine.Object.Destroy(wallObject);
    }

    private static MorningStarHitContext CreateHitContext(int damage, float speed)
    {
        return new MorningStarHitContext(
            damage,
            Vector2.zero,
            0f,
            Vector2.zero,
            Vector2.right,
            speed,
            1f);
    }

    private static AudioClip LoadClip(string path)
    {
        AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        Require(clip != null, $"AudioClip missing: {path}");
        return clip;
    }

    private static AudioClip GetPlayerClip(Player player, string fieldName)
    {
        return (AudioClip)GetField(typeof(Player), fieldName).GetValue(player);
    }

    private static void RequirePlayerFloat(Player player, string fieldName, float expected)
    {
        float actual = (float)GetField(typeof(Player), fieldName).GetValue(player);
        Require(Mathf.Approximately(actual, expected), $"{fieldName}={actual}, expected {expected}");
    }

    private static void SetPlayerField(Player player, string fieldName, object value)
    {
        GetField(typeof(Player), fieldName).SetValue(player, value);
    }

    private static void InvokePlayer(Player player, string methodName, params object[] arguments)
    {
        MethodInfo method = typeof(Player).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (method == null)
            throw new MissingMethodException(typeof(Player).Name, methodName);
        method.Invoke(player, arguments);
    }

    private static void SetField(object target, string fieldName, object value)
    {
        GetField(target.GetType(), fieldName).SetValue(target, value);
    }

    private static FieldInfo GetField(Type type, string fieldName)
    {
        FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null)
            throw new MissingFieldException(type.Name, fieldName);
        return field;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void Complete()
    {
        completed = true;
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= CountLog;
        SessionState.SetInt(WarningKey, warningCount);
        SessionState.SetInt(ErrorKey, errorCount);
        SessionState.SetString(ResultKey,
            "PASS: fixed Player values, single looping BGM, actual jump+random voice/no-repeat, landing edge/no-repeat, footstep, Player/Enemy/Wall hit SFX and BGM separation verified.");
        EditorApplication.isPlaying = false;
    }

    private static void Fail(Exception exception)
    {
        completed = true;
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
