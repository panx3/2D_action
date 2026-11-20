using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Opt-in check of real ball collisions, three-hit gating, pause, and bridge support.</summary>
[InitializeOnLoad]
public static class FallingPillarBridgeChecks
{
    private const string Key = "Tekkyu.PillarBridgeChecks";
    private static IEnumerator routine;
    private static double deadline;
    private static bool previousBackground;
    private static int errors;

    static FallingPillarBridgeChecks()
    {
        if (SessionState.GetBool(Key, false)) Subscribe();
    }

    [MenuItem("Tools/Tekkyu/Stage2/Verify Falling Pillar Bridge")]
    public static void Run()
    {
        var scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlaying || scene.path != "Assets/Scenes/Stage2.unity"
            || EditorUtility.scriptCompilationFailed)
            throw new InvalidOperationException("Open Stage2 in Edit Mode with successful compilation first.");
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
            previousBackground = Application.runInBackground;
            Application.runInBackground = true;
            errors = 0;
            Application.logMessageReceived += CountError;
            deadline = EditorApplication.timeSinceStartup + 35d;
            routine = Check();
            EditorApplication.update += Tick;
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= CountError;
            EditorApplication.playModeStateChanged -= StateChanged;
            SessionState.SetBool(Key, false);
        }
    }

    private static void CountError(string message, string trace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors++;
    }

    private static void Tick()
    {
        try
        {
            Require(EditorApplication.timeSinceStartup < deadline, "Check timed out.");
            if (routine.MoveNext()) return;
            Require(errors == 0, "Runtime Errors/Exceptions=" + errors);
            Finish("PASS: three real hits; right-edge pivot; shaped colliders; reduced stump overlap; pause; bridge support; runtime errors=0.");
        }
        catch (Exception e) { Finish("FAIL: " + e.Message); }
    }

    private static IEnumerator Check()
    {
        var launcher = Object.FindAnyObjectByType<MorningStarLauncher>();
        var pillar = Object.FindAnyObjectByType<FallingPillarBridge>();
        Require(launcher != null && pillar != null, "Stage2 actors found.");
        var footing = pillar.RemainingBase;
        Require(footing != null && !footing.activeSelf, "The intact sprite contains the original footing.");
        Vector3 footingPosition = footing.transform.position;
        float columnX = pillar.transform.position.x;
        Require(pillar.GetComponent<PolygonCollider2D>().GetTotalPointCount() > 8,
            "Standing collision follows the shaft, cap and stepped footing.");
        Vector2 pivot = pillar.FallPivot;
        Require(pivot.x > pillar.transform.position.x + 1f && pivot.y < pillar.transform.position.y,
            "Pivot is at the right edge of the fracture.");
        double introEnd = EditorApplication.timeSinceStartup + 10d;
        while ((Time.timeScale == 0f || launcher.State == MorningStarLauncher.MorningStarState.StartIntro)
            && EditorApplication.timeSinceStartup < introEnd) yield return null;
        Require(Time.timeScale > 0f, "Intro completed.");

        var weak = new MorningStarHitContext(100, Vector2.zero, 0f, pillar.transform.position, Vector2.right, 0.5f, 3f, launcher);
        pillar.OnMorningStarHit(weak);
        Require(pillar.HitCount == 0, "Weak contact ignored even with high damage.");
        var player = launcher.GetComponent<Player>();
        player.enabled = false;
        var playerBody = player.GetComponent<Rigidbody2D>();
        playerBody.constraints = RigidbodyConstraints2D.FreezeAll;
        playerBody.position = new Vector2(columnX - 3.5f, footingPosition.y + 2.04f);
        playerBody.linearVelocity = Vector2.zero;
        MethodInfo fire = typeof(MorningStarLauncher).GetMethod("FireImmediate", BindingFlags.Instance | BindingFlags.NonPublic);
        Require(fire != null, "Existing fire entry point found.");
        for (int hit = 1; hit <= 3; hit++)
        {
            launcher.ResetForRespawn();
            fire.Invoke(launcher, new object[] { Vector2.right });
            double until = EditorApplication.timeSinceStartup + 2d;
            while (pillar.HitCount < hit && EditorApplication.timeSinceStartup < until) yield return null;
            Require(pillar.HitCount == hit, "Actual ball collision #" + hit + " (count=" + pillar.HitCount + ").");
            pillar.OnMorningStarHit(new MorningStarHitContext(100, Vector2.zero, 0f, pillar.transform.position,
                Vector2.right, 20f, 3f, launcher));
            Require(pillar.HitCount == hit, "Same-frame duplicate ignored.");
            if (hit < 3)
            {
                Require(!pillar.IsFalling && pillar.GetComponent<Collider2D>().enabled, "Still standing before third hit.");
                float wait = Time.time + 0.5f;
                while (Time.time < wait) yield return null;
            }
        }
        Require(pillar.IsFalling && !pillar.GetComponent<Collider2D>().enabled, "Third hit starts safe collapse.");
        float angle = pillar.GetComponent<Rigidbody2D>().rotation;
        Time.timeScale = 0f;
        double pauseUntil = EditorApplication.timeSinceStartup + 0.2d;
        while (EditorApplication.timeSinceStartup < pauseUntil) yield return null;
        Require(Mathf.Abs(Mathf.DeltaAngle(angle, pillar.GetComponent<Rigidbody2D>().rotation)) < 0.1f, "Pause stops collapse.");
        Time.timeScale = 1f;
        float startFall = Time.time;
        while (Time.time < startFall + 0.42f) yield return null;
        Vector2 movingPivot = pillar.transform.TransformPoint(new Vector2(25f / 24f, -2f / 24f));
        Require(Vector2.Distance(movingPivot, pivot) < 0.04f, "Initial rotation stays around the marked right-edge pivot.");
        while (!pillar.IsBridge) yield return null;
        var solid = pillar.GetComponent<PolygonCollider2D>();
        Require(solid.enabled && pillar.GetComponent<Rigidbody2D>().bodyType == RigidbodyType2D.Static, "Landed bridge is static and solid.");
        Require(footing.activeInHierarchy && Vector3.Distance(footing.transform.position, footingPosition) < 0.01f
            && footing.GetComponent<Collider2D>().enabled, "Fractured footing remains solid in its original position.");
        Physics2D.SyncTransforms();
        var footRenderer = footing.GetComponent<SpriteRenderer>();
        var fallenRenderer = pillar.GetComponentInChildren<SpriteRenderer>();
        Require(fallenRenderer.bounds.min.x >= footRenderer.bounds.max.x - 0.05f,
            "Detached column clears the footing silhouette.");
        float loadX = columnX + 5.5f;
        float surface = Physics2D.Raycast(new Vector2(loadX, footingPosition.y + 12f), Vector2.down, 15f, LayerMask.GetMask("Walls")).point.y;
        Require(Mathf.Abs(solid.bounds.min.y - footingPosition.y) < 0.2f, "Fallen pillar rests on the shore.");
        for (float x = columnX + 3f; x <= columnX + 12.5f; x += 0.5f)
        {
            var hit = Physics2D.Raycast(new Vector2(x, surface + 1f), Vector2.down, 2f, LayerMask.GetMask("Walls"));
            Require(hit.collider == solid && Mathf.Abs(hit.point.y - surface) < 0.05f, "Continuous level bridge at X=" + x);
        }
        var load = new GameObject("__PillarSupportProbe");
        load.transform.position = new Vector3(loadX, surface + 0.95f, 0f);
        load.AddComponent<BoxCollider2D>().size = Vector2.one * 0.5f;
        var rb = load.AddComponent<Rigidbody2D>();
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        float settle = Time.time + 1f;
        while (Time.time < settle) yield return null;
        Require(Mathf.Abs(rb.position.y - surface - 0.26f) < 0.08f && Mathf.Abs(rb.linearVelocity.y) < 0.1f, "Bridge supports dynamic body.");
        pillar.OnMorningStarHit(new MorningStarHitContext(100, Vector2.zero, 0f, pillar.transform.position, Vector2.right, 20f, 3f));
        Require(pillar.IsBridge && pillar.HitCount == 3, "Finished bridge ignores further attacks.");
        Object.Destroy(load);
        CaptureLanding(pillar);
    }

    private static void CaptureLanding(FallingPillarBridge pillar)
    {
        var cameraObject = new GameObject("__PillarVerificationCamera") { hideFlags = HideFlags.HideAndDontSave };
        var camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 5.2f;
        camera.transform.position = new Vector3(pillar.transform.position.x + 4f, pillar.WalkableHeight + 1f, -30f);
        var target = new RenderTexture(1280, 720, 24);
        var oldTarget = RenderTexture.active;
        Texture2D texture = null;
        try
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            texture.Apply();
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pillar-pivot-landed.png"), texture.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = null;
            RenderTexture.active = oldTarget;
            target.Release();
            Object.DestroyImmediate(target);
            if (texture != null) Object.DestroyImmediate(texture);
            Object.DestroyImmediate(cameraObject);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Finish(string result)
    {
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= CountError;
        Time.timeScale = 1f;
        Application.runInBackground = previousBackground;
        SessionState.SetString(Key + ".Result", result);
        Debug.Log("[PillarBridgeChecks] " + result);
        EditorApplication.isPlaying = false;
    }
}
