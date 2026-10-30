using System;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

/// <summary>One bounded smoke pass through real scenes and Input System events; preserves the editor scene setup.</summary>
[InitializeOnLoad]
public static class StageSelectFlowProbe
{
    private const string Key = "StageSelectFlowProbe.";
    private static double started, changedAt;
    private static int step, errors, warnings;
    private static bool capturing, sawOpening, sawZoom, sawLoading, sawReveal, sawIntro;
    private static Gamepad gamepad;
    private static Keyboard keyboard;
    private static string output;
    private static double openingVisibleAt;
    private static bool capturedBirth;
    private static bool capturedInsert, capturedGrowth, capturedForeground, sawFusionLight;
    static StageSelectFlowProbe() { if (SessionState.GetBool(Key + "Running", false)) Subscribe(); }

    public static string Run(string outputDirectory)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorUtility.scriptCompilationFailed)
            throw new InvalidOperationException("Run in Edit Mode with a clean compilation.");
        SessionState.SetString(Key+"Glass",VerifyGlassRendering());
        SessionState.SetString(Key + "PreviousStart", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetString(Key + "PreviousScene", SceneManager.GetActiveScene().path);
        SessionState.SetBool(Key + "PreviousDirty", SceneManager.GetActiveScene().isDirty);
        SessionState.SetString(Key + "Output", outputDirectory);
        SessionState.SetString(Key + "Result", "RUNNING"); SessionState.SetBool(Key + "Running", true);
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/TitleScene.unity");
        EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor")).Focus();
        Subscribe(); EditorApplication.isPlaying = true;
        return "Started bounded Title → Select → Title → Select → Stage1 probe; original edit scenes are not saved.";
    }
    public static string Result() => SessionState.GetString(Key + "Result", "NOT RUN");
    private static void Subscribe()
    { EditorApplication.playModeStateChanged -= State; EditorApplication.playModeStateChanged += State; }
    private static void State(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            output = SessionState.GetString(Key + "Output", ""); Directory.CreateDirectory(output);
            started = changedAt = EditorApplication.timeSinceStartup; step = errors = warnings = 0;
            sawOpening = sawZoom = sawLoading = sawReveal = sawIntro = capturing = false;
            openingVisibleAt = -1; capturedBirth = false;
            capturedInsert = capturedGrowth = false;
            capturedForeground=sawFusionLight=false;
            StageSelectProgress.TestNamespace = "FlowProbe.";
            StageSelectProgress.ClearTestData("Stage1"); StageSelectProgress.ClearTestData("Stage2");
            gamepad = InputSystem.AddDevice<Gamepad>(); keyboard = InputSystem.AddDevice<Keyboard>();
            Application.logMessageReceived += Log; EditorApplication.update += Tick;
        }
        else if (state == PlayModeStateChange.ExitingPlayMode)
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
            if (gamepad != null && gamepad.added) InputSystem.RemoveDevice(gamepad);
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            StageSelectProgress.ClearTestData("Stage1"); StageSelectProgress.ClearTestData("Stage2");
            StageSelectProgress.TestNamespace = null;
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Key + "PreviousStart", ""));
            SessionState.SetBool(Key + "Running", false); EditorApplication.playModeStateChanged -= State;
            bool preserved = SceneManager.GetActiveScene().path == SessionState.GetString(Key + "PreviousScene", "")
                && SceneManager.GetActiveScene().isDirty == SessionState.GetBool(Key + "PreviousDirty", false);
            string result = Result() + "; original edit scene/dirty state preserved=" + preserved;
            if (!preserved) result = "FAIL: " + result;
            SessionState.SetString(Key + "Result", result); Debug.Log("[StageSelectProbe] " + result);
        }
    }
    private static void Log(string message, string stack, LogType type)
    {
        if (stack != null && stack.Contains("UnityEditor.Search.SearchDatabase")) return;
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors++;
        if (type == LogType.Warning) warnings++;
    }
    private static void Next() { step++; changedAt = EditorApplication.timeSinceStartup; }
    private static double Elapsed => EditorApplication.timeSinceStartup - changedAt;
    private static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        try
        {
            Require(EditorApplication.timeSinceStartup - started < 100, "flow timed out at step " + step);
            Require(errors == 0, "runtime Error/Exception detected");
            if (capturing) return;
            var title = UnityEngine.Object.FindAnyObjectByType<TitleScreenController>();
            var select = UnityEngine.Object.FindAnyObjectByType<StageSelectController>();
            if (select != null)
            {
                var view=new SerializedObject(select);
                var light=view.FindProperty("fusionLight").objectReferenceValue as UnityEngine.UI.Image;
                sawFusionLight |= light!=null && light.color.a>.05f;
                var cardRect=(RectTransform)view.FindProperty("cartridge").objectReferenceValue;
                var front=(RectTransform)view.FindProperty("console").objectReferenceValue;
                var deck=front.parent.Find("ConsoleBackDeck");
                Require(deck.GetSiblingIndex()<cardRect.GetSiblingIndex() && cardRect.GetSiblingIndex()<front.GetSiblingIndex(),"back deck / cartridge / front lip draw order");
                sawOpening |= select.Phase == StageSelectController.SelectPhase.Opening;
                sawZoom |= select.Phase == StageSelectController.SelectPhase.Zooming;
                sawLoading |= select.Phase == StageSelectController.SelectPhase.Loading;
                sawReveal |= select.Phase == StageSelectController.SelectPhase.Revealing;
                if (step == 1 && select.Phase == StageSelectController.SelectPhase.Opening && !TitleStageTransition.IsTransitioning)
                {
                    if (openingVisibleAt < 0) openingVisibleAt = EditorApplication.timeSinceStartup;
                    if (!capturedBirth && EditorApplication.timeSinceStartup - openingVisibleAt > .64)
                    { capturedBirth = true; Capture(select, "00-crystal-birth"); return; }
                }
            }
            switch (step)
            {
                case 0:
                    if (title == null || !title.InputReady) return;
                    Require(title.StageSceneName == "StageSelectScene", "title route");
                    title.OnStartPressed(); title.OnStartPressed(); Next(); break;
                case 1:
                    if (select == null || !select.InputReady) return;
                    Require(select.SelectedStage.sceneName == "Stage1" && select.SelectedStage.CanLaunch && !select.SelectedStage.NeedsReveal, "Stage1 initially available without reveal");
                    Require(UnityEngine.Object.FindObjectsByType<CRTFilterController>(FindObjectsSortMode.None).Length == 0, "selection must not have fullscreen CRT");
                    Capture(select, "01-select-ready"); Next(); break;
                case 2:
                    InputSystem.QueueStateEvent(gamepad, new GamepadState { leftStick = Vector2.right }); Next(); break;
                case 3:
                    if (Elapsed < .09) return;
                    InputSystem.QueueStateEvent(gamepad, new GamepadState()); Next(); break;
                case 4:
                    if (!select.InputReady) return;
                    Require(select.SelectedIndex == 1 && !select.SelectedStage.CanLaunch, "stick/unavailable cartridge");
                    Require(select.GetComponent<StageSelectPreview>().SignalLost, "locked preview is only static");
                    var lockedView = new SerializedObject(select);
                    Require(((CanvasGroup)lockedView.FindProperty("cartridgeGroup").objectReferenceValue).alpha == 0
                        && ((CanvasGroup)lockedView.FindProperty("crystalGroup").objectReferenceValue).alpha == 0, "locked stage has no cartridge or crystal mass");
                    select.Confirm(); select.Confirm(); Require(select.InputReady, "unavailable confirm guard");
                    Capture(select, "02-machine-city"); Next(); break;
                case 5:
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(UnityEngine.InputSystem.Key.LeftArrow)); Next(); break;
                case 6:
                    if (Elapsed < .09) return;
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState()); Next(); break;
                case 7:
                    if (!select.InputReady) return;
                    Require(select.SelectedIndex == 0, "keyboard left");
                    InputSystem.QueueStateEvent(gamepad, new GamepadState().WithButton(GamepadButton.DpadLeft)); Next(); break;
                case 8:
                    if (Elapsed < .09) return;
                    InputSystem.QueueStateEvent(gamepad, new GamepadState()); Next(); break;
                case 9:
                    if (!select.InputReady) return;
                    Require(select.SelectedIndex == 1, "dpad wrap");
                    select.Move(-1); select.Move(1); select.Move(-1); Next(); break;
                case 10:
                    if (!select.InputReady) return;
                    Require(select.SelectedIndex == 1, "bounded last-direction queue");
                    select.Move(1); select.Back(); select.Back(); Next(); break;
                case 11:
                    if (title == null || !title.InputReady) return;
                    title.OnStartPressed(); Next(); break;
                case 12:
                    if (select == null || !select.InputReady) return;
                    if (select.SelectedIndex != 0) { select.Move(-1); return; }
                    select.Confirm(); select.Confirm();
                    Require(StageSelectController.IsEnteringStage && TitleStageTransition.IsTransitioning, "single entry/shared intro gate");
                    Next(); break;
                case 13:
                    if (!capturedInsert && select.Phase == StageSelectController.SelectPhase.Inserting && Elapsed > .65)
                    {
                        var seated = new SerializedObject(select);
                        Require(((CanvasGroup)seated.FindProperty("cartridgeGroup").objectReferenceValue).alpha > .99f, "seated cartridge stays visible");
                        var card = (RectTransform)seated.FindProperty("cartridge").objectReferenceValue;
                        // Frame scheduling can stretch a coroutine past its nominal time; observe the actual seated pose.
                        if (Mathf.Abs(card.anchoredPosition.y - seated.FindProperty("insertPosition").vector2Value.y) >= 3) return;
                        capturedInsert = true; Capture(select, "03a-cartridge-seated"); return;
                    }
                    if (select.Phase != StageSelectController.SelectPhase.Zooming) return;
                    Require(capturedInsert, "seated cartridge observation");
                    if (select.GetComponentInChildren<Canvas>().transform.Find("Theatre").localScale.x < 1.25f) return;
                    Capture(select, "03-crt-zoom"); Next(); break;
                case 14:
                    if (SceneManager.GetActiveScene().name != "Stage1" || TitleStageTransition.IsTransitioning) return;
                    Require(sawZoom && sawLoading && sawReveal, "CRT zoom/load/reveal phases");
                    Require(!StageSelectController.IsEnteringStage && Mathf.Approximately(Time.timeScale, 1), "gate/time cleanup");
                    Require(UnityEngine.Object.FindObjectsByType<StageSelectController>(FindObjectsSortMode.None).Length == 0, "persistent theatre cleanup");
                    Next(); break;
                case 15:
                    var intro = UnityEngine.Object.FindAnyObjectByType<PlayerStartIntro>();
                    if (intro == null || intro.Phase != PlayerStartIntro.IntroPhase.Complete) return;
                    sawIntro = true;
                    Require(GameBgmController.Instance != null && GameBgmController.Instance.CurrentState == GameBgmController.BgmState.Stage, "stage BGM");
                    Require(UnityEngine.Object.FindObjectsByType<CRTFilterController>(FindObjectsSortMode.None).Length == 1, "one original gameplay CRT");
                    capturing = true; intro.StartCoroutine(Snapshot("04-stage1-start")); Next(); break;
                case 16:
                    Require(PinkJewelCollection.Current == null || PinkJewelCollection.Current.CollectedMask == 0, "clear test must not require collection pickups");
                    var goalPoint = UnityEngine.Object.FindAnyObjectByType<GoalPoint>();
                    Require(goalPoint != null, "stage goal crystal");
                    typeof(GoalPoint).GetMethod("ReachGoal", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(goalPoint, null);
                    Require(StageSelectProgress.IsCleared("Stage1"), "actual goal callback persists clear milestone");
                    Next(); break;
                case 17:
                    if (select != null) { capturedBirth = false; Next(); break; }
                    UnityEngine.Object.FindAnyObjectByType<GoalMenuController>()?.LoadNextScene(); break;
                case 18:
                    Require(select.SelectedIndex == 1 && select.SelectedStage.IsUnlocked, "clear selects newly unlocked Stage2");
                    if(!capturedForeground && select.UnlockAssemblyProgress>.12f && select.UnlockAssemblyProgress<.4f)
                    {
                        int large=0, below=0;
                        float centerY=((RectTransform)new SerializedObject(select).FindProperty("cartridge").objectReferenceValue).anchoredPosition.y;
                        foreach(var r in select.GetComponentsInChildren<RectTransform>())
                            if(r.name.StartsWith("Shard_") && r.localScale.x>1.5f)
                            { large++; if(r.anchoredPosition.y<centerY) below++; }
                        Require(large>=3 && below==large,"large crystal fragments rise from the lower foreground");
                        capturedForeground=true;Capture(select,"05b-foreground-fragments");return;
                    }
                    var growing = (RectTransform)new SerializedObject(select).FindProperty("crystal").objectReferenceValue;
                    if (!capturedGrowth && growing.localScale.x > .25f && growing.localScale.x < .7f)
                    { capturedGrowth = true; Capture(select, "05a-crystal-growing"); return; }
                    var fracture = new SerializedObject(select).FindProperty("cracks").objectReferenceValue as CanvasGroup;
                    if (select.Phase == StageSelectController.SelectPhase.Opening && fracture.alpha > .2f)
                    {
                        Require(select.SelectedStage.NeedsReveal, "reveal not acknowledged early");
                        Require(capturedGrowth, "progressive crystal growth observed");
                        Require(capturedForeground && sawFusionLight,"foreground fusion and white light observed");
                        Capture(select, "05-stage2-crystal-unlock"); Next();
                    }
                    break;
                case 19:
                    Require(select.SelectedStage.NeedsReveal, "interrupted reveal must remain pending");
                    select.Back(); Next(); break;
                case 20:
                    if (title == null || !title.InputReady) return;
                    title.OnStartPressed(); Next(); break;
                case 21:
                    if (select == null || !select.InputReady) return;
                    Require(select.SelectedIndex == 1 && !select.SelectedStage.NeedsReveal, "interrupted unlock replays and completes");
                    Capture(select, "06-stage2-unlocked"); Next(); break;
                case 22:
                    if (select != null) { select.Back(); return; }
                    if (title == null || !title.InputReady) return;
                    title.OnStartPressed(); Next(); break;
                case 23:
                    if (select == null || !select.InputReady) return;
                    Require(select.SelectedStage.IsUnlocked && !select.SelectedStage.NeedsReveal, "unlock persists / completed animation never replays");
                    SessionState.SetString(Key + "Result", "PASS: draw order throughout insertion; seated cartridge; locked static; foreground shards/white fusion/rectangular growth; unlock persistence; inputs/CRT entry/Stage1 intro/BGM; "+SessionState.GetString(Key+"Glass","")+"; errors="+errors+"; warnings="+warnings);
                    EditorApplication.isPlaying = false; break;
            }
        }
        catch (Exception exception)
        {
            SessionState.SetString(Key + "Result", "FAIL step " + step + ": " + exception.Message);
            Debug.LogException(exception); EditorApplication.isPlaying = false;
        }
    }
    private static void Capture(StageSelectController owner, string name)
    { capturing = true; owner.StartCoroutine(Snapshot(name)); }
    private static IEnumerator Snapshot(string name)
    {
        yield return new WaitForEndOfFrame();
        Texture2D texture = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(Path.Combine(output, name + ".png"), texture.EncodeToPNG());
        UnityEngine.Object.Destroy(texture); capturing = false;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private static string VerifyGlassRendering()
    {
        var material=new Material(Shader.Find("Hidden/IronBallGirl/StageSelectGlass"));
        var source=new Texture2D(320,200,TextureFormat.RGBA32,false);
        var pixels=new Color[320*200];
        for(int y=0;y<200;y++) for(int x=0;x<320;x++) pixels[y*320+x]=x>=228&&x<=231?Color.white:Color.black;
        source.SetPixels(pixels);source.Apply();source.filterMode=FilterMode.Bilinear;
        var target=new RenderTexture(600,400,0,RenderTextureFormat.ARGB32);target.Create();
        var readback=new Texture2D(600,400,TextureFormat.RGBA32,false);
        var old=RenderTexture.active;
        try
        {
            material.SetFloat("_Curvature",.18f);material.SetFloat("_Brightness",1);
            material.SetVector("_GlassSize",new Vector4(720,440,58,0));
            Graphics.Blit(source,target,material);RenderTexture.active=target;
            readback.ReadPixels(new Rect(0,0,600,400),0,0);readback.Apply();
            float Center(int row)
            {
                float sum=0,weight=0;
                for(int x=300;x<565;x++){float w=readback.GetPixel(x,row).r;sum+=x*w;weight+=w;}
                Require(weight>.2f,"CRT calibration stripe is rendered");return sum/weight;
            }
            float displacement=Mathf.Abs(Center(200)-Center(340));
            Require(displacement>4,"GPU radial barrel distortion is visible");
            material.SetFloat("_Scanlines",.32f);material.SetFloat("_ScanlineCount",240);
            Graphics.Blit(Texture2D.grayTexture,target,material);
            readback.ReadPixels(new Rect(0,0,600,400),0,0);readback.Apply();
            float low=1,high=0;
            for(int y=40;y<360;y++){float v=readback.GetPixel(300,y).r;low=Mathf.Min(low,v);high=Mathf.Max(high,v);}
            Require(high-low>.035f,"GPU scanline contrast is visible");
            return "GPU glass: warp="+displacement.ToString("F1")+"px, scanline contrast="+(high-low).ToString("F3");
        }
        finally
        {
            RenderTexture.active=old;target.Release();UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(source);UnityEngine.Object.DestroyImmediate(readback);UnityEngine.Object.DestroyImmediate(material);
        }
    }
}
