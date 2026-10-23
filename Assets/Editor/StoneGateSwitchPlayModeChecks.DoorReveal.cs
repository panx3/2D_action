using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;

public static partial class StoneGateSwitchPlayModeChecks
{
    private static int revealCase, revealEvents;
    private static WeightSwitch revealSwitch;
    private static GimmickDoor revealDoor;
    private static CameraFollow revealFollow;
    private static Player revealActor;
    private static GoalMenuController revealClock;
    private static PauseMenuController revealPauseMenu;
    private static AudioSource revealSound;
    private static Vector3 cameraBeforeReveal, playerBeforeReveal;
    private static double caseBegan, openedAt, returningAt;
    private static double clockBeforeReveal;
    private static float scaledTimeBeforeReveal;
    private static bool sawPan, checkedFrozen, checkedOpenFade;

    private static void TickDoorRevealChecks()
    {
        switch (revealCase)
        {
            case 0:
                revealFollow = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
                if (revealFollow == null) throw new Exception("Production CameraFollow is missing.");
                // The production start intro keeps its existing priority.
                if (revealFollow.IsEventControlled || revealFollow.IsRespawnCameraHeld) return;
                revealActor = revealFollow.FollowPlayer;
                if (revealActor != null && revealActor.GetComponent<PlayerStartIntro>() != null
                    && revealActor.GetComponent<PlayerStartIntro>().IsPlaying) return;
                Require(revealFollow != null, "Production CameraFollow exists for reveal checks");
                Require(revealActor != null && !revealActor.IsGoalInputBlocked, "Production Player is ready after its intro");
                revealClock = UnityEngine.Object.FindAnyObjectByType<GoalMenuController>(FindObjectsInactive.Include);
                revealPauseMenu = UnityEngine.Object.FindAnyObjectByType<PauseMenuController>(FindObjectsInactive.Include);
                Require(revealClock != null && revealPauseMenu != null, "Production clear-time and Pause owners are present");
                revealFollow.GetComponent<CameraShake2D>()?.ResetShake();
                Time.timeScale = 0.65f;
                var launcher = revealActor.GetComponent<MorningStarLauncher>();
                launcher.SendMessage("PlayCombatHitStop", 0.08f);
                Require(launcher.IsHitStopping, "Combat hit stop is active before the reveal handoff");
                PrepareRevealCase();
                playerBeforeReveal = revealActor.transform.position;
                scaledTimeBeforeReveal = Time.time;
                clockBeforeReveal = revealClock != null ? revealClock.ElapsedSeconds : 0d;
                PressRevealSwitch();
                Require(revealSwitch.IsDoorRevealPlaying && Time.timeScale == 0f && revealActor.IsGimmickInputBlocked,
                    "Persistent Open connection resolves its door; first press locks input and freezes the world after sound");
                Require(!launcher.IsHitStopping, "Reveal cancels the earlier hit stop before acquiring time");
                Require(!revealDoor.IsOpen && revealEvents == 0, "Door stays closed at the switch press");
                revealPauseMenu?.OpenPause();
                Require(revealPauseMenu == null || (!revealPauseMenu.IsPaused && revealPauseMenu.IsExternallyBlocked),
                    "Pause cannot take time ownership during the reveal");
                sawPan = checkedFrozen = checkedOpenFade = false;
                openedAt = returningAt = -1d;
                revealCase = 1;
                break;

            case 1:
                Vector3 focus = revealFollow.ClampToBounds(revealDoor.RevealFocusPosition);
                focus.z = cameraBeforeReveal.z;
                if (revealSwitch.IsDoorRevealPlaying)
                {
                    RequireFrozenDuringReveal();
                    if (!checkedFrozen && EditorApplication.timeSinceStartup - caseBegan > 0.30)
                    {
                        Require(revealSound.isPlaying, "Press sound continues while timeScale is zero");
                        Require((revealActor.transform.position - playerBeforeReveal).sqrMagnitude < 0.0001f,
                            "Actual Player transform remains frozen during the camera move");
                        Require(Mathf.Abs(Time.time - scaledTimeBeforeReveal) < 0.001f, "Scaled game time is frozen");
                        Require(revealClock == null || Math.Abs(revealClock.ElapsedSeconds - clockBeforeReveal) < 0.03d,
                            "Clear-time counter excludes the reveal");
                        Require(!revealDoor.IsOpen, "Door remains closed during the outbound pan");
                        checkedFrozen = true;
                    }
                    if (!revealDoor.IsOpen && (revealFollow.transform.position - cameraBeforeReveal).magnitude > 0.5f)
                        sawPan = true;
                    if (revealDoor.IsOpen && openedAt < 0d)
                    {
                        openedAt = EditorApplication.timeSinceStartup;
                        Require(sawPan && (revealFollow.transform.position - focus).magnitude < 0.03f && revealEvents == 1,
                            "Only arrival at the door dispatches its Open connection once");
                    }
                    if (openedAt > 0d && !checkedOpenFade && EditorApplication.timeSinceStartup - openedAt > 0.36d)
                    {
                        Require(revealDoor.transform.Find("Door_Barrier").GetComponent<SpriteRenderer>().color.a < 0.001f,
                            "Door membrane finishes fading while world time remains stopped");
                        checkedOpenFade = true;
                    }
                    if (openedAt > 0d && returningAt < 0d && (revealFollow.transform.position - focus).magnitude > 0.2f)
                        returningAt = EditorApplication.timeSinceStartup;
                }
                else
                {
                    Require(checkedFrozen && checkedOpenFade && openedAt > 0d && returningAt > 0d,
                        "Reveal visits the closed door, opens it, holds, then returns");
                    Require(Mathf.Abs(Time.timeScale - 0.65f) < 0.001f, "Completion restores the pre-hit-stop non-1 time scale");
                    Require((revealFollow.transform.position - cameraBeforeReveal).magnitude < 0.08f
                        && EditorApplication.timeSinceStartup - returningAt < 1.0d,
                        "Camera returns to the captured Player view faster than the outbound move");
                    Require(!revealActor.IsGimmickInputBlocked && !revealFollow.IsEventControlled
                        && (revealClock == null || !revealClock.IsGimmickPresentationPaused)
                        && (revealPauseMenu == null || !revealPauseMenu.IsExternallyBlocked),
                        "Completion releases camera, input, timer and pause ownership");
                    Time.timeScale = 1f;
                    PrepareRevealCase();
                    PressRevealSwitch();
                    revealCase = 2;
                }
                break;

            case 2:
                if (EditorApplication.timeSinceStartup - caseBegan < 0.25d) return;
                revealSwitch.enabled = false;
                Require(!revealSwitch.IsDoorRevealPlaying && revealDoor.IsOpen && revealEvents == 1,
                    "Disabling a switch mid-pan commits its latched connection");
                Require(Time.timeScale == 1f && !revealActor.IsGimmickInputBlocked && !revealFollow.IsEventControlled
                    && (revealFollow.transform.position - cameraBeforeReveal).magnitude < 0.03f,
                    "Interrupted reveal restores time, controls and the Player view");
                var other = new GameObject("OtherCameraPresentationProbe");
                Require(revealFollow.BeginEventControl(other), "Another presentation can own the camera");
                PrepareRevealCase();
                PressRevealSwitch();
                Require(!revealSwitch.IsDoorRevealPlaying && revealDoor.IsOpen && revealEvents == 1
                    && revealFollow.IsEventControlledBy(other) && Time.timeScale == 1f,
                    "Camera contention falls back to direct activation without stealing ownership");
                revealFollow.EndEventControl(other);
                UnityEngine.Object.Destroy(other);
                PrepareRevealCase();
                PressRevealSwitch();
                revealActor.SetGoalInputBlocked(true);
                revealCase = 3;
                break;

            case 3:
                if (revealSwitch.IsDoorRevealPlaying) return;
                Require(Time.timeScale == 0f && revealActor.IsGoalInputBlocked && !revealActor.IsGimmickInputBlocked,
                    "Goal handoff keeps time stopped and preserves the Goal input lock");
                revealActor.SetGoalInputBlocked(false);
                revealPauseMenu?.SetExternalPauseBlocked(false);
                Time.timeScale = 1f;
                PrepareRevealCase();
                PressRevealSwitch();
                revealFollow.HoldForRespawn();
                revealCase = 4;
                break;

            case 4:
                if (revealSwitch.IsDoorRevealPlaying) return;
                Require(Time.timeScale == 1f && !revealActor.IsGimmickInputBlocked && revealFollow.IsRespawnCameraHeld,
                    "Respawn handoff releases reveal time without stealing the held camera");
                revealFollow.EndRespawnTravel();
                Require(runtimeErrors == 0, "No runtime Error/Exception including reveal lifecycle checks");
                Finish("PASS (" + checks.Count + ")\n" + string.Join("\n", checks));
                break;
        }
    }

