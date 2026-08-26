using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class PlayerJumpAnimationSetup
{
    private const string ControllerPath = "Assets/Animation/Player.controller";
    private const string SpriteSheetPath = "Assets/image_/jump_test_jump.png";
    private const string JumpUpPath = "Assets/Animation/JumpUp.anim";
    private const string JumpFallPath = "Assets/Animation/JumpFall.anim";
    private const string JumpLandPath = "Assets/Animation/JumpLand.anim";

    [MenuItem("鉄球少女/Player/3段階ジャンプアニメーションを設定")]
    public static void Apply()
    {
        Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath(SpriteSheetPath)
            .OfType<Sprite>()
            .OrderBy(sprite => sprite.name, StringComparer.Ordinal)
            .ToArray();
        if (sprites.Length != 4)
            throw new InvalidOperationException($"{SpriteSheetPath} のSprite数が4ではありません: {sprites.Length}");

        AnimationClip jumpUp = CreateOrUpdateClip(
            JumpUpPath,
            new[]
            {
                new ObjectReferenceKeyframe { time = 0f, value = sprites[0] },
                new ObjectReferenceKeyframe { time = 0.10f, value = sprites[1] },
            });
        AnimationClip jumpFall = CreateOrUpdateClip(
            JumpFallPath,
            new[]
            {
                new ObjectReferenceKeyframe { time = 0f, value = sprites[2] },
            });
        AnimationClip jumpLand = CreateOrUpdateClip(
            JumpLandPath,
            new[]
            {
                new ObjectReferenceKeyframe { time = 0f, value = sprites[3] },
                new ObjectReferenceKeyframe { time = 5f / 60f, value = sprites[3] },
            });

        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null || controller.layers.Length == 0)
            throw new InvalidOperationException(ControllerPath + " を読み込めませんでした。");

        EnsureParameter(controller, "Jump", AnimatorControllerParameterType.Bool, false);
        EnsureParameter(controller, "VerticalSpeed", AnimatorControllerParameterType.Float, true);
        EnsureParameter(controller, "Land", AnimatorControllerParameterType.Trigger, true);

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        AnimatorState idle = FindState(machine, "Idle");
        AnimatorState walk = FindState(machine, "Walk");
        if (idle == null || walk == null)
            throw new InvalidOperationException("既存のIdle / Walk Stateが見つかりません。");

        RemovePreviousJumpStates(machine);

        AnimatorState up = machine.AddState("JumpUp", new Vector3(500f, 10f));
        AnimatorState fall = machine.AddState("JumpFall", new Vector3(700f, 10f));
        AnimatorState land = machine.AddState("JumpLand", new Vector3(900f, 10f));
        up.motion = jumpUp;
        fall.motion = jumpFall;
        land.motion = jumpLand;

        AnimatorStateTransition toUp = machine.AddAnyStateTransition(up);
        ConfigureImmediate(toUp);
        toUp.AddCondition(AnimatorConditionMode.If, 0f, "Jump");
        toUp.AddCondition(AnimatorConditionMode.Greater, 0.01f, "VerticalSpeed");

        AnimatorStateTransition toFall = machine.AddAnyStateTransition(fall);
        ConfigureImmediate(toFall);
        toFall.AddCondition(AnimatorConditionMode.If, 0f, "Jump");
        toFall.AddCondition(AnimatorConditionMode.Less, 0.01f, "VerticalSpeed");

        AnimatorStateTransition toLand = machine.AddAnyStateTransition(land);
        ConfigureImmediate(toLand);
        toLand.AddCondition(AnimatorConditionMode.If, 0f, "Land");

        AnimatorStateTransition upToFall = up.AddTransition(fall);
        ConfigureImmediate(upToFall);
        upToFall.AddCondition(AnimatorConditionMode.Less, 0.01f, "VerticalSpeed");

        // Scene開始直後、最初のPhysics接触が確定する前にJumpFallへ入った場合の復帰経路。
        // 実着地時はAnyStateのLand Trigger遷移が先にJumpLandへ遷移する。
        AnimatorStateTransition fallToIdle = fall.AddTransition(idle);
        ConfigureImmediate(fallToIdle);
        fallToIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "Jump");
        fallToIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "Walk");

        AnimatorStateTransition fallToWalk = fall.AddTransition(walk);
        ConfigureImmediate(fallToWalk);
        fallToWalk.AddCondition(AnimatorConditionMode.IfNot, 0f, "Jump");
        fallToWalk.AddCondition(AnimatorConditionMode.If, 0f, "Walk");

        AnimatorStateTransition landToIdle = land.AddTransition(idle);
        ConfigureLandingExit(landToIdle);
        landToIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "Walk");

        AnimatorStateTransition landToWalk = land.AddTransition(walk);
        ConfigureLandingExit(landToWalk);
        landToWalk.AddCondition(AnimatorConditionMode.If, 0f, "Walk");

        EditorUtility.SetDirty(jumpUp);
        EditorUtility.SetDirty(jumpFall);
        EditorUtility.SetDirty(jumpLand);
        EditorUtility.SetDirty(machine);
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[JumpAnimationSetup] JumpUp / JumpFall / JumpLandを設定しました。JumpLand=約0.10秒");
    }

    public static void Validate()
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        AnimationClip up = AssetDatabase.LoadAssetAtPath<AnimationClip>(JumpUpPath);
        AnimationClip fall = AssetDatabase.LoadAssetAtPath<AnimationClip>(JumpFallPath);
        AnimationClip land = AssetDatabase.LoadAssetAtPath<AnimationClip>(JumpLandPath);

        string states = string.Join(",", machine.states.Select(child => child.state.name));
        string parameters = string.Join(",", controller.parameters.Select(parameter => parameter.name + ":" + parameter.type));
        int upFrames = GetSpriteFrames(up).Length;
        int fallFrames = GetSpriteFrames(fall).Select(frame => frame.value).Distinct().Count();
        int landFrames = GetSpriteFrames(land).Select(frame => frame.value).Distinct().Count();
        Debug.Log($"[JumpAnimationValidation] states={states}; params={parameters}; upKeys={upFrames}; fallUniqueSprites={fallFrames}; landUniqueSprites={landFrames}; landLength={land.length:F3}");

        if (FindState(machine, "Idle") == null || FindState(machine, "Walk") == null
            || FindState(machine, "JumpUp") == null || FindState(machine, "JumpFall") == null
            || FindState(machine, "JumpLand") == null || upFrames != 2
            || fallFrames != 1 || landFrames != 1 || land.length < 0.08f || land.length > 0.12f)
        {
            throw new InvalidOperationException("3段階ジャンプAnimatorの検証に失敗しました。");
        }
    }

    private static AnimationClip CreateOrUpdateClip(string path, ObjectReferenceKeyframe[] frames)
    {
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null)
        {
            clip = new AnimationClip();
            AssetDatabase.CreateAsset(clip, path);
        }

        foreach (EditorCurveBinding existing in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            AnimationUtility.SetObjectReferenceCurve(clip, existing, null);

        clip.name = System.IO.Path.GetFileNameWithoutExtension(path);
        clip.frameRate = 60f;
        clip.wrapMode = WrapMode.Once;
        EditorCurveBinding binding = new EditorCurveBinding
        {
            path = string.Empty,
            type = typeof(SpriteRenderer),
            propertyName = "m_Sprite",
        };
        AnimationUtility.SetObjectReferenceCurve(clip, binding, frames);

        SerializedObject serialized = new SerializedObject(clip);
        SerializedProperty loop = serialized.FindProperty("m_AnimationClipSettings.m_LoopTime");
        if (loop != null)
            loop.boolValue = false;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return clip;
    }

    private static void EnsureParameter(
        AnimatorController controller,
        string name,
        AnimatorControllerParameterType type,
        bool addIfMissing)
    {
        AnimatorControllerParameter existing = controller.parameters.FirstOrDefault(parameter => parameter.name == name);
        if (existing != null)
        {
            if (existing.type != type)
                throw new InvalidOperationException($"Animator Parameter {name} の型が想定と異なります。");
            return;
        }

        if (!addIfMissing)
            throw new InvalidOperationException("既存Animator Parameter " + name + " が見つかりません。");
        controller.AddParameter(name, type);
    }

    private static void RemovePreviousJumpStates(AnimatorStateMachine machine)
    {
        HashSet<AnimatorState> jumpStates = machine.states
            .Select(child => child.state)
            .Where(state => state.name == "Jump" || state.name == "JumpUp"
                || state.name == "JumpFall" || state.name == "JumpLand")
            .ToHashSet();

        foreach (AnimatorStateTransition transition in machine.anyStateTransitions.ToArray())
        {
            if (jumpStates.Contains(transition.destinationState)
                || transition.conditions.Any(IsJumpCondition))
                machine.RemoveAnyStateTransition(transition);
        }

        foreach (ChildAnimatorState child in machine.states.ToArray())
        foreach (AnimatorStateTransition transition in child.state.transitions.ToArray())
        {
            if (jumpStates.Contains(transition.destinationState)
                || transition.conditions.Any(IsJumpCondition))
                child.state.RemoveTransition(transition);
        }

        foreach (AnimatorState state in jumpStates)
            machine.RemoveState(state);
    }

    private static bool IsJumpCondition(AnimatorCondition condition)
    {
        return condition.parameter == "Jump" || condition.parameter == "VerticalSpeed"
            || condition.parameter == "Land";
    }

    private static AnimatorState FindState(AnimatorStateMachine machine, string name)
    {
        foreach (ChildAnimatorState child in machine.states)
            if (child.state.name == name)
                return child.state;
        return null;
    }

    private static ObjectReferenceKeyframe[] GetSpriteFrames(AnimationClip clip)
    {
        if (clip == null)
            return Array.Empty<ObjectReferenceKeyframe>();
        EditorCurveBinding binding = AnimationUtility.GetObjectReferenceCurveBindings(clip)
            .FirstOrDefault(candidate => candidate.propertyName == "m_Sprite");
        return string.IsNullOrEmpty(binding.propertyName)
            ? Array.Empty<ObjectReferenceKeyframe>()
            : AnimationUtility.GetObjectReferenceCurve(clip, binding);
    }

    private static void ConfigureImmediate(AnimatorStateTransition transition)
    {
        transition.hasExitTime = false;
        transition.duration = 0f;
        transition.hasFixedDuration = true;
        transition.canTransitionToSelf = false;
        transition.interruptionSource = TransitionInterruptionSource.None;
    }

    private static void ConfigureLandingExit(AnimatorStateTransition transition)
    {
        transition.hasExitTime = true;
        transition.exitTime = 1f;
        transition.duration = 0f;
        transition.hasFixedDuration = true;
        transition.canTransitionToSelf = false;
        transition.interruptionSource = TransitionInterruptionSource.None;
    }
}
