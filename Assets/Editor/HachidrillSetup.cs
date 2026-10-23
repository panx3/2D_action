using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>既存の原画と共通ダメージ素材から新規Prefabだけを作成する。</summary>
public static class HachidrillSetup
{
    public const string SpritePath = "Assets/image_/ハチドリル_浮遊.png";
    public const string PrefabPath = "Assets/Prefabs/Enemies/Hachidrill.prefab";
    public const string ClipPath = "Assets/Animation/HachidrillHover.anim";
    public const string ControllerPath = "Assets/Animation/Hachidrill.controller";

    [MenuItem("Tools/Hachidrill/Create Prefab")]
    public static void CreatePrefab()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        foreach (string path in new[] { PrefabPath, ClipPath, ControllerPath })
            if (AssetDatabase.LoadMainAssetAtPath(path) != null)
                throw new InvalidOperationException("Already exists; preserve Inspector adjustments: " + path);
        Sprite[] frames = Frames();
        CheckSprites(frames);
        var common = AssetDatabase.LoadAssetAtPath<GameObject>(BeamScorpionSetup.PrefabPath);
        if (common == null || common.GetComponent<EnemyDamageFeedback>() == null)
            throw new InvalidOperationException("Common enemy damage feedback is missing.");

        var clip = new AnimationClip { name = "Hover", frameRate = 6f };
        var keys = new ObjectReferenceKeyframe[7];
        for (int i = 0; i < 7; i++) keys[i] = new ObjectReferenceKeyframe { time = i / 6f, value = frames[i % 6] };
        AnimationUtility.SetObjectReferenceCurve(clip, EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite"), keys);
        var clipSettings = AnimationUtility.GetAnimationClipSettings(clip);
        clipSettings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, clipSettings);
        AssetDatabase.CreateAsset(clip, ClipPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        var machine = controller.layers[0].stateMachine;
        var hover = machine.AddState("Hover"); hover.motion = clip; machine.defaultState = hover;
        EditorUtility.SetDirty(controller); AssetDatabase.SaveAssetIfDirty(controller);

        var root = new GameObject("Hachidrill");
        try
        {
            root.layer = common.layer;
            var body = root.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.constraints = RigidbodyConstraints2D.FreezeRotation | RigidbodyConstraints2D.FreezePositionY;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            var collider = root.AddComponent<BoxCollider2D>();
            collider.sharedMaterial = common.GetComponent<BoxCollider2D>().sharedMaterial;
            collider.size = new Vector2(0.9f, 0.76f); collider.offset = new Vector2(0f, -0.1f);

            var visualRoot = new GameObject("VisualRoot").transform;
            visualRoot.SetParent(root.transform, false); visualRoot.gameObject.layer = root.layer;
            var visual = new GameObject("Visual").transform;
            visual.SetParent(visualRoot, false); visual.gameObject.layer = root.layer;
            visual.localScale = new Vector3(2f, 2f, 1f);
            var renderer = visual.gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = frames[0]; renderer.sortingOrder = 5;
            renderer.sharedMaterial = common.transform.Find("Visual").GetComponent<SpriteRenderer>().sharedMaterial;
            var animator = visual.gameObject.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var health = root.AddComponent<EnemyHealth>();
            EditorUtility.CopySerialized(common.GetComponent<EnemyHealth>(), health);
            var flight = root.AddComponent<HachidrillController>();
            var movement = new SerializedObject(flight);
            Ref(movement, "visualRoot", visualRoot); Ref(movement, "visualRenderer", renderer); Ref(movement, "visualAnimator", animator);
            movement.ApplyModifiedPropertiesWithoutUndo();

            var hp = new SerializedObject(health);
            hp.FindProperty("maxHp").intValue = 1;
            Ref(hp, "hitFlashRenderer", renderer); Ref(hp, "hitAudioSource", null);
            hp.FindProperty("disableOnDeath").arraySize = 1;
            hp.FindProperty("disableOnDeath").GetArrayElementAtIndex(0).objectReferenceValue = flight;
            hp.ApplyModifiedPropertiesWithoutUndo();

            var feedback = root.AddComponent<EnemyDamageFeedback>();
            EditorUtility.CopySerialized(common.GetComponent<EnemyDamageFeedback>(), feedback);
            var effects = new SerializedObject(feedback);
            effects.FindProperty("bodyRenderers").arraySize = 1;
            effects.FindProperty("bodyRenderers").GetArrayElementAtIndex(0).objectReferenceValue = renderer;
            effects.FindProperty("gaugeOffset").vector3Value = new Vector3(0f, 1f, 0f);
            effects.FindProperty("criticalOffset").vector3Value = Vector3.zero;
            effects.FindProperty("defeatOffset").vector3Value = Vector3.zero;
            effects.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        HachidrillAttackSetup.Apply();
    }

    public static Sprite[] Frames() => AssetDatabase.LoadAllAssetsAtPath(SpritePath).OfType<Sprite>().OrderBy(s => s.rect.x).ToArray();

    private static void CheckSprites(Sprite[] frames)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(SpritePath);
        if (frames.Length != 6 || importer.filterMode != FilterMode.Point || importer.mipmapEnabled
            || importer.textureCompression != TextureImporterCompression.Uncompressed)
            throw new InvalidOperationException("Expected six Point/uncompressed/no-mipmap sprites.");
        foreach (var frame in frames)
            if (Mathf.Abs(frame.pivot.x - Mathf.Round(frame.pivot.x)) > 0.001f
                || Mathf.Abs(frame.pivot.y - Mathf.Round(frame.pivot.y)) > 0.001f)
                throw new InvalidOperationException("Align sprite pivots to the body on integer pixels before creation.");
    }

