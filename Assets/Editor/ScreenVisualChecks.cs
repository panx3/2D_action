using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.U2D;
using Object = UnityEngine.Object;

/// <summary>Explicit Play Mode integration check. Results are written outside Assets.</summary>
[InitializeOnLoad]
public static class ScreenVisualChecks
{
    private const string Key = "Tekkyu.ScreenVisualChecks";
    public static string Result => SessionState.GetString(Key + ".Result", "Not run");
    public static string Output => Path.Combine(Path.GetTempPath(), "tekkyu-screen-visuals", "validation");
    private static readonly StringBuilder Report = new StringBuilder();
    private static int errors;
    private static double started;
    private static bool oldBackground;

    static ScreenVisualChecks()
    {
        if (SessionState.GetBool(Key, false)) Subscribe();
    }
    [MenuItem("Tools/Tekkyu/Verify Screen Visuals")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().isDirty || EditorUtility.scriptCompilationFailed)
            throw new InvalidOperationException("Start from a saved CompletScene and clean compilation.");
        if (SceneManager.GetActiveScene().path != "Assets/Scenes/CompletScene.unity") throw new InvalidOperationException("Open CompletScene.");
        Directory.CreateDirectory(Output);
        SessionState.SetBool(Key, true);
        SessionState.SetString(Key + ".Result", "RUNNING");
        Subscribe();
        EditorApplication.isPlaying = true;
    }
    private static void Subscribe()
    {
        EditorApplication.playModeStateChanged -= StateChanged;
        EditorApplication.playModeStateChanged += StateChanged;
    }
    private static void StateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            Report.Clear(); errors = 0;
            started = EditorApplication.timeSinceStartup;
            oldBackground = Application.runInBackground;
            Application.runInBackground = true;
            Application.logMessageReceived += OnLog;
            new GameObject("ScreenVisualCheckDriver").AddComponent<ScreenVisualCheckDriver>();
            EditorApplication.update += Watchdog;
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            Application.logMessageReceived -= OnLog;
            EditorApplication.update -= Watchdog;
            EditorApplication.playModeStateChanged -= StateChanged;
            SessionState.SetBool(Key, false);
        }
    }
    private static void OnLog(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
        { errors++; Report.AppendLine(type + ": " + message); }
        else if (type == LogType.Warning) Report.AppendLine("Warning: " + message);
    }
    private static void Watchdog()
    {
        if (EditorApplication.timeSinceStartup - started > 150) Finish(false, "Timeout");
    }
    internal static void Finish(bool success, string message)
    {
        Application.logMessageReceived -= OnLog;
        EditorApplication.update -= Watchdog;
        Application.runInBackground = oldBackground;
        Time.timeScale = 1;
        string text = (success && errors == 0 ? "PASS" : "FAIL") + "\n" + Report + "\n" + message + "\nErrors=" + errors;
        File.WriteAllText(Path.Combine(Output, "results.txt"), text);
        SessionState.SetString(Key + ".Result", text);
        Debug.Log("[ScreenVisualChecks] " + text);
        EditorApplication.isPlaying = false;
    }
    private static void Require(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
        Report.AppendLine("PASS " + message);
    }
    internal static IEnumerator Check()
    {
        for (int i = 0; i < 10; i++) yield return null;
        var c = Object.FindAnyObjectByType<ScreenVisualController>();
        Require(c != null && c.BoundRendererCount > 3, "Scene controller binds background and gameplay renderers");
        Require(Object.FindObjectsByType<ScreenVisualController>(FindObjectsSortMode.None).Length == 1, "One scene owner");
        ScreenVisualProfile originalProfile = c.profile;
        c.profile = Object.Instantiate(originalProfile); // Never modify the saved profile while testing.
        var p = c.profile;
        Camera camera = Camera.main;
        var follow = camera.GetComponent<CameraFollow>();
        var intro = Object.FindAnyObjectByType<PlayerStartIntro>();
        double wait = Time.realtimeSinceStartupAsDouble + 8;
        while (intro != null && intro.IsPlaying && Time.realtimeSinceStartupAsDouble < wait) yield return null;
        Time.timeScale = 0;
        if (follow != null) follow.enabled = false;
        foreach (var canv in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (canv.name == "StageIntroCanvas") canv.gameObject.SetActive(false);
        var backgrounds = Object.FindObjectsByType<ParallaxBackgroundLayer>(FindObjectsSortMode.None);
        var savedCamera = camera.transform.position;
        var renderers = backgrounds.Select(x => x.GetComponent<SpriteRenderer>()).ToArray();
        c.visualsEnabled = false; c.ApplyNow();
        yield return null; yield return null;
        var originals = renderers.Select(x => x.sharedMaterial).ToArray();
        ScreenCapture.CaptureScreenshot(Path.Combine(Output, "off.png"));
        for (int i = 0; i < 3; i++) yield return null;
        c.visualsEnabled = true; c.ApplyNow();
        Require(renderers.All(r => r.sharedMaterial.GetFloat("_SV_Active") == 1), "All five visual groups enabled");
        yield return null; yield return null;
        var dust = c.GetComponentInChildren<ParticleSystem>();
        Require(dust != null, "Generated dust atlas wired");
        dust.Simulate(5, true, false);
        Require(dust.particleCount > 0 && dust.particleCount <= 96, "Dust emitted with bounded count");
        ScreenCapture.CaptureScreenshot(Path.Combine(Output, "on.png"));
        yield return null; yield return null;
        p.vignette.enabled = false; c.ApplyNow();
        Require(!c.GetComponentsInChildren<Canvas>(true).Single().enabled, "Vignette toggle");
        p.vignette.enabled = true;
        p.blur.backgroundBlurEnabled = false; c.ApplyNow();
        Require(renderers.All(r => r.sharedMaterial.GetVector("_SV_Blur").x == 0), "Background blur toggle");
        p.blur.backgroundBlurEnabled = true; p.blur.peripheralBlurEnabled = false; c.ApplyNow();
        Require(renderers.All(r => r.sharedMaterial.GetVector("_SV_Blur").z == 0), "Peripheral blur toggle");
        p.blur.peripheralBlurEnabled = true; p.atmosphere.atmosphereEnabled = false; c.ApplyNow();
        Require(renderers.All(r => r.sharedMaterial.GetVector("_SV_Tone").z == 0), "Aerial perspective toggle");
        p.atmosphere.atmosphereEnabled = true; p.atmosphere.fogEnabled = false; c.ApplyNow();
        Require(renderers.All(r => r.sharedMaterial.GetVector("_SV_Fog").x == 0), "Fog toggle");
        p.atmosphere.fogEnabled = true; p.atmosphere.particleEnabled = false; c.ApplyNow();
        Require(dust.particleCount == 0 && !dust.isPlaying, "Particle toggle clears live particles");
        p.atmosphere.particleEnabled = true; p.lighting.enabled = false; c.ApplyNow();
        Require(Shader.GetGlobalFloat("_SV_LightingEnabled") == 0, "Ambient/local lighting toggle");
        p.lighting.enabled = true;
        p.quality = ScreenVisualProfile.Quality.Low; c.ApplyNow();
        Require(renderers.All(r => r.sharedMaterial.GetVector("_SV_Blur").x == 0 && r.sharedMaterial.GetVector("_SV_Blur").z == 0), "Low quality skips blur");
        p.quality = ScreenVisualProfile.Quality.Medium; c.ApplyNow();
        p.quality = ScreenVisualProfile.Quality.High;
        // A fixture light makes local illumination and target masks measurable at the initial camera.
        var lamp = new GameObject("ScreenVisualFixtureLight");
        lamp.transform.position = camera.transform.position + Vector3.forward * 10;
        var light = lamp.AddComponent<ScreenLocalLight2D>();
        light.targetLayers = LayerMask.GetMask("player");
        c.ApplyNow();
        Require(c.ActiveLightCount > 0, "Nearby local light selected");
        Require(Shader.GetGlobalVectorArray("_SV_LightMasks").Any(m => m.x == light.targetLayers.value), "Light target mask uploaded");
        light.lightEnabled = false; c.ApplyNow();
        Object.Destroy(lamp);
        var goal = Object.FindAnyObjectByType<GoalPoint>();
        Vector3 crystalCenter = goal.GetComponentInChildren<SpriteRenderer>().bounds.center;
        camera.transform.position = new Vector3(crystalCenter.x, crystalCenter.y, savedCamera.z);
        c.ApplyNow(); yield return null; yield return null;
        Require(c.ActiveLightCount > 0, "Existing crystal has a surface light");
        ScreenCapture.CaptureScreenshot(Path.Combine(Output, "crystal.png"));
        yield return null; yield return null;
        // Large positive/negative camera travel crosses several mirrored background tiles.
        foreach (float x in new[] { -400f, 0, 400f, 1200f })
        {
            camera.transform.position = new Vector3(x, savedCamera.y, savedCamera.z);
            yield return null; yield return null;
            foreach (var bg in backgrounds.Where(b => b.LoopsHorizontally))
            {
                var source = bg.GetComponent<SpriteRenderer>();
                float w = source.bounds.size.x;
                var copies = bg.GetComponentsInChildren<SpriteRenderer>();
                Require(copies.Length == 3, "Parallax reuses exactly three tiles at x=" + x);
                var sorted = copies.OrderBy(r => r.bounds.center.x).ToArray();
                Require(Mathf.Abs(sorted[0].bounds.max.x - sorted[1].bounds.min.x) < 0.01f && Mathf.Abs(sorted[1].bounds.max.x - sorted[2].bounds.min.x) < 0.01f, "Loop bounds meet without gaps");
                Require(Mathf.Abs(source.bounds.center.x - x) <= w * 0.51f, "Wrapped center remains near camera");
            }
        }
        camera.transform.position = savedCamera;
        c.visualsEnabled = false; c.ApplyNow(); yield return null;
        Require(renderers.Select((r, i) => r.sharedMaterial == originals[i]).All(x => x), "Master OFF restores original material references");
        Require(Shader.GetGlobalFloat("_SV_LightingEnabled") == 0 && dust.particleCount == 0, "Master OFF resets lighting and particles");
        c.visualsEnabled = true; c.ApplyNow();
        c.enabled = false; yield return null;
        Require(renderers.Select((r, i) => r.sharedMaterial == originals[i]).All(x => x), "Component disable restores originals");
        c.enabled = true; c.ApplyNow();
        var hp = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None).First(x => x.name == "HpCanvas");
        Require(hp.sortingOrder > c.GetComponentsInChildren<Canvas>().Single().sortingOrder, "HP and menus draw above vignette");
        Require(hp.GetComponentInChildren<UnityEngine.UI.Graphic>(true) != null, "HP UI remains present");
        Require(camera.GetComponent<PixelPerfectCamera>() != null, "Pixel Perfect Camera retained");
        foreach (var bg in renderers) Require(bg.sprite.texture.filterMode == FilterMode.Point, "Background retains Point filter");
        // Camera regeneration: use the same projection and tag, then remove the replacement fixture.
        camera.tag = "Untagged"; camera.gameObject.SetActive(false);
        var replacementGO = new GameObject("ScreenVisualReplacementCamera");
        replacementGO.tag = "MainCamera";
        var replacement = replacementGO.AddComponent<Camera>(); replacement.CopyFrom(camera);
        replacement.transform.position = savedCamera;
        yield return null; yield return null;
        Require(c.TargetCamera == replacement, "Controller reconnects a regenerated camera");
        replacementGO.tag = "Untagged"; Object.Destroy(replacementGO);
        camera.gameObject.SetActive(true); camera.tag = "MainCamera";
        yield return null; yield return null;
        c.visualsEnabled = false; c.ApplyNow();
        yield return Benchmark("OFF", 120);
        c.visualsEnabled = true; c.ApplyNow();
        dust.Simulate(8, true, true);
        Report.AppendLine("ON benchmark live particles=" + dust.particleCount);
        yield return Benchmark("ON", 120);
        // Null profile and empty layers use the safe default.
        c.profile = null; c.ApplyNow(); yield return null;
        Require(c.Settings != null, "Missing profile is safe");
        c.profile = p;
        CheckSurfaceLighting(c, p);
        c.profile = originalProfile; c.ApplyNow();
        RenderTexture oldTarget = camera.targetTexture;
        float oldAspect = camera.aspect;
        foreach (Vector2Int size in new[] { new Vector2Int(960, 540), new Vector2Int(1024, 768), new Vector2Int(640, 960) })
        {
            var target = new RenderTexture(size.x, size.y, 0) { filterMode = FilterMode.Point };
            target.Create(); camera.targetTexture = target;
            camera.aspect = (float)size.x / size.y;
            yield return null; yield return null;
            c.ApplyNow(); camera.Render();
            Require(renderers.All(r => r.bounds.size.x >= camera.orthographicSize * 2 * camera.aspect - 0.01f), "Background covers aspect " + size);
            camera.targetTexture = oldTarget; target.Release(); Object.Destroy(target);
        }
        camera.aspect = oldAspect;
        yield return null;
        // The driver survives these real scene loads; all presentation objects must stay scene-owned.
        Time.timeScale = 1;
        yield return SceneManager.LoadSceneAsync("TitleScene");
        yield return null; yield return null;
        Require(Object.FindObjectsByType<ScreenVisualController>(FindObjectsSortMode.None).Length == 0, "Gameplay controller removed on Title transition");
        Require(Shader.GetGlobalFloat("_SV_LightingEnabled") == 0, "Lighting globals reset after scene unload");
        Require(Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None).All(x => x.name != "Screen Visual Ambient Dust"), "Ambient particles removed on scene unload");
        yield return SceneManager.LoadSceneAsync("CompletScene");
        yield return null; yield return null;
        var fresh = Object.FindObjectsByType<ScreenVisualController>(FindObjectsSortMode.None);
        Require(fresh.Length == 1 && fresh[0].profile == originalProfile, "Stage reload applies the serialized profile once");
        Require(errors == 0, "No runtime Error/Exception");
        Report.AppendLine("Frame comparison uses the same paused scene, VSync and resolution. GPU timing is unavailable without a platform GPU profiler.");
        Object.Destroy(p);
    }

    private static IEnumerator Benchmark(string name, int frames)
    {
        for (int i = 0; i < 15; i++) yield return null;
        double start = Time.realtimeSinceStartupAsDouble;
        var samples = new List<float>(frames);
        long allocation = 0;
        int draws = 0;
        using (var gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1))
        for (int i = 0; i < frames; i++)
        {
            yield return null; samples.Add(Time.unscaledDeltaTime * 1000);
            allocation += gc.Valid ? gc.LastValue : 0;
            draws += UnityStats.drawCalls;
        }
        double seconds = Time.realtimeSinceStartupAsDouble - start;
        samples.Sort();
        long mono = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
        Report.AppendLine(name + ": frames=" + frames + " fps=" + (frames / seconds).ToString("F1") + " medianMs=" + samples[frames / 2].ToString("F2") + " p95Ms=" + samples[(int)(frames * 0.95)].ToString("F2") + " monoMB=" + (mono / 1048576f).ToString("F1") + " drawCallsAvg=" + (draws / (float)frames).ToString("F1") + " wholeEditorGcBytesAvg=" + (allocation / frames));
    }

    private static void CheckSurfaceLighting(ScreenVisualController controller, ScreenVisualProfile p)
    {
        var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        texture.SetPixel(0, 0, new Color(0.25f, 0.25f, 0.25f, 1)); texture.Apply();
        var sprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.one * 0.5f, 1);
        var original = new Material(Shader.Find("Sprites/Default"));
        var sample = new GameObject("SurfaceLightFixture") { layer = 31 };
        sample.transform.position = new Vector3(5000, 5000, 0);
        var renderer = sample.AddComponent<SpriteRenderer>(); renderer.sprite = sprite; renderer.sharedMaterial = original;
        var cameraGO = new GameObject("SurfaceLightFixtureCamera");
        cameraGO.transform.position = new Vector3(5000, 5000, -10);
        var camera = cameraGO.AddComponent<Camera>();
        camera.enabled = false; camera.orthographic = true; camera.orthographicSize = 1;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; camera.cullingMask = 1 << 31;
        var target = new RenderTexture(16, 16, 0, RenderTextureFormat.ARGB32); target.Create(); camera.targetTexture = target;
        var readback = new Texture2D(1, 1, TextureFormat.RGB24, false);
        var emitter = new GameObject("SurfaceLightFixtureEmitter"); emitter.transform.position = sample.transform.position;
        var light = emitter.AddComponent<ScreenLocalLight2D>();
        light.radius = 10000; light.intensity = 0.5f; light.color = Color.white;
        light.targetLayers = 1 << 31; light.lightEnabled = false;
        controller.RefreshBindings();
        Color oldAmbient = p.lighting.ambientColor;
        float oldIntensity = p.lighting.ambientIntensity;
        p.lighting.ambientColor = Color.white; p.lighting.ambientIntensity = 1;
        controller.ApplyNow(); float baseline = ReadSurface(camera, readback);
        light.lightEnabled = true; controller.ApplyNow(); float lit = ReadSurface(camera, readback);
        Require(lit - baseline > 0.04f, "Local light changes actual Sprite pixel luminance");
        light.targetLayers = 0; controller.ApplyNow(); float excluded = ReadSurface(camera, readback);
        Require(Mathf.Abs(excluded - baseline) < 0.015f, "Excluded GameObject layer receives no local light");
        p.lighting.ambientIntensity = 0.75f; controller.ApplyNow(); float dim = ReadSurface(camera, readback);
        Require(baseline - dim > 0.02f, "Ambient intensity changes actual Sprite pixel luminance");
        Report.AppendLine("Surface sample baseline=" + baseline.ToString("F3") + " local=" + lit.ToString("F3") + " excluded=" + excluded.ToString("F3") + " ambient75=" + dim.ToString("F3"));
        p.lighting.ambientColor = oldAmbient; p.lighting.ambientIntensity = oldIntensity;
        camera.targetTexture = null; target.Release();
        Object.Destroy(sample); Object.Destroy(emitter); Object.Destroy(cameraGO);
        Object.Destroy(original); Object.Destroy(sprite); Object.Destroy(texture); Object.Destroy(target); Object.Destroy(readback);
    }
    private static float ReadSurface(Camera camera, Texture2D texture)
    {
        RenderTexture previous = RenderTexture.active;
        camera.Render(); RenderTexture.active = camera.targetTexture;
        texture.ReadPixels(new Rect(8, 8, 1, 1), 0, 0); texture.Apply();
        float luminance = texture.GetPixel(0, 0).grayscale;
        RenderTexture.active = previous;
        return luminance;
    }
}

public sealed class ScreenVisualCheckDriver : MonoBehaviour
{
    private IEnumerator Start()
    {
        DontDestroyOnLoad(gameObject);
        // Catch nested iterator exceptions as well as the outer sequence.
        var stack = new Stack<IEnumerator>(); stack.Push(ScreenVisualChecks.Check());
        while (stack.Count > 0)
        {
            object next = null;
            try
            {
                IEnumerator routine = stack.Peek();
                if (!routine.MoveNext()) { stack.Pop(); continue; }
                next = routine.Current;
                if (next is IEnumerator nested) { stack.Push(nested); continue; }
            }
            catch (Exception e) { ScreenVisualChecks.Finish(false, e.ToString()); yield break; }
            yield return next;
        }
        ScreenVisualChecks.Finish(true, "Completed");
    }
}
