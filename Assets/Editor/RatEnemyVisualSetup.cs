using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// ネズミEnemyの固定判定、4-frame歩行Visual、専用破片Prefabを再現可能に設定する。
/// AI・HP・Rigidbody2D・攻撃値は変更しない。
/// </summary>
public static class RatEnemyVisualSetup
{
    private const string WalkSheetPath = "Assets/image_/エネミー歩行アニ.png";
    private const string FragmentSheetPath = "Assets/image_/エネミー撃破破片.png";
    private const string BaseEnemyPath = "Assets/Prefabs/Enemy.prefab";
    private const string TekkyuEnemyPath = "Assets/Prefabs/Enemies/TekkyuEnemy.prefab";
    private const string FragmentPrefabPath = "Assets/Prefabs/Enemies/EnemyFragment.prefab";
    private const string AnimatorPath = "Assets/Animation/EnemyVisual.controller";
    private const string WalkClipPath = "Assets/Animation/EnemyWalkVisual.anim";
    private const string IdleClipPath = "Assets/Animation/EnemyIdleVisual.anim";

    private static readonly Vector2 BaseColliderSize = new Vector2(0.76f, 0.62f);
    private static readonly Vector2 BaseColliderOffset = new Vector2(-0.05f, -0.095f);

    [MenuItem("Tools/Tekkyu Enemy/Apply Rat Visual And Fragments")]
    public static void Apply()
    {
        ConfigureSpriteSheet(WalkSheetPath, 55f, 0.4255319f);
        ConfigureSpriteSheet(FragmentSheetPath, 100f, 0.5f);
        ConfigureFragmentPrefab();
        ConfigureEnemyPrefab(BaseEnemyPath, Vector3.one, BaseColliderSize, BaseColliderOffset);
        ConfigureEnemyPrefab(
            TekkyuEnemyPath,
            new Vector3(2f, 2f, 1f),
            BaseColliderSize * 2f,
            BaseColliderOffset * 2f);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Validate();
        Debug.Log("[RatEnemyVisualSetup] Fixed collider, sprite-only 10 FPS walk and EnemyFragment sprites applied.");
    }

    [MenuItem("Tools/Tekkyu Enemy/Validate Rat Visual And Fragments")]
    public static void Validate()
    {
        ValidateClip(WalkClipPath, 4);
        ValidateClip(IdleClipPath, 1);
        ValidateEnemyPrefab(BaseEnemyPath, Vector3.one, BaseColliderSize, BaseColliderOffset);
        ValidateEnemyPrefab(
            TekkyuEnemyPath,
            new Vector3(2f, 2f, 1f),
            BaseColliderSize * 2f,
            BaseColliderOffset * 2f);

        GameObject fragmentPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FragmentPrefabPath);
        Check(fragmentPrefab != null, "EnemyFragment prefab is missing");
        EnemyFragment fragment = fragmentPrefab.GetComponent<EnemyFragment>();
        Check(fragment != null && fragment.FragmentSpriteCount >= 4,
            "EnemyFragment does not contain multiple fragment sprites");
        Check(fragmentPrefab.GetComponent<SpriteRenderer>() != null, "EnemyFragment SpriteRenderer is missing");
        Check(fragmentPrefab.GetComponent<Rigidbody2D>() != null, "EnemyFragment Rigidbody2D is missing");
        Check(fragmentPrefab.GetComponent<Collider2D>() == null,
            "EnemyFragment must not collide with Player or other fragments");
    }

    private static void ConfigureSpriteSheet(string path, float pixelsPerUnit, float pivotY)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
            throw new InvalidOperationException($"TextureImporter is missing: {path}");

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = pixelsPerUnit;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.crunchedCompression = false;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;

#pragma warning disable 0618
        SpriteMetaData[] sprites = importer.spritesheet;
        for (int i = 0; i < sprites.Length; i++)
        {
            SpriteMetaData sprite = sprites[i];
            sprite.alignment = (int)SpriteAlignment.Custom;
            sprite.pivot = new Vector2(0.5f, pivotY);
            sprites[i] = sprite;
        }
        importer.spritesheet = sprites;
