using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Checks two complete starts, including a longer interlude, and the untouched entrance gate.</summary>
[InitializeOnLoad]
public static class TitleLoadingTransitionPlayModeTest
{
    private const string RunningKey = "TitleLoadingTransitionPlayModeTest.Running";
    private const string ResultKey = "TitleLoadingTransitionPlayModeTest.Result";
    private const string PreviewKey = "TitleLoadingTransitionPlayModeTest.Previews";
    private static double startedAt;
    private static int step, cycle, errors, warnings;
    private static bool finished, sawCover, sawLoading, sawPendulum, sawReveal, sawJump, capturedLoading, capturedEntrance;
    private static float firstAngle, normalDuration, expectedLoadingMinimum;
    private static string previews;
    private static readonly List<KeyValuePair<string, Texture2D>> captures = new List<KeyValuePair<string, Texture2D>>();

    static TitleLoadingTransitionPlayModeTest()
    { if (SessionState.GetBool(RunningKey, false)) Subscribe(); }
    public static void Run() => RunWithPreviews(null);
    public static void RunWithPreviews(string outputDirectory)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().isDirty)
            throw new InvalidOperationException("Run from a saved scene in Edit Mode.");
        SessionState.SetBool(RunningKey, true); SessionState.EraseString(ResultKey);
        SessionState.SetString(PreviewKey, outputDirectory ?? "");
        EditorSceneManager.OpenScene(TitleSceneBuilder.TitleScenePath);
        if (!Application.isBatchMode) EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor")).Focus();
        Subscribe(); EditorApplication.isPlaying = true;
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
            previews = SessionState.GetString(PreviewKey, "");
            startedAt = EditorApplication.timeSinceStartup;
            step = cycle = errors = warnings = 0; finished = false; ResetObservations();
            Application.logMessageReceived += CountLog; EditorApplication.update += Tick;
        }
        else if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(RunningKey, false))
        {
            SessionState.SetBool(RunningKey, false); EditorApplication.playModeStateChanged -= OnState;
            string result = SessionState.GetString(ResultKey, "FAIL: stopped early");
            Debug.Log("[TitleLoadingTransitionTest] " + result);
            if (Application.isBatchMode) EditorApplication.Exit(result.StartsWith("PASS") ? 0 : 1);
        }
    }
    private static void CountLog(string message, string stack, LogType type)
    {
        if (stack != null && stack.Contains("UnityEditor.Search.SearchDatabase")) return;
        if (type == LogType.Warning) warnings++;
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors++;
    }
    private static void ResetObservations()
    { sawCover = sawLoading = sawPendulum = sawReveal = sawJump = capturedLoading = capturedEntrance = false; firstAngle = 0; }
    private static void Tick()
    {
        if (finished) return;
        EditorApplication.QueuePlayerLoopUpdate();
        try
        {
            Require(EditorApplication.timeSinceStartup - startedAt < 18, "cycle timed out");
            if (step == 0)
            {
                var title = UnityEngine.Object.FindAnyObjectByType<TitleScreenController>();
                if (title == null || !title.InputReady) return;
                if (cycle == 1)
                {
                    var settings = new SerializedObject(title).FindProperty("transitionSettings");
                    settings.FindPropertyRelative("minimumLoadingDuration").floatValue = title.MinimumLoadingDuration + .5f;
                    settings.serializedObject.ApplyModifiedPropertiesWithoutUndo();
                }
                expectedLoadingMinimum = title.MinimumLoadingDuration;
                var menu = (RectTransform)new SerializedObject(title).FindProperty("startPanel").objectReferenceValue;
                foreach (var text in menu.GetComponentsInChildren<TMPro.TMP_Text>())
                    foreach (char letter in text.text) Require(letter < 128, "Japanese remained on a title button");
                Capture("00-quiet-title");
                title.OnStartPressed(); title.OnStartPressed();
                Require(title.IsStarting && !title.InputReady && TitleStageTransition.IsTransitioning, "immediate guard/feedback");
                Require(TitleStageTransition.Phase == TitleStageTransition.TransitionPhase.TitleDeparture, "departure phase missing");
                step = 1; return;
            }
            var intro = UnityEngine.Object.FindAnyObjectByType<PlayerStartIntro>();
            if (TitleStageTransition.IsTransitioning)
            {
                if (intro != null)
                {
                    Require(intro.Phase == PlayerStartIntro.IntroPhase.WaitingForStage, "entrance played behind veil");
                    Require(!intro.GetComponent<Player>().InputEnabled, "input leaked through veil");
                    Require(!intro.GetComponent<Rigidbody2D>().simulated, "intro physics advanced behind veil");
                    var bgm = GameBgmController.Instance;
                    Require(bgm == null || !bgm.Source.isPlaying, "stage music began before the reveal completed");
                }
                Require(Mathf.Approximately(Time.timeScale, 0), "transition lost its time gate");
                var phase = TitleStageTransition.Phase;
                if (phase == TitleStageTransition.TransitionPhase.Covering && TitleStageTransition.WipeProgress > .42f && !sawCover)
                {
                    var atmosphere = UnityEngine.Object.FindAnyObjectByType<TitleAtmosphere>();
                    var background = (UnityEngine.UI.RawImage)new SerializedObject(atmosphere).FindProperty("background").objectReferenceValue;
                    Require(background.material.GetFloat("_MenuVisibility") < .001f, "menu shadow remained after the menu disappeared");
                    Capture("01-cover-left-to-right"); sawCover = true;
                }
                if (TitleStageTransition.IsLoadingVisible)
                {
                    Require(intro != null, "visible loading animation began before scene initialization");
                    var pivot = GameObject.Find("PendulumPivot").transform;
                    float angle = pivot.eulerAngles.z;
                    if (!sawLoading)
                    {
                        var icon = pivot.Find("LoadingBall").GetComponent<UnityEngine.UI.Image>();
                        Require(AssetDatabase.GetAssetPath(icon.sprite) == "Assets/image_/TitleLoadingSilhouette.png", "loading reused gameplay artwork");
                        firstAngle = angle; sawLoading = true;
                    }
                    else if (Mathf.Abs(Mathf.DeltaAngle(firstAngle, angle)) > 1) sawPendulum = true;
                    if (!capturedLoading && sawPendulum) { Capture("02-loading-pendulum"); capturedLoading = true; }
                }
                if (phase == TitleStageTransition.TransitionPhase.Revealing && TitleStageTransition.WipeProgress > .42f && !sawReveal)
                {
                    var material = GameObject.Find("DotVeil").GetComponent<UnityEngine.UI.Image>().material;
                    Require(material.GetFloat("_RightToLeft") < .5f, "stage did not reveal from the left");
                    Capture("03-reveal-left-to-right"); sawReveal = true;
                }
                return;
            }
            if (SceneManager.GetActiveScene().name != Path.GetFileNameWithoutExtension(TitleSceneBuilder.StageScenePath) || intro == null) return;
            if (intro.Phase == PlayerStartIntro.IntroPhase.Jump)
            {
                sawJump = true;
                Vector3 point = Camera.main.WorldToViewportPoint(intro.transform.position);
                if (!capturedEntrance && point.x > .08f && point.x < .92f && point.y > .08f && point.y < .92f)
                { Capture("04-existing-entrance"); capturedEntrance = true; }
            }
            if (intro.Phase != PlayerStartIntro.IntroPhase.Complete) return;
            Require(sawCover && sawLoading && sawPendulum && sawReveal && sawJump && capturedEntrance, "a visual phase was not observed");
            Require(TitleStageTransition.ActivationCount == 1 && TitleStageTransition.WasBlackAtActivation, "activation guard/opaque gate");
            Require(TitleStageTransition.LastLoadingVisibleDuration >= expectedLoadingMinimum - .01f, "loading minimum was not respected");
            Require(intro.GetComponent<Player>().InputEnabled && intro.GetComponent<Rigidbody2D>().simulated, "entrance did not restore player control/physics");
            Require(Mathf.Approximately(Time.timeScale, 1), "time scale not restored");
            Require(GameBgmController.Instance.Source.isPlaying, "stage music did not begin");
            Require(errors == 0 && warnings == 0, "runtime errors=" + errors + " warnings=" + warnings);
            if (cycle == 0)
            {
                normalDuration = TitleStageTransition.LastTotalDuration;
                FlushCaptures();
                cycle = 1; step = 0; ResetObservations(); startedAt = EditorApplication.timeSinceStartup;
                SceneManager.LoadScene("TitleScene"); return;
            }
            Finish("PASS: two starts; English menu; double-input guard; left-to-right reveal; menu shadow cleared; original silhouette pendulum after initialization; extended interlude; visible existing entrance; input/physics/music restored; errors=0 warnings=0; normalTransition=" + normalDuration.ToString("F2") + "s");
        }
        catch (Exception e) { Finish("FAIL: " + e.Message + "; cycle=" + cycle + "; step=" + step); }
    }
    private static void Capture(string name)
    {
        if (cycle != 0 || string.IsNullOrEmpty(previews) || Application.isBatchMode) return;
        var type = Type.GetType("UnityEditor.GameView,UnityEditor");
        var window = EditorWindow.GetWindow(type);
        var texture = type.GetField("m_RenderTexture", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window) as RenderTexture;
        if (texture == null) return;
        var previous = RenderTexture.active;
        var image = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
        try
        {
            RenderTexture.active = texture;
            image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); image.Apply();
            if (SystemInfo.graphicsUVStartsAtTop)
            {
                var pixels = image.GetPixels32(); int width = image.width, height = image.height;
                for (int y = 0; y < height / 2; y++) for (int x = 0; x < width; x++)
                { int a = y * width + x, b = (height - y - 1) * width + x; var c = pixels[a]; pixels[a] = pixels[b]; pixels[b] = c; }
                image.SetPixels32(pixels); image.Apply();
            }
            // Encode after the transition so preview export does not distort its timing.
            captures.Add(new KeyValuePair<string, Texture2D>(name, image)); image = null;
        }
        finally { RenderTexture.active = previous; if (image != null) UnityEngine.Object.DestroyImmediate(image); }
    }
    private static void FlushCaptures()
    {
        if (captures.Count == 0) return;
        Directory.CreateDirectory(previews);
        foreach (var capture in captures)
        {
            File.WriteAllBytes(Path.Combine(previews, capture.Key + ".png"), capture.Value.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(capture.Value);
        }
        captures.Clear();
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Finish(string message)
    {
        finished = true; EditorApplication.update -= Tick; Application.logMessageReceived -= CountLog;
        FlushCaptures();
        SessionState.SetString(ResultKey, message); EditorApplication.isPlaying = false;
    }
}
