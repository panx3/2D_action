using UnityEngine;

/// <summary>One powered cycle per scene. The keeper holds the passage while the hands turn without collision.</summary>
[DisallowMultipleComponent]
public sealed class ClockTower : MonoBehaviour
{
    public enum ClockState { Idle, Activating, Rotating, Locking, Completed }

    [SerializeField] private ElectricReceiverCable receiver;
    [SerializeField] private SpriteRenderer face;
    [SerializeField] private SpriteRenderer minuteHand;
    [SerializeField] private SpriteRenderer hourHand;
    [SerializeField] private Collider2D minuteBarrier;
    [SerializeField] private Collider2D hourBarrier;
    [SerializeField] private Collider2D keeperBarrier;
    [SerializeField] private Transform upperKeeper;
    [SerializeField] private Transform lowerKeeper;
    [SerializeField] private Collider2D bridge;
    [SerializeField] private Collider2D ballBridge;

    [Header("Rotation")]
    [SerializeField, Min(1f)] private float minuteSpeed = 480f;
    [SerializeField, Min(0f)] private float accelerationTime = 0.36f;
    [SerializeField, Min(0f)] private float decelerationTime = 0.48f;
    [SerializeField, Min(0.01f)] private float hourStepTime = 0.13f;
    [SerializeField, Min(0f)] private float activationTime = 0.30f;
    [SerializeField, Min(0f)] private float lockingTime = 0.22f;
    [SerializeField, Range(0, 24), Tooltip("3 turns move the hour hand from 6 to 9. A bridge requires a horizontal final pose.")]
    private int minuteTurns = 3;
    [SerializeField, Range(0f, 359f), Tooltip("Clockwise from 12 o'clock. 90 degrees aligns the minute hand with the right bank.")]
    private float finalMinuteAngle = 90f;

    [Header("Presentation")]
    [SerializeField] private Transform driveGear;
    [SerializeField] private Transform idlerGear;
    [SerializeField, Min(0f)] private float gearRatio = 0.65f;
    [SerializeField, Min(0f)] private float keeperRetraction = 0.24f;
    [SerializeField] private SpriteRenderer[] powerLights;
    [SerializeField] private SpriteRenderer[] lockSparks;
    [SerializeField] private bool frameActivation = true;
    [SerializeField] private CameraFollow stageCamera;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip powerSound;
    [SerializeField] private AudioClip gearSound;
    [SerializeField] private AudioClip spinSound;
    [SerializeField] private AudioClip stepSound;
    [SerializeField] private AudioClip lockSound;

    public ClockState State { get; private set; }
    public float MinuteTravel { get; private set; }
    public int HourSteps { get; private set; }

    private float phaseTime;
    private float stepElapsed;
    private float peakSpeed;
    private float cruiseTime;
    private float gearTravel;
    private Vector3 upperClosed, lowerClosed;
    private Camera lens;
    private CameraShake2D cameraShake;
    private Player actor;
    private PlayerHealth health;
    private Vector3 startingCameraPosition;
    private float startingLens;
    private float cameraTime;
    private float upperTravel, lowerTravel;

    private float TotalTravel => minuteTurns * 360f + finalMinuteAngle;

    private void Awake()
    {
        upperClosed = upperKeeper.localPosition;
        lowerClosed = lowerKeeper.localPosition;
        upperTravel = upperKeeper.GetComponent<SpriteRenderer>().sprite.bounds.size.y;
        lowerTravel = lowerKeeper.GetComponent<SpriteRenderer>().sprite.bounds.size.y;
        SetHands(0f, 0f);
        minuteBarrier.enabled = hourBarrier.enabled = keeperBarrier.enabled = true;
        bridge.enabled = ballBridge.enabled = false;
        SetLight(powerLights, 0f);
        SetLight(lockSparks, 0f);

        if (stageCamera == null && Camera.main != null)
            stageCamera = Camera.main.GetComponent<CameraFollow>();
        if (stageCamera != null)
        {
            lens = stageCamera.GetComponent<Camera>();
            cameraShake = stageCamera.GetComponent<CameraShake2D>();
        }
    }

