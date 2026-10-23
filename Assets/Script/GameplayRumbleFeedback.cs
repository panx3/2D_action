using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Adapts committed gameplay events to the common manager; never owns input, charge time, HP or physics.</summary>
[DisallowMultipleComponent, DefaultExecutionOrder(200)]
public sealed class GameplayRumbleFeedback : MonoBehaviour
{
    private GamepadRumbleManager manager;
    private Player player;
    private PlayerHealth health;
    private MorningStarLauncher launcher;
    private PauseMenuController pause;
    private WeightSwitch[] switches;
    private bool wasCharging;
    private uint chargeHandle;
    private float previousCharge;

    private void OnEnable()
    {
        manager = GetComponent<GamepadRumbleManager>();
        if (manager == null) { enabled = false; return; }
        SceneManager.sceneLoaded += SceneLoaded;
        SceneManager.activeSceneChanged += ActiveSceneChanged;
        SceneManager.sceneUnloaded += SceneUnloaded;
        BindScene();
    }
    private void OnDisable()
    {
        SceneManager.sceneLoaded -= SceneLoaded;
        SceneManager.activeSceneChanged -= ActiveSceneChanged;
        SceneManager.sceneUnloaded -= SceneUnloaded;
        Unbind();
        if (manager != null) manager.SetSuspended(true);
    }
    private void BindScene()
    {
        Unbind();
        Scene scene = SceneManager.GetActiveScene();
        foreach (var candidate in FindObjectsByType<Player>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (candidate.gameObject.scene == scene) { player = candidate; break; }
        if (player != null)
        {
            health = player.GetComponent<PlayerHealth>();
            launcher = player.GetComponent<MorningStarLauncher>();
        }
        foreach (var candidate in FindObjectsByType<PauseMenuController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (candidate.gameObject.scene == scene) { pause = candidate; break; }
        if (health != null) { health.OnDamaged += Damaged; health.OnDead += Died; }
        if (launcher != null) { launcher.LaunchCommitted += Launched; launcher.EnemyHitConfirmed += EnemyHit; }
        if (pause != null) pause.PauseStateChanged += PauseChanged;
        switches = FindObjectsByType<WeightSwitch>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var weightSwitch in switches) if (weightSwitch.gameObject.scene == scene) weightSwitch.Pressed += SwitchPressed;
        wasCharging = launcher != null && launcher.State == MorningStarLauncher.MorningStarState.SpinCharging;
        if (manager != null) manager.SetSuspended(!CanPlay());
    }
    private void Unbind()
    {
        StopCharge();
        if (health != null) { health.OnDamaged -= Damaged; health.OnDead -= Died; }
        if (launcher != null) { launcher.LaunchCommitted -= Launched; launcher.EnemyHitConfirmed -= EnemyHit; }
        if (pause != null) pause.PauseStateChanged -= PauseChanged;
        if (switches != null) foreach (var weightSwitch in switches) if (weightSwitch != null) weightSwitch.Pressed -= SwitchPressed;
        player = null; health = null; launcher = null; pause = null; switches = null;
        wasCharging = false;
    }
    private bool CanPlay() => manager != null && player != null && player.isActiveAndEnabled && player.InputEnabled
        && !player.IsGoalInputBlocked && health != null && !health.IsDead && !health.IsRespawning
        && launcher != null && launcher.isActiveAndEnabled && (pause == null || !pause.IsPaused)
        && (Time.timeScale > 0f || launcher.IsHitStopping);

    private void LateUpdate()
    {
        if (manager == null) return;
        if (health != null && health.IsDead)
        {
            StopCharge();
            // Death fades on unscaled time, including the already-open Game Over screen.
            // A real pause/focus loss still cancels it. Never accept more gameplay after the tail.
            manager.SetSuspended((pause != null && pause.IsPaused) || manager.ActiveEvent != GamepadRumbleEvent.PlayerDeath);
            return;
        }
        bool canPlay = CanPlay();
        manager.SetSuspended(!canPlay);
        bool charging = launcher != null && launcher.isActiveAndEnabled && launcher.State == MorningStarLauncher.MorningStarState.SpinCharging;
        if (!charging || !canPlay) StopCharge();
        else if (!wasCharging)
        {
            previousCharge = launcher.ChargeProgress;
            chargeHandle = manager.BeginContinuous(GamepadRumbleEvent.Charge, previousCharge);
        }
        else if (chargeHandle != 0 && Mathf.Abs(launcher.ChargeProgress - previousCharge) >= 0.005f)
        {
            previousCharge = launcher.ChargeProgress;
            manager.UpdateContinuous(chargeHandle, previousCharge);
        }
        // A discarded charge is not restarted after damage, OFF, pause or reconnect.
        wasCharging = charging;
    }
    private void StopCharge()
    {
        if (manager != null) manager.StopContinuous(chargeHandle);
        chargeHandle = 0;
    }
    private void Damaged() { if (CanPlay() && health.CurrentHp > 0) manager.Play(GamepadRumbleEvent.PlayerDamage); }
    private void Died()
    {
        StopCharge();
        if (manager == null || (pause != null && pause.IsPaused)) return;
        manager.SetSuspended(false);
        manager.Play(GamepadRumbleEvent.PlayerDeath);
    }
    private void Launched(float amount)
    {
        StopCharge();
        if (CanPlay()) manager.Play(amount >= 1f ? GamepadRumbleEvent.FullChargeThrow : GamepadRumbleEvent.NormalThrow);
    }
    private void EnemyHit() { if (CanPlay()) manager.Play(GamepadRumbleEvent.EnemyHit); }
    private void SwitchPressed() { if (CanPlay()) manager.Play(GamepadRumbleEvent.WeightSwitch); }
    private void PauseChanged(bool paused) { StopCharge(); manager.SetSuspended(paused || !CanPlay()); }
    private void SceneLoaded(Scene scene, LoadSceneMode mode) => BindScene();
    private void ActiveSceneChanged(Scene oldScene, Scene newScene) => BindScene();
    private void SceneUnloaded(Scene scene) { if (player != null && player.gameObject.scene == scene) { Unbind(); manager.SetSuspended(true); } }
}
