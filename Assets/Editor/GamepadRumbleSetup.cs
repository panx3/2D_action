using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Installs the production Resources prefab without saving or changing the open scene.</summary>
public static class GamepadRumbleSetup
{
    public const string PrefabPath = "Assets/Resources/Feedback/GamepadRumbleManager.prefab";
    [MenuItem("Tools/Tekkyu/Install Gamepad Rumble")]
    public static string Install()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null) return "Existing rumble settings preserved.";
        Directory.CreateDirectory("Assets/Resources/Feedback");
        AssetDatabase.Refresh();
        var root = new GameObject("GamepadRumbleManager");
        try
        {
            root.AddComponent<GamepadRumbleManager>();
            root.AddComponent<GameplayRumbleFeedback>();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        return "Installed " + PrefabPath + "; scene not saved.";
    }
}

// Play Mode exit/domain reload must stop hardware even when normal runtime destruction is skipped.
[InitializeOnLoad]
internal static class GamepadRumbleEditorLifecycle
{
    static GamepadRumbleEditorLifecycle()
    {
        EditorApplication.playModeStateChanged += StateChanged;
        AssemblyReloadEvents.beforeAssemblyReload += Stop;
    }
    private static void StateChanged(PlayModeStateChange state)
    { if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.EnteredEditMode) Stop(); }
    private static void Stop()
    {
        if (GamepadRumbleManager.Instance != null) GamepadRumbleManager.Instance.StopAll();
        foreach (var pad in UnityEngine.InputSystem.Gamepad.all)
        { try { pad.SetMotorSpeeds(0f, 0f); pad.ResetHaptics(); } catch (Exception) { } }
    }
}
