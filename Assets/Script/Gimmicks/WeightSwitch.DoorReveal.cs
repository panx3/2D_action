using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public partial class WeightSwitch
{
    [Header("Linked Door Reveal")]
    [SerializeField] private bool showDoorReveal = true;
    [SerializeField, Tooltip("未指定ならonPressedに接続されたGimmickDoor.Openから取得。")]
    private GimmickDoor doorToShow;
    [SerializeField] private CameraFollow doorRevealCamera;
    [SerializeField, Min(0f)] private float pressBeatDuration = 0.12f;
    [SerializeField, Min(0.01f)] private float panToDoorDuration = 1.6f;
    [SerializeField, Min(0f)] private float beforeDoorOpenDelay = 0.18f;
    [SerializeField, Min(0f)] private float doorOpenHoldDuration = 0.70f;
    [SerializeField, Min(0.01f)] private float returnToPlayerDuration = 0.40f;

    private Coroutine doorRevealRoutine;
    private bool revealActive, finishingReveal, activationDispatched, revealingSwitch;
    private bool previousPauseBlocked, previousPlayerBlocked, previousTimerPaused;
    private float revealPreviousTimeScale;
    private Vector3 revealReturnPosition;
    private Player revealPlayer;
    private PlayerHealth revealHealth;
    private GoalMenuController revealTimer;
    private PauseMenuController revealPause;

    public bool IsDoorRevealPlaying => revealActive;

    private void Start()
    {
        ResolveLinkedDoor()?.RegisterSwitch(this);
    }

    private GimmickDoor ResolveLinkedDoor()
    {
        if (doorToShow == null && onPressed != null)
        {
            for (int i = 0; i < onPressed.GetPersistentEventCount(); i++)
            {
                if (onPressed.GetPersistentTarget(i) is GimmickDoor door
                    && onPressed.GetPersistentListenerState(i) != UnityEngine.Events.UnityEventCallState.Off
                    && onPressed.GetPersistentMethodName(i) == nameof(GimmickDoor.Open))
                {
                    doorToShow = door;
                    break;
                }
            }
        }
        return doorToShow;
    }

    internal bool TryBeginSwitchReveal(GimmickDoor door)
    {
        if (isPressed || ResolveLinkedDoor() != door) return false;
        return TryBeginReveal(true);
    }

    private bool TryBeginDoorReveal()
    {
        return showDoorReveal && TryBeginReveal(false);
    }

    private bool TryBeginReveal(bool showSwitch)
    {
        if (revealActive || !isActiveAndEnabled) return false;
        ResolveLinkedDoor();
        if (doorToShow == null || !doorToShow.isActiveAndEnabled || doorToShow.IsOpen) return false;
        if (doorRevealCamera == null && Camera.main != null)
            doorRevealCamera = Camera.main.GetComponent<CameraFollow>();
        if (doorRevealCamera == null || !doorRevealCamera.isActiveAndEnabled) return false;
        revealPlayer = doorRevealCamera.FollowPlayer;
        if (revealPlayer == null || !revealPlayer.isActiveAndEnabled || revealPlayer.IsGoalInputBlocked
            || revealPlayer.IsGimmickInputBlocked) return false;
        var intro = revealPlayer.GetComponent<PlayerStartIntro>();
        if (intro != null && intro.IsPlaying) return false;
        revealHealth = revealPlayer.GetComponent<PlayerHealth>();
        if (revealHealth != null && (revealHealth.IsDead || revealHealth.IsRespawning)) return false;
        Scene playerScene = revealPlayer.gameObject.scene;
        revealPause = FindInScene<PauseMenuController>(playerScene);
        revealTimer = FindInScene<GoalMenuController>(playerScene);
        if (revealPause != null && (revealPause.IsPaused || revealPause.IsExternallyBlocked)) return false;
        var launcher = revealPlayer.GetComponent<MorningStarLauncher>();
        if (Time.timeScale <= 0f && (launcher == null || !launcher.IsHitStopping)) return false;
        if (!doorRevealCamera.BeginEventControl(this)) return false;

        revealReturnPosition = doorRevealCamera.transform.position;
        previousPlayerBlocked = revealPlayer.IsGimmickInputBlocked;
        previousPauseBlocked = revealPause != null && revealPause.IsExternallyBlocked;
        previousTimerPaused = revealTimer != null && revealTimer.IsGimmickPresentationPaused;
        // Input lock first cancels combat hit stop, restoring its saved time before we take it.
        revealPlayer.SetGimmickInputBlocked(true);
        revealPreviousTimeScale = Time.timeScale;
        if (revealPause != null) revealPause.SetExternalPauseBlocked(true);
        if (revealTimer != null) revealTimer.SetGimmickPresentationPaused(true);
        revealingSwitch = showSwitch;
        revealActive = true;
        Time.timeScale = 0f;
        doorRevealRoutine = StartCoroutine(DoorRevealRoutine());
        return true;
    }

    private IEnumerator DoorRevealRoutine()
    {
        try
        {
            yield return WaitForReveal(pressBeatDuration);
            if (!CanContinueReveal()) yield break;
            Vector3 target = revealingSwitch
                ? (detectionCollider != null ? detectionCollider.bounds.center : transform.position)
                : doorToShow.RevealFocusPosition;
            Vector3 focus = doorRevealCamera.ClampToBounds(target);
            focus.z = revealReturnPosition.z;
            yield return MoveRevealCamera(revealReturnPosition, focus, panToDoorDuration);
            if (!CanContinueReveal()) yield break;
            yield return WaitForReveal(beforeDoorOpenDelay);
            if (!CanContinueReveal()) yield break;
            if (!revealingSwitch) DispatchPressedConnection();
            yield return WaitForReveal(doorOpenHoldDuration);
            if (!CanContinueReveal()) yield break;
            yield return MoveRevealCamera(doorRevealCamera.transform.position, revealReturnPosition, returnToPlayerDuration);
        }
        finally { FinishDoorReveal(); }
    }

    private IEnumerator WaitForReveal(float seconds)
    {
        float elapsed = 0f;
        while (elapsed < seconds && CanContinueReveal())
        {
            yield return null;
            elapsed += Time.unscaledDeltaTime;
        }
    }

    private IEnumerator MoveRevealCamera(Vector3 from, Vector3 to, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration && CanContinueReveal())
        {
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, duration));
            float smooth = t * t * t * (t * (t * 6f - 15f) + 10f);
            doorRevealCamera.SetEventPosition(this, Vector3.LerpUnclamped(from, to, smooth));
            yield return null;
            elapsed += Time.unscaledDeltaTime;
        }
        if (CanContinueReveal()) doorRevealCamera.SetEventPosition(this, to);
    }

    private bool CanContinueReveal()
    {
        return revealActive && isActiveAndEnabled && doorToShow != null && doorToShow.isActiveAndEnabled
            && (!revealingSwitch || (!isPressed && !doorToShow.IsOpen))
            && Mathf.Approximately(Time.timeScale, 0f)
            && doorRevealCamera != null && doorRevealCamera.isActiveAndEnabled && doorRevealCamera.IsEventControlledBy(this)
            && revealPlayer != null && revealPlayer.isActiveAndEnabled && !revealPlayer.IsGoalInputBlocked
            && (revealHealth == null || (!revealHealth.IsDead && !revealHealth.IsRespawning))
            && (revealTimer == null || !revealTimer.IsGoalReached)
            && (revealPause == null || !revealPause.IsPaused);
    }

    private void DispatchPressedConnection()
    {
        if (activationDispatched) return;
        activationDispatched = true;
        onPressed?.Invoke();
    }

    private void CancelDoorReveal()
    {
        if (doorRevealRoutine != null)
        {
            StopCoroutine(doorRevealRoutine);
            doorRevealRoutine = null;
        }
        FinishDoorReveal();
    }

    private void FinishDoorReveal()
    {
        if (!revealActive || finishingReveal) return;
        finishingReveal = true;
        try
        {
            // An interrupted reveal still commits its one-shot switch connection.
            if (!revealingSwitch && Application.isPlaying && gameObject.scene.isLoaded)
                DispatchPressedConnection();
        }
        finally
        {
            bool keepStopped = (revealPlayer != null && revealPlayer.IsGoalInputBlocked)
                || (revealTimer != null && revealTimer.IsGoalReached)
                || (revealPause != null && revealPause.IsPaused);
            bool ownsCamera = doorRevealCamera != null && doorRevealCamera.IsEventControlledBy(this);
            if (ownsCamera)
            {
                if (!keepStopped) doorRevealCamera.SetEventPosition(this, revealReturnPosition);
                doorRevealCamera.EndEventControl(this);
            }
            if (revealPlayer != null) revealPlayer.SetGimmickInputBlocked(previousPlayerBlocked);
            if (revealTimer != null) revealTimer.SetGimmickPresentationPaused(previousTimerPaused);
            if (revealPause != null && !keepStopped) revealPause.SetExternalPauseBlocked(previousPauseBlocked);
            if (!keepStopped && Mathf.Approximately(Time.timeScale, 0f)) Time.timeScale = revealPreviousTimeScale;
            revealActive = false;
            revealingSwitch = false;
            doorRevealRoutine = null;
            finishingReveal = false;
        }
    }

    private static T FindInScene<T>(Scene scene) where T : Component
    {
        foreach (T component in Object.FindObjectsByType<T>(FindObjectsInactive.Include))
            if (component.gameObject.scene == scene) return component;
        return null;
    }
}
