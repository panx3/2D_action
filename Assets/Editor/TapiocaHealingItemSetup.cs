using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>確定済みの素材を取り込み、回復アイテムの再利用可能なPrefabを作る。</summary>
public static class TapiocaHealingItemSetup
{
    public const string SpritePath = "Assets/image_/Items/TapiocaMilkTea.png";
    public const string SoundPath = "Assets/Audio/SFX/Items/TapiocaHeal.wav";
    public const string PrefabPath = "Assets/Prefabs/Items/TapiocaMilkTea.prefab";
    public const string EffectPath = "Assets/Prefabs/Items/TapiocaPlayerHeal.prefab";
    public const string DrinkingSoundPath = "Assets/Audio/SFX/Items/eat_07.mp3";
    public const string RecoverySoundPath = "Assets/Audio/SFX/Items/Inspiration03-1_High.wav";
    private const string GeneratedFolder = "Assets/Generated/Tapioca";

    public static void CreateAssets()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
        EnsureFolder("Assets/image_/Items");
        EnsureFolder("Assets/Audio/SFX/Items");
        EnsureFolder("Assets/Prefabs/Items");
        EnsureFolder(GeneratedFolder);
        ConfigureSprite(SpritePath, 28f, FilterMode.Point);
        CreateSound();
        Sprite glow = CreateGlow();
        Material dots = GetMaterial(GeneratedFolder + "/TapiocaDots.mat", null);
        Material soft = GetMaterial(GeneratedFolder + "/TapiocaGlow.mat", glow.texture);
        GameObject effect = AssetDatabase.LoadAssetAtPath<GameObject>(EffectPath);
        if (effect == null) effect = CreatePlayerEffect(dots, soft);
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
            CreatePickup(glow, dots, effect.GetComponent<ParticleSystem>());
        if (File.Exists(DrinkingSoundPath) && File.Exists(RecoverySoundPath)) ConfigureTwoStageRecovery();
        AssetDatabase.SaveAssets();
    }

    public static void ConfigureTwoStageRecovery()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
        AudioClip drinking = ImportPickupAudio(DrinkingSoundPath);
        AudioClip recovery = ImportPickupAudio(RecoverySoundPath);
        Material ringMaterial = GetMaterial(GeneratedFolder + "/RecoveryRing.mat", CreateRecoverySprite(false).texture);
        Material sparkleMaterial = GetMaterial(GeneratedFolder + "/RecoverySparkle.mat", CreateRecoverySprite(true).texture);
        GameObject effect = PrefabUtility.LoadPrefabContents(EffectPath);
        try
        {
            ParticleSystem particles = effect.GetComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.duration = 0.35f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.65f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.13f);
            main.maxParticles = 48;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            var emission = particles.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 12),
                new ParticleSystem.Burst(0.14f, 8), new ParticleSystem.Burst(0.28f, 6) });
            var shape = particles.shape;
            shape.scale = new Vector3(1.35f, 2.3f, 0f);
            particles.GetComponent<ParticleSystemRenderer>().sharedMaterial = sparkleMaterial;

            var aura = effect.transform.Find("SoftCharge").GetComponent<ParticleSystem>();
            aura.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var auraMain = aura.main;
            auraMain.startLifetime = 0.8f;
            auraMain.startSize = 3.3f;
            auraMain.startColor = new Color(0.5f, 1f, 0.88f, 0.36f);
            aura.GetComponent<ParticleSystemRenderer>().sortingOrder = 1;

            ParticleSystem ring = RecoveryChild(effect.transform, "RecoveryPixelRing", ringMaterial);
            var ringMain = ring.main;
            ringMain.startLifetime = 0.65f;
            ringMain.startSize = 3.3f;
            ringMain.startSpeed = 0f;
            ringMain.startColor = new Color(0.45f, 1f, 0.92f, 0.75f);
            var ringShape = ring.shape;
            ringShape.enabled = false;
            var ringEmission = ring.emission;
            ringEmission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            var ringSize = ring.sizeOverLifetime;
            ringSize.enabled = true;
            ringSize.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.4f, 1f, 1.15f));
            ring.GetComponent<ParticleSystemRenderer>().sortingOrder = 1;

            ParticleSystem streaks = RecoveryChild(effect.transform, "RecoveryLightStreaks",
                AssetDatabase.LoadAssetAtPath<Material>(GeneratedFolder + "/TapiocaDots.mat"));
            var streakMain = streaks.main;
            streakMain.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.7f);
            streakMain.startSpeed = 0f;
            streakMain.startSize3D = true;
            streakMain.startSizeX = 0.045f;
            streakMain.startSizeY = 0.32f;
            streakMain.startSizeZ = 1f;
            streakMain.startColor = new Color(0.7f, 1f, 0.98f, 0.9f);
            var streakShape = streaks.shape;
            streakShape.shapeType = ParticleSystemShapeType.Box;
            streakShape.scale = new Vector3(1.6f, 1.2f, 0f);
            streakShape.rotation = Vector3.zero;
            var velocity = streaks.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = 0f; velocity.y = 1.6f; velocity.z = 0f;
            var streakEmission = streaks.emission;
            streakEmission.SetBursts(new[] { new ParticleSystem.Burst(0.02f, 8), new ParticleSystem.Burst(0.18f, 4) });
            PrefabUtility.SaveAsPrefabAsset(effect, EffectPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(effect); }

        GameObject pickup = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var settings = new SerializedObject(pickup.GetComponent<TapiocaHealingPickup>());
            settings.FindProperty("pickupClip").objectReferenceValue = drinking;
            settings.FindProperty("drinkingDuration").floatValue = 0f;
            settings.FindProperty("recoveryClip").objectReferenceValue = recovery;
            settings.FindProperty("playerHealingEffect").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>(EffectPath).GetComponent<ParticleSystem>();
            settings.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(pickup, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(pickup); }
    }

    private static AudioClip ImportPickupAudio(string path)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = AssetImporter.GetAtPath(path) as AudioImporter;
        if (importer == null) throw new FileNotFoundException("Missing pickup sound", path);
        var settings = importer.defaultSampleSettings;
        settings.loadType = AudioClipLoadType.DecompressOnLoad;
        settings.compressionFormat = AudioCompressionFormat.PCM;
        importer.defaultSampleSettings = settings;
        importer.forceToMono = true;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    }

    private static ParticleSystem RecoveryChild(Transform parent, string name, Material material)
    {
        Transform existing = parent.Find(name);
        if (existing != null) return existing.GetComponent<ParticleSystem>();
        ParticleSystem particles = CreateDots(name, parent, material, false);
        var main = particles.main;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        return particles;
    }

    private static Sprite CreateRecoverySprite(bool sparkle)
    {
        string path = GeneratedFolder + (sparkle ? "/RecoverySparkle.png" : "/RecoveryRing.png");
        if (!File.Exists(path))
        {
            int size = sparkle ? 9 : 24;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Abs(x - (size - 1) * 0.5f);
                    float dy = Mathf.Abs(y - (size - 1) * 0.5f);
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    bool visible = sparkle ? (dx < 0.6f || dy < 0.6f || dx + dy < 2.5f)
                        : r >= 9.6f && r <= 10.8f;
                    texture.SetPixel(x, y, visible ? Color.white : Color.clear);
                }
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
        }
        ConfigureSprite(path, 24f, FilterMode.Point);
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    public static string Place(float x)
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/CompletScene.unity" || scene.isDirty)
            throw new InvalidOperationException("Open the saved CompletScene before placement.");
        Physics2D.SyncTransforms();
        var hit = Physics2D.RaycastAll(new Vector2(x, 12f), Vector2.down, 40f)
            .Where(h => !h.collider.isTrigger && h.collider.CompareTag("Floor"))
            .OrderByDescending(h => h.point.y).FirstOrDefault();
        if (hit.collider == null) throw new InvalidOperationException("No floor at placement x=" + x);
        var existing = UnityEngine.Object.FindObjectsByType<TapiocaHealingPickup>(FindObjectsInactive.Include,
            FindObjectsSortMode.None).FirstOrDefault(p => p.name == "TapiocaMilkTea_Stage1");
        if (existing != null) return "Existing pickup preserved at " + existing.transform.position;
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        Undo.RegisterCreatedObjectUndo(instance, "Place Tapioca Milk Tea");
        instance.name = "TapiocaMilkTea_Stage1";
        Transform parent = scene.GetRootGameObjects().FirstOrDefault(o => o.name == "Test_Stage")?.transform;
        instance.transform.SetParent(parent, true);
        instance.transform.position = new Vector3(x, hit.point.y + 0.68f, 0f);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save CompletScene.");
        return "Placed " + instance.name + " at " + instance.transform.position + " over " + hit.collider.name;
    }

    private static void CreatePickup(Sprite glow, Material dots, ParticleSystem effect)
    {
        var root = new GameObject("TapiocaMilkTea");
        try
        {
            root.AddComponent<BoxCollider2D>().size = new Vector2(1.42f, 2.7f);
            root.GetComponent<BoxCollider2D>().offset = new Vector2(0f, 0.85f);
            root.GetComponent<BoxCollider2D>().isTrigger = true;
            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            visual.localPosition = new Vector3(0f, 0.85f, 0f);
            var halo = ChildSprite("SoftGlow", visual, glow, -1);
            halo.transform.localScale = Vector3.one * 3.2f;
            halo.color = new Color(0.55f, 0.95f, 1f, 0.38f);
            var drink = ChildSprite("Drink", visual, AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath), 0);
            ParticleSystem idle = CreateDots("IdleDots", root.transform, dots, true);
            idle.transform.localPosition = visual.localPosition;
            var idleMain = idle.main;
            idleMain.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.5f);
            idleMain.startSpeed = new ParticleSystem.MinMaxCurve(0.12f, 0.28f);
            idleMain.startSize = new ParticleSystem.MinMaxCurve(0.045f, 0.075f);
            idleMain.maxParticles = 12;
            var idleEmission = idle.emission;
            idleEmission.rateOverTime = 3.2f;
            var idleShape = idle.shape;
            idleShape.shapeType = ParticleSystemShapeType.Box;
            idleShape.scale = new Vector3(1.6f, 2f, 0f);
            idleShape.rotation = new Vector3(-90f, 0f, 0f);
            ParticleSystem burst = CreateDots("PickupDots", root.transform, dots, false);
            burst.transform.localPosition = visual.localPosition;
            var burstEmission = burst.emission;
            burstEmission.SetBursts(new[] { new ParticleSystem.Burst(0f, 6) });
            var burstMain = burst.main;
            burstMain.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
            burstMain.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.28f);
            burstMain.startSize = new ParticleSystem.MinMaxCurve(0.045f, 0.085f);
            var burstShape = burst.shape;
            burstShape.shapeType = ParticleSystemShapeType.Circle;
            burstShape.radius = 0.15f;
            var item = root.AddComponent<TapiocaHealingPickup>();
            var so = new SerializedObject(item);
            so.FindProperty("visualRoot").objectReferenceValue = visual;
            so.FindProperty("drinkRenderer").objectReferenceValue = drink;
            so.FindProperty("glowRenderer").objectReferenceValue = halo;
            so.FindProperty("idleParticles").objectReferenceValue = idle;
            so.FindProperty("pickupBurst").objectReferenceValue = burst;
            so.FindProperty("playerHealingEffect").objectReferenceValue = effect;
            so.FindProperty("pickupClip").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(SoundPath);
            so.ApplyModifiedPropertiesWithoutUndo();
            ConfigureSurroundings(root);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    public static void ConfigureSurroundings(GameObject root)
    {
        Transform visual = root.transform.Find("Visual");
        SpriteRenderer drink = visual.Find("Drink").GetComponent<SpriteRenderer>();
        ParticleSystem idle = root.transform.Find("IdleDots").GetComponent<ParticleSystem>();
        idle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        idle.transform.localPosition = new Vector3(-1.45f, visual.localPosition.y, 0f);
        ConfigureSideParticles(idle, drink);
        Transform right = idle.transform.Find("RightDots");
        if (right == null)
        {
            var go = UnityEngine.Object.Instantiate(idle.gameObject, idle.transform, false);
            go.name = "RightDots";
            right = go.transform;
        }
        right.localPosition = new Vector3(2.9f, 0f, 0f);
        ConfigureSideParticles(right.GetComponent<ParticleSystem>(), drink);
        var settings = new SerializedObject(root.GetComponent<TapiocaHealingPickup>());
        float rate = settings.FindProperty("particleRate").floatValue * 0.5f;
        var leftEmission = idle.emission;
        leftEmission.rateOverTime = rate;
        var rightEmission = right.GetComponent<ParticleSystem>().emission;
        rightEmission.rateOverTime = rate;

        var burst = root.transform.Find("PickupDots").GetComponent<ParticleSystem>();
        burst.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var shape = burst.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 1.5f;
        shape.radiusThickness = 0f;
        var burstRenderer = burst.GetComponent<ParticleSystemRenderer>();
        burstRenderer.sortingLayerID = drink.sortingLayerID;
        burstRenderer.sortingOrder = drink.sortingOrder - 2;

        ConfigureGroundShadow(root);
    }

    public static void ConfigureGroundShadow(GameObject root)
    {
        EnsureFolder(GeneratedFolder);
        Sprite shadowSprite = CreateGroundShadowSprite();
        var settings = new SerializedObject(root.GetComponent<TapiocaHealingPickup>());
        Transform shadowTransform = root.transform.Find("GroundShadow");
        SpriteRenderer shadow = shadowTransform != null
            ? shadowTransform.GetComponent<SpriteRenderer>()
            : ChildSprite("GroundShadow", root.transform, shadowSprite, 1);
        shadow.sprite = shadowSprite;
        shadow.transform.localPosition = new Vector3(0f, -0.655f, 0f);
        shadow.transform.localScale = new Vector3(1.1f, 0.84f, 1f);
        shadow.color = new Color(0.055f, 0.04f, 0.09f, settings.FindProperty("shadowOpacity").floatValue);
        settings.FindProperty("groundShadow").objectReferenceValue = shadow;
        settings.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Sprite CreateGroundShadowSprite()
    {
        string path = GeneratedFolder + "/GroundShadow.png";
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing == null || existing.width != 32 || existing.height != 8)
        {
            var texture = new Texture2D(32, 8, TextureFormat.RGBA32, false);
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 32; x++)
                {
                    float radius = new Vector2((x + 0.5f - 16f) / 16f, (y + 0.5f - 4f) / 4f).magnitude;
                    // ぼかさず、中心と縁の2段階でドットの輪郭を保つ。
                    float alpha = radius <= 0.82f ? 1f : radius <= 1f ? 0.45f : 0f;
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
        }
        ConfigureSprite(path, 16f, FilterMode.Point);
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static void ConfigureSideParticles(ParticleSystem particles, SpriteRenderer drink)
    {
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startSpeed = 0f;
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(0.24f, 2.2f, 0f);
        shape.rotation = Vector3.zero;
        var velocity = particles.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.x = 0f;
        velocity.y = 0.22f;
        velocity.z = 0f;
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.sortingLayerID = drink.sortingLayerID;
        renderer.sortingOrder = drink.sortingOrder - 2;
    }

    private static GameObject CreatePlayerEffect(Material dots, Material soft)
    {
        var root = new GameObject("TapiocaPlayerHeal");
        try
        {
            ParticleSystem ps = root.AddComponent<ParticleSystem>();
            ConfigureDots(ps, dots, false);
            var main = ps.main;
            main.duration = 0.18f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.65f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.7f, 1.3f);
            main.stopAction = ParticleSystemStopAction.Destroy;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(0.85f, 1.5f, 0f);
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var emission = ps.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 8) });
            var auraObject = new GameObject("SoftCharge");
            auraObject.transform.SetParent(root.transform, false);
            ParticleSystem aura = auraObject.AddComponent<ParticleSystem>();
            aura.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var auraMain = aura.main;
            auraMain.playOnAwake = false;
            auraMain.loop = false;
            auraMain.duration = 0.2f;
            auraMain.startLifetime = 0.35f;
            auraMain.startSpeed = 0f;
            auraMain.startSize = 2.1f;
            auraMain.startColor = new Color(0.65f, 1f, 0.94f, 0.14f);
            auraMain.simulationSpace = ParticleSystemSimulationSpace.Local;
            var auraEmission = aura.emission;
            auraEmission.rateOverTime = 0f;
            auraEmission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            var auraShape = aura.shape;
            auraShape.enabled = false;
            FadeParticles(aura);
            var renderer = aura.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = soft;
            renderer.sortingLayerName = "Default";
            renderer.sortingOrder = 12;
            return PrefabUtility.SaveAsPrefabAsset(root, EffectPath);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static ParticleSystem CreateDots(string name, Transform parent, Material material, bool loop)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ConfigureDots(ps, material, loop);
        return ps;
    }

    private static void ConfigureDots(ParticleSystem ps, Material material, bool loop)
    {
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = loop;
        main.loop = loop;
        main.duration = 0.2f;
        main.startLifetime = 0.6f;
        main.startSpeed = 0.5f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.045f);
        main.maxParticles = 24;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.55f, 0.95f, 1f, 0.85f),
            new Color(1f, 0.8f, 0.94f, 0.85f));
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        FadeParticles(ps);
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.sortingOrder = 13;
    }

    private static void FadeParticles(ParticleSystem ps)
    {
        var color = ps.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0f, 1f) });
        color.color = gradient;
    }

    private static SpriteRenderer ChildSprite(string name, Transform parent, Sprite sprite, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = order;
        return renderer;
    }

    private static Sprite CreateGlow()
    {
        string path = GeneratedFolder + "/SoftGlow.png";
        if (!File.Exists(path))
        {
            var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    float r = new Vector2((x - 15.5f) / 15.5f, (y - 15.5f) / 15.5f).magnitude;
                    float a = Mathf.Pow(Mathf.Max(0f, 1f - r * r), 3f);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
        }
        ConfigureSprite(path, 32f, FilterMode.Bilinear);
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static Material GetMaterial(string path, Texture texture)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        material = new Material(Shader.Find("Sprites/Default"));
        if (texture != null) material.mainTexture = texture;
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static void ConfigureSprite(string path, float ppu, FilterMode filter)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) throw new FileNotFoundException("Missing source image", path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = ppu;
        importer.filterMode = filter;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.SaveAndReimport();
    }

    private static void CreateSound()
    {
        if (!File.Exists(SoundPath))
        {
            const int rate = 44100;
            const float duration = 0.56f;
            var samples = new float[Mathf.RoundToInt(rate * duration)];
            var random = new System.Random(741);
            double phase = 0;
            double noise = 0;
            float peak = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                double t = i / (double)rate;
                phase += 2 * Math.PI * (320 + 600 * Math.Exp(-t * 48)) / rate;
                double pop = Math.Sin(phase) * Math.Exp(-t * 32) * (1 - Math.Exp(-t * 800));
                double u = Math.Max(0, t - 0.065);
                double sparkle = (Math.Sin(2 * Math.PI * 660 * u) + 0.45 * Math.Sin(2 * Math.PI * 990 * u))
                    * Math.Exp(-u * 10) * (1 - Math.Exp(-u * 90));
                noise = noise * 0.82 + (random.NextDouble() * 2 - 1) * 0.18;
                double fizz = noise * Math.Exp(-u * 8) * (1 - Math.Exp(-u * 25));
                samples[i] = (float)((pop * 0.65 + sparkle * 0.24 + fizz * 0.14)
                    * Math.Min(1, (duration - t) / 0.035));
                peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
            }
            using (var writer = new BinaryWriter(File.Create(SoundPath)))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples.Length * 2);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
                writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2);
                writer.Write((short)2); writer.Write((short)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(samples.Length * 2);
                foreach (float value in samples) writer.Write((short)(value / Mathf.Max(peak, 0.001f) * 0.78f * short.MaxValue));
            }
        }
        AssetDatabase.ImportAsset(SoundPath, ImportAssetOptions.ForceSynchronousImport);
        var importer = (AudioImporter)AssetImporter.GetAtPath(SoundPath);
        var settings = importer.defaultSampleSettings;
        settings.loadType = AudioClipLoadType.DecompressOnLoad;
        settings.compressionFormat = AudioCompressionFormat.PCM;
        importer.defaultSampleSettings = settings;
        importer.forceToMono = true;
        importer.SaveAndReimport();
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = path.Substring(0, path.LastIndexOf('/'));
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
