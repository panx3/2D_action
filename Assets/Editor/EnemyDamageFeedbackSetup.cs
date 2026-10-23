using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>インポート済みの生成素材から共通Prefabを作り、サソリだけに適用する。</summary>
public static class EnemyDamageFeedbackSetup
{
    public const string ArtFolder = "Assets/Art/EnemyDamageFeedback";
    public const string PrefabFolder = "Assets/Prefabs/Enemies/DamageFeedback";
    public const string FlashMaterialPath = "Assets/Materials/EnemyHitFlash.mat";

    [MenuItem("Tools/Enemies/Damage Feedback/Create Assets And Apply To Scorpion")]
    public static void CreateAndApply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        var gauge = Sprites("GaugeAtlas"); var hit = Sprites("HitAtlas");
        var critical = Sprites("CriticalAtlas"); var defeat = Sprites("DefeatAtlas");
        if (gauge.Length != 3 || hit.Length != 4 || critical.Length != 8 || defeat.Length != 6)
            throw new InvalidOperationException("Expected sliced sprites: gauge3, hit4, critical8, defeat6.");
        Folder(PrefabFolder);
        if (AssetDatabase.LoadAssetAtPath<Material>(FlashMaterialPath) == null)
        {
            Shader shader = Shader.Find("IronBallGirl/Enemy Hit Flash");
            if (shader == null) throw new InvalidOperationException("Enemy Hit Flash shader is missing.");
            AssetDatabase.CreateAsset(new Material(shader), FlashMaterialPath);
        }
        CreateGauge(gauge);
        CreateOneShot("Hit", hit, 14f);
        CreateOneShot("Defeat", defeat, 14f);
        CreateCritical(critical);
        ApplyToScorpion(); Validate();
    }

    private static void ApplyToScorpion()
    {
        var root = PrefabUtility.LoadPrefabContents(BeamScorpionSetup.PrefabPath);
        try
        {
            var health = new SerializedObject(root.GetComponent<EnemyHealth>());
            health.FindProperty("maxHp").intValue = 3;
            health.FindProperty("maxDamagePerHit").intValue = 1;
            health.FindProperty("hitInvincibleDuration").floatValue = 0.25f;
            health.FindProperty("hitFlashDuration").floatValue = 0f;
            health.ApplyModifiedPropertiesWithoutUndo();
            var feedback = root.GetComponent<EnemyDamageFeedback>();
            if (feedback == null) feedback = root.AddComponent<EnemyDamageFeedback>();
            var data = new SerializedObject(feedback);
            data.FindProperty("bodyRenderers").arraySize = 1;
            data.FindProperty("bodyRenderers").GetArrayElementAtIndex(0).objectReferenceValue = root.transform.Find("Visual").GetComponent<SpriteRenderer>();
            Ref(data, "flashMaterial", AssetDatabase.LoadAssetAtPath<Material>(FlashMaterialPath));
            Ref(data, "gaugePrefab", AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/MiniHealthGauge.prefab").GetComponent<EnemyMiniHealthGauge>());
            Ref(data, "hitEffectPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/Hit.prefab"));
            Ref(data, "criticalEffectPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/Critical.prefab"));
            Ref(data, "defeatEffectPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/Defeat.prefab"));
            data.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, BeamScorpionSetup.PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    [MenuItem("Tools/Enemies/Damage Feedback/Validate")]
    public static void Validate()
    {
        var root = AssetDatabase.LoadAssetAtPath<GameObject>(BeamScorpionSetup.PrefabPath);
        if (root.GetComponent<EnemyHealth>().MaxHp != 3) throw new InvalidOperationException("Scorpion must have HP3.");
        var feedback = root.GetComponent<EnemyDamageFeedback>();
        if (feedback == null) throw new InvalidOperationException("EnemyDamageFeedback is missing.");
        var data = new SerializedObject(feedback);
        foreach (string name in new[] { "flashMaterial", "gaugePrefab", "hitEffectPrefab", "criticalEffectPrefab", "defeatEffectPrefab" })
            if (data.FindProperty(name).objectReferenceValue == null) throw new InvalidOperationException("Missing reference: " + name);
        var mat = AssetDatabase.LoadAssetAtPath<Material>(FlashMaterialPath);
        if (mat == null || !mat.shader.isSupported || ShaderUtil.ShaderHasError(mat.shader))
            throw new InvalidOperationException("Flash shader is invalid.");
        foreach (string name in new[] { "GaugeAtlas", "HitAtlas", "CriticalAtlas", "DefeatAtlas" })
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(ArtFolder + "/" + name + ".png");
            if (importer.filterMode != FilterMode.Point || importer.mipmapEnabled || importer.textureCompression != TextureImporterCompression.Uncompressed)
                throw new InvalidOperationException("Pixel-art import settings are invalid: " + name);
        }
        Debug.Log("[EnemyDamageFeedback] Assets validated: HP3, three-hit cap, generated sprites, flash and replaceable VFX.");
    }

    private static Sprite[] Sprites(string name) => AssetDatabase.LoadAllAssetsAtPath(ArtFolder + "/" + name + ".png")
        .OfType<Sprite>().OrderBy(sprite => sprite.name, StringComparer.Ordinal).ToArray();
    private static void Ref(SerializedObject data, string name, UnityEngine.Object value) => data.FindProperty(name).objectReferenceValue = value;
    private static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = path.Substring(0, path.LastIndexOf('/')); Folder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
    }
    private static void CreateGauge(Sprite[] sprites)
    {
        string path = PrefabFolder + "/MiniHealthGauge.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;
        var root = new GameObject("MiniHealthGauge");
        try
        {
            var gauge = root.AddComponent<EnemyMiniHealthGauge>(); var data = new SerializedObject(gauge);
            Ref(data, "frameSprite", sprites[0]); Ref(data, "litSprite", sprites[1]); Ref(data, "emptySprite", sprites[2]);
            data.ApplyModifiedPropertiesWithoutUndo(); PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    private static void CreateOneShot(string name, Sprite[] sprites, float fps)
    {
        string path = PrefabFolder + "/" + name + ".prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;
        var root = new GameObject("Enemy" + name);
        try { AddFlipbook(root, sprites, fps, false, 0f, 0f, Vector2.zero); PrefabUtility.SaveAsPrefabAsset(root, path); }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    private static void CreateCritical(Sprite[] sprites)
    {
        string path = PrefabFolder + "/Critical.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;
        var root = new GameObject("EnemyCritical");
        root.transform.localScale = new Vector3(1.6f, 1.6f, 1f);
        try
        {
            var sparks = new GameObject("MalfunctionSparks"); sparks.transform.SetParent(root.transform, false);
            sparks.transform.localPosition = new Vector3(-0.15f, 0.2f, 0f);
            AddFlipbook(sparks, sprites.Take(4).ToArray(), 12f, true, 0.65f, 0f, new Vector2(0.06f, 0.08f));
            var smoke = new GameObject("Smoke"); smoke.transform.SetParent(root.transform, false);
            smoke.transform.localPosition = new Vector3(0.2f, 0.1f, 0f);
            AddFlipbook(smoke, sprites.Skip(4).ToArray(), 8f, true, 0.9f, 0.2f, new Vector2(0.1f, 0.35f));
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    private static void AddFlipbook(GameObject root, Sprite[] sprites, float fps, bool loop, float gap, float delay, Vector2 drift)
    {
        var renderer = root.AddComponent<SpriteRenderer>(); renderer.sprite = sprites[0]; renderer.sortingOrder = 23;
        var effect = root.AddComponent<EnemySpriteEffect>(); var data = new SerializedObject(effect);
        Ref(data, "visual", renderer);
        var frames = data.FindProperty("frames"); frames.arraySize = sprites.Length;
        for (int i = 0; i < sprites.Length; i++) frames.GetArrayElementAtIndex(i).objectReferenceValue = sprites[i];
        data.FindProperty("framesPerSecond").floatValue = fps; data.FindProperty("loop").boolValue = loop;
        data.FindProperty("loopGap").floatValue = gap; data.FindProperty("startDelay").floatValue = delay;
        data.FindProperty("drift").vector2Value = drift; data.ApplyModifiedPropertiesWithoutUndo();
    }
}
