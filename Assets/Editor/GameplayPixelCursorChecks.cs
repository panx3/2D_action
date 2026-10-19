using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

/// <summary>Explicit Play Mode checks; synthetic input exists only during this check.</summary>
[InitializeOnLoad]
public static class GameplayPixelCursorChecks
{
    private const string Key = "Tekkyu.PixelCursorChecks";
    private static readonly string Output = Path.Combine(Path.GetTempPath(), "TekkyuPixelCursorChecks");
    private static readonly StringBuilder Report = new StringBuilder();
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static GameplayPixelCursor cursor;
    private static Mouse mouse;
    private static Keyboard keyboard;
    private static Gamepad pad;
    private static Mouse originalMouse;
    private static Keyboard originalKeyboard;
    private static Gamepad originalPad;
    private static MouseState mouseState;
    private static KeyboardState keyboardState;
    private static GamepadState padState;
    private static IEnumerator routine;
    private static int lastFrame, errors;
    private static double started;
    private static bool previousBackground;

    static GameplayPixelCursorChecks()
    {
        if (SessionState.GetBool(Key, false)) Subscribe();
    }

    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty)
            throw new InvalidOperationException("Start from saved CompletScene in Edit Mode.");
        if (EditorSceneManager.GetActiveScene().path != "Assets/Scenes/CompletScene.unity")
            throw new InvalidOperationException("Open CompletScene first.");
        SessionState.SetBool(Key, true);
        SessionState.EraseString(Key + ".Result");
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
            Directory.CreateDirectory(Output);
            Report.Clear(); errors = 0; lastFrame = -1;
            started = EditorApplication.timeSinceStartup;
            previousBackground = Application.runInBackground;
            Application.runInBackground = true;
            Application.logMessageReceived += OnLog;
            EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor")).Focus();
            routine = Check();
            new GameObject("CursorCheckDriver").AddComponent<GameplayPixelCursorCheckDriver>();
            EditorApplication.update += Watchdog;
        }
        else if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Key, false))
        {
            SessionState.SetBool(Key, false);
            EditorApplication.playModeStateChanged -= OnState;
            EditorApplication.update -= Watchdog;
        }
    }

    internal static void Tick()
    {
        if (!EditorApplication.isPlaying || routine == null || Time.frameCount == lastFrame) return;
        lastFrame = Time.frameCount;
        try
        {
            if (EditorApplication.timeSinceStartup - started > 60d) throw new TimeoutException("Play check timed out.");
            if (!routine.MoveNext()) Finish(true, "");
        }
        catch (Exception e) { Finish(false, e.ToString()); }
    }

    private static void Watchdog()
    {
        if (routine != null && EditorApplication.timeSinceStartup - started > 60d)
            Finish(false, "Play check timed out.");
    }

    private static IEnumerator Check()
    {
        var player = Object.FindAnyObjectByType<Player>();
        var launcher = Object.FindAnyObjectByType<MorningStarLauncher>();
        cursor = Object.FindAnyObjectByType<GameplayPixelCursor>();
        Require(player != null && launcher != null && cursor != null, "Required scene objects");
        while (!player.InputEnabled) yield return null;
        Require((bool)Call("IsGameplay"), "Gameplay gate after intro");
        var arrow = Get<UnityEngine.UI.Image>("arrowSprite");
        var root = Get<RectTransform>("cursorRoot");
        var chain = Get<RectTransform>("chainRoot");
        var links = Get<UnityEngine.UI.Image[]>("chainLinks");
        Require(cursor.GetComponentsInChildren<Rigidbody2D>(true).Length == 0, "No physics bodies");
        Require(cursor.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null && !arrow.raycastTarget
            && !links[0].raycastTarget && !links[1].raycastTarget, "Does not intercept clicks");
        Require(AssetDatabase.GetAssetPath(arrow.sprite) == GameplayPixelCursorSetup.ArrowPath
            && AssetDatabase.GetAssetPath(links[0].sprite) == GameplayPixelCursorSetup.ChainPath
            && AssetDatabase.GetAssetPath(links[1].sprite) == GameplayPixelCursorSetup.ChainPath, "Supplied textures only");

        // Force only the display method while testing geometry; desktop pointer focus is
        // deliberately independent of synthetic Input System coordinates.
        cursor.enabled = false;
        Cursor.visible = true;
        Vector2 point = new Vector2(Screen.width * 0.58f, Screen.height * 0.55f);
        foreach (float scale in new[] { 1f, 1.5f, 2f })
        {
            Set("cursorScale", scale);
            Call("ShowCursor", point);
            Vector2 tip = RectTransformUtility.WorldToScreenPoint(null,
                arrow.rectTransform.TransformPoint(new Vector3(16f * scale, -14f * scale)));
            Require(Vector2.Distance(tip, point) < 0.01f, "Tip/click alignment at scale " + scale);
            Vector2 connection = RectTransformUtility.WorldToScreenPoint(null, chain.position);
            Require(Vector2.Distance(connection, point + new Vector2(3.5f, -17.5f) * scale) < 0.01f, "Root attachment");
            Require(root.localRotation == Quaternion.identity && arrow.rectTransform.localRotation == Quaternion.identity,
                "Arrow unaffected by physics");
        }
        Set("cursorScale", 1f);
        Call("ShowCursor", point);
        Require(cursor.IsShowing && !Cursor.visible, "OS hidden only while owned");
        yield return null;
        CaptureGameView();
        Call("OnApplicationFocus", false);
        Require(!cursor.IsShowing && Cursor.visible, "Focus loss restores OS cursor");
        Call("OnApplicationFocus", true);
        Call("ShowCursor", point);
        Call("OnApplicationPause", true);
        Require(!cursor.IsShowing && Cursor.visible, "Application pause restores OS cursor");
        Call("OnApplicationPause", false);
        cursor.enabled = true;
        Call("ShowCursor", point);
        cursor.enabled = false;
        Require(!cursor.IsShowing && Cursor.visible, "Disable restores OS cursor");
        cursor.enabled = true;
        var duplicate = Object.Instantiate(cursor.gameObject);
        Require(!duplicate.GetComponent<GameplayPixelCursor>().enabled, "Duplicate display prevented");
        Object.Destroy(duplicate);
        Report.AppendLine("PASS source sprites, nonblocking UI, exact hotspot at 1/1.5/2x, anchored root, focus/pause/disable, duplicate guard");

        for (int fpsIndex = 0; fpsIndex < 3; fpsIndex++)
        {
            int fps = new[] { 30, 60, 144 }[fpsIndex];
            float dt = 1f / fps;
            Call("UpdateView", point, true);
            Vector2 anchor = point + new Vector2(3.5f, -17.5f);
            for (int i = 0; i < fps; i++) { anchor.x += 100f * dt; Call("SimulateChain", anchor, dt); }
            Require(Get<float[]>("angles")[0] < -0.005f, "Slow motion trails at " + fps);
            for (int i = 0; i < fps / 5; i++) { anchor.x -= 900f * dt; Call("SimulateChain", anchor, dt); }
            float beforeStop = Get<float[]>("angles")[0];
            bool crossed = false;
            for (int i = 0; i < fps * 3; i++)
            {
                Call("SimulateChain", anchor, dt);
                foreach (float a in Get<float[]>("angles"))
                    Require(!float.IsNaN(a) && Mathf.Abs(a) <= 32f * Mathf.Deg2Rad + 0.0001f, "Bounded motion");
                if (beforeStop * Get<float[]>("angles")[0] < 0f) crossed = true;
            }
            Require(crossed && Mathf.Abs(Get<float[]>("angles")[0]) < 0.01f, "Overshoot and settling at " + fps);
        }
        Report.AppendLine("PASS slow/fast movement, reversal, stop overshoot and settling at 30/60/144 Hz; swing <= 32 degrees");

        var pause = Object.FindAnyObjectByType<PauseMenuController>();
        pause.OpenPause();
        Require(pause.IsPaused && !(bool)Call("IsGameplay"), "Pause gate");
        Call("LateUpdate");
        Require(!cursor.IsShowing && Cursor.visible, "Menu restores OS cursor");
        for (float end = Time.unscaledTime + 0.5f; Time.unscaledTime < end;) yield return null;
        pause.ClosePause();
        while (pause.IsPaused) yield return null;
        Call("ShowCursor", point);
        Require(cursor.GetComponent<Canvas>().renderMode == RenderMode.ScreenSpaceOverlay, "Resume bypasses CRT conversion");
        Require((bool)Call("IsGameplay"), "Gameplay resumes");
        Call("ReleaseCursor");

        originalMouse = Mouse.current; originalKeyboard = Keyboard.current; originalPad = Gamepad.current;
        mouse = InputSystem.AddDevice<Mouse>("CursorCheckMouse");
        keyboard = InputSystem.AddDevice<Keyboard>("CursorCheckKeyboard");
        pad = InputSystem.AddDevice<Gamepad>("CursorCheckGamepad");
        mouseState = new MouseState { position = point };
        keyboardState = new KeyboardState(); padState = new GamepadState();
        InputSystem.onBeforeUpdate += FeedInput;
        yield return null;
        padState = new GamepadState { leftStick = new Vector2(0.5f, 0f) };
        yield return null; yield return null;
        Call("LateUpdate");
        Require(Get<bool>("usingGamepad"), "Gamepad hides custom pointer");
        padState = new GamepadState();
        mouseState.position += Vector2.right * 10f;
        yield return null; yield return null;
        Call("LateUpdate");
        Require(!Get<bool>("usingGamepad"), "Mouse restores custom pointer mode");
        Report.AppendLine("PASS pause/resume, CRT overlay isolation and gamepad/mouse presentation switching");

        var body = player.GetComponent<Rigidbody2D>();
        float x = body.position.x;
        keyboardState = new KeyboardState(UnityEngine.InputSystem.Key.D);
        for (float end = Time.time + 0.25f; Time.time < end;) yield return null;
        keyboardState = new KeyboardState();
        Require(body.position.x > x + 0.05f, "Existing keyboard movement");
        while (!player.IsGrounded) yield return null;
        int jumps = player.JumpVoicePlayCount;
        keyboardState = new KeyboardState(UnityEngine.InputSystem.Key.Space);
        for (float end = Time.time + 0.15f; Time.time < end;) yield return null;
        keyboardState = new KeyboardState();
        Require(player.JumpVoicePlayCount > jumps && !player.IsGrounded, "Existing jump action");
        while (!player.IsGrounded) yield return null;

        launcher.ResetForRespawn();
        yield return null;
        mouseState = mouseState.WithButton(MouseButton.Left, true);
        for (float end = Time.time + 0.04f; Time.time < end;) yield return null;
        mouseState = mouseState.WithButton(MouseButton.Left, false);
        bool launched = false;
        for (float end = Time.time + 1.2f; Time.time < end;)
        {
            launched |= launcher.State == MorningStarLauncher.MorningStarState.Thrown;
            yield return null;
        }
        Require(launched, "Existing short click launches");
        launcher.ResetForRespawn();
        yield return null; yield return null;
        mouseState = mouseState.WithButton(MouseButton.Left, true);
        for (float end = Time.time + 1.7f; Time.time < end;) yield return null;
        Require(launcher.State == MorningStarLauncher.MorningStarState.SpinCharging && launcher.ChargeProgress > 0.5f,
            "Existing hold charge");
        Call("UpdateView", point, false);
        Require(arrow.sprite == AssetDatabase.LoadAssetAtPath<Sprite>(GameplayPixelCursorSetup.ArrowPath), "No charge cursor change");
        mouseState = mouseState.WithButton(MouseButton.Left, false);
        launched = false;
        for (float end = Time.time + 1.2f; Time.time < end;)
        {
            launched |= launcher.State == MorningStarLauncher.MorningStarState.Thrown;
            yield return null;
        }
        Require(launched, "Existing charged release launches");
        Report.AppendLine("PASS existing move/jump, short-click launch, long-hold spin charge and charged release through Input System");
        Call("ShowCursor", point);
        Call("OnApplicationQuit");
        Require(Cursor.visible && !cursor.IsShowing, "Quit restores OS cursor");
        Require(errors == 0, "Runtime Errors/Exceptions=" + errors);
        Report.AppendLine("PASS quit cleanup; runtime Errors/Exceptions=0");
    }

    private static void FeedInput()
    {
        if (InputState.currentUpdateType != InputUpdateType.Dynamic) return;
        InputSystem.QueueStateEvent(mouse, mouseState);
        InputSystem.QueueStateEvent(keyboard, keyboardState);
        InputSystem.QueueStateEvent(pad, padState);
    }

    private static void CaptureGameView()
    {
        var game = EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor"));
        var texture = game.GetType().GetField("m_RenderTexture", Private)?.GetValue(game) as RenderTexture;
        if (texture == null) return;
        var old = RenderTexture.active;
        var copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
        try
        {
            RenderTexture.active = texture;
            copy.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            if (SystemInfo.graphicsUVStartsAtTop)
            {
                Color32[] source = copy.GetPixels32();
                var upright = new Color32[source.Length];
                for (int y = 0; y < texture.height; y++)
                    Array.Copy(source, y * texture.width, upright, (texture.height - 1 - y) * texture.width, texture.width);
                copy.SetPixels32(upright);
            }
            copy.Apply();
            File.WriteAllBytes(Path.Combine(Output, "cursor-game-view.png"), copy.EncodeToPNG());
        }
        finally { RenderTexture.active = old; Object.DestroyImmediate(copy); }
    }

    private static object Call(string name, params object[] args) => typeof(GameplayPixelCursor).GetMethod(name, Private).Invoke(cursor, args);
    private static T Get<T>(string name) => (T)typeof(GameplayPixelCursor).GetField(name, Private).GetValue(cursor);
    private static void Set(string name, object value) => typeof(GameplayPixelCursor).GetField(name, Private).SetValue(cursor, value);
    private static void Require(bool value, string detail) { if (!value) throw new InvalidOperationException(detail); }
    private static void OnLog(string message, string trace, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors++; }

    private static void Finish(bool pass, string detail)
    {
        InputSystem.onBeforeUpdate -= FeedInput;
        if (mouse != null) InputSystem.RemoveDevice(mouse);
        if (keyboard != null) InputSystem.RemoveDevice(keyboard);
        if (pad != null) InputSystem.RemoveDevice(pad);
        originalMouse?.MakeCurrent(); originalKeyboard?.MakeCurrent(); originalPad?.MakeCurrent();
        if (cursor != null) { Call("ReleaseCursor"); cursor.enabled = false; }
        Time.timeScale = 1f;
        Application.runInBackground = previousBackground;
        EditorApplication.update -= Watchdog;
        Application.logMessageReceived -= OnLog;
        string result = (pass ? "PASS\n" : "FAIL\n") + Report + detail;
        File.WriteAllText(Path.Combine(Output, "result.txt"), result);
        SessionState.SetString(Key + ".Result", result);
        Debug.Log("[PixelCursorChecks] " + result);
        routine = null;
        EditorApplication.isPlaying = false;
    }
}

// Synthetic devices have separate Editor and game state buffers. Sample in the
// actual player loop, after the production cursor, rather than EditorApplication.update.
[DefaultExecutionOrder(11000)]
public sealed class GameplayPixelCursorCheckDriver : MonoBehaviour
{
    private void LateUpdate() => GameplayPixelCursorChecks.Tick();
}
