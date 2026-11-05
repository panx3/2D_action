using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

/// <summary>既存Input Systemの入力で走行・跳躍・着地を確認する短いPlay Mode検査。</summary>
public static class PlayerMotionV2PlayExercise
{
    private static Player player;
    private static Gamepad pad;
    private static Gamepad previousPad;
    private static float began;
    private static int phase = -1;
    private static bool sawRun, sawRise, sawFall, sawLand, wasAirborne;
    private static string stage;
    private static string reportDirectory;

    public static string Start()
        => StartAt("Docs/Art/PlayerMotionV2/Implementation");

    public static string StartAt(string outputDirectory)
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play Mode required.");
        reportDirectory = outputDirectory;
        Directory.CreateDirectory(reportDirectory);
        player = UnityEngine.Object.FindFirstObjectByType<Player>();
        var intro = player.GetComponent<PlayerStartIntro>();
        if (intro != null) intro.enabled = false;
        player.SetStartIntroControl(false);
        player.GetComponent<PlayerHealth>().ResetToFullHp();
        player.GetComponent<PlayerHealth>().BeginRespawnProtection();
        player.GetComponent<MorningStarLauncher>().EndRespawnPresentation();
        player.GetComponent<MorningStarLauncher>().ResetForRespawn();
        player.SetGoalInputBlocked(false);
        player.SetGimmickInputBlocked(false);
        player.SetInputEnabled(true);
        var body = player.GetComponent<Rigidbody2D>();
        body.simulated = true;
        body.linearVelocity = Vector2.zero;
        previousPad = Gamepad.current;
        pad = InputSystem.AddDevice<Gamepad>();
        pad.MakeCurrent();
        sawRun = sawRise = sawFall = sawLand = wasAirborne = false;
        phase = -1;
        began = Time.time;
        stage = SceneManager.GetActiveScene().name;
        Time.timeScale = 1f;
        Application.runInBackground = true;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.isPaused = false;
        return "Running real gamepad movement/jump exercise in " + stage + ".";
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || player == null) { Finish(false); return; }
        float elapsed = Time.time - began;
        int nextPhase = elapsed < 0.25f ? 0 : elapsed < 0.65f ? 1 : elapsed < 0.82f ? 2
            : elapsed < 1.65f ? 3 : elapsed < 2.1f ? 4 : elapsed < 2.45f ? 5 : 6;
        if (nextPhase != phase)
        {
            phase = nextPhase;
            Vector2 stick = phase >= 1 && phase <= 3 ? Vector2.right : phase == 5 ? Vector2.left : Vector2.zero;
            var state = new GamepadState { leftStick = stick };
            if (phase == 2) state = state.WithButton(GamepadButton.South);
            InputSystem.QueueStateEvent(pad, state);
        }
        var sprite = player.BodySprite.sprite;
        string name = sprite != null ? sprite.name : "";
        Vector2 velocity = player.GetComponent<Rigidbody2D>().linearVelocity;
        if (!sawRun && (name.StartsWith("Run_", StringComparison.Ordinal) || name.StartsWith("RunRefined_", StringComparison.Ordinal)) && Mathf.Abs(velocity.x) > 0.5f)
        { sawRun = true; Capture(stage + "-run.png"); }
        if (!sawRise && !player.IsGrounded && velocity.y > 0.5f)
        { sawRise = true; wasAirborne = true; Capture(stage + "-rise.png"); }
        if (!sawFall && wasAirborne && !player.IsGrounded && velocity.y < -1.5f)
        { sawFall = true; Capture(stage + "-fall.png"); }
        if (!sawLand && sawFall && player.IsGrounded)
        { sawLand = true; Capture(stage + "-land.png"); }
        if (phase == 6 || elapsed > 4f) Finish(true);
    }

    private static void Finish(bool completed)
    {
        EditorApplication.update -= Tick;
        if (pad != null && pad.added) InputSystem.RemoveDevice(pad);
        if (previousPad != null && previousPad.added) previousPad.MakeCurrent();
        pad = null;
        if (!EditorApplication.isPlaying) return;
        EditorApplication.isPaused = true;
        string result = "completed=" + completed + " run=" + sawRun + " rise=" + sawRise
            + " fall=" + sawFall + " land=" + sawLand + " frame=" + Time.frameCount;
        Directory.CreateDirectory(reportDirectory);
        File.WriteAllText(reportDirectory + "/" + stage + "-input-exercise.txt", result);
    }

    private static void Capture(string name)
    {
        Camera camera = Camera.main;
        if (camera == null) return;
        RenderTexture previous = camera.targetTexture, active = RenderTexture.active;
        var target = new RenderTexture(1600, 900, 24);
        var image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
            image.Apply();
            File.WriteAllBytes(reportDirectory + "/" + name, image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = previous;
            RenderTexture.active = active;
            target.Release();
            UnityEngine.Object.Destroy(image);
            UnityEngine.Object.Destroy(target);
        }
    }
}