    private void OnEnable()
    {
        if (receiver == null) return;
        receiver.PowerStarted += BeginActivation;
        if (receiver.IsPowered) BeginActivation();
    }

    private void OnDisable()
    {
        if (receiver != null) receiver.PowerStarted -= BeginActivation;
        ReleaseCamera();
    }

    private void BeginActivation()
    {
        if (State != ClockState.Idle) return;

        // The fixed keeper already overlaps the two blades. Removing their contacts
        // before moving them leaves a continuous wall without a moving crush surface.
        State = ClockState.Activating;
        phaseTime = 0f;
        minuteBarrier.enabled = hourBarrier.enabled = false;
        float rampTime = accelerationTime + decelerationTime;
        peakSpeed = rampTime > 0f ? Mathf.Min(minuteSpeed, 2f * TotalTravel / rampTime) : minuteSpeed;
        cruiseTime = peakSpeed > 0f ? Mathf.Max(0f, TotalTravel / peakSpeed - rampTime * 0.5f) : 0f;
        Play(powerSound);
        BeginCamera();
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f || State == ClockState.Idle) return;
        phaseTime += dt;
        switch (State)
        {
            case ClockState.Activating:
                TurnGears(dt * 40f);
                SetLight(powerLights, Mathf.Clamp01(phaseTime * 7f));
                SetLight(lockSparks, 1f - Mathf.Clamp01(phaseTime / 0.32f));
                if (phaseTime >= activationTime)
                {
                    State = ClockState.Rotating;
                    phaseTime = 0f;
                    Play(gearSound);
                    Play(spinSound);
                }
                break;
            case ClockState.Rotating:
                AdvanceHands(dt);
                break;
            case ClockState.Locking:
                if (phaseTime >= lockingTime) Complete();
                break;
            case ClockState.Completed:
                float open = Mathf.SmoothStep(0f, 1f, phaseTime / Mathf.Max(0.01f, keeperRetraction));
                upperKeeper.localPosition = upperClosed + Vector3.up * (upperTravel * open);
                lowerKeeper.localPosition = lowerClosed + Vector3.down * (lowerTravel * open);
                SetLight(lockSparks, 1f - Mathf.Clamp01(phaseTime / 0.30f));
                if (open >= 1f && phaseTime >= 0.30f)
                {
                    upperKeeper.gameObject.SetActive(false);
                    lowerKeeper.gameObject.SetActive(false);
                    enabled = false;
                }
                break;
        }
        UpdateCamera(dt);
    }

    private void AdvanceHands(float dt)
    {
        float travel = TravelAt(phaseTime);
        TurnGears((travel - MinuteTravel) * gearRatio);
        MinuteTravel = travel;
        int earned = Mathf.Min(minuteTurns, Mathf.FloorToInt(MinuteTravel / 360f));
        float stepFraction = 0f;
        if (HourSteps < earned)
        {
            if (stepElapsed == 0f) Play(stepSound);
            stepElapsed += dt;
            stepFraction = Mathf.SmoothStep(0f, 1f, stepElapsed / hourStepTime);
            if (stepElapsed >= hourStepTime)
            {
                HourSteps++;
                stepElapsed = stepFraction = 0f;
            }
        }
        SetHands(MinuteTravel, (HourSteps + stepFraction) * 30f);
        SetLight(powerLights, 0.72f + 0.28f * Mathf.Sin(phaseTime * 19f));
        if (MinuteTravel >= TotalTravel && HourSteps == minuteTurns)
        {
            State = ClockState.Locking;
            phaseTime = 0f;
        }
    }

    private float TravelAt(float time)
    {
        if (time < accelerationTime)
            return peakSpeed * time * time / (2f * accelerationTime);

        time -= accelerationTime;
        float accelerationTravel = peakSpeed * accelerationTime * 0.5f;
        if (time < cruiseTime) return accelerationTravel + peakSpeed * time;

        time -= cruiseTime;
        if (time >= decelerationTime) return TotalTravel;
        return accelerationTravel + peakSpeed * (cruiseTime + time - time * time / (2f * decelerationTime));
    }

    private void Complete()
    {
        if (Mathf.Abs(Mathf.DeltaAngle(finalMinuteAngle, 90f)) > 0.01f ||
            Mathf.Abs(Mathf.DeltaAngle(minuteTurns * 30f, 90f)) > 0.01f)
        {
            Debug.LogError("ClockTower: both hands must finish horizontal to release the bridge.", this);
            ReleaseCamera();
            enabled = false;
            return;
        }

        // Match the walkable plane exactly, including after long frames.
        SetHands(90f, 90f);
        bridge.enabled = ballBridge.enabled = true;
        keeperBarrier.enabled = false;
        State = ClockState.Completed;
        phaseTime = 0f;
        SetLight(powerLights, 1f);
        SetLight(lockSparks, 1f);
        Play(lockSound);
        ReleaseCamera();
        if (cameraShake != null && actor != null && Mathf.Abs(actor.transform.position.x - transform.position.x) < 19f)
            cameraShake.Shake(0.16f, 0.13f);
    }

    private void SetHands(float minute, float hour)
    {
        minuteHand.transform.localRotation = Quaternion.Euler(0f, 0f, -minute);
        hourHand.transform.localRotation = Quaternion.Euler(0f, 0f, 180f - hour);
    }

    private void TurnGears(float degrees)
    {
        gearTravel = Mathf.Repeat(gearTravel + degrees, 1440f);
        driveGear.localRotation = Quaternion.Euler(0f, 0f, -gearTravel);
        idlerGear.localRotation = Quaternion.Euler(0f, 0f, gearTravel * 1.75f);
    }

    private static void SetLight(SpriteRenderer[] lights, float alpha)
    {
        foreach (SpriteRenderer light in lights)
        {
            Color color = light.color;
            color.a = alpha;
            light.color = color;
        }
    }

    private void Play(AudioClip clip)
    {
        if (clip != null && audioSource != null) audioSource.PlayOneShot(clip);
    }

    private void BeginCamera()
    {
        actor = receiver.Launcher != null ? receiver.Launcher.GetComponent<Player>() : null;
        if (!frameActivation || actor == null || lens == null || !stageCamera.isActiveAndEnabled ||
            actor.IsGoalInputBlocked || !stageCamera.BeginEventControl(this))
            return;
        health = actor.GetComponent<PlayerHealth>();
        startingCameraPosition = stageCamera.transform.position;
        startingLens = lens.orthographicSize;
        cameraTime = 0f;
    }

    private void UpdateCamera(float dt)
    {
        if (stageCamera == null || !stageCamera.IsEventControlledBy(this)) return;
        if (actor == null || actor.IsGoalInputBlocked || (health != null && (health.IsDead || health.IsRespawning)) ||
            Mathf.Abs(actor.transform.position.x - transform.position.x) > 18f)
        {
            ReleaseCamera();
            return;
        }
        Bounds frame = face.bounds;
        frame.Encapsulate(actor.transform.position + Vector3.up * 1.7f);
        frame.Expand(new Vector3(3.6f, 1.3f, 0f));
        cameraTime += dt;
        float blend = Mathf.SmoothStep(0f, 1f, cameraTime / 0.45f);
        float height = Mathf.Max(frame.extents.y, frame.extents.x / Mathf.Max(0.1f, lens.aspect));
        stageCamera.SetEventFraming(this, Vector3.Lerp(startingCameraPosition, frame.center, blend),
            Mathf.Lerp(startingLens, Mathf.Max(startingLens, height), blend));
    }

    private void ReleaseCamera()
    {
        if (stageCamera == null || !stageCamera.IsEventControlledBy(this)) return;
        // CameraFollow adopts this framing and blends back along its normal tracking path.
        stageCamera.EndEventControl(this);
    }
}
