using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class GimmickVisualSetup
{
    private const string MagnetTexturePath = "Assets/image_/磁石ドット絵.png";
    private const string DoorTexturePath = "Assets/image_/扉開閉.png";
    private const string DoorOverlayTexturePath = "Assets/image_/扉開閉オーバーレイ.png";
    private const string FloorSwitchTexturePath = "Assets/image_/床スイッチドット絵.png";
    private const string HitSwitchTexturePath = "Assets/image_/扉スイッチ.png";

    private const string MagnetPrefabPath = "Assets/Prefabs/Gimmicks/MagnetPoint.prefab";
    private const string DoorPrefabPath = "Assets/Prefabs/Gimmicks/GimmickDoor.prefab";
    private const string WeightSwitchPrefabPath = "Assets/Prefabs/Gimmicks/WeightSwitch.prefab";
    private const string HitSwitchPrefabPath = "Assets/Prefabs/Gimmicks/HitSwitch.prefab";

    [MenuItem("鉄球少女/Gimmick/対応ビジュアルを適用")]
    public static void Apply()
    {
        ConfigureTexture(MagnetTexturePath);
        ConfigureTexture(DoorTexturePath);
        ConfigureTexture(DoorOverlayTexturePath);
        ConfigureTexture(FloorSwitchTexturePath);
        ConfigureTexture(HitSwitchTexturePath);

        Sprite magnet = LoadSprite(MagnetTexturePath, "磁石ドット絵_0");
        Sprite doorClosed = LoadSprite(DoorTexturePath, "扉開閉_0");
        Sprite doorOpen = LoadSprite(DoorTexturePath, "扉開閉_1");
        Sprite doorFront = LoadSprite(DoorOverlayTexturePath, "扉開閉オーバーレイ_0");
        Sprite floorSwitchOff = LoadSprite(FloorSwitchTexturePath, "床スイッチドット絵_0");
        Sprite floorSwitchOn = LoadSprite(FloorSwitchTexturePath, "床スイッチドット絵_1");
        Sprite hitSwitchOff = LoadSprite(HitSwitchTexturePath, "扉スイッチ_0");
        Sprite hitSwitchOn = LoadSprite(HitSwitchTexturePath, "扉スイッチ_1");

        ConfigureMagnetPrefab(magnet);
        ConfigureDoorPrefab(doorClosed, doorOpen, doorFront);
        ConfigureWeightSwitchPrefab(floorSwitchOff, floorSwitchOn);
        ConfigureHitSwitchPrefab(hitSwitchOff, hitSwitchOn);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        ValidateOrThrow();
        Debug.Log("[GimmickVisualSetup] Applied Magnet / Door / WeightSwitch / HitSwitch visuals.");
    }

    [MenuItem("鉄球少女/Gimmick/対応ビジュアルを検証")]
    public static void ValidateOrThrow()
    {
        ValidateTexture(MagnetTexturePath, 1);
        ValidateTexture(DoorTexturePath, 2);
        ValidateTexture(DoorOverlayTexturePath, 1);
        ValidateTexture(FloorSwitchTexturePath, 2);
        ValidateTexture(HitSwitchTexturePath, 2);

        ValidatePrefab<MagnetPoint>(MagnetPrefabPath, "Visual");
        ValidatePrefab<GimmickDoor>(DoorPrefabPath, "Door_Back", "Door_Barrier", "Door_Front");
        ValidatePrefab<WeightSwitch>(WeightSwitchPrefabPath, "Visual");
        ValidatePrefab<HitSwitch>(HitSwitchPrefabPath, "Visual");
    }

    private static void ConfigureTexture(string path)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
            throw new InvalidOperationException($"TextureImporter missing: {path}");

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = 100f;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.SaveAndReimport();
    }

    private static void ConfigureMagnetPrefab(Sprite magnetSprite)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(MagnetPrefabPath);
        try
        {
            DisableRootRenderer(root);
            SpriteRenderer visual = EnsureRenderer(root.transform, "Visual", 5);
            visual.sprite = magnetSprite;
            visual.color = Color.white;
            visual.transform.localScale = new Vector3(2.4f, 2.4f, 1f);

            MagnetPoint magnet = RequireComponent<MagnetPoint>(root);
            SerializedObject serialized = new SerializedObject(magnet);
            SetObject(serialized, "spriteRenderer", visual);
            SetObject(serialized, "visualRoot", visual.transform);
            SetColor(serialized, "idleColor", new Color(0.78f, 0.82f, 0.9f, 1f));
            SetColor(serialized, "activeColor", new Color(0.9f, 1f, 1f, 1f));
            SetColor(serialized, "nearColor", Color.white);
            SetFloat(serialized, "activePulseAmount", 0.04f);
            SetFloat(serialized, "activePulseSpeed", 4f);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, MagnetPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ConfigureDoorPrefab(Sprite closedSprite, Sprite openSprite, Sprite frontSprite)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(DoorPrefabPath);
        try
        {
            DisableRootRenderer(root);
            SpriteRenderer back = EnsureRenderer(root.transform, "Door_Back", -2);
            SpriteRenderer barrier = EnsureRenderer(root.transform, "Door_Barrier", 2);
            SpriteRenderer front = EnsureRenderer(root.transform, "Door_Front", 8);
            back.sprite = openSprite;
            barrier.sprite = closedSprite;
            front.sprite = frontSprite;
            back.color = Color.white;
            barrier.color = Color.white;
            front.color = Color.white;

            GimmickDoor door = RequireComponent<GimmickDoor>(root);
            SerializedObject serialized = new SerializedObject(door);
            SetObject(serialized, "backRenderer", back);
            SetObject(serialized, "barrierRenderer", barrier);
            SetObject(serialized, "frontRenderer", front);
            SetObject(serialized, "closedSprite", closedSprite);
            SetObject(serialized, "openSprite", openSprite);
            SetColor(serialized, "closedColor", Color.white);
            SetColor(serialized, "openColor", Color.white);
            SetFloat(serialized, "transitionDuration", 0.22f);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, DoorPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ConfigureWeightSwitchPrefab(Sprite offSprite, Sprite onSprite)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(WeightSwitchPrefabPath);
        try
        {
            DisableRootRenderer(root);
            SpriteRenderer visual = EnsureRenderer(root.transform, "Visual", 5);
            visual.sprite = offSprite;
            visual.color = Color.white;
            visual.transform.localScale = new Vector3(1f, 3f, 1f);

            WeightSwitch weightSwitch = RequireComponent<WeightSwitch>(root);
            SerializedObject serialized = new SerializedObject(weightSwitch);
            SetObject(serialized, "spriteRenderer", visual);
            SetObject(serialized, "visualRoot", visual.transform);
            SetObject(serialized, "offSprite", offSprite);
            SetObject(serialized, "onSprite", onSprite);
            SetColor(serialized, "offColor", Color.white);
            SetColor(serialized, "onColor", Color.white);
            SetVector3(serialized, "pressedLocalOffset", new Vector3(0f, -0.05f, 0f));
            SetFloat(serialized, "visualMoveSpeed", 0.5f);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, WeightSwitchPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ConfigureHitSwitchPrefab(Sprite offSprite, Sprite onSprite)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(HitSwitchPrefabPath);
        try
        {
            DisableRootRenderer(root);
            SpriteRenderer visual = EnsureRenderer(root.transform, "Visual", 5);
            visual.sprite = offSprite;
            visual.color = Color.white;
            visual.transform.localScale = new Vector3(2.4f, 0.6f, 1f);

            HitSwitch hitSwitch = RequireComponent<HitSwitch>(root);
            SerializedObject serialized = new SerializedObject(hitSwitch);
            SetObject(serialized, "spriteRenderer", visual);
            SetObject(serialized, "offSprite", offSprite);
            SetObject(serialized, "onSprite", onSprite);
            SetColor(serialized, "offColor", Color.white);
            SetColor(serialized, "onColor", Color.white);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, HitSwitchPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static SpriteRenderer EnsureRenderer(Transform parent, string childName, int sortingOrder)
    {
        Transform child = parent.Find(childName);
        if (child == null)
        {
            child = new GameObject(childName).transform;
            child.SetParent(parent, false);
        }

        child.gameObject.layer = parent.gameObject.layer;
        child.localPosition = Vector3.zero;
        child.localRotation = Quaternion.identity;
        if (child.localScale == Vector3.zero)
            child.localScale = Vector3.one;

        SpriteRenderer renderer = child.GetComponent<SpriteRenderer>();
        if (renderer == null)
            renderer = child.gameObject.AddComponent<SpriteRenderer>();
        renderer.sortingLayerID = 0;
        renderer.sortingOrder = sortingOrder;
        return renderer;
    }

    private static void DisableRootRenderer(GameObject root)
    {
        SpriteRenderer rootRenderer = root.GetComponent<SpriteRenderer>();
        if (rootRenderer != null)
            rootRenderer.enabled = false;
    }

    private static Sprite LoadSprite(string path, string spriteName)
    {
        Sprite sprite = AssetDatabase.LoadAllAssetsAtPath(path)
            .OfType<Sprite>()
            .FirstOrDefault(candidate => candidate.name == spriteName);
        if (sprite == null)
            throw new InvalidOperationException($"Sprite missing: {path} / {spriteName}");
        return sprite;
    }

    private static void ValidateTexture(string path, int expectedSpriteCount)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null
            || importer.textureType != TextureImporterType.Sprite
            || importer.spriteImportMode != SpriteImportMode.Multiple
            || importer.filterMode != FilterMode.Point
            || importer.textureCompression != TextureImporterCompression.Uncompressed
            || importer.mipmapEnabled
            || !importer.alphaIsTransparency)
        {
            throw new InvalidOperationException($"Invalid pixel-art import settings: {path}");
        }

        int spriteCount = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().Count();
        if (spriteCount != expectedSpriteCount)
            throw new InvalidOperationException($"Sprite count mismatch: {path} ({spriteCount}/{expectedSpriteCount})");
    }

    private static void ValidatePrefab<T>(string path, params string[] requiredChildren) where T : Component
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            RequireComponent<T>(root);
            foreach (string childName in requiredChildren)
            {
                Transform child = root.transform.Find(childName);
                if (child == null || child.GetComponent<SpriteRenderer>() == null)
                    throw new InvalidOperationException($"{path}: Visual child missing: {childName}");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static T RequireComponent<T>(GameObject root) where T : Component
    {
        T component = root.GetComponent<T>();
        if (component == null)
            throw new InvalidOperationException($"{root.name}: {typeof(T).Name} missing");
        return component;
    }

    private static SerializedProperty RequireProperty(SerializedObject serialized, string propertyName)
    {
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null)
            throw new InvalidOperationException($"{serialized.targetObject.name}: property missing: {propertyName}");
        return property;
    }

    private static void SetObject(SerializedObject serialized, string name, UnityEngine.Object value)
        => RequireProperty(serialized, name).objectReferenceValue = value;

    private static void SetColor(SerializedObject serialized, string name, Color value)
        => RequireProperty(serialized, name).colorValue = value;

    private static void SetFloat(SerializedObject serialized, string name, float value)
        => RequireProperty(serialized, name).floatValue = value;

    private static void SetVector3(SerializedObject serialized, string name, Vector3 value)
        => RequireProperty(serialized, name).vector3Value = value;
}
