using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

public enum GamepadRumbleEvent { PlayerDamage, EnemyHit, NormalThrow, FullChargeThrow, WeightSwitch, PlayerDeath, Charge, Custom }

[Serializable]
public sealed class GamepadRumblePattern
{
    public GamepadRumbleEvent Event;
    [Range(0f, 1f)] public float LowFrequencyIntensity;
    [Range(0f, 1f)] public float HighFrequencyIntensity;
    [Min(0f)] public float Duration;
    public int Priority;
    public AnimationCurve IntensityCurve = AnimationCurve.Linear(0f, 1f, 1f, 1f);

    public GamepadRumblePattern(GamepadRumbleEvent kind, float low, float high, float duration, int priority)
    { Event = kind; LowFrequencyIntensity = low; HighFrequencyIntensity = high; Duration = duration; Priority = priority; }
    public GamepadRumblePattern(float low, float high, float duration, int priority)
        : this(GamepadRumbleEvent.Custom, low, high, duration, priority) { }
}

/// <summary>Single writer for two-motor rumble. Requests replace or are discarded; nothing is queued to resume.</summary>
[DisallowMultipleComponent, DefaultExecutionOrder(1000)]
public sealed class GamepadRumbleManager : MonoBehaviour
{
    public const string ResourcePath = "Feedback/GamepadRumbleManager";
    private const double CommandInterval = 1d / 30d;
    [SerializeField] private bool rumbleEnabled = true;
    [SerializeField, Range(0f, 1f)] private float masterIntensity = 1f;
    [SerializeField] private GamepadRumblePattern[] patterns =
    {
        new GamepadRumblePattern(GamepadRumbleEvent.PlayerDamage, 0.6f, 0.8f, 0.20f, 80),
        new GamepadRumblePattern(GamepadRumbleEvent.EnemyHit, 0.25f, 0.45f, 0.08f, 50),
        new GamepadRumblePattern(GamepadRumbleEvent.NormalThrow, 0.15f, 0.25f, 0.06f, 30),
        new GamepadRumblePattern(GamepadRumbleEvent.FullChargeThrow, 0.5f, 0.7f, 0.15f, 60),
        new GamepadRumblePattern(GamepadRumbleEvent.WeightSwitch, 0.2f, 0.35f, 0.10f, 40),
        new GamepadRumblePattern(GamepadRumbleEvent.PlayerDeath, 0.8f, 0.9f, 0.50f, 100)
            { IntensityCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f) },
        new GamepadRumblePattern(GamepadRumbleEvent.Charge, 0.12f, 0.2f, 0f, 10)
    };
    [SerializeField, Tooltip("Actual Launcher.ChargeProgress (0..1) -> intensity multiplier. No independent charge timer.")]
    private AnimationCurve chargeRumbleCurve = AnimationCurve.EaseInOut(0f, 0.15f, 1f, 1f);

    public static GamepadRumbleManager Instance { get; private set; }
    public bool RumbleEnabled { get => rumbleEnabled; set { rumbleEnabled = value; if (!value) StopAll(); } }
    public float MasterIntensity { get => masterIntensity; set { masterIntensity = Unit(value); if (masterIntensity <= 0f) StopAll(); } }
    public GamepadRumbleEvent? ActiveEvent => active != null ? active.Event : (GamepadRumbleEvent?)null;

    private Gamepad gamepad;
    private GamepadRumblePattern active;
    private bool continuous, suspended, hasFocus, applicationPaused;
    private bool usingGamepad;
    private Mouse activityMouse;
    private Gamepad activityGamepad;
    private Vector2 previousMousePosition, previousLeftStick, previousRightStick;
    private uint generation, activeHandle;
    private float chargeAmount, sentLow, sentHigh;
    private double startedAt, nextWriteAt, refreshAt;
    private readonly HashSet<int> failedDevices = new HashSet<int>();
    private readonly List<PendingStop> pendingStops = new List<PendingStop>();
    private sealed class PendingStop { public Gamepad Pad; public double Next, Until; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;
        var existing = FindAnyObjectByType<GamepadRumbleManager>();
        if (existing != null) { existing.Register(); return; }
        var prefab = Resources.Load<GamepadRumbleManager>(ResourcePath);
        if (prefab != null) Instantiate(prefab).name = "GamepadRumbleManager";
        else new GameObject("GamepadRumbleManager").AddComponent<GamepadRumbleManager>();
    }

    private void Awake() => Register();
    private void Register()
    {
        if (Instance != null && Instance != this)
        {
            enabled = false;
            var duplicateBridge = GetComponent<GameplayRumbleFeedback>();
            if (duplicateBridge != null) duplicateBridge.enabled = false;
            Destroy(gameObject);
            return;
        }
        Instance = this;
        hasFocus = Application.isFocused;
        if (Application.isPlaying) DontDestroyOnLoad(gameObject);
        if (GetComponent<GameplayRumbleFeedback>() == null) gameObject.AddComponent<GameplayRumbleFeedback>();
    }
    private void OnEnable()
    {
        usingGamepad = false;
        activityMouse = Mouse.current;
        previousMousePosition = activityMouse != null ? activityMouse.position.ReadValue() : Vector2.zero;
        activityGamepad = null;
        previousLeftStick = previousRightStick = Vector2.zero;
        InputSystem.onAfterUpdate += UpdateInputDevice;
        InputSystem.onDeviceChange += DeviceChanged;
        SceneManager.sceneLoaded += SceneLoaded;
        SceneManager.sceneUnloaded += SceneUnloaded;
        SceneManager.activeSceneChanged += ActiveSceneChanged;
    }
    private void OnDisable()
    {
        InputSystem.onAfterUpdate -= UpdateInputDevice;
        InputSystem.onDeviceChange -= DeviceChanged;
        SceneManager.sceneLoaded -= SceneLoaded;
        SceneManager.sceneUnloaded -= SceneUnloaded;
        SceneManager.activeSceneChanged -= ActiveSceneChanged;
        StopAll();
        ResetOwnedDevices();
    }
    private void OnDestroy() { if (Instance == this) Instance = null; }
    private void OnApplicationQuit() { StopAll(); ResetOwnedDevices(); }
    private void OnApplicationFocus(bool focused) { hasFocus = focused; if (!focused) StopAll(); }
    private void OnApplicationPause(bool paused) { applicationPaused = paused; if (paused) StopAll(); }
    private void OnValidate()
    {
        masterIntensity = Unit(masterIntensity);
        if (Application.isPlaying && (!rumbleEnabled || masterIntensity <= 0f)) StopAll();
    }

    public static bool PlayEvent(GamepadRumbleEvent kind) => Instance != null && Instance.Play(kind);
    public bool Play(GamepadRumbleEvent kind) => Play(FindPattern(kind));
    public bool Play(GamepadRumblePattern pattern)
    {
        if (pattern == null || pattern.Duration <= 0f || float.IsNaN(pattern.Duration) || float.IsInfinity(pattern.Duration)) return false;
        return Accept(pattern, false, 0f) != 0;
    }
    // Future bosses/gimmicks can supply a reusable Inspector pattern without copying motor code.
    public uint BeginContinuous(GamepadRumbleEvent kind, float normalizedAmount = 0f)
        => Accept(FindPattern(kind), true, normalizedAmount);
    public void UpdateContinuous(uint handle, float normalizedAmount)
    {
        if (continuous && handle != 0 && handle == activeHandle) chargeAmount = Unit(normalizedAmount);
    }
    public void StopContinuous(uint handle)
    {
        if (continuous && handle != 0 && handle == activeHandle) StopAll();
    }
    public void SetSuspended(bool value)
    {
        if (suspended == value) return;
        suspended = value;
        if (value) StopAll();
    }
    public bool IsContinuousActive(uint handle) => continuous && active != null && handle != 0 && handle == activeHandle;

    private GamepadRumblePattern FindPattern(GamepadRumbleEvent kind)
    {
        if (patterns != null) foreach (var pattern in patterns) if (pattern != null && pattern.Event == kind) return pattern;
        return null;
    }
    private uint Accept(GamepadRumblePattern pattern, bool isContinuous, float amount)
    {
        if (!usingGamepad || !isActiveAndEnabled || !rumbleEnabled || suspended || !hasFocus || applicationPaused || masterIntensity <= 0f || pattern == null) return 0;
        SyncDevice();
        if (gamepad == null || failedDevices.Contains(gamepad.deviceId)) return 0;
        double now = Time.unscaledTimeAsDouble;
        // Expired requests have no priority, even before LateUpdate has processed their end.
        if (active != null && !continuous && now - startedAt >= active.Duration) active = null;
        if (active != null && active.Priority > pattern.Priority) return 0;
        active = pattern; continuous = isContinuous; chargeAmount = Unit(amount); startedAt = now;
        activeHandle = ++generation; if (activeHandle == 0) activeHandle = ++generation;
        // A fresh request must never be silenced by retries from an older stop.
        pendingStops.RemoveAll(p => p.Pad == gamepad);
        return activeHandle;
    }
    private void SyncDevice()
    {
        Gamepad desired = Gamepad.current;
        if (desired != null && (!desired.added || !desired.enabled)) desired = null;
        if (gamepad == desired) return;
        StopAll(); // Do not transfer an old effect to a newly selected/reconnected pad.
        gamepad = desired;
        sentLow = sentHigh = 0f;
    }
    private void LateUpdate()
    {
        SyncDevice();
        if ((!usingGamepad || !rumbleEnabled || suspended || !hasFocus || applicationPaused || masterIntensity <= 0f) && active != null) StopAll();
        PumpStops();
        if (active == null || gamepad == null) return;
        double now = Time.unscaledTimeAsDouble;
        float t = continuous ? chargeAmount : (float)((now - startedAt) / active.Duration);
        if (!continuous && t >= 1f) { StopAll(); return; }
        AnimationCurve curve = continuous ? chargeRumbleCurve : active.IntensityCurve;
        float gain = Unit(masterIntensity) * Unit(curve != null && curve.length > 0 ? curve.Evaluate(Unit(t)) : 1f);
        float low = Unit(active.LowFrequencyIntensity) * gain, high = Unit(active.HighFrequencyIntensity) * gain;
        bool changed = Mathf.Abs(low - sentLow) >= 1f / 255f || Mathf.Abs(high - sentHigh) >= 1f / 255f;
        if (now < nextWriteAt || (!changed && now < refreshAt)) return;
        if (Write(gamepad, low, high)) { sentLow = low; sentHigh = high; }
        nextWriteAt = now + CommandInterval;
        refreshAt = now + 0.1d;
    }

    private void UpdateInputDevice()
    {
        // Run before gameplay Update/FixedUpdate submits feedback, including mouse input
        // read directly by the Launcher rather than through PlayerInput actions.
        if (InputState.currentUpdateType != InputUpdateType.Dynamic
            && InputState.currentUpdateType != InputUpdateType.Fixed) return;

        bool keyboardMouseActive = false;
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.wasUpdatedThisFrame)
            foreach (var key in keyboard.allKeys)
                if (key.wasPressedThisFrame || key.wasReleasedThisFrame) { keyboardMouseActive = true; break; }

        Mouse mouse = Mouse.current;
        if (mouse != activityMouse)
        {
            activityMouse = mouse;
            previousMousePosition = mouse != null ? mouse.position.ReadValue() : Vector2.zero;
        }
        if (mouse != null)
        {
            Vector2 position = mouse.position.ReadValue();
            keyboardMouseActive |= (position - previousMousePosition).sqrMagnitude > 0.01f
                || mouse.scroll.ReadValue().sqrMagnitude > 0.01f;
            previousMousePosition = position;
            if (mouse.wasUpdatedThisFrame)
                foreach (var control in mouse.allControls)
                    if (control is ButtonControl button && (button.wasPressedThisFrame || button.wasReleasedThisFrame))
                        keyboardMouseActive = true;
        }

        Gamepad pad = Gamepad.current;
        if (pad != activityGamepad)
        {
            activityGamepad = pad;
            previousLeftStick = previousRightStick = Vector2.zero;
        }
        bool gamepadActive = false;
        if (pad != null && pad.wasUpdatedThisFrame)
        {
            Vector2 left = pad.leftStick.ReadValue(), right = pad.rightStick.ReadValue();
            gamepadActive = (left.sqrMagnitude > 0.0625f && (left - previousLeftStick).sqrMagnitude > 0.0001f)
                || (right.sqrMagnitude > 0.0625f && (right - previousRightStick).sqrMagnitude > 0.0001f);
            previousLeftStick = left;
            previousRightStick = right;
            foreach (var control in pad.allControls)
                if (control is ButtonControl button && button.wasPressedThisFrame) gamepadActive = true;
        }

        // Sample both devices so unchanged held sticks cannot reclaim feedback.
        if (keyboardMouseActive)
        {
            usingGamepad = false;
            if (active != null) StopAll();
        }
        else if (gamepadActive) usingGamepad = true;
    }

    public void StopAll()
    {
        active = null; continuous = false; activeHandle = 0;
        if (gamepad == null) return;
        double now = Time.unscaledTimeAsDouble;
        bool alreadyPending = pendingStops.Exists(p => p.Pad == gamepad);
        if (alreadyPending) return;
        Write(gamepad, 0f, 0f);
        sentLow = sentHigh = 0f;
        nextWriteAt = now + CommandInterval;
        // HID IOCTL can drop a busy command. Retry zero for a bounded window; never resume a request.
        pendingStops.Add(new PendingStop { Pad = gamepad, Next = now + CommandInterval, Until = now + 0.2d });
    }
    private void PumpStops()
    {
        double now = Time.unscaledTimeAsDouble;
        for (int i = pendingStops.Count - 1; i >= 0; i--)
        {
            PendingStop stop = pendingStops[i];
            if (!stop.Pad.added || now > stop.Until) { pendingStops.RemoveAt(i); continue; }
            if (now < stop.Next) continue;
            Write(stop.Pad, 0f, 0f); stop.Next = now + CommandInterval;
        }
    }
    private bool Write(Gamepad pad, float low, float high)
    {
        try { pad.SetMotorSpeeds(low, high); return true; }
        catch (Exception)
        {
            failedDevices.Add(pad.deviceId);
            if (pad == gamepad) { active = null; continuous = false; activeHandle = 0; }
            return false; // Optional feedback must never break gameplay.
        }
    }
    private void ResetOwnedDevices()
    {
        if (gamepad != null) ResetDevice(gamepad);
        foreach (var stop in pendingStops) ResetDevice(stop.Pad);
        pendingStops.Clear();
    }
    private static void ResetDevice(Gamepad pad) { try { pad.SetMotorSpeeds(0f, 0f); pad.ResetHaptics(); } catch (Exception) { } }
    private void DeviceChanged(InputDevice device, InputDeviceChange change)
    {
        if (!(device is Gamepad pad)) return;
        if (change == InputDeviceChange.Disconnected || change == InputDeviceChange.Removed || change == InputDeviceChange.Disabled)
        {
            if (pad == gamepad) { StopAll(); ResetDevice(pad); gamepad = null; }
        }
        else if (change == InputDeviceChange.Reconnected || change == InputDeviceChange.Added) failedDevices.Remove(pad.deviceId);
    }
    private void SceneLoaded(Scene scene, LoadSceneMode mode) => StopAll();
    private void SceneUnloaded(Scene scene) => StopAll();
    private void ActiveSceneChanged(Scene oldScene, Scene newScene) => StopAll();
    private static float Unit(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Clamp01(value);
}