#pragma warning restore 0618
        importer.SaveAndReimport();
    }

    private static void ConfigureFragmentPrefab()
    {
        Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath(FragmentSheetPath)
            .OfType<Sprite>()
            .OrderBy(SpriteIndex)
            .ToArray();
        if (sprites.Length < 4)
            throw new InvalidOperationException("Fragment sheet must contain multiple sliced sprites.");

        GameObject root = PrefabUtility.LoadPrefabContents(FragmentPrefabPath);
        try
        {
            foreach (Collider2D collider in root.GetComponents<Collider2D>())
                UnityEngine.Object.DestroyImmediate(collider);

            SpriteRenderer renderer = Require<SpriteRenderer>(root);
            renderer.sprite = sprites[0];
            EnemyFragment fragment = root.GetComponent<EnemyFragment>();
            if (fragment == null)
                fragment = root.AddComponent<EnemyFragment>();

            SerializedObject serialized = new SerializedObject(fragment);
            SerializedProperty array = serialized.FindProperty("fragmentSprites");
            array.arraySize = sprites.Length;
            for (int i = 0; i < sprites.Length; i++)
                array.GetArrayElementAtIndex(i).objectReferenceValue = sprites[i];
            serialized.FindProperty("fadeDuration").floatValue = 0.4f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, FragmentPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ConfigureEnemyPrefab(
        string path,
        Vector3 visualScale,
        Vector2 colliderSize,
        Vector2 colliderOffset)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            Transform visual = root.transform.Find("Visual");
            if (visual == null)
                throw new InvalidOperationException($"Visual child is missing: {path}");

            Sprite[] frames = AssetDatabase.LoadAllAssetsAtPath(WalkSheetPath)
                .OfType<Sprite>()
                .OrderBy(SpriteIndex)
                .ToArray();
            SpriteRenderer renderer = Require<SpriteRenderer>(visual.gameObject);
            Animator animator = Require<Animator>(visual.gameObject);
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(AnimatorPath);
            if (frames.Length != 4 || controller == null)
                throw new InvalidOperationException("Walk frames or existing Enemy Animator is missing.");

            visual.localPosition = Vector3.zero;
            visual.localRotation = Quaternion.identity;
            visual.localScale = visualScale;
            renderer.sprite = frames[0];
            animator.runtimeAnimatorController = controller;

            BoxCollider2D collider = Require<BoxCollider2D>(root);
            collider.size = colliderSize;
            collider.offset = colliderOffset;
            collider.isTrigger = false;

            Enemy enemy = Require<Enemy>(root);
            SerializedObject enemySerialized = new SerializedObject(enemy);
            enemySerialized.FindProperty("_visualAnimator").objectReferenceValue = animator;
            enemySerialized.FindProperty("_visualRenderer").objectReferenceValue = renderer;
            enemySerialized.ApplyModifiedPropertiesWithoutUndo();

            EnemyHealth health = Require<EnemyHealth>(root);
            SerializedObject healthSerialized = new SerializedObject(health);
            healthSerialized.FindProperty("fragmentPrefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>(FragmentPrefabPath);
            healthSerialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ValidateClip(string path, int expectedSpriteCount)
    {
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        Check(clip != null, $"AnimationClip is missing: {path}");
        Check(Mathf.Approximately(clip.frameRate, 10f), $"{clip.name} is not 10 FPS");
        Check(AnimationUtility.GetCurveBindings(clip).Length == 0,
            $"{clip.name} changes Transform or another numeric property");
        EditorCurveBinding[] objectBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
        Check(objectBindings.Length == 1
              && objectBindings[0].type == typeof(SpriteRenderer)
              && objectBindings[0].propertyName == "m_Sprite",
            $"{clip.name} must change SpriteRenderer.sprite only");
        Check(AnimationUtility.GetObjectReferenceCurve(clip, objectBindings[0]).Length == expectedSpriteCount,
            $"{clip.name} has an unexpected sprite frame count");
    }

    private static void ValidateEnemyPrefab(
        string path,
        Vector3 visualScale,
        Vector2 colliderSize,
        Vector2 colliderOffset)
    {
        GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Check(root != null, $"Enemy prefab is missing: {path}");
        Transform visual = root.transform.Find("Visual");
        Check(root.GetComponent<Rigidbody2D>() != null && root.GetComponent<Enemy>() != null,
            $"Root physics/AI is missing: {path}");
        Check(root.GetComponent<SpriteRenderer>() == null && root.GetComponent<Animator>() == null,
            $"Root still owns visual components: {path}");
        Check(visual != null && visual.GetComponent<SpriteRenderer>() != null
              && visual.GetComponent<Animator>() != null,
            $"Visual child is incomplete: {path}");
        Check(Vector3.Distance(visual.localScale, visualScale) < 0.001f,
            $"Visual scale is unexpected: {path}");

        BoxCollider2D collider = root.GetComponent<BoxCollider2D>();
        Check(collider != null && Vector2.Distance(collider.size, colliderSize) < 0.001f
              && Vector2.Distance(collider.offset, colliderOffset) < 0.001f,
            $"Root collider is not fixed to the rat body/feet: {path}");
    }

    private static int SpriteIndex(Sprite sprite)
    {
        int separator = sprite.name.LastIndexOf('_');
        return separator >= 0 && int.TryParse(sprite.name.Substring(separator + 1), out int index)
            ? index
            : int.MaxValue;
    }

    private static T Require<T>(GameObject gameObject) where T : Component
    {
        T component = gameObject.GetComponent<T>();
        if (component == null)
            throw new InvalidOperationException($"{gameObject.name} is missing {typeof(T).Name}");
        return component;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("[RatEnemyVisualSetup] " + message);
    }
}
