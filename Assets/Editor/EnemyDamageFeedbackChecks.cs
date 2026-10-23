using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class EnemyDamageFeedbackChecks
{
    private const string Key = "EnemyDamageFeedbackChecks";
    private static Scene scene;
    private static EnemyHealth main;
    private static EnemyDamageFeedback feedback;
    private static SpriteRenderer[] mainRenderers;
    private static Material[] originalMaterials;
    private static EnemyHealth[] reusable;
    private static EnemyHealth hp1;
    private static SpriteRenderer unaffected;
    private static Material unaffectedMaterial;
    private static GameObject defeat;
    private static readonly HashSet<Sprite> criticalFrames = new HashSet<Sprite>();
    private static float started, firstHitAt, secondHitAt, deathAt;
    private static int phase, count, errors, acceptedHits;
    private static bool originalBackground;

    static EnemyDamageFeedbackChecks() { EditorApplication.playModeStateChanged += PlayState; }
    [MenuItem("Tools/Enemies/Damage Feedback/Run Play Mode Checks")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        EnemyDamageFeedbackSetup.Validate();
        SessionState.SetBool(Key, true); SessionState.SetString(Key + ".Result", "Running");
        EditorApplication.isPlaying = true;
    }
    public static string Result() => SessionState.GetString(Key + ".Result", "Not run");
    private static void PlayState(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            phase = count = errors = acceptedHits = 0; criticalFrames.Clear();
            originalBackground = Application.runInBackground; Application.runInBackground = true;
            Application.logMessageReceived += Log;
            try { CreateWorld(); started = Time.time; EditorApplication.update += Tick; }
            catch (Exception e) { Finish("FAIL setup: " + e); }
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
            SessionState.SetBool(Key, false); Debug.Log("[EnemyDamageFeedbackChecks] " + Result());
        }
    }
    private static void CreateWorld()
    {
        scene = SceneManager.CreateScene("EnemyDamageFeedbackValidation");
        main = Enemy("MainDamageEnemy", 2000f, 3, true);
        feedback = main.GetComponent<EnemyDamageFeedback>();
        main.HitConfirmed += (context, lethal) => acceptedHits++;
        mainRenderers = (SpriteRenderer[])Get(feedback, "bodyRenderers");
        originalMaterials = mainRenderers.Select(r => r.sharedMaterial).ToArray();
        reusable = new[] { Enemy("ReusableHP2", 2020f, 2), Enemy("ReusableHP4", 2040f, 4), Enemy("ReusableHP5", 2060f, 5) };
        hp1 = Enemy("OptionalHP1", 2080f, 1);
        var hp1Feedback = hp1.GetComponent<EnemyDamageFeedback>();
        foreach (string field in new[] { "gaugeEnabled", "criticalEffectEnabled", "hitEffectEnabled", "defeatEffectEnabled", "flashEnabled" })
            Set(hp1Feedback, field, false);
        var other = Enemy("UnaffectedEnemy", 2100f, 3);
        other.GetComponent<EnemyDamageFeedback>().enabled = false;
        unaffected = other.transform.Find("Visual").GetComponent<SpriteRenderer>(); unaffectedMaterial = unaffected.sharedMaterial;
    }
    private static EnemyHealth Enemy(string name, float x, int hp, bool extraRenderer = false)
    {
        var container = new GameObject("InactiveSpawn"); SceneManager.MoveGameObjectToScene(container, scene); container.SetActive(false);
        var root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(BeamScorpionSetup.PrefabPath), container.transform);
        root.SetActive(false); root.transform.SetParent(null, true); UnityEngine.Object.Destroy(container);
        root.name = name; root.transform.position = new Vector3(x, 0.1f, 0f);
        root.GetComponent<BeamScorpionController>().enabled = false;
        var body = root.GetComponent<Rigidbody2D>(); body.gravityScale = 0f; body.constraints = RigidbodyConstraints2D.FreezeAll;
        var health = root.GetComponent<EnemyHealth>(); Set(health, "maxHp", hp);
        if (extraRenderer)
        {
            var source = root.transform.Find("Visual").GetComponent<SpriteRenderer>();
            var secondary = new GameObject("SecondaryPart").AddComponent<SpriteRenderer>();
            secondary.transform.SetParent(source.transform, false); secondary.transform.localPosition = new Vector3(0.2f, 0.3f, 0f);
            secondary.transform.localScale = Vector3.one * 0.15f; secondary.sprite = source.sprite;
            secondary.sharedMaterial = source.sharedMaterial; secondary.sortingOrder = source.sortingOrder + 2;
            Set(root.GetComponent<EnemyDamageFeedback>(), "bodyRenderers", new[] { source, secondary });
        }
        root.SetActive(true); return health;
    }
    private static void Hit(EnemyHealth health, int damage = 24) => health.OnMorningStarHit(
        new MorningStarHitContext(damage, Vector2.zero, 0f, (Vector2)health.transform.position + new Vector2(-0.4f, 1.2f), Vector2.right, 20f, 2f));
    private static EnemyMiniHealthGauge Gauge(EnemyDamageFeedback owner) => (EnemyMiniHealthGauge)Get(owner, "gauge");

    private static void Tick()
    {
        try
        {
            float elapsed = Time.time - started;
            if (feedback != null && feedback.CriticalActive)
                foreach (var renderer in ((GameObject)Get(feedback, "critical")).GetComponentsInChildren<SpriteRenderer>())
                    if (renderer.enabled) criticalFrames.Add(renderer.sprite);
            if (phase == 0 && elapsed > 0.15f)
            {
                Check(main.CurrentHp == 3 && !feedback.GaugeVisible && !feedback.CriticalActive, "Full HP starts without gauge or critical VFX.");
                Hit(main); firstHitAt = Time.time;
                Check(main.CurrentHp == 2 && acceptedHits == 1, "Charged damage24 is capped to one HP.");
                Check(feedback.GaugeVisible && feedback.FlashActive && !feedback.CriticalActive, "First hit shows gauge and flash only.");
                CheckBar(Gauge(feedback), 2f / 3f);
                int hitCount = Roots("Hit").Length;
                Hit(main);
                Check(main.CurrentHp == 2 && acceptedHits == 1 && Roots("Hit").Length == hitCount, "Rejected invincible contact does not repeat feedback.");
                Check(Roots("Hit").Any(go => Vector2.Distance(go.transform.position, (Vector2)main.transform.position + new Vector2(-0.4f, 1.2f)) < 0.01f),
                    "Hit VFX spawns at the accepted impact point.");
                foreach (var health in reusable)
                {
                    Hit(health, 1); var view = health.GetComponent<EnemyDamageFeedback>();
                    CheckBar(Gauge(view), (health.MaxHp - 1f) / health.MaxHp);
                }
                var reset = reusable[0]; reset.ResetHealth(); var resetView = reset.GetComponent<EnemyDamageFeedback>();
                Check(reset.CurrentHp == 2 && !resetView.GaugeVisible && !resetView.CriticalActive && !resetView.FlashActive,
                    "ResetHealth removes gauge, flash and critical loop.");
                Hit(hp1, 1);
                Check(hp1.CurrentHp == 0 && !hp1.GetComponent<EnemyDamageFeedback>().GaugeVisible, "HP1 enemy supports disabled presentation.");
                // 非設定のEnemyHealthは従来の速度依存ダメージをそのまま受ける。
                var legacy = new GameObject("LegacyHealth"); SceneManager.MoveGameObjectToScene(legacy, scene);
                legacy.SetActive(false); var oldHealth = legacy.AddComponent<EnemyHealth>(); Set(oldHealth, "maxHp", 10); legacy.SetActive(true);
                Hit(oldHealth, 4); Check(oldHealth.CurrentHp == 6, "Default damage behavior remains unchanged for other enemies.");
                phase = 1;
            }
            if (phase == 1 && Time.time - firstHitAt > 0.03f)
            {
                var overlays = (SpriteRenderer[])Get(feedback, "overlays");
                Check(overlays.Length == 2 && overlays.All(r => r.enabled && r.color == Color.white), "All body renderers flash white.");
                Check(mainRenderers.Select((r, i) => r.sharedMaterial == originalMaterials[i]).All(value => value)
                    && unaffected.sharedMaterial == unaffectedMaterial && unaffected.color == Color.white, "Source/shared materials and other enemies remain untouched.");
                Snapshot("EnemyDamageFeedbackHit.png"); phase = 2;
            }
            if (phase == 2 && Time.time - firstHitAt > 0.7f)
            {
                Check(!feedback.FlashActive && ((SpriteRenderer[])Get(feedback, "overlays")).All(r => !r.enabled), "White flash ends after 0.1 seconds.");
                Hit(main); secondHitAt = Time.time;
                Check(main.CurrentHp == 1 && acceptedHits == 2 && feedback.CriticalActive, "Second accepted hit enters critical HP1.");
                CheckBar(Gauge(feedback), 1f / 3f);
                phase = 3;
            }
            if (phase == 3 && Time.time - secondHitAt > 0.14f)
            {
                Snapshot("EnemyDamageFeedbackCritical.png"); phase = 4;
            }
            if (phase == 4 && Time.time - secondHitAt > 0.95f)
            {
                Check(feedback.GaugeVisible, "A repeated hit extends the original gauge timer.");
                Check(feedback.CriticalActive && criticalFrames.Count >= 3, "Critical spark/smoke animation continues.");
                mainRenderers[0].flipX = true;
                Check(Gauge(feedback).transform.rotation == Quaternion.identity && Gauge(feedback).transform.lossyScale.x > 0f,
                    "Facing changes do not mirror the detached gauge.");
                phase = 5;
            }
            if (phase == 5 && Time.time - secondHitAt > 1.65f)
            {
                Check(!feedback.GaugeVisible && feedback.CriticalActive, "Gauge expires after 1.5s while critical VFX persists.");
                Hit(main); deathAt = Time.time;
                Check(main.CurrentHp == 0 && main.IsDeathHandled && acceptedHits == 3, "Exactly three accepted hits defeat the scorpion.");
                Check(!feedback.GaugeVisible && !feedback.CriticalActive, "Death removes gauge and critical VFX immediately.");
                defeat = Roots("Defeat").Single();
                Check(defeat.transform.parent == null && defeat.GetComponent<EnemySpriteEffect>() != null, "Defeat animation is detached before enemy destruction.");
                Hit(main); Check(Roots("Defeat").Length == 1, "Repeated lethal callbacks do not duplicate destruction VFX.");
                phase = 6;
            }
            if (phase == 6 && Time.time - deathAt > 0.05f)
            {
                Check(main == null && defeat != null, "Destruction VFX outlives the destroyed enemy."); phase = 7;
            }
            if (phase == 7 && Time.time - deathAt > 0.4f)
            {
                var smoke = defeat.GetComponent<SpriteRenderer>();
                Check(smoke.enabled && smoke.color.a > 0f && smoke.color.a < 1f,
                    "Destruction smoke fades before the final frame is removed.");
                phase = 8;
            }
            if (phase == 8 && Time.time - deathAt > 0.75f)
            {
                Check(defeat == null && Roots("Hit").Length == 0, "One-shot VFX clean themselves up.");
                Check(errors == 0, "No runtime Error/Exception.");
                Finish("PASS (" + count + "), runtimeErrors=" + errors);
            }
            if (elapsed > 12f) Finish("FAIL timeout at phase " + phase);
        }
        catch (Exception e) { Finish("FAIL: " + e.Message); }
    }
    private static void CheckBar(EnemyMiniHealthGauge gauge, float expected)
    {
        var fill = gauge.transform.Find("Fill").GetComponent<SpriteRenderer>();
        var background = gauge.transform.Find("Background").GetComponent<SpriteRenderer>();
        Check(gauge.transform.childCount == 3 && Mathf.Abs(fill.bounds.size.x / background.bounds.size.x - expected) < 0.001f
            && Mathf.Abs(fill.bounds.min.x - background.bounds.min.x) < 0.001f,
            "One continuous bar keeps its left edge and displays the HP ratio.");
    }
    private static GameObject[] Roots(string prefix) => scene.GetRootGameObjects().Where(go => go.name.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
    private static void Snapshot(string name)
    {
        var cameraObject = new GameObject("FeedbackCheckCamera"); SceneManager.MoveGameObjectToScene(cameraObject, scene);
        var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.scene = scene;
        camera.transform.position = new Vector3(2000f, 1.65f, -10f); camera.orthographic = true; camera.orthographicSize = 1.9f;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.055f, 0.045f, 0.075f);
        var rt = new RenderTexture(768, 512, 24); var previous = RenderTexture.active; Texture2D image = null;
        try
        {
            camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
            image = new Texture2D(768, 512, TextureFormat.RGBA32, false); image.ReadPixels(new Rect(0f, 0f, 768, 512), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(Path.GetTempPath(), name), image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous; camera.targetTexture = null; rt.Release(); UnityEngine.Object.Destroy(rt);
            if (image != null) UnityEngine.Object.Destroy(image); UnityEngine.Object.Destroy(cameraObject);
        }
    }
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static object Get(object target, string field) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); count++; }
    private static void Log(string message, string trace, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors++; }
    private static void Finish(string result)
    {
        EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
        SessionState.SetString(Key + ".Result", result); Application.runInBackground = originalBackground;
        EditorApplication.isPlaying = false;
    }
}
