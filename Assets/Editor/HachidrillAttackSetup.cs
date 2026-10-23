using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>既存Prefabの値/参照を保ち、攻撃用の差分だけを追加する。</summary>
public static class HachidrillAttackSetup
{
    public const string SpritePath = "Assets/image_/ハチドリル_攻撃.png";
    public const string ClipPath = "Assets/Animation/HachidrillAttack.anim";
    public static Sprite[] Frames() => AssetDatabase.LoadAllAssetsAtPath(SpritePath).OfType<Sprite>().OrderBy(s => s.rect.x).ToArray();

    [MenuItem("Tools/Hachidrill/Add Drill Attack")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        Sprite[] frames = Frames();
        var importer = (TextureImporter)AssetImporter.GetAtPath(SpritePath);
        if (frames.Length != 6 || importer.filterMode != FilterMode.Point || importer.mipmapEnabled
            || importer.textureCompression != TextureImporterCompression.Uncompressed)
            throw new InvalidOperationException("Expected six Point/no-mipmap/uncompressed attack sprites.");
        foreach (var frame in frames)
            if (Mathf.Abs(frame.pivot.x - Mathf.Round(frame.pivot.x)) > 0.001f || Mathf.Abs(frame.pivot.y - Mathf.Round(frame.pivot.y)) > 0.001f)
                throw new InvalidOperationException("Align attack pivots to the body before applying.");

        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
        if (clip == null)
        {
            clip = new AnimationClip { name = "Attack", frameRate = 8f };
            var keys = new ObjectReferenceKeyframe[6];
            for (int i = 0; i < keys.Length; i++) keys[i] = new ObjectReferenceKeyframe { time = i / 8f, value = frames[i] };
            AnimationUtility.SetObjectReferenceCurve(clip, EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite"), keys);
            var data = AnimationUtility.GetAnimationClipSettings(clip); data.loopTime = false; data.stopTime = 0.75f;
            AnimationUtility.SetAnimationClipSettings(clip, data); AssetDatabase.CreateAsset(clip, ClipPath);
        }
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(HachidrillSetup.ControllerPath);
        var machine = controller.layers[0].stateMachine;
        var attackState = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == "Attack");
        if (attackState == null)
        {
            attackState = machine.AddState("Attack"); attackState.motion = clip;
            var hover = machine.states.Select(s => s.state).Single(s => s.name == "Hover");
            var exit = attackState.AddTransition(hover); exit.hasExitTime = true; exit.exitTime = 1f; exit.duration = 0f;
            EditorUtility.SetDirty(controller); AssetDatabase.SaveAssetIfDirty(controller);
        }
        var root = PrefabUtility.LoadPrefabContents(HachidrillSetup.PrefabPath);
        try
        {
            var ai = root.GetComponent<HachidrillAttackController>();
            if (ai == null)
            {
                ai = root.AddComponent<HachidrillAttackController>();
                var visual = root.GetComponentInChildren<Animator>().transform;
                var hitboxRoot = new GameObject("AttackHitbox"); hitboxRoot.layer = root.layer; hitboxRoot.transform.SetParent(visual, false);
                var hitbox = hitboxRoot.AddComponent<BoxCollider2D>(); hitbox.isTrigger = true; hitbox.enabled = false;
                hitbox.size = new Vector2(0.16f, 0.16f);
                var data = new SerializedObject(ai);
                data.FindProperty("attackHitbox").objectReferenceValue = hitbox;
                data.FindProperty("attackClip").objectReferenceValue = clip;
                var sprites = data.FindProperty("attackFrames"); sprites.arraySize = 6;
                var offsets = data.FindProperty("drillOffsets"); offsets.arraySize = 6;
                // 各コマの実際のドリル先端周辺（原画ピクセル座標）。突き出しは3/5コマ目。
                Vector2[] tips = { new Vector2(48,84), new Vector2(102,65), new Vector2(143,60),
                    new Vector2(215,64), new Vector2(257,55), new Vector2(326,84) };
                for (int i = 0; i < 6; i++)
                {
                    sprites.GetArrayElementAtIndex(i).objectReferenceValue = frames[i];
                    offsets.GetArrayElementAtIndex(i).vector2Value = (tips[i] - frames[i].rect.position - frames[i].pivot) / frames[i].pixelsPerUnit;
                }
                data.ApplyModifiedPropertiesWithoutUndo();
                hitbox.transform.localPosition = offsets.GetArrayElementAtIndex(2).vector2Value;
                var hp = new SerializedObject(root.GetComponent<EnemyHealth>());
                var disable = hp.FindProperty("disableOnDeath"); int count = disable.arraySize;
                disable.arraySize = count + 1; disable.GetArrayElementAtIndex(count).objectReferenceValue = ai;
                hp.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, HachidrillSetup.PrefabPath);
            }
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        HachidrillSetup.Validate();
    }
}
