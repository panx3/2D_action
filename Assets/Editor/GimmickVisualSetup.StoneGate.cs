using System;
using UnityEditor;
using UnityEngine;

public static partial class GimmickVisualSetup
{
    private const string StoneArtPath = "Assets/Art/Gimmicks/StoneGate/";
    private const string SwitchPressClipPath = "Assets/Audio/SFX/Imported/StoneSwitchPress.mp3";
    private const string DoorOpenClipPath = "Assets/Audio/SFX/Imported/StoneGateOpen.mp3";
    private static readonly string[] StoneSpriteNames =
    {
        "Gate_Back", "Gate_Front", "Gate_Membrane", "Switch_Raised",
        "Switch_Pressed", "Switch_Base", "Switch_Cap", "Switch_Piston"
    };

    [MenuItem("鉄球少女/Gimmick/石造ゲートと一度式床スイッチを適用")]
    public static void ApplyStoneGateAndSwitch()
    {
        foreach (string name in StoneSpriteNames)
            ConfigureStoneTexture(name);

        foreach (string path in new[] { SwitchPressClipPath, DoorOpenClipPath })
        {
            AudioImporter audio = AssetImporter.GetAtPath(path) as AudioImporter;
            if (audio == null)
                throw new InvalidOperationException("Gimmick sound is missing: " + path);
            AudioImporterSampleSettings settings = audio.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.PCM;
            settings.preloadAudioData = true;
            audio.defaultSampleSettings = settings;
            audio.SaveAndReimport();
        }

        GameObject door = PrefabUtility.LoadPrefabContents(DoorPrefabPath);
        try
        {
            ConfigureStoneDoor(door);
            PrefabUtility.SaveAsPrefabAsset(door, DoorPrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(door); }

        GameObject floorSwitch = PrefabUtility.LoadPrefabContents(WeightSwitchPrefabPath);
        try
        {
            ConfigureStoneSwitch(floorSwitch);
            PrefabUtility.SaveAsPrefabAsset(floorSwitch, WeightSwitchPrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(floorSwitch); }

        // Preserve root placements, event links and unrelated unsaved scene work.
        foreach (GimmickDoor instance in UnityEngine.Object.FindObjectsByType<GimmickDoor>(FindObjectsInactive.Include))
        {
            if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instance.gameObject) != DoorPrefabPath)
                continue;
            Undo.RegisterFullObjectHierarchyUndo(instance.gameObject, "Update stone gate artwork");
            ConfigureStoneDoor(instance.gameObject);
            RecordArtworkOverrides(instance.gameObject);
        }
        foreach (WeightSwitch instance in UnityEngine.Object.FindObjectsByType<WeightSwitch>(FindObjectsInactive.Include))
        {
            if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instance.gameObject) != WeightSwitchPrefabPath)
                continue;
            Undo.RegisterFullObjectHierarchyUndo(instance.gameObject, "Update stone pressure switch");
            ConfigureStoneSwitch(instance.gameObject);
            RecordArtworkOverrides(instance.gameObject);
        }
        ValidateStoneArtwork();
    }

    private static void ConfigureStoneTexture(string name)
    {
        string path = StoneArtPath + name + ".png";
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) throw new InvalidOperationException("Missing stone sprite: " + path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 100f;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.npotScale = TextureImporterNPOTScale.None;
        var textureSettings = new TextureImporterSettings();
        importer.ReadTextureSettings(textureSettings);
        textureSettings.spriteAlignment = (int)SpriteAlignment.Custom;
        textureSettings.spritePivot = new Vector2(0.5f, name == "Switch_Piston" ? 120f / 392f : 0f);
        textureSettings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(textureSettings);
        importer.SaveAndReimport();
    }

    private static Sprite StoneSprite(string name)
    {
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(StoneArtPath + name + ".png");
        if (sprite == null) throw new InvalidOperationException("Missing stone sprite: " + name);
        return sprite;
    }

    private static void ConfigureStoneDoor(GameObject root)
    {
        DisableRootRenderer(root);
        SpriteRenderer back = EnsureRenderer(root.transform, "Door_Back", -2);
        SpriteRenderer barrier = EnsureRenderer(root.transform, "Door_Barrier", 1);
        // Cover the chain (8), Morning Star (9) and its feedback (up to 11), below foreground foliage (30).
        SpriteRenderer front = EnsureRenderer(root.transform, "Door_Front", 20);
        back.sprite = StoneSprite("Gate_Back");
        barrier.sprite = StoneSprite("Gate_Membrane");
        front.sprite = StoneSprite("Gate_Front");
        Vector3 artworkScale = ArtworkScale(root, 1.65f);
        foreach (SpriteRenderer renderer in new[] { back, barrier, front })
        {
            renderer.transform.localPosition = new Vector3(0f, -0.55f, 0f);
            renderer.transform.localScale = artworkScale;
            renderer.color = Color.white;
        }
        // Only the energy film blocks movement. Stone pillars are artwork layers.
        BoxCollider2D blocker = RequireComponent<BoxCollider2D>(root);
        blocker.isTrigger = false;
        blocker.size = new Vector2(0.12f / Mathf.Max(0.001f, Mathf.Abs(root.transform.lossyScale.x)), 1.30f * artworkScale.y);
        blocker.offset = (Vector2)back.transform.localPosition + new Vector2(0f, 0.65f * artworkScale.y);
        var serialized = new SerializedObject(RequireComponent<GimmickDoor>(root));
        SetObject(serialized, "backRenderer", back);
        SetObject(serialized, "barrierRenderer", barrier);
        SetObject(serialized, "frontRenderer", front);
        SetObject(serialized, "closedSprite", barrier.sprite);
        SetObject(serialized, "openSprite", back.sprite);
        SetColor(serialized, "closedColor", Color.white);
        SetColor(serialized, "openColor", Color.white);
        SetFloat(serialized, "transitionDuration", 0.28f);
        SetFloat(serialized, "barrierThickness", 0.12f);
        SetObject(serialized, "openClip", AssetDatabase.LoadAssetAtPath<AudioClip>(DoorOpenClipPath));
        SetFloat(serialized, "openVolume", 0.9f);
        RequireProperty(serialized, "alignArtworkLayers").boolValue = true;
        SetVector3(serialized, "artworkLocalPosition", back.transform.localPosition);
        SetVector3(serialized, "artworkLocalScale", artworkScale);
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    [MenuItem("鉄球少女/Gimmick/床スイッチのPlayer足場判定を適用")]
    public static void ApplySwitchPlayerCollision()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(WeightSwitchPrefabPath);
        try
        {
            ConfigureSwitchPlayerColliders(root);
            PrefabUtility.SaveAsPrefabAsset(root, WeightSwitchPrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }

        foreach (WeightSwitch instance in UnityEngine.Object.FindObjectsByType<WeightSwitch>(FindObjectsInactive.Include))
        {
            if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instance.gameObject) != WeightSwitchPrefabPath)
                continue;
            Undo.RegisterFullObjectHierarchyUndo(instance.gameObject, "Add switch Player support");
            ConfigureSwitchPlayerColliders(instance.gameObject);
            RecordArtworkOverrides(instance.gameObject);
        }
    }

    private static void ConfigureSwitchPlayerColliders(GameObject root)
    {
        Transform visual = root.transform.Find("Visual");
        if (visual == null || visual.Find("PressureCap") == null)
            throw new InvalidOperationException("Stone switch pressure mechanism is missing.");
        ConfigurePlayerSupport(visual, new Vector2(1.78f, 0.40f), new Vector2(0f, 0.20f));
        ConfigurePlayerSupport(visual.Find("PressureCap"), new Vector2(1.58f, 0.36f), new Vector2(0f, 0.75f));
    }

    private static void ConfigurePlayerSupport(Transform host, Vector2 size, Vector2 offset)
    {
        int playerMask = LayerMask.GetMask("player");
        if (playerMask == 0) throw new InvalidOperationException("The player layer is missing.");
        BoxCollider2D support = host.GetComponent<BoxCollider2D>();
        if (support == null) support = host.gameObject.AddComponent<BoxCollider2D>();
        support.enabled = true;
        support.isTrigger = false;
        support.size = size;
        support.offset = offset;
        support.includeLayers = playerMask;
        support.excludeLayers = ~playerMask;
        support.layerOverridePriority = 1;
        host.gameObject.tag = "Floor";
    }

    private static void ConfigureStoneSwitch(GameObject root)
    {
        DisableRootRenderer(root);
        SpriteRenderer visual = EnsureRenderer(root.transform, "Visual", 5);
        visual.transform.localPosition = new Vector3(0f, -0.5f, 0f);
        visual.transform.localScale = ArtworkScale(root, 0.53f);
        visual.sprite = StoneSprite("Switch_Base");
        visual.color = Color.white;
        SpriteRenderer cap = EnsureRenderer(visual.transform, "PressureCap", 6);
        cap.sprite = StoneSprite("Switch_Cap");
        cap.transform.localScale = Vector3.one;
        cap.enabled = true;
        SpriteRenderer piston = EnsureRenderer(visual.transform, "Piston", 4);
        piston.sprite = StoneSprite("Switch_Piston");
        piston.transform.localPosition = new Vector3(0f, 0.30f, 0f);
        piston.transform.localScale = Vector3.one;
        piston.enabled = true;
        BoxCollider2D trigger = RequireComponent<BoxCollider2D>(root);
        trigger.isTrigger = true;
        trigger.size = Vector2.Scale(new Vector2(1.58f, 0.40f), visual.transform.localScale);
        trigger.offset = (Vector2)visual.transform.localPosition + new Vector2(0f, 0.75f * visual.transform.localScale.y);
        var serialized = new SerializedObject(RequireComponent<WeightSwitch>(root));
        SetObject(serialized, "spriteRenderer", visual);
        SetObject(serialized, "visualRoot", visual.transform);
        SetObject(serialized, "offSprite", StoneSprite("Switch_Raised"));
        SetObject(serialized, "onSprite", StoneSprite("Switch_Pressed"));
        SetObject(serialized, "restingBaseSprite", visual.sprite);
        SetObject(serialized, "pressureCapRenderer", cap);
        SetObject(serialized, "pistonRenderer", piston);
        SetVector3(serialized, "artworkLocalPosition", visual.transform.localPosition);
        SetVector3(serialized, "artworkLocalScale", visual.transform.localScale);
        SetVector3(serialized, "pressedLocalOffset", Vector3.zero);
        SetColor(serialized, "offColor", Color.white);
        SetColor(serialized, "onColor", Color.white);
        SetFloat(serialized, "pressTravel", 0.4875f);
        SetFloat(serialized, "pressDuration", 0.16f);
        SetObject(serialized, "pressClip", AssetDatabase.LoadAssetAtPath<AudioClip>(SwitchPressClipPath));
        SetFloat(serialized, "pressVolume", 0.9f);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        ConfigureSwitchPlayerColliders(root);
    }

    private static void RecordArtworkOverrides(GameObject root)
    {
        foreach (Component component in root.GetComponentsInChildren<Component>(true))
        {
            if (component is Transform || component is SpriteRenderer || component is Collider2D || component is WeightSwitch || component is GimmickDoor)
                PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);
    }

    private static Vector3 ArtworkScale(GameObject root, float horizontalScale)
    {
        Vector3 parentScale = root.transform.lossyScale;
        return new Vector3(horizontalScale,
            horizontalScale * Mathf.Abs(parentScale.x) / Mathf.Max(0.001f, Mathf.Abs(parentScale.y)), 1f);
    }

    private static void ValidateStoneArtwork()
    {
        foreach (string name in StoneSpriteNames)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(StoneArtPath + name + ".png");
            if (importer == null || importer.filterMode != FilterMode.Point || importer.mipmapEnabled || importer.textureCompression != TextureImporterCompression.Uncompressed)
                throw new InvalidOperationException("Invalid stone artwork import: " + name);
            StoneSprite(name);
        }
    }
}
