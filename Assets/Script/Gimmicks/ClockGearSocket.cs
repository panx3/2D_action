using UnityEngine;

/// <summary>Receives the clock's loose gear and couples it to the spinning motor.</summary>
[DisallowMultipleComponent]
public sealed class ClockGearSocket : MonoBehaviour
{
    [SerializeField] private ClockTower tower;
    [SerializeField] private Rigidbody2D gear;
    [SerializeField] private CircleCollider2D gearCollider;
    [SerializeField] private SpriteRenderer gearSprite;
    [SerializeField] private Transform mouth;
    [SerializeField] private Transform axle;
    [SerializeField] private Transform motor;
    [SerializeField] private SpriteRenderer engagedLamp;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip seatSound;

    [SerializeField, Min(0.1f)] private float openingWidth = 3.5f;
    [SerializeField, Min(0.01f)] private float captureDepth = 0.2f;
    [SerializeField, Min(0.01f)] private float seatingDuration = 0.25f;
    [SerializeField, Min(0.1f)] private float maxRollSpeed = 4.5f;
    [SerializeField, Min(0f)] private float motorSpeed = 68f;
    [SerializeField] private LayerMask respawnBlockers;

    private const float ToothAngle = 360f / 14f;
    private enum GearState { Rolling, WaitingToReturn, Seating, Engaged }
    private GearState state;
    private Player player;
    private Vector3 spawnPosition, seatingStart;
    private float spawnAngle, seatingStartAngle, motorAngle, meshPhase, seatingTime, radius;
    private ContactFilter2D returnFilter;
    private readonly Collider2D[] returnHits = new Collider2D[1];

    private void Awake()
    {
        if (tower == null || gear == null || gearCollider == null || gearSprite == null ||
            mouth == null || axle == null || motor == null)
        {
            Debug.LogError("ClockGearSocket: assign the clock, gear, mouth, axle and motor.", this);
            enabled = false;
            return;
        }

        spawnPosition = gear.transform.position;
        spawnAngle = gear.rotation;
        motorAngle = motor.localEulerAngles.z;
        radius = gearCollider.radius * Mathf.Abs(gearCollider.transform.lossyScale.x);
        returnFilter = new ContactFilter2D { useTriggers = false };
        returnFilter.SetLayerMask(respawnBlockers);
        if (engagedLamp != null) engagedLamp.enabled = false;
    }

    private void FixedUpdate()
    {
        if (state == GearState.WaitingToReturn)
        {
            TryReturnGear();
            return;
        }
        if (state != GearState.Rolling) return;

        Vector2 center = gear.position;
        Vector2 opening = mouth.position;
        if (center.y < opening.y - 4f || center.x < spawnPosition.x - 3.6f ||
            center.x > opening.x + openingWidth * 0.5f + radius + 0.2f)
        {
            state = GearState.WaitingToReturn;
            gear.simulated = false;
            gearSprite.enabled = false;
            TryReturnGear();
            return;
        }

        Vector2 velocity = gear.linearVelocity;
        float rollSpeed = Mathf.Clamp(velocity.x, -maxRollSpeed, maxRollSpeed);
        if (rollSpeed != velocity.x)
            gear.linearVelocity = new Vector2(rollSpeed, velocity.y);

        // The circle has to fit between both rims and fall below the loading deck.
        float clearance = Mathf.Max(0f, openingWidth * 0.5f - radius) + 2f * Physics2D.defaultContactOffset;
        if (tower.isActiveAndEnabled && tower.State == ClockTower.ClockState.Idle &&
            Mathf.Abs(center.x - opening.x) <= clearance && center.y >= opening.y &&
            center.y - radius <= opening.y - captureDepth && velocity.y <= 0f)
            BeginSeating();
    }

    private void BeginSeating()
    {
        state = GearState.Seating;
        seatingStart = new Vector3(gear.position.x, gear.position.y, gear.transform.position.z);
        seatingStartAngle = gear.rotation;
        // Identical 14-tooth gears need a half-tooth phase. Choose the nearest matching tooth.
        float difference = ToothAngle * 0.5f - motorAngle - seatingStartAngle;
        float correction = Mathf.Repeat(difference + ToothAngle * 0.5f, ToothAngle) - ToothAngle * 0.5f;
        meshPhase = seatingStartAngle + correction + motorAngle;
        gear.linearVelocity = Vector2.zero;
        gear.angularVelocity = 0f;
        gear.simulated = false;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f || tower.State == ClockTower.ClockState.Completed) return;

        motorAngle = Mathf.Repeat(motorAngle - motorSpeed * dt, 360f);
        motor.localRotation = Quaternion.Euler(0f, 0f, motorAngle);
        if (state == GearState.Engaged)
        {
            gear.transform.rotation = Quaternion.Euler(0f, 0f, meshPhase - motorAngle);
            return;
        }
        if (state != GearState.Seating) return;

        seatingTime += dt;
        float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(seatingTime / seatingDuration));
        gear.transform.SetPositionAndRotation(Vector3.Lerp(seatingStart, axle.position, t),
            Quaternion.Euler(0f, 0f, Mathf.LerpAngle(seatingStartAngle, meshPhase - motorAngle, t)));

        if (seatingTime < seatingDuration || !tower.isActiveAndEnabled) return;
        if (!tower.TryActivate(FindPlayer())) return;

        state = GearState.Engaged;
        if (engagedLamp != null) engagedLamp.enabled = true;
        if (audioSource != null && seatSound != null) audioSource.PlayOneShot(seatSound, 0.65f);
    }

    private Player FindPlayer()
    {
        if (player != null && player.isActiveAndEnabled) return player;
        foreach (GameObject root in gameObject.scene.GetRootGameObjects())
        {
            player = root.GetComponentInChildren<Player>();
            if (player != null && player.isActiveAndEnabled) return player;
        }
        return null;
    }

    private void TryReturnGear()
    {
        if (Physics2D.OverlapCircle(spawnPosition, radius, returnFilter, returnHits) > 0) return;

        gear.transform.SetPositionAndRotation(spawnPosition, Quaternion.Euler(0f, 0f, spawnAngle));
        gear.position = spawnPosition;
        gear.rotation = spawnAngle;
        gear.simulated = true;
        gear.linearVelocity = Vector2.zero;
        gear.angularVelocity = 0f;
        gear.WakeUp();
        gearSprite.enabled = true;
        state = GearState.Rolling;
    }
}
