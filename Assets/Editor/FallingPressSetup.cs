using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>新規アセットの作成と、Stage2への1組だけの配置。</summary>
public static class FallingPressSetup
{
    public const string Art = "Assets/Art/FallingPress/";
    public const string Folder = "Assets/Prefabs/Environment/Stage2/FallingPress";
    public const string PressPath = Folder + "/FallingPress.prefab";
    public const string StandPath = Folder + "/PressBallStand.prefab";

    [MenuItem("Tools/Falling Press/Create Assets And Stage2 Pair")]
    public static void Create()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/Stage2.unity" || scene.isDirty)
            throw new InvalidOperationException("Open saved Stage2 before installing the pair.");
        if (GameObject.Find("Stage2FallingPress") != null || AssetDatabase.LoadAssetAtPath<GameObject>(PressPath) != null)
            throw new InvalidOperationException("Falling press already exists; edit the existing pair instead.");
        System.IO.Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();
        ImportSprite("PressBody", 36f, new Vector2(0.5f, 0f));
        ImportSprite("BallStand", 35.26f, new Vector2(0.5f, 0f));
        ImportSprite("Piston", 24f, new Vector2(0.5f, 0.5f));
        ImportSprite("LightPixel", 1f, new Vector2(0.5f, 0.5f));

        GameObject stand = BuildStand();
        PrefabUtility.SaveAsPrefabAsset(stand, StandPath);
        UnityEngine.Object.DestroyImmediate(stand);
        GameObject press = BuildPress();
        PrefabUtility.SaveAsPrefabAsset(press, PressPath);
        UnityEngine.Object.DestroyImmediate(press);
        var group = new GameObject("Stage2FallingPress");
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PressPath), scene);
        instance.transform.SetParent(group.transform);
        instance.transform.position = new Vector3(88f, 1.23808f, 0f);
        var standInstance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(StandPath), scene);
        standInstance.transform.SetParent(group.transform);
        standInstance.transform.position = instance.transform.position;
        var instanceController = instance.GetComponent<FallingPressController>();
        Set(instanceController, "ballStand", standInstance.GetComponent<PressBallStandController>());
        PrefabUtility.RecordPrefabInstancePropertyModifications(instanceController);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = instance;
        Debug.Log("[FallingPress] Saved two prefabs, pixel sprites, and one Stage2 pair at x=88.");
    }

    private static void ImportSprite(string name, float ppu, Vector2 pivot)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(Art + name + ".png");
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = ppu;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)SpriteAlignment.Custom; settings.spritePivot = pivot;
        importer.SetTextureSettings(settings); importer.SaveAndReimport();
    }

    private static GameObject Child(string name, Transform parent)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false); return go;
    }
    private static SpriteRenderer Sprite(string name, string asset, Transform parent, int order)
    {
        var sr = Child(name, parent).AddComponent<SpriteRenderer>();
        sr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Art + asset + ".png");
        sr.sortingOrder = order; return sr;
    }
    private static void Set(UnityEngine.Object target, string name, UnityEngine.Object value)
    {
        var so = new SerializedObject(target); so.FindProperty(name).objectReferenceValue = value; so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static GameObject BuildStand()
    {
        var root = new GameObject("PressBallStand");
        var visuals = Child("VisualRoot", root.transform);
        var body = Sprite("BaseWithBallMark", "BallStand", visuals.transform, 1);
        body.transform.localScale = new Vector3(1f, 1f, 1f);
        var light = Sprite("PlacementIndicator", "LightPixel", visuals.transform, 1);
        light.transform.localPosition = new Vector3(0f, 1.68f, 0f);
        light.transform.localScale = new Vector3(0.55f, 0.06f, 1f);
        var detection = Child("DetectionRoot_BallOnly", root.transform); detection.layer = 2;
        var surface = detection.AddComponent<BoxCollider2D>();
        surface.size = new Vector2(3.7f, 1.9f); surface.offset = new Vector2(0f, 0.95f);
        // 奥側の台座。Playerは手前を通り、鉄球だけが支持面に接触する。
        surface.includeLayers = 1 << 7; surface.excludeLayers = ~(1 << 7); surface.layerOverridePriority = 20;
        var controller = root.AddComponent<PressBallStandController>();
        Set(controller, "supportSurface", surface); Set(controller, "indicator", light);
        return root;
    }

    private static GameObject BuildPress()
    {
        var root = new GameObject("FallingPress");
        var anchor = Child("VisualRoot_FixedSupports", root.transform);
        var rods = new Transform[2];
        for (int i = 0; i < 2; i++)
        {
            var rod = Sprite("HydraulicPiston_" + i, "Piston", anchor.transform, 0);
            rod.transform.localPosition = new Vector3(i == 0 ? -2.1f : 2.1f, 8.6f, 0f);
            rod.transform.localScale = new Vector3(0.8f, 0.4f, 1f); rods[i] = rod.transform;
        }
        var moving = Child("MovingHead", root.transform); moving.layer = 2;
        moving.transform.localPosition = new Vector3(0f, 6.04f, 0f);
        var rb = moving.AddComponent<Rigidbody2D>(); rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f; rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous; rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        var visual = Child("VisualRoot", moving.transform);
        var body = Sprite("PressBody", "PressBody", visual.transform, 3);
        var light = Sprite("WarningLight", "LightPixel", visual.transform, 4);
        light.transform.localPosition = new Vector3(0f, 1.35f, 0f);
        var collider = moving.AddComponent<BoxCollider2D>();
        collider.includeLayers = 1 << 3; collider.excludeLayers = ~(1 << 3); collider.layerOverridePriority = 30;
        var detector = Child("DamageDetector", moving.transform); detector.layer = 2;
        var trigger = detector.AddComponent<BoxCollider2D>(); trigger.isTrigger = true;
        trigger.includeLayers = 1 << 3; trigger.excludeLayers = ~(1 << 3); trigger.layerOverridePriority = 30;
        var audio = root.AddComponent<AudioSource>(); audio.playOnAwake = false; audio.spatialBlend = 0.8f;
        audio.rolloffMode = AudioRolloffMode.Linear; audio.minDistance = 3f; audio.maxDistance = 22f; audio.volume = 0.65f;
        var vfx = Child("VFXRoot_ImpactSparks", root.transform);
        var particles = vfx.AddComponent<ParticleSystem>(); particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main; main.playOnAwake = false; main.loop = false; main.duration = 0.5f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.3f); main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.12f); main.startColor = new Color(1f, 0.73f, 0.22f);
        main.gravityModifier = 1.2f; main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = particles.emission; emission.enabled = false;
        var shape = particles.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.12f;
        var renderer = particles.GetComponent<ParticleSystemRenderer>(); renderer.sortingOrder = 5;
        var material = new Material(Shader.Find("Sprites/Default"));
        material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "LightPixel.png");
        AssetDatabase.CreateAsset(material, Art + "ImpactSparks.mat"); renderer.sharedMaterial = material;
        var controller = root.AddComponent<FallingPressController>();
        Set(controller, "head", rb); Set(controller, "pressCollider", collider); Set(controller, "damageDetector", trigger);
        Set(controller, "body", body); Set(controller, "warningLight", light); Set(controller, "audioSource", audio);
        Set(controller, "floorImpact", AssetDatabase.LoadAssetAtPath<AudioClip>(Art + "PressImpact.wav"));
        Set(controller, "ballImpact", AssetDatabase.LoadAssetAtPath<AudioClip>(Art + "BallClang.wav"));
        Set(controller, "sparks", particles);
        var so = new SerializedObject(controller); var array = so.FindProperty("pistons"); array.arraySize = rods.Length;
        for (int i = 0; i < rods.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = rods[i];
        so.ApplyModifiedPropertiesWithoutUndo(); controller.ApplyGeometry();
        return root;
    }

    [MenuItem("Tools/Falling Press/Validate Saved Assets")]
    public static void Validate()
    {
        var press = AssetDatabase.LoadAssetAtPath<GameObject>(PressPath);
        var stand = AssetDatabase.LoadAssetAtPath<GameObject>(StandPath);
        if (press == null || stand == null) throw new Exception("Missing prefabs.");
        var controller = press.GetComponent<FallingPressController>();
        if (controller == null || controller.BallStand != null || press.GetComponentInChildren<PressBallStandController>(true) != null)
            throw new Exception("Press prefab must be independent; assign the stand on the scene instance.");
        if (stand.GetComponent<PressBallStandController>() == null) throw new Exception("Missing stand controller.");
        foreach (var c in press.GetComponentsInChildren<Collider2D>())
            if ((c.excludeLayers.value & (1 << 7)) == 0)
                throw new Exception("Incorrect actor collision filtering: " + c.name);
        foreach (var c in stand.GetComponentsInChildren<Collider2D>())
            if ((c.excludeLayers.value & (1 << 3)) == 0)
                throw new Exception("Incorrect stand collision filtering: " + c.name);
        var assigned = new System.Collections.Generic.HashSet<PressBallStandController>();
        foreach (var instance in UnityEngine.Object.FindObjectsByType<FallingPressController>(FindObjectsSortMode.None))
        {
            if (instance.gameObject.scene.path != "Assets/Scenes/Stage2.unity") continue;
            if (instance.BallStand == null || instance.BallStand.transform.IsChildOf(instance.transform) ||
                !assigned.Add(instance.BallStand) ||
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instance.BallStand.gameObject) != StandPath)
                throw new Exception("Stage2 press must reference its own separate stand prefab instance.");
        }
        foreach (string name in new[] { "PressBody", "BallStand", "Piston", "LightPixel" })
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(Art + name + ".png");
            if (ti.filterMode != FilterMode.Point || ti.mipmapEnabled || ti.textureCompression != TextureImporterCompression.Uncompressed)
                throw new Exception("Incorrect pixel import: " + name);
        }
        if (press.GetComponentsInChildren<Transform>(true).Any(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) != 0))
            throw new Exception("Missing scripts.");
        Debug.Log("[FallingPress] PASS: prefabs, pair, collision exclusions, sprites, and scripts.");
    }
}
