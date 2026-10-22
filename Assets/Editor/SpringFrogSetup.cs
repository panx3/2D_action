using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class SpringFrogSetup
{
    public const string PrefabPath = "Assets/Prefabs/Enemies/SpringFrog.prefab";
    private const string SpritePath = "Assets/image_/バネガエル.png";

    [MenuItem("Tools/Spring Frog/Create Prefab")]
    public static void CreatePrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
            throw new InvalidOperationException("SpringFrog already exists. Edit its Inspector to preserve tuned values.");

        Sprite[] frames = AssetDatabase.LoadAllAssetsAtPath(SpritePath).OfType<Sprite>()
            .OrderBy(sprite => sprite.rect.x).ToArray();
        if (frames.Length != 6)
            throw new InvalidOperationException("SpringFrog requires the six existing sliced sprites.");
        TextureImporter importer = AssetImporter.GetAtPath(SpritePath) as TextureImporter;
        if (importer == null || importer.filterMode != FilterMode.Point || importer.mipmapEnabled
            || importer.textureCompression != TextureImporterCompression.Uncompressed)
            throw new InvalidOperationException("SpringFrog sprites must use Point, no mipmaps and no compression.");

        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/WrenchCrab.prefab");
        AudioClip jumpClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SFX/Imported/TrampolineBounce.wav");
        if (source == null || jumpClip == null)
            throw new InvalidOperationException("WrenchCrab or TrampolineBounce is missing.");

        GameObject root = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(source));
        try
        {
            root.name = "SpringFrog";
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;
            UnityEngine.Object.DestroyImmediate(root.GetComponent<WrenchCrab>());
            Animator animator = root.GetComponentInChildren<Animator>();
            if (animator != null)
                UnityEngine.Object.DestroyImmediate(animator);

            SpriteRenderer renderer = root.transform.Find("Visual").GetComponent<SpriteRenderer>();
            renderer.sprite = frames[0];
            renderer.flipX = false;
            renderer.transform.localScale = new Vector3(4f, 4f, 1f);
            BoxCollider2D collider = root.GetComponent<BoxCollider2D>();
            collider.size = new Vector2(1.12f, 0.88f);
            collider.offset = new Vector2(0f, -0.04f);

            AudioSource audio = root.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.loop = false;
            audio.spatialBlend = 0f;
            audio.outputAudioMixerGroup = source.GetComponentsInChildren<AudioSource>(true)
                .Select(item => item.outputAudioMixerGroup).FirstOrDefault(group => group != null);

            SpringFrog frog = root.AddComponent<SpringFrog>();
            SerializedObject movement = new SerializedObject(frog);
            movement.FindProperty("_visualRenderer").objectReferenceValue = renderer;
            movement.FindProperty("_groundCheck").objectReferenceValue = CreateCheck(root, "GroundCheck", new Vector2(0f, -0.455f));
            movement.FindProperty("_edgeCheck").objectReferenceValue = CreateCheck(root, "EdgeCheck", new Vector2(0.585f, -0.455f));
            movement.FindProperty("_wallCheck").objectReferenceValue = CreateCheck(root, "WallCheck", new Vector2(0.56f, -0.04f));
            movement.FindProperty("_audioSource").objectReferenceValue = audio;
            movement.FindProperty("_jumpSE").objectReferenceValue = jumpClip;
            SerializedProperty sprites = movement.FindProperty("_jumpFrames");
            sprites.arraySize = frames.Length;
            for (int frameIndex = 0; frameIndex < frames.Length; frameIndex++)
                sprites.GetArrayElementAtIndex(frameIndex).objectReferenceValue = frames[frameIndex];
            movement.ApplyModifiedPropertiesWithoutUndo();

            EnemyHealth health = root.GetComponent<EnemyHealth>();
            SerializedObject healthData = new SerializedObject(health);
            healthData.FindProperty("maxHp").intValue = 1;
            healthData.FindProperty("hitFlashRenderer").objectReferenceValue = renderer;
            SerializedProperty disable = healthData.FindProperty("disableOnDeath");
            disable.arraySize = 1;
            disable.GetArrayElementAtIndex(0).objectReferenceValue = frog;
            healthData.FindProperty("fragmentPrefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/EnemyFragment.prefab");
            healthData.FindProperty("fragmentCount").intValue = 10;
            healthData.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject shadow = new SerializedObject(root.GetComponent<GroundProjectedShadow>());
            shadow.FindProperty("width").floatValue = 1.06f;
            shadow.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        Validate();
    }

    [MenuItem("Tools/Spring Frog/Validate Prefab")]
    public static void Validate()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
            throw new InvalidOperationException("SpringFrog prefab is missing.");
        SpringFrog frog = prefab.GetComponent<SpringFrog>();
        if (frog == null || prefab.GetComponent<EnemyHealth>().MaxHp != 1 || prefab.GetComponent<WrenchCrab>() != null)
            throw new InvalidOperationException("SpringFrog movement/health configuration is invalid.");
        SerializedObject settings = new SerializedObject(frog);
        foreach (string property in new[] { "_groundCheck", "_edgeCheck", "_wallCheck", "_audioSource", "_jumpSE", "_visualRenderer" })
            if (settings.FindProperty(property).objectReferenceValue == null)
                throw new InvalidOperationException("Missing SpringFrog reference: " + property);
        if (settings.FindProperty("_jumpFrames").arraySize != 6)
            throw new InvalidOperationException("SpringFrog requires six animation frames.");
        AudioSource audio = prefab.GetComponent<AudioSource>();
        if (audio == null || audio.playOnAwake || audio.loop)
            throw new InvalidOperationException("SpringFrog AudioSource must not auto-play or loop.");
        Debug.Log("[SpringFrog] Prefab validation passed: six frames, HP1, checks and jump SFX assigned.");
    }

    private static Transform CreateCheck(GameObject root, string name, Vector2 position)
    {
        Transform check = new GameObject(name).transform;
        check.SetParent(root.transform, false);
        check.localPosition = position;
        return check;
    }
}
