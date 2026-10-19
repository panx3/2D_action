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
using UnityEngine.SceneManagement;
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
        SessionState.SetBool(Key + ".EscapeOnly", false);
        SessionState.SetBool(Key + ".HitStopOnly", false);
        SessionState.SetBool(Key + ".FastMotionOnly", false);
        SessionState.EraseString(Key + ".Result");
        Subscribe();
        EditorApplication.isPlaying = true;
    }

    public static void RunEscapeCheck()
    {
        Run();
        SessionState.SetBool(Key + ".EscapeOnly", true);
    }

    public static void RunHitStopCheck(bool baseline = false)
    {
        Run();
        SessionState.SetBool(Key + ".HitStopOnly", true);
        SessionState.SetBool(Key + ".HitStopBaseline", baseline);
    }

    public static void RunFastMotionCheck()
    {
        Run();
        SessionState.SetBool(Key + ".FastMotionOnly", true);
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
            routine = SessionState.GetBool(Key + ".FastMotionOnly", false) ? CheckFastMotion()
                : SessionState.GetBool(Key + ".HitStopOnly", false)
                ? CheckHitStop(SessionState.GetBool(Key + ".HitStopBaseline", false))
                : SessionState.GetBool(Key + ".EscapeOnly", false) ? CheckEscape() : Check();
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

    private static bool PointerDisplayOwned => cursor.IsShowing && Cursor.visible == cursor.IsUsingNativeCursor;

    private static IEnumerator CheckFastMotion()
    {
        cursor = Object.FindAnyObjectByType<GameplayPixelCursor>();
        var player = Object.FindAnyObjectByType<Player>();
        while (!player.InputEnabled) yield return null;
        cursor.enabled = false;
        Vector2 point = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        Set("useNativeCursor", false);
        Call("ShowCursor", point);
        Require(!cursor.IsUsingNativeCursor && !Cursor.visible && Get<UnityEngine.UI.Image>("arrowSprite").enabled,
            "A/B baseline is the previous frame-bound UI cursor");
        Set("useNativeCursor", true);
        var show = (Action<Vector2>)Delegate.CreateDelegate(typeof(Action<Vector2>), cursor,
            typeof(GameplayPixelCursor).GetMethod("ShowCursor", Private));
        int samples = 0;
        double rasterMilliseconds = 0;
        var timer = new System.Diagnostics.Stopwatch();
        foreach (float scale in new[] { 1f, 1.5f, 2f, 3f })
        {
            Set("cursorScale", scale);
            show(point);
            Require(cursor.IsUsingNativeCursor && PointerDisplayOwned, "Native cursor is accepted at scale " + scale);
            VerifyNativeArrowPixels(scale);
            for (int i = 0; i < 24; i++)
            {
                // Rapid sweeps, reversals and stops; the head remains exact and all
                // parts share one native image instead of detaching across frames.
                point = new Vector2(Screen.width * (i % 2 == 0 ? 0.08f : 0.92f), Screen.height * (0.2f + i % 4 * 0.18f));
                timer.Restart(); show(point); timer.Stop();
                rasterMilliseconds += timer.Elapsed.TotalMilliseconds; samples++;
                Require(cursor.IsUsingNativeCursor && !Get<UnityEngine.UI.Image>("arrowSprite").enabled
                    && !Get<UnityEngine.UI.Image[]>("chainLinks")[0].enabled, "No frame-bound duplicate head/chain");
                Require(Vector2.Distance((Vector2)Get<RectTransform>("cursorRoot").position, point) < 0.01f,
                    "Logical click point never smoothed or predicted");
                yield return null;
            }
        }
        File.WriteAllBytes(Path.Combine(Output, "native-chain-cursor.png"), cursor.NativeCursorTexture.EncodeToPNG());
        Require(rasterMilliseconds / samples < 8d, "Cursor composition must stay lightweight");
        Report.AppendLine("PASS native RGBA cursor at 1/1.5/2/3x, supplied arrow pixels, exact hotspot, attached chain/shadows, rapid sweeps/reversals; mean composition ms=" + (rasterMilliseconds / samples).ToString("F3"));

        // A stationary pose should not upload again just because the mouse moves.
        Set("cursorScale", 3f);
        Set("usingGamepad", true);
        for (float end = Time.unscaledTime + 0.4f; Time.unscaledTime < end;)
        { show(point); yield return null; }
        show(point);
        object raster = Get<object>("nativeRaster");
        var uploads = raster.GetType().GetProperty("UploadCount");
        int before = (int)uploads.GetValue(raster);
        for (int i = 0; i < 30; i++) show(point + Vector2.right * i * 20f);
        Require((int)uploads.GetValue(raster) == before, "Pointer translation needs no texture uploads");
        File.WriteAllBytes(Path.Combine(Output, "native-menu-cursor.png"), cursor.NativeCursorTexture.EncodeToPNG());
        Set("usingGamepad", false);
        Report.AppendLine("PASS native translation is independent of UI redraws; unchanged poses reuse the cursor texture");
        var escape = CheckEscape();
        while (escape.MoveNext()) yield return escape.Current;
    }

    private static void VerifyNativeArrowPixels(float scale)
    {
        var source = Get<UnityEngine.UI.Image>("arrowSprite").sprite.texture;
        var original = source.GetPixels32();
        var texture = cursor.NativeCursorTexture;
        var rendered = texture.GetPixels32();
        Vector2 target = cursor.NativeCursorHotspot;
        Vector2 sourceTip = Get<Vector2>("hotspot");
        int compared = 0, shadows = 0;
        for (int y = 0; y < source.height; y++)
            for (int x = 0; x < source.width; x++)
            {
                Color32 expected = original[y * source.width + x];
                if (expected.a != 255) continue;
                int px = Mathf.FloorToInt(target.x + (x - sourceTip.x + 0.5f) * scale);
                int py = Mathf.FloorToInt(texture.height - target.y + (y - source.height + sourceTip.y + 0.5f) * scale);
                Require(px >= 0 && py >= 0 && px < texture.width && py < texture.height, "All opaque arrow pixels fit");
                Color32 actual = rendered[py * texture.width + px];
                Require(Mathf.Abs(actual.r - expected.r) <= 1 && Mathf.Abs(actual.g - expected.g) <= 1
                    && Mathf.Abs(actual.b - expected.b) <= 1 && actual.a == 255, "Native pointer preserves source pixels and tip mapping");
                compared++;
            }
        foreach (var pixel in rendered)
            if (pixel.r == 0 && pixel.g == 0 && pixel.b == 0 && pixel.a > 80 && pixel.a < 240) shadows++;
        Require(compared > 20 && shadows > 0, "Arrow source and permanent shadow are present");
        Require(target.x >= 0 && target.y >= 0 && target.x < texture.width && target.y < texture.height, "Native hotspot is valid");
    }

    private static IEnumerator CheckHitStop(bool baseline)
    {
        cursor = Object.FindAnyObjectByType<GameplayPixelCursor>();
        var player = Object.FindAnyObjectByType<Player>();
        var launcher = Object.FindAnyObjectByType<MorningStarLauncher>();
        var pause = Object.FindAnyObjectByType<PauseMenuController>();
        var wall = Object.FindAnyObjectByType<BreakableWall>();
        var enemy = Object.FindAnyObjectByType<EnemyHealth>();
        Require(cursor != null && player != null && launcher != null && pause != null && wall != null && enemy != null,
            "Required production scene owners and hit receivers");
        while (!player.InputEnabled) yield return null;
        cursor.enabled = false;
        Vector2 point = new Vector2(Screen.width * 0.58f, Screen.height * 0.55f);
        var hitStop = typeof(MorningStarLauncher).GetMethod("PlayCombatHitStop", Private);
        string[] cases = { "Block destruction", "Enemy death", "Hook hit stop" };
        for (int test = 0; test < cases.Length; test++)
        {
            for (float end = Time.unscaledTime + 0.4f; Time.unscaledTime < end;)
            { Call("ShowCursor", point); yield return null; }
            Call("ShowCursor", point);
            Require(cursor.IsChainStyle && !cursor.IsChangingDesign, "Stable gameplay cursor before impact");
            float before = Get<float>("styleChangedAt");
            float duration = test == 2 ? 0.04f : 0.1f;
            var context = new MorningStarHitContext(1000, Vector2.zero, duration,
                Vector2.zero, Vector2.right, test == 0 ? wall.BreakSpeedThreshold + 1f : 12f, 1f);
            if (test == 0)
            {
                wall.OnMorningStarHit(context);
                Require(wall.IsBroken, "Production block destruction");
            }
            else if (test == 1)
            {
                enemy.OnMorningStarHit(context);
                Require(enemy.IsDeathHandled, "Production enemy death");
            }
            // The same hit-stop method called immediately after a combat receiver.
            // Invoke it without changing combat input, ball state or physics.
            hitStop.Invoke(launcher, new object[] { duration });
            Require(Time.timeScale == 0f, "Existing hit stop pauses scaled time");
            bool sawNoise = false;
            for (float end = Time.unscaledTime + 0.45f; Time.unscaledTime < end;)
            {
                Call("ShowCursor", point);
                sawNoise |= cursor.IsChangingDesign || Get<RectTransform>("noiseRoot").gameObject.activeSelf;
                if (!baseline)
                    Require(cursor.IsChainStyle && Get<float>("styleChangedAt").Equals(before),
                        "Combat never requests a design change");
                yield return null;
            }
            Require(sawNoise == baseline && Time.timeScale > 0f, "Hit stop noise and time restoration");
            Report.AppendLine(cases[test] + ": noise=" + sawNoise + ", time restored=True");
        }
        if (!baseline)
        {
            pause.OpenPause();
            Require(pause.IsPaused, "Actual pause opens");
            bool sawNoise = false;
            for (float end = Time.unscaledTime + 0.4f; Time.unscaledTime < end;)
            { Call("ShowCursor", point); sawNoise |= cursor.IsChangingDesign; yield return null; }
            Require(sawNoise && !cursor.IsChainStyle, "Pause still switches to menu with noise");
            pause.ClosePause(); sawNoise = false;
            for (float end = Time.unscaledTime + 0.9f; Time.unscaledTime < end;)
            { Call("ShowCursor", point); sawNoise |= cursor.IsChangingDesign; yield return null; }
            Require(!pause.IsPaused && sawNoise && cursor.IsChainStyle, "Resume still switches back with noise");
            Report.AppendLine("PASS pause/resume retain the requested design-switch noise");
        }
        Require(errors == 0, "Runtime Errors/Exceptions=" + errors);
        Report.AppendLine("Runtime Errors/Exceptions=0");
    }

    private static IEnumerator CheckEscape()
    {
        cursor = Object.FindAnyObjectByType<GameplayPixelCursor>();
        var player = Object.FindAnyObjectByType<Player>();
        var pause = Object.FindAnyObjectByType<PauseMenuController>();
        while (!player.InputEnabled) yield return null;
        var gameView = EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor"));
        cursor.enabled = false;
        Cursor.visible = true;
        Vector2 point = new Vector2(Screen.width * 0.58f, Screen.height * 0.55f);
        originalKeyboard = Keyboard.current;
        keyboard = InputSystem.AddDevice<Keyboard>("CursorEscapeCheckKeyboard");
        keyboardState = new KeyboardState();
        InputSystem.onBeforeUpdate += FeedInput;
        yield return null;
        for (int cycle = 0; cycle < 3; cycle++)
        {
            Call("ShowCursor", point);
            // Send to the actual Editor GameView handler as well as the Input System.
            // A synthetic Input System key alone never hits the Editor's ESC override.
            gameView.SendEvent(Event.KeyboardEvent("escape"));
            keyboardState = new KeyboardState(UnityEngine.InputSystem.Key.Escape);
            yield return null;
            keyboardState = new KeyboardState();
            Require(pause.IsPaused, "ESC opens existing pause action");
            for (float end = Time.unscaledTime + 0.3f; Time.unscaledTime < end;)
            {
                Call("ShowCursor", point);
                Require(PointerDisplayOwned, "ESC pause retains the custom cursor without clicking");
                Require(Cursor.lockState == CursorLockMode.None, "No cursor locking introduced");
                yield return null;
            }
            Call("ShowCursor", point);
            Require(!cursor.IsChainStyle, "Pause uses menu design after ESC");
            gameView.SendEvent(Event.KeyboardEvent("escape"));
            keyboardState = new KeyboardState(UnityEngine.InputSystem.Key.Escape);
            yield return null;
            keyboardState = new KeyboardState();
            for (float end = Time.unscaledTime + 0.5f; Time.unscaledTime < end;)
            { Call("ShowCursor", point); yield return null; }
            Call("ShowCursor", point);
            Require(!pause.IsPaused && cursor.IsChainStyle && PointerDisplayOwned, "ESC resumes with custom cursor without clicking");
        }
        Call("OnApplicationFocus", false);
        Require(!cursor.IsShowing && Cursor.visible, "Focus loss still restores OS cursor");
        Call("OnApplicationFocus", true);
        Call("ShowCursor", point);
        Call("OnApplicationQuit");
        Require(Cursor.visible && !cursor.IsShowing, "Quit still restores OS cursor");
        Require(errors == 0, "Runtime Errors/Exceptions=" + errors);
        Report.AppendLine("PASS three ESC pause/resume cycles through GameView events and Input System; no clicks; native/UI ownership retained; both designs; no cursor lock; focus/quit restore; Errors/Exceptions=0");
        Report.AppendLine("Final physical-pointer smoothness and native rendering outside the Game View require user visual confirmation.");
    }

    private static IEnumerator Check()
    {
        var player = Object.FindAnyObjectByType<Player>();
        var launcher = Object.FindAnyObjectByType<MorningStarLauncher>();
        cursor = Object.FindAnyObjectByType<GameplayPixelCursor>();
        Require(player != null && launcher != null && cursor != null, "Required scene objects");
        while (!player.InputEnabled) yield return null;
        Require((bool)Call("IsGameplay"), "Gameplay gate after intro");
        cursor.enabled = false;
        Cursor.visible = true;
        Vector2 point = new Vector2(Screen.width * 0.58f, Screen.height * 0.55f);
        for (float end = Time.unscaledTime + 0.2f; Time.unscaledTime < end;)
        { Call("ShowCursor", point); yield return null; }
        var arrow = Get<UnityEngine.UI.Image>("arrowSprite");
        var root = Get<RectTransform>("cursorRoot");
        var chain = Get<RectTransform>("chainRoot");
        var links = Get<UnityEngine.UI.Image[]>("chainLinks");
        Require(cursor.GetComponentsInChildren<Rigidbody2D>(true).Length == 0, "No physics bodies");
        Require(cursor.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null && !arrow.raycastTarget
            && !links[0].raycastTarget && !links[1].raycastTarget, "Does not intercept clicks");
        foreach (var graphic in cursor.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
            Require(!graphic.raycastTarget, "Noise and cursors do not intercept clicks");
        Require(cursor.GetComponentsInChildren<UnityEngine.UI.Shadow>(true).Length == 3, "Arrow and both chain pieces have shadows");
        Require(AssetDatabase.GetAssetPath(arrow.sprite) == GameplayPixelCursorSetup.ArrowPath
            && AssetDatabase.GetAssetPath(links[0].sprite) == GameplayPixelCursorSetup.ChainPath
            && AssetDatabase.GetAssetPath(links[1].sprite) == GameplayPixelCursorSetup.ChainPath, "Supplied textures only");

        // Force only the display method while testing geometry; desktop pointer focus is
        // deliberately independent of synthetic Input System coordinates.
        foreach (float scale in new[] { 1f, 1.5f, 2f, 3f })
        {
            Set("cursorScale", scale);
            Call("ShowCursor", point);
            Vector2 tip = RectTransformUtility.WorldToScreenPoint(null,
                arrow.rectTransform.TransformPoint(new Vector3(16f * scale, -14f * scale)));
            Require(Vector2.Distance(tip, point) < 0.01f, "Tip/click alignment at scale " + scale);
            Vector2 connection = RectTransformUtility.WorldToScreenPoint(null, chain.position);
            Require(Vector2.Distance(connection, point + new Vector2(8f, -14.5f) * scale) < 0.01f, "Root attachment at inner notch");
            Require(root.localRotation == Quaternion.identity && arrow.rectTransform.localRotation == Quaternion.identity,
                "Arrow unaffected by physics");
        }
        Set("cursorScale", 3f);
        Call("ShowCursor", point);
        Require(PointerDisplayOwned, "Only the custom pointer is displayed while owned");
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
        Report.AppendLine("PASS source sprites, nonblocking UI, exact hotspot at 1/1.5/2/3x, inner-notch attachment, focus/pause/disable, duplicate guard");

        for (int fpsIndex = 0; fpsIndex < 3; fpsIndex++)
        {
            int fps = new[] { 30, 60, 144 }[fpsIndex];
            float dt = 1f / fps;
            Call("UpdateView", point, true);
            Vector2 anchor = point + new Vector2(24f, -43.5f);
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
        cursor.enabled = false;
        pause.OpenPause();
        Require(pause.IsPaused && !(bool)Call("IsGameplay"), "Pause gate");
        bool sawNoise = false;
        for (float end = Time.unscaledTime + 0.35f; Time.unscaledTime < end;)
        {
            Call("ShowCursor", point);
            sawNoise |= Get<RectTransform>("noiseRoot").gameObject.activeSelf
                && Get<UnityEngine.UI.Image[]>("noisePixels")[0].color.a > 0f;
            Require(Vector2.Distance(RectTransformUtility.WorldToScreenPoint(null,
                arrow.rectTransform.TransformPoint(new Vector3(48f, -42f))), point) < 0.01f, "No tip jitter during switch");
            yield return null;
        }
        Call("ShowCursor", point);
        Require(sawNoise && !cursor.IsChangingDesign && PointerDisplayOwned,
            $"Paused noise: saw={sawNoise}, changing={cursor.IsChangingDesign}, showing={cursor.IsShowing}, OS={Cursor.visible}");
        Require(!cursor.IsChainStyle && !chain.gameObject.activeSelf
            && AssetDatabase.GetAssetPath(arrow.sprite) == GameplayPixelCursorSetup.MenuPath, "Pause uses supplied menu design");
        Require(arrow.rectTransform.sizeDelta == new Vector2(141f, 144f), "Identical display size for both source images");
        Require(arrow.GetComponent<UnityEngine.UI.Shadow>().effectColor.a > 0.7f, "Menu shadow remains visible");
        Require(!Get<RectTransform>("noiseRoot").gameObject.activeSelf, "No residual noise");
        CaptureGameView("pause-cursor.png");
        pause.ClosePause();
        while (pause.IsPaused) { Call("ShowCursor", point); yield return null; }
        for (float end = Time.unscaledTime + 0.2f; Time.unscaledTime < end;)
        { Call("ShowCursor", point); yield return null; }
        Call("ShowCursor", point);
        Require(cursor.IsChainStyle && chain.gameObject.activeSelf, "Resume restores chain design");
        Require(cursor.GetComponent<Canvas>().renderMode == RenderMode.ScreenSpaceOverlay, "Resume bypasses CRT conversion");
        Require((bool)Call("IsGameplay"), "Gameplay resumes");
        Call("ReleaseCursor");
        cursor.enabled = true;
        Report.AppendLine("PASS both shadows, paused menu sprite, unscaled switch noise, exact tip throughout switch, no residual noise, chain on Resume");

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
        Require(Get<bool>("usingGamepad"), "Gamepad presentation detected");
        for (float end = Time.unscaledTime + 0.2f; Time.unscaledTime < end;)
        { Call("ShowCursor", point); yield return null; }
        Call("ShowCursor", point);
        Require(!cursor.IsChainStyle && PointerDisplayOwned, "Gamepad fallback uses new custom pointer");
        padState = new GamepadState();
        mouseState.position += Vector2.right * 10f;
        yield return null; yield return null;
        Call("LateUpdate");
        Require(!Get<bool>("usingGamepad"), "Mouse restores custom pointer mode");
        for (float end = Time.unscaledTime + 0.2f; Time.unscaledTime < end;)
        { Call("ShowCursor", point); yield return null; }
        Call("ShowCursor", point);
        Require(cursor.IsChainStyle, "Mouse restores chain design");
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

        var originalCursor = cursor;
        Object.DontDestroyOnLoad(GameObject.Find("CursorCheckDriver"));
        SceneManager.LoadScene("TitleScene");
        yield return null; yield return null;
        Require(Object.FindObjectsByType<GameplayPixelCursor>(FindObjectsInactive.Include).Length == 1
            && Object.FindAnyObjectByType<GameplayPixelCursor>() == originalCursor, "One persistent cursor after loading TitleScene");
        for (float end = Time.unscaledTime + 0.3f; Time.unscaledTime < end;)
        { Call("ShowCursor", point); yield return null; }
        Call("ShowCursor", point);
        Require(cursor.IsShowing && !cursor.IsChainStyle && arrow.sprite == Get<Sprite>("menuSprite"), "Title always uses menu design");
        Require(cursor.GetComponent<Canvas>().sortingOrder == short.MaxValue, "Cursor above full-screen overlays");
        CaptureGameView("title-cursor.png");
        SceneManager.LoadScene("CompletScene");
        yield return null; yield return null;
        player = Object.FindAnyObjectByType<Player>();
        while (!player.InputEnabled) yield return null;
        for (float end = Time.unscaledTime + 0.3f; Time.unscaledTime < end;)
        { Call("ShowCursor", point); yield return null; }
        Call("ShowCursor", point);
        Require(cursor.IsChainStyle && Object.FindObjectsByType<GameplayPixelCursor>(FindObjectsInactive.Include).Length == 1,
            "Reload refreshes scene owners without duplicate cursors");
        Report.AppendLine("PASS TitleScene menu cursor, one persistent owner, CompletScene reload and refreshed gameplay bindings");
        Call("ShowCursor", point);
        Call("OnApplicationQuit");
        Require(Cursor.visible && !cursor.IsShowing, "Quit restores OS cursor");
        Require(errors == 0, "Runtime Errors/Exceptions=" + errors);
        Report.AppendLine("PASS quit cleanup; runtime Errors/Exceptions=0");
    }

    private static void FeedInput()
    {
        if (InputState.currentUpdateType != InputUpdateType.Dynamic) return;
        if (mouse != null) InputSystem.QueueStateEvent(mouse, mouseState);
        if (keyboard != null) InputSystem.QueueStateEvent(keyboard, keyboardState);
        if (pad != null) InputSystem.QueueStateEvent(pad, padState);
    }

    private static void CaptureGameView(string filename = "cursor-game-view.png")
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
            File.WriteAllBytes(Path.Combine(Output, filename), copy.EncodeToPNG());
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
        string filename = SessionState.GetBool(Key + ".FastMotionOnly", false) ? "fast-motion.txt"
            : SessionState.GetBool(Key + ".HitStopOnly", false)
            ? SessionState.GetBool(Key + ".HitStopBaseline", false) ? "hitstop-baseline.txt" : "hitstop.txt"
            : "result.txt";
        File.WriteAllText(Path.Combine(Output, filename), result);
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
