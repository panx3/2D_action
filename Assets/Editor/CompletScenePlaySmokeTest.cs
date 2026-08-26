using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.U2D;

[InitializeOnLoad]
public static class CompletScenePlaySmokeTest
{
    private const string RunningKey = "CompletScenePlaySmokeTest.Running";
    private static double enteredAt;
    private static bool screenshotRequested;

    static CompletScenePlaySmokeTest()
    {
        if (SessionState.GetBool(RunningKey, false))
            Subscribe();
    }

    public static void Run()
    {
        SessionState.SetBool(RunningKey, true);
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
            screenshotRequested = false;
            Screen.SetResolution(1920, 1080, false);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }
        else if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(RunningKey, false))
        {
            SessionState.SetBool(RunningKey, false);
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            Debug.Log("[CompletPlaySmoke] Play Mode exited normally.");
            EditorApplication.Exit(0);
        }
    }

    private static void Tick()
    {
        double elapsed = EditorApplication.timeSinceStartup - enteredAt;
        if (elapsed < 5d)
            return;

        if (!screenshotRequested)
        {
            screenshotRequested = true;
            CaptureCamera("background-playmode-1920x1080.png", 1920, 1080);
            Debug.Log("[CompletPlaySmoke] Saved 1920x1080 camera capture.");
            return;
        }

        if (elapsed < 7d)
            return;

        EditorApplication.update -= Tick;
        Player player = Object.FindAnyObjectByType<Player>();
        MorningStarLauncher launcher = Object.FindAnyObjectByType<MorningStarLauncher>();
        CameraFollow camera = Object.FindAnyObjectByType<CameraFollow>();
        SegmentHpBarUI hud = Object.FindAnyObjectByType<SegmentHpBarUI>();
        Debug.Log($"[CompletPlaySmoke] player={player != null}, launcher={launcher != null}, camera={camera != null}, hud={hud != null}, timeScale={Time.timeScale}");
        EditorApplication.isPlaying = false;
    }

    private static void CaptureCamera(string fileName, int width, int height)
    {
        Camera camera = Camera.main;
        if (camera == null)
            return;

        RenderTexture previousTarget = camera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;
        PixelPerfectCamera pixelPerfect = camera.GetComponent<PixelPerfectCamera>();
        bool pixelPerfectWasEnabled = pixelPerfect != null && pixelPerfect.enabled;
        RenderTexture renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGB24, false);
        try
        {
            if (pixelPerfect != null)
                pixelPerfect.enabled = false;
            camera.targetTexture = renderTexture;
            camera.Render();
            RenderTexture.active = renderTexture;
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texture.Apply();
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", fileName));
            File.WriteAllBytes(path, texture.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            if (pixelPerfect != null)
                pixelPerfect.enabled = pixelPerfectWasEnabled;
            Object.DestroyImmediate(texture);
            Object.DestroyImmediate(renderTexture);
        }
    }
}
