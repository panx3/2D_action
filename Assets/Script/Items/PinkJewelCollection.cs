using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>One attempt's three jewels. A normal scene load starts fresh; only an explicit respawn reload carries state.</summary>
[DefaultExecutionOrder(-800)]
[DisallowMultipleComponent]
public sealed class PinkJewelCollection : MonoBehaviour
{
    public const int SlotCount = 3;
    public static PinkJewelCollection Current { get; private set; }

    private static int attemptSceneHandle = int.MinValue;
    private static string attemptScenePath;
    private static int collectedMask;
    private static string respawnReloadPath;
    private GoalMenuController goal;
    private int finalMask;

    public event Action<int> Changed;
    public int CollectedMask => collectedMask;
    public bool IsFinalized { get; private set; }
    public int ResultMask => IsFinalized ? finalMask : collectedMask;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        SceneManager.sceneLoaded -= CompleteRespawnReload;
        Current = null;
        attemptSceneHandle = int.MinValue;
        attemptScenePath = respawnReloadPath = null;
        collectedMask = 0;
    }

    private void Awake()
    {
        if (Current != null && Current != this)
        {
            Debug.LogError("[PinkJewel] Only one collection manager is allowed per stage.", this);
            enabled = false;
            return;
        }
        Scene scene = gameObject.scene;
        if (attemptSceneHandle != scene.handle)
        {
            bool resume = respawnReloadPath == scene.path && attemptScenePath == scene.path;
            if (!resume) collectedMask = 0;
            respawnReloadPath = null;
            attemptSceneHandle = scene.handle;
            attemptScenePath = scene.path;
        }
        Current = this;
        goal = FindAnyObjectByType<GoalMenuController>(FindObjectsInactive.Include);
    }

    private void LateUpdate() => CaptureResultIfCleared();

    private void OnDestroy()
    {
        if (Current == this) Current = null;
    }

    public bool IsCollected(int slot) => slot >= 1 && slot <= SlotCount
        && (collectedMask & (1 << (slot - 1))) != 0;

    public bool TryCollect(int slot)
    {
        CaptureResultIfCleared();
        if (!isActiveAndEnabled || Current != this || IsFinalized
            || slot < 1 || slot > SlotCount || IsCollected(slot)) return false;
        collectedMask |= 1 << (slot - 1);
        Changed?.Invoke(collectedMask);
        return true;
    }

    private void CaptureResultIfCleared()
    {
        // The existing goal owns the clear moment, including its celebration before the menu opens.
        if (IsFinalized || goal == null || (!goal.IsClearTimeFinalized && !goal.IsGoalReached)) return;
        finalMask = collectedMask;
        IsFinalized = true;
    }

    /// <summary>
    /// Call immediately before a SAME-STAGE scene reload used for an ordinary respawn.
    /// Current GimmickRespawnController restores in place, so it needs no hook.
    /// Never call from Retry/Title/Next; those loads intentionally start a new attempt.
    /// </summary>
    public static void PrepareForRespawnReload()
    {
        if (Current == null || Current.IsFinalized) return;
        respawnReloadPath = Current.gameObject.scene.path;
        SceneManager.sceneLoaded -= CompleteRespawnReload;
        SceneManager.sceneLoaded += CompleteRespawnReload;
    }

    private static void CompleteRespawnReload(Scene scene, LoadSceneMode mode)
    {
        // Consume the intent on the next load even if it goes through Title or another stage.
        // For the matching stage, Awake has already read it before sceneLoaded is raised.
        respawnReloadPath = null;
        SceneManager.sceneLoaded -= CompleteRespawnReload;
    }
}