    private static void RequireFrozenDuringReveal()
    {
        if (Time.timeScale != 0f || !revealActor.IsGimmickInputBlocked || !revealFollow.IsEventControlledBy(revealSwitch))
            throw new Exception("Reveal lost its clock, input or camera ownership before returning.");
    }

    private static void PrepareRevealCase()
    {
        cameraBeforeReveal = revealFollow.transform.position;
        revealDoor = Spawn<GimmickDoor>("Assets/Prefabs/Gimmicks/GimmickDoor.prefab",
            new Vector3(cameraBeforeReveal.x + 14f, cameraBeforeReveal.y + 0.5f, 0f));
        revealSwitch = Spawn<WeightSwitch>("Assets/Prefabs/Gimmicks/WeightSwitch.prefab", new Vector3(-30f, 0.15f, 0f));
        typeof(WeightSwitch).GetField("doorRevealCamera", Fields).SetValue(revealSwitch, revealFollow);
        revealEvents = 0;
        var evt = (UnityEvent)typeof(WeightSwitch).GetField("onPressed", Fields).GetValue(revealSwitch);
        UnityEditor.Events.UnityEventTools.AddPersistentListener(evt, revealDoor.Open);
        evt.AddListener(() => revealEvents++);
        revealSound = revealSwitch.gameObject.AddComponent<AudioSource>();
        revealSound.playOnAwake = false;
        typeof(WeightSwitch).GetField("pressAudioSource", Fields).SetValue(revealSwitch, revealSound);
    }

    private static void PressRevealSwitch()
    {
        caseBegan = EditorApplication.timeSinceStartup;
        revealSwitch.SendMessage("OnTriggerEnter2D", ball.GetComponent<Collider2D>());
    }
}
