using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Explicit checks of the cursor switch and final crystal -> logo -> CRT sequence.</summary>
[InitializeOnLoad]
public static class GoalPresentationPlayModeChecks
{
    private const string Key = "Tekkyu.GoalPresentationChecks";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly string Output = Path.Combine(Path.GetTempPath(), "TekkyuGoalPresentationChecks");
    private static readonly StringBuilder Report = new StringBuilder();
    private static IEnumerator routine;
    private static int errors;
    private static double started;
    private static bool oldBackground;

    static GoalPresentationPlayModeChecks()
    { if (SessionState.GetBool(Key, false)) Subscribe(); }

    public static void Run(bool baseline = false)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty
            || EditorSceneManager.GetActiveScene().path != "Assets/Scenes/CompletScene.unity")
            throw new InvalidOperationException("Start from saved CompletScene in Edit Mode.");
        SessionState.SetBool(Key, true);
        SessionState.SetBool(Key + ".Baseline", baseline);
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
            Directory.CreateDirectory(Output); Report.Clear(); errors = 0;
            started = EditorApplication.timeSinceStartup;
            oldBackground = Application.runInBackground; Application.runInBackground = true;
            Application.logMessageReceived += OnLog;
            EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor")).Focus();
            routine = Check(SessionState.GetBool(Key + ".Baseline", false));
            new GameObject("GoalPresentationCheckDriver").AddComponent<GoalPresentationCheckDriver>();
            EditorApplication.update += Watchdog;
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(Key, false);
            EditorApplication.playModeStateChanged -= OnState;
            EditorApplication.update -= Watchdog;
        }
    }

    internal static void Tick()
    {
        if (routine == null) return;
        try { if (!routine.MoveNext()) Finish(true, ""); }
        catch (Exception e) { Finish(false, e.ToString()); }
    }

    private static void Watchdog()
    { if (routine != null && EditorApplication.timeSinceStartup - started > 45d) Finish(false, "Timed out"); }

    private static IEnumerator Check(bool baseline)
    {
        var player = Object.FindAnyObjectByType<Player>();
        var shake = Camera.main.GetComponent<CameraShake2D>();
        var ui = Object.FindAnyObjectByType<CrystalAcquiredUI>();
        var goal = Object.FindAnyObjectByType<GoalPoint>();
        var menu = Object.FindAnyObjectByType<GoalMenuController>();
        var cursor = Object.FindAnyObjectByType<GameplayPixelCursor>();
        while (!player.InputEnabled) yield return null;
        if (!baseline)
        {
            // Reproduce the timer problem independently, then check paused shakes.
            Time.timeScale = 0f;
            shake.Shake(0.12f, 0.06f);
            Vector3 pausedPosition = shake.transform.position;
            for (int i = 0; i < 5; i++)
            { yield return null; Require(Vector3.Distance(pausedPosition, shake.transform.position) < 0.00001f, "No shake while gameplay is stopped"); }
            shake.ResetShake(); Time.timeScale = 1f;

            var pause = Object.FindAnyObjectByType<PauseMenuController>();
            cursor.enabled = false;
            Vector2 point = new Vector2(Screen.width * 0.58f, Screen.height * 0.55f);
            for (float end = Time.unscaledTime + 0.4f; Time.unscaledTime < end;)
            { Call(cursor, "ShowCursor", point); yield return null; }
            pause.OpenPause();
            Call(cursor, "ShowCursor", point);
            Set(cursor, "styleChangedAt", Time.unscaledTime - Get<float>(cursor, "transitionDuration") * 0.5f);
            Call(cursor, "ShowCursor", point);
            var pixels = Get<UnityEngine.UI.Image[]>(cursor, "noisePixels");
            Require(pixels.Length >= 16 && pixels[0].color.a >= 0.8f && pixels[0].rectTransform.sizeDelta.x >= 15f,
                "Switch has dense, opaque and wide noise fragments");
            var rows = new System.Collections.Generic.HashSet<float>();
            foreach (var pixel in pixels) rows.Add(pixel.rectTransform.anchoredPosition.y);
            Require(rows.Count >= 12, "Noise fragments cover different rows of the cursor");
            var arrow = Get<UnityEngine.UI.Image>(cursor, "arrowSprite");
            Vector2 tip = RectTransformUtility.WorldToScreenPoint(null, arrow.rectTransform.TransformPoint(new Vector3(48f, -42f)));
            Require(Vector2.Distance(point, tip) < 0.01f, "Noise never shifts the click tip");
            yield return null;
            Capture("cursor-noise.png");
            for (float end = Time.unscaledTime + 0.35f; Time.unscaledTime < end;)
            { Call(cursor, "ShowCursor", point); yield return null; }
            Call(cursor, "ShowCursor", point);
            Require(!Get<RectTransform>(cursor, "noiseRoot").gameObject.activeSelf, "Noise ends while paused");
            pause.ClosePause(); while (pause.IsPaused) yield return null;
            cursor.enabled = true;
            Report.AppendLine("PASS paused shake guard, stronger cursor noise, unchanged hotspot and noise cleanup");
        }

        for (int hit = 0; hit < goal.RequiredHits; hit++)
        {
            goal.OnMorningStarHit(new MorningStarHitContext(1, Vector2.zero, 0f, goal.transform.position, Vector2.right, 12f, 1f));
            if (hit < goal.RequiredHits - 1)
                for (float end = Time.unscaledTime + 0.18f; Time.unscaledTime < end;) yield return null;
        }
        Require(goal.IsBroken && ui.IsPlaying, "Final crystal starts celebration");
        float finalHitAt = Time.unscaledTime;
        bool sawImpactShake = shake.IsShaking;
        var logo = Get<RectTransform>(ui, "celebrationLogo");
        var image = Get<UnityEngine.UI.Image>(ui, "logoImage");
        var burst = Get<GoalCelebrationBurstGraphic>(ui, "burstGraphic");
        bool capturedImpact = false, capturedReward = false;
        if (!baseline)
            Require(burst != null && !burst.raycastTarget && burst.GetComponent<Collider2D>() == null,
                "Reward accents are presentation-only and do not intercept input");
        while (Time.unscaledTime - finalHitAt < 0.65f)
        {
            if (!baseline && image.color.a > 0.01f)
            {
                Require(!shake.IsShaking && Time.timeScale == 0f, "Shake and gameplay stop before the logo appears");
                Require(ui.RewardSoundPlayCount == 1, "Reward chime starts exactly once with the logo");
            }
            float age = Time.unscaledTime - finalHitAt;
            if (!baseline && !capturedImpact && age >= 0.07f)
            {
                Require(burst.IsAnimating, "Impact burst runs before reward reveal");
                // Saving a full-resolution PNG can stall long enough to skip the
                // next beat. Prioritize the reward frame for the live capture.
                capturedImpact = true;
            }
            if (!baseline && !capturedReward && age >= 0.36f)
            {
                Require(burst.IsAnimating && Get<float>(burst, "age") >= 0.24f, "Pixel burst advances during stopped time");
                Require(Get<AudioSource>(ui, "rewardAudioSource").isPlaying, "Assigned reward chime plays while stopped");
                Capture("crystal-reward-burst.png"); capturedReward = true;
            }
            yield return null;
        }
        if (!baseline) Require(capturedImpact && capturedReward, "Both impact/reward beats were observed");
        Vector3 frozenPosition = shake.transform.position;
        float maxMovement = 0f;
        for (float end = Time.unscaledTime + 0.35f; Time.unscaledTime < end;)
        {
            maxMovement = Mathf.Max(maxMovement, Vector3.Distance(frozenPosition, shake.transform.position));
            yield return null;
        }
        Report.AppendLine($"impactShake={sawImpactShake} timeScale={Time.timeScale} remainingShake={shake.IsShaking} stoppedCameraMovement={maxMovement:F6} logoScale={logo.localScale}");
        if (baseline)
        { Report.AppendLine("BASELINE: scaled shake timer remains active while random offsets still render at timeScale=0."); yield break; }
        Require(sawImpactShake && !shake.IsShaking && maxMovement < 0.00001f, "Impact then exact camera stop");
        Require(logo.localScale.x >= 1.55f, "Acquisition logo is larger");
        Require(!ui.Play(null) && ui.RewardSoundPlayCount == 1, "Duplicate clear requests never replay reward audio");
        Capture("crystal-acquired.png");
        while (!menu.IsGoalReached)
        {
            Require(Get<GameObject>(ui, "presentationRoot").activeSelf, "No empty frame/wait before the result");
            yield return null;
        }
        Require(Time.unscaledTime - finalHitAt < 2.7f, "Reward handoff remains brisk");
        Require(!burst.IsAnimating && !Get<GameObject>(ui, "presentationRoot").activeSelf, "Burst cleaned up before the CRT result");
        Require(player.IsGoalInputBlocked && Time.timeScale == 0f, "Result retains stopped time and gameplay input ownership");
        Require(GameBgmController.Instance == null || GameBgmController.Instance.CurrentState == GameBgmController.BgmState.Goal,
            "Existing goal BGM flow is preserved");
        var overlay = Get<CRTOverlayController>(menu, "overlay");
        while (!overlay.IsReady) yield return null;
        frozenPosition = shake.transform.position;
        var content = Get<CanvasGroup>(overlay, "content").transform;
        Vector3 contentPosition = content.position;
        // New feedback requests after clear must not move the result's camera/UI either.
        shake.Shake(0.12f, 0.06f);
        for (float end = Time.unscaledTime + 0.35f; Time.unscaledTime < end;)
        {
            Require(Vector3.Distance(frozenPosition, shake.transform.position) < 0.00001f, "CRT camera stays fixed");
            Require(Vector3.Distance(contentPosition, content.position) < 0.00001f, "CRT content stays fixed");
            yield return null;
        }
        Capture("clear-crt.png");
        Require(errors == 0, "Runtime Errors/Exceptions=" + errors);
        Report.AppendLine("PASS impact burst -> exact stop -> pixel reward burst + one chime -> continuous CRT result; input/BGM ownership preserved; Errors/Exceptions=0");
    }

    private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);
    private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
    private static void Require(bool success, string message) { if (!success) throw new InvalidOperationException(message); }
    private static void OnLog(string message, string trace, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors++; }

    private static void Capture(string name)
    {
        var game = EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor"));
        var rt = game.GetType().GetField("m_RenderTexture", Private)?.GetValue(game) as RenderTexture;
        if (rt == null) return;
        var previous = RenderTexture.active;
        var texture = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
        try
        {
            RenderTexture.active = rt; texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            if (SystemInfo.graphicsUVStartsAtTop)
            {
                var source = texture.GetPixels32(); var flipped = new Color32[source.Length];
                for (int y = 0; y < rt.height; y++) Array.Copy(source, y * rt.width, flipped, (rt.height - 1 - y) * rt.width, rt.width);
                texture.SetPixels32(flipped);
            }
            texture.Apply(); File.WriteAllBytes(Path.Combine(Output, name), texture.EncodeToPNG());
        }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(texture); }
    }

    private static void Finish(bool success, string detail)
    {
        EditorApplication.update -= Watchdog; Application.logMessageReceived -= OnLog;
        Time.timeScale = 1f; Application.runInBackground = oldBackground;
        string result = (success ? "PASS\n" : "FAIL\n") + Report + detail;
        File.WriteAllText(Path.Combine(Output, SessionState.GetBool(Key + ".Baseline", false) ? "baseline.txt" : "result.txt"), result);
        Debug.Log("[GoalPresentationChecks] " + result);
        routine = null; EditorApplication.isPlaying = false;
    }
}

[DefaultExecutionOrder(11000)]
public sealed class GoalPresentationCheckDriver : MonoBehaviour
{ private void LateUpdate() => GoalPresentationPlayModeChecks.Tick(); }