    [MenuItem("Tools/Hachidrill/Validate Prefab")]
    public static void Validate()
    {
        CheckSprites(Frames());
        var root = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (root == null || root.GetComponentsInChildren<Component>(true).Any(c => c == null))
            throw new InvalidOperationException("Missing Hachidrill prefab/script.");
        var flight = root.GetComponent<HachidrillController>();
        var health = root.GetComponent<EnemyHealth>();
        var body = root.GetComponent<Rigidbody2D>();
        if (flight == null || health == null || body == null || body.bodyType != RigidbodyType2D.Dynamic
            || body.gravityScale != 0f || (body.constraints & RigidbodyConstraints2D.FreezePositionY) == 0)
            throw new InvalidOperationException("Invalid flight/HP/physics configuration.");
        var settings = new SerializedObject(flight);
        foreach (string field in new[] { "visualRoot", "visualRenderer", "visualAnimator" })
            if (settings.FindProperty(field).objectReferenceValue == null) throw new InvalidOperationException("Missing " + field);
        if (new SerializedObject(health).FindProperty("disableOnDeath").GetArrayElementAtIndex(0).objectReferenceValue != flight)
            throw new InvalidOperationException("Death must stop flight.");
        var feedback = new SerializedObject(root.GetComponent<EnemyDamageFeedback>());
        foreach (string field in new[] { "flashMaterial", "gaugePrefab", "hitEffectPrefab", "criticalEffectPrefab", "defeatEffectPrefab" })
            if (feedback.FindProperty(field).objectReferenceValue == null) throw new InvalidOperationException("Missing feedback: " + field);
        var controller = (AnimatorController)root.GetComponentInChildren<Animator>().runtimeAnimatorController;
        var machine = controller.layers[0].stateMachine;
        if (machine.states.Length != 2 || machine.defaultState.name != "Hover" || machine.anyStateTransitions.Length != 0
            || controller.parameters.Length != 0)
            throw new InvalidOperationException("Expected Hover and Attack states.");
        var clip = (AnimationClip)machine.defaultState.motion;
        var bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
        if (!clip.isLooping || AnimationUtility.GetCurveBindings(clip).Length != 0 || bindings.Length != 1
            || bindings[0].propertyName != "m_Sprite"
            || AnimationUtility.GetObjectReferenceCurve(clip, bindings[0]).Take(6).Select(k => k.value).Distinct().Count() != 6)
            throw new InvalidOperationException("Expected six sprite-only loop frames.");
        var ai = root.GetComponent<HachidrillAttackController>();
        if (ai == null) throw new InvalidOperationException("Missing drill AI.");
        var attackData = new SerializedObject(ai);
        var hitbox = (BoxCollider2D)attackData.FindProperty("attackHitbox").objectReferenceValue;
        var attackClip = (AnimationClip)attackData.FindProperty("attackClip").objectReferenceValue;
        if (hitbox == null || hitbox.enabled || !hitbox.isTrigger || attackClip == null || attackClip.isLooping
            || attackData.FindProperty("attackFrames").arraySize != 6 || attackData.FindProperty("drillOffsets").arraySize != 6)
            throw new InvalidOperationException("Invalid attack references/loop/hitbox.");
        var disable = new SerializedObject(health).FindProperty("disableOnDeath");
        if (!Enumerable.Range(0, disable.arraySize).Any(i => disable.GetArrayElementAtIndex(i).objectReferenceValue == ai))
            throw new InvalidOperationException("Death must stop drill AI.");
        Debug.Log("[Hachidrill] Prefab PASS: Hover + nonloop Attack, six original frames each, disabled drill trigger, common HP/feedback/death.");
    }

    private static void Ref(SerializedObject target, string field, UnityEngine.Object value) => target.FindProperty(field).objectReferenceValue = value;
}
