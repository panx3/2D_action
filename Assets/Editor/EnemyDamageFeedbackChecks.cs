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
    private static EnemyHealth normal, mantis;
    private static EnemyMiniHealthGauge normalDefeat, bossDefeat;
    private static AsyncOperation unloading;
    private static SpriteRenderer unaffected;
    private static Material unaffectedMaterial;
    private static GameObject defeat;
    private static readonly HashSet<Sprite> criticalFrames = new HashSet<Sprite>();
    private static float started, firstHitAt, rapidHitAt, secondHitAt, deathAt;
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
        foreach (string field in new[] { "criticalEffectEnabled", "hitEffectEnabled", "defeatEffectEnabled", "flashEnabled" })
            Set(hp1Feedback, field, false);
        var other = Enemy("UnaffectedEnemy", 2100f, 3);
        other.GetComponent<EnemyDamageFeedback>().enabled = false;
        unaffected = other.transform.Find("Visual").GetComponent<SpriteRenderer>(); unaffectedMaterial = unaffected.sharedMaterial;

        var plain = new GameObject("NormalHP5");
        SceneManager.MoveGameObjectToScene(plain, scene);
        plain.SetActive(false);
        plain.transform.position = new Vector3(2004.5f, 0.1f, 0f);
        var rat = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemy.prefab");
        plain.AddComponent<SpriteRenderer>().sprite = rat.transform.Find("Visual").GetComponent<SpriteRenderer>().sprite;
        normal = plain.AddComponent<EnemyHealth>();
        Set(normal, "maxHp", 5);
        Set(normal, "destroyOnDeath", false);
        Set(normal, "hitInvincibleDuration", 0.01f);
        plain.SetActive(true);

        var mantisRoot = CloneInactive("Assets/Prefabs/Enemies/SlashMantis.prefab", "Mantis", 2120f);
        mantisRoot.GetComponent<SlashMantis>().enabled = false;
        mantis = mantisRoot.GetComponent<EnemyHealth>();
        mantisRoot.SetActive(true);
    }
    private static EnemyHealth Enemy(string name, float x, int hp, bool extraRenderer = false)
    {
        var root = CloneInactive(BeamScorpionSetup.PrefabPath, name, x);
        root.GetComponent<BeamScorpionController>().enabled = false;
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
    private static GameObject CloneInactive(string path, string name, float x)
    {
        var container = new GameObject("InactiveSpawn");
        SceneManager.MoveGameObjectToScene(container, scene);
        container.SetActive(false);
        var root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path), container.transform);
        root.SetActive(false);
        root.transform.SetParent(null, true);
        UnityEngine.Object.Destroy(container);
        root.name = name;
        root.transform.position = new Vector3(x, 0.1f, 0f);
        var body = root.GetComponent<Rigidbody2D>();
        body.gravityScale = 0f;
        body.constraints = RigidbodyConstraints2D.FreezeAll;
        return root;
    }
    private static void Hit(EnemyHealth health, int damage = 24) => health.OnMorningStarHit(
        new MorningStarHitContext(damage, Vector2.zero, 0f, (Vector2)health.transform.position + new Vector2(-0.4f, 1.2f), Vector2.right, 20f, 2f));
    private static EnemyMiniHealthGauge Gauge(EnemyDamageFeedback owner) => (EnemyMiniHealthGauge)Get(owner, "gauge");
    private static EnemyDamageFeedback View(EnemyHealth health) => health.GetComponent<EnemyDamageFeedback>();
    private static SpriteRenderer Layer(EnemyMiniHealthGauge gauge, string name) =>
        gauge.transform.Find("Artwork/" + name).GetComponent<SpriteRenderer>();

    private sealed class BlockDamage : IEnemyMorningStarDamageModifier
    {
        public int ModifyMorningStarDamage(MorningStarHitContext context, int damage, int currentHp) => 0;
    }
    private sealed class DeferredDeathGuard : IEnemyDeathGuard
    {
        bool IEnemyDeathGuard.DeferDeath => true;
    }

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
                Check(!View(normal).GaugeVisible && !View(mantis).GaugeVisible, "Regular enemy and mantis start with hidden gauges.");
                Hit(main); firstHitAt = Time.time;
                Check(main.CurrentHp == 2 && acceptedHits == 1, "Charged damage24 is capped to one HP.");
                Check(feedback.GaugeVisible && feedback.FlashActive && !feedback.CriticalActive, "First hit shows gauge and flash only.");
                int hitCount = Roots("Hit").Length;
                Hit(main);
                Check(main.CurrentHp == 2 && acceptedHits == 1 && Roots("Hit").Length == hitCount, "Rejected invincible contact does not repeat feedback.");
                Check(Roots("Hit").Any(go => Vector2.Distance(go.transform.position, (Vector2)main.transform.position + new Vector2(-0.4f, 1.2f)) < 0.01f),
                    "Hit VFX spawns at the accepted impact point.");
                foreach (var health in reusable) Hit(health, 1);
                mantis.TakeExplosionDamage(1, Vector2.zero, mantis.transform.position);
                Hit(hp1, 1);
                Check(hp1.CurrentHp == 0 && Gauge(View(hp1)) == null, "Max HP1 cannot create a gauge even with presentation enabled.");
                Set(normal, "_morningStarDamageModifier", new BlockDamage());
                Hit(normal, 1);
                Check(normal.CurrentHp == 5 && !View(normal).GaugeVisible, "A confirmed contact for zero damage leaves the gauge hidden.");
                Set(normal, "_morningStarDamageModifier", null);
                phase = 1;
            }
            if (phase == 1 && Time.time - firstHitAt > 0.14f)
            {
                CheckBar(Gauge(feedback), 2f / 3f);
                CheckBar(Gauge(View(mantis)), mantis.CurrentHp / (float)mantis.MaxHp);
                Check(mantis.CurrentHp == mantis.MaxHp - 1 &&
                    Layer(Gauge(View(mantis)), "Frame").sprite.name == "GaugeMinibossFrame", "Mantis uses the same miniboss style as scorpion.");
                foreach (var health in reusable) CheckBar(Gauge(View(health)), health.CurrentHp / (float)health.MaxHp);
                var reset = reusable[0];
                reset.ResetHealth();
                Check(reset.CurrentHp == 2 && !View(reset).GaugeVisible && !View(reset).CriticalActive && !View(reset).FlashActive,
                    "ResetHealth removes gauge, flash and critical loop.");
                Check(!feedback.FlashActive && ((SpriteRenderer[])Get(feedback, "overlays")).All(r => !r.enabled), "Body flash ends after 0.1 seconds.");
                Check(mainRenderers.Select((r, i) => r.sharedMaterial == originalMaterials[i]).All(value => value)
                    && unaffected.sharedMaterial == unaffectedMaterial && unaffected.color == Color.white, "Source/shared materials and other enemies remain untouched.");
                Hit(normal, 1);
                rapidHitAt = Time.time;
                Check(normal.CurrentHp == 4 && View(normal).GaugeVisible &&
                    Layer(Gauge(View(normal)), "Frame").sprite.name == "GaugeFrame", "Automatic presentation uses the regular style.");
                phase = 2;
            }
            if (phase == 2 && Time.time - rapidHitAt > 0.055f)
            {
                var view = Gauge(View(normal));
                float before = FillRatio(view, "Fill");
                Hit(normal, 1);
                Check(normal.CurrentHp == 3 && Gauge(View(normal)) == view &&
                    FillRatio(view, "Fill") <= before + 0.001f, "A second hit updates the same gauge without reversing the draining bar.");
                Check(FillRatio(view, "DamageTrail") > FillRatio(view, "Fill"), "Damage trail keeps the previous health above the red bar.");
                Check(view.GetComponentsInChildren<SpriteRenderer>().Any(r => r.name == "Spark" && r.enabled), "Damage emits pixel sparks.");
                normal.transform.SetPositionAndRotation(normal.transform.position + Vector3.up * 0.35f, Quaternion.Euler(0f, 0f, 32f));
                normal.transform.localScale = new Vector3(-1f, 1f, 1f);
                phase = 3;
            }
            if (phase == 3 && Time.time - firstHitAt > 0.8f)
            {
                var view = Gauge(View(normal));
                CheckBar(view, 3f / 5f);
                Check(Mathf.Abs(FillRatio(view, "DamageTrail") - 3f / 5f) < 0.001f &&
                    !Layer(view, "FrameFlash").enabled, "Trail catches up and flash clears on successive hits.");
                var artwork = view.transform.Find("Artwork");
                Check(artwork.localScale == Vector3.one && artwork.localPosition == Vector3.zero &&
                    view.transform.rotation == Quaternion.identity && view.transform.lossyScale.x > 0f,
                    "Shake and pop reset and the gauge does not inherit rotation or reflection.");
                var expected = new Vector3(normal.transform.position.x,
                    normal.GetComponent<SpriteRenderer>().bounds.max.y + 0.36f, normal.transform.position.z);
                Check(Vector3.Distance(view.transform.position, expected) < 0.001f && view.gameObject.scene == scene, "Gauge follows the moved and elevated enemy.");
                normal.transform.rotation = Quaternion.identity;
                Hit(main);
                Hit(normal, 2);
                secondHitAt = Time.time;
                Check(main.CurrentHp == 1 && normal.CurrentHp == 1 && acceptedHits == 2 && feedback.CriticalActive, "Remaining HP1 stays eligible.");
                phase = 4;
            }
            if (phase == 4 && Time.time - secondHitAt > 0.13f)
            {
                CheckBar(Gauge(feedback), 1f / 3f);
                CheckBar(Gauge(View(normal)), 1f / 5f);
                Check(Layer(Gauge(feedback), "Frame").sprite.name == "GaugeMinibossFrame", "Scorpion uses the miniboss frame.");
                Snapshot("EnemyHealthGauge-CRT.png");
                phase = 5;
            }
            if (phase == 5 && Time.time - firstHitAt > 3.28f)
            {
                Check(feedback.GaugeVisible && View(normal).GaugeVisible && feedback.CriticalActive && criticalFrames.Count >= 3,
                    "A new hit extends the gauge timer while critical VFX continues.");
                phase = 6;
            }
            if (phase == 6 && Time.time - secondHitAt > 3.08f)
            {
                float alpha = Layer(Gauge(feedback), "Frame").color.a;
                Check(alpha > 0f && alpha < 1f, "After three seconds the gauge fades before hiding.");
                phase = 7;
            }
            if (phase == 7 && Time.time - secondHitAt > 3.25f)
            {
                Check(!feedback.GaugeVisible && !View(normal).GaugeVisible && feedback.CriticalActive, "HUD hides while critical effects persist.");
                Set(main, "_deathGuard", new DeferredDeathGuard());
                Hit(main);
                Check(main.CurrentHp == 0 && main.IsDeathPending && feedback.GaugeVisible && !Gauge(feedback).IsDefeating,
                    "A deferred defeat starts the HP drain without shattering.");
                deathAt = Time.time;
                phase = 8;
            }
            if (phase == 8 && Time.time - deathAt > 0.13f)
            {
                Check(!Layer(Gauge(feedback), "Fill").enabled, "Deferred defeat shows the real empty bar.");
                var previousDefeatCount = Roots("Defeat").Length;
                main.CompleteDeferredDeath();
                bossDefeat = Gauge(feedback);
                Hit(normal, 1);
                normalDefeat = Gauge(View(normal));
                deathAt = Time.time;
                Check(main.IsDeathHandled && acceptedHits == 4 && bossDefeat.IsDefeating && normalDefeat.IsDefeating,
                    "Deferred and direct lethal hits start their independent defeat timelines.");
                Check(!normal.gameObject.activeSelf && !feedback.CriticalActive, "Enemy deactivation and critical cleanup are immediate.");
                defeat = Roots("Defeat").Single();
                Check(defeat.transform.parent == null && defeat.GetComponent<EnemySpriteEffect>() != null, "Defeat animation is detached before enemy destruction.");
                main.CompleteDeferredDeath();
                Hit(main);
                Check(Gauge(feedback) == bossDefeat && Roots("Defeat").Length == previousDefeatCount + 1, "Repeated lethal callbacks cannot duplicate defeat effects.");
                phase = 9;
            }
            if (phase == 9 && Time.time - deathAt > 0.206f)
            {
                Check(main == null && defeat != null && bossDefeat != null && normalDefeat != null &&
                    bossDefeat.transform.parent == null && normalDefeat.transform.parent == null,
                    "Both gauges outlive their destroyed/deactivated enemies.");
                Check(FragmentCount(normalDefeat) == 10 && FragmentCount(bossDefeat) == 16 &&
                    !normalDefeat.transform.Find("Artwork").gameObject.activeSelf && !bossDefeat.transform.Find("Artwork").gameObject.activeSelf,
                    "The metal frame breaks into the configured pieces.");
                Snapshot("EnemyHealthGauge-Shatter.png");
                phase = 10;
            }
            if (phase == 10 && Time.time - deathAt > 0.33f && normalDefeat == null)
            {
                Check(bossDefeat != null || Time.time - deathAt >= 0.5f, "Normal defeat finishes before the miniboss defeat.");
                normal.ResetHealth();
                normal.gameObject.SetActive(true);
                Check(normal.CurrentHp == normal.MaxHp && !View(normal).GaugeVisible, "Reactivated enemy starts with full HP and hidden gauge.");
                phase = 11;
            }
            if (phase == 11 && Time.time - deathAt > 0.75f)
            {
                Check(bossDefeat == null && defeat == null && Roots("Hit").Length == 0, "One-shot VFX and detached gauges clean themselves up.");
                Hit(normal, normal.MaxHp);
                normalDefeat = Gauge(View(normal));
                Check(normalDefeat != null && normalDefeat.gameObject.scene == scene, "Defeat effect belongs to the enemy's scene.");
                unloading = SceneManager.UnloadSceneAsync(scene);
                phase = 12;
            }
            if (phase == 12 && unloading.isDone)
            {
                Check(normalDefeat == null, "Scene unload removes gauges even before their defeat timer ends.");
                Check(errors == 0, "No runtime Error/Exception.");
                Finish("PASS (" + count + "), runtimeErrors=" + errors);
            }
            if (elapsed > 12f) Finish("FAIL timeout at phase " + phase);
        }
        catch (Exception e) { Finish("FAIL: " + e.Message); }
    }
    private static void CheckBar(EnemyMiniHealthGauge gauge, float expected)
    {
        var fill = Layer(gauge, "Fill");
        var background = Layer(gauge, "Background");
        Check(Mathf.Abs(FillRatio(gauge, "Fill") - expected) < 0.001f
            && Mathf.Abs(fill.bounds.min.x - background.bounds.min.x) < 0.001f,
            "One continuous bar keeps its left edge and displays the HP ratio.");
    }
    private static float FillRatio(EnemyMiniHealthGauge gauge, string layer) =>
        Layer(gauge, layer).bounds.size.x / Layer(gauge, "Background").bounds.size.x;
    private static int FragmentCount(EnemyMiniHealthGauge gauge) =>
        gauge.GetComponentsInChildren<SpriteRenderer>().Count(renderer => renderer.name == "MetalFragment");
    private static GameObject[] Roots(string prefix) => scene.GetRootGameObjects().Where(go => go.name.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
    private static void Snapshot(string name)
    {
        var cameraObject = new GameObject("FeedbackCheckCamera"); SceneManager.MoveGameObjectToScene(cameraObject, scene);
        var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.scene = scene;
        camera.transform.position = new Vector3(2002.25f, 1.6f, -10f); camera.orthographic = true; camera.orthographicSize = 5f;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.055f, 0.045f, 0.075f);
        cameraObject.SetActive(false);
        var crt = cameraObject.AddComponent<CRTFilterController>();
        Set(crt, "targetCamera", camera);
        Set(crt, "crtShader", Shader.Find("Hidden/IronBallGirl/CRTFilter"));
        Set(crt, "includeOverlayCanvases", false);
        cameraObject.SetActive(true);
        Check(cameraObject.GetComponents<MonoBehaviour>().Any(component => component.GetType().Name == "CRTFilterRenderer" &&
            component.isActiveAndEnabled), "Play Mode capture uses the existing CRT renderer.");
        var rt = new RenderTexture(640, 360, 24); var previous = RenderTexture.active; Texture2D image = null;
        try
        {
            camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
            image = new Texture2D(640, 360, TextureFormat.RGBA32, false); image.ReadPixels(new Rect(0f, 0f, 640, 360), 0, 0); image.Apply();
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
