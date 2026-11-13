using UnityEngine;

/// <summary>Three distinct Morning Star impacts break the base and lower the pillar into a permanent bridge.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D), typeof(PolygonCollider2D))]
public sealed class FallingPillarBridge : MonoBehaviour, IMorningStarHitReceiver
{
    [Header("鉄球の命中")]
    [SerializeField, Min(1)] private int requiredHits = 3;
    [SerializeField, Min(0f)] private float minimumImpactSpeed = 4f;
    [SerializeField, Min(0.01f)] private float hitCooldown = 0.3f;

    [Header("倒れる方向・時間")]
    [SerializeField, Tooltip("右へ倒す場合 -90、左へ倒す場合 90。折れ口が回転中心です。")]
    private float fallAngle = -90f;
    [SerializeField, Min(0.1f)] private float fallDuration = 1.25f;
    [SerializeField, Min(0f)] private float breakDelay = 0.2f;
    [SerializeField, Min(0f), Tooltip("折れ口の高さから着地位置までの落差。根元はその場に残ります。")]
    private float landingDrop = 0.7368f;
    [SerializeField, Tooltip("柱のローカル座標で指定する折れ口の右端。")]
    private Vector2 fallPivotOffset;
    [SerializeField, Min(0f), Tooltip("着地直前に根元から外側へ離れる距離。")]
    private float landingSeparation;
    [SerializeField] private Vector2[] fallenColliderPoints;
    [SerializeField] private Vector2 fallenColliderSize = new Vector2(3.35f, 11.3f);
    [SerializeField] private Vector2 fallenColliderOffset = new Vector2(0f, 5.9f);

    [Header("見た目・効果音")]
    [SerializeField] private SpriteRenderer pillarVisual;
    [SerializeField, Tooltip("無傷、1打目、2打目、折れた状態の順。")]
    private Sprite[] damageStages;
    [SerializeField] private GameObject brokenBase;
    [SerializeField] private AudioClip hitClip;
    [SerializeField] private AudioClip collapseClip;
    [SerializeField] private GameObject metalFragmentPrefab;
    [SerializeField] private AudioClip landingClip;
    [SerializeField] private ParticleSystem landingDustPrefab;
    [SerializeField, Min(0f)] private float landingShakeStrength = 0.16f;
    [SerializeField, Min(0f)] private float landingSettleDistance = 0.1f;

    private Rigidbody2D body;
    private PolygonCollider2D solid;
    private AudioSource audioSource;
    private CameraShake2D cameraShake;
    private Vector3 visualRestPosition;
    private float standingAngle;
    private float nextHitTime;
    private float shakeRemaining;
    private float fallElapsed;
    private Vector2 standingPosition;
    private bool baseDetached;
    private float settleRemaining;
    private Vector2 pivotWorld;
    private Vector2 scaledPivotOffset;

    public Vector2 FallPivot => pivotWorld;

    public float WalkableHeight => solid != null ? solid.bounds.max.y : transform.position.y;
    public GameObject RemainingBase => brokenBase;

    public int HitCount { get; private set; }
    public int RequiredHits => requiredHits;
    public bool IsFalling { get; private set; }
    public bool IsBridge { get; private set; }

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        solid = GetComponent<PolygonCollider2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        body.gravityScale = 0f;
        standingAngle = body.rotation;
        standingPosition = body.position;
        scaledPivotOffset = Vector2.Scale(fallPivotOffset, transform.lossyScale);
        pivotWorld = standingPosition + (Vector2)(Quaternion.Euler(0f, 0f, standingAngle) * scaledPivotOffset);
        if (pillarVisual != null) visualRestPosition = pillarVisual.transform.localPosition;
        if (brokenBase != null) brokenBase.SetActive(false);
        audioSource = OneShotAudioUtility.FindWorldImpactSource();
        cameraShake = FindAnyObjectByType<CameraShake2D>();
        ApplyDamageStage(0);
    }

    public void OnMorningStarHit(MorningStarHitContext context)
    {
        // Launcher owns collision/state filtering and the ball response. Count impacts, not damage:
        // a charged attack must not skip the pillar's readable three-hit sequence.
        if (!isActiveAndEnabled || IsFalling || IsBridge || context.Damage <= 0
            || context.ImpactSpeed < minimumImpactSpeed || Time.time < nextHitTime)
            return;

        nextHitTime = Time.time + Mathf.Max(0.01f, hitCooldown);
        HitCount++;
        bool breaksNow = HitCount >= Mathf.Max(1, requiredHits);
        shakeRemaining = 0.18f;
        ApplyDamageStage(breaksNow ? 3 : Mathf.Min(HitCount, 2));
        OneShotAudioUtility.Play2D(audioSource, hitClip, 0.65f, context.ImpactPoint);
        FragmentBurst2D.Spawn(metalFragmentPrefab, context.ImpactPoint, -context.ImpactDirection,
            HitCount >= requiredHits ? 12 : 4, 0.15f, 1f, 3f, 0.9f);

        if (!breaksNow) return;

        IsFalling = true;
        fallElapsed = 0f;
        // Animate without a sweeping solid collider. Enable the walkable surface only on landing.
        // No Player/MorningStar rigidbody settings or global collision masks are changed.
        solid.enabled = false;
        if (fallenColliderPoints != null && fallenColliderPoints.Length >= 3)
            solid.SetPath(0, fallenColliderPoints);
        if (brokenBase != null)
        {
            // The stump owns its own static body. Detach it before moving the severed upper column.
            brokenBase.transform.SetParent(transform.parent, true);
            brokenBase.SetActive(true);
            baseDetached = true;
        }
        OneShotAudioUtility.Play2D(audioSource, collapseClip, 0.8f, transform.position);
        FragmentBurst2D.Spawn(metalFragmentPrefab, transform.position, Vector2.up,
            8, 0.6f, 1f, 2.5f, 0.9f);
    }

    private void FixedUpdate()
    {
        if (!IsFalling) return;
        fallElapsed += Time.fixedDeltaTime;
        float t = Mathf.Clamp01((fallElapsed - breakDelay) / Mathf.Max(0.1f, fallDuration));
        // Slow initial bend, then accelerating weight. A fixed endpoint keeps the bridge usable.
        float angle = standingAngle + fallAngle * t * t;
        Vector2 rotatedPivot = Quaternion.Euler(0f, 0f, angle) * scaledPivotOffset;
        Vector2 separation = new Vector2(-Mathf.Sign(fallAngle) * landingSeparation, -landingDrop);
        Vector2 position = pivotWorld - rotatedPivot + separation * (t * t * t * t);
        if (t < 1f)
        {
            body.MoveRotation(angle);
            body.MovePosition(position);
            return;
        }

        body.rotation = standingAngle + fallAngle;
        body.position = position;
        body.bodyType = RigidbodyType2D.Static;
        solid.enabled = true;
        IsFalling = false;
        IsBridge = true;
        shakeRemaining = 0f;
        if (pillarVisual != null) pillarVisual.transform.localPosition = visualRestPosition;
        settleRemaining = 0.24f;
        OneShotAudioUtility.Play2D(audioSource, landingClip, 1f, solid.bounds.center);
        OneShotAudioUtility.Play2D(audioSource, hitClip, 0.4f, solid.bounds.center);
        if (cameraShake != null) cameraShake.Shake(0.18f, landingShakeStrength);
        // Use the landed body pose, which is authoritative in this physics step.
        Vector2 axis = Quaternion.Euler(0f, 0f, body.rotation) * Vector2.up;
        Vector3 nearEnd = body.position + axis * 0.5f;
        Vector3 farEnd = body.position + axis * (fallenColliderSize.y - 0.5f);
        nearEnd.y = farEnd.y = solid.bounds.min.y + 0.08f;
        PlayLandingDust(nearEnd);
        PlayLandingDust(farEnd);
    }

    private void LateUpdate()
    {
        if (pillarVisual == null) return;
        shakeRemaining = Mathf.Max(0f, shakeRemaining - Time.deltaTime);
        float amount = 0.055f * (shakeRemaining / 0.18f);
        pillarVisual.transform.localPosition = visualRestPosition
            + Vector3.right * (Mathf.Sin(Time.time * 90f) * amount);
        if (settleRemaining > 0f)
        {
            settleRemaining = Mathf.Max(0f, settleRemaining - Time.deltaTime);
            float progress = 1f - settleRemaining / 0.24f;
            // One short compression, not a springy bounce. The supporting collider remains fixed.
            pillarVisual.transform.localPosition += transform.InverseTransformVector(Vector3.down)
                * (Mathf.Sin(progress * Mathf.PI) * landingSettleDistance);
        }
    }

    private void PlayLandingDust(Vector3 position)
    {
        if (landingDustPrefab == null) return;
        var dust = Instantiate(landingDustPrefab, position, Quaternion.identity);
        dust.Play();
        Destroy(dust.gameObject, 2f);
    }

    private void OnEnable()
    {
        if (baseDetached && brokenBase != null) brokenBase.SetActive(true);
    }

    private void OnDisable()
    {
        shakeRemaining = 0f;
        settleRemaining = 0f;
        if (baseDetached && brokenBase != null && !gameObject.activeInHierarchy) brokenBase.SetActive(false);
        if (pillarVisual != null) pillarVisual.transform.localPosition = visualRestPosition;
        // Fall progress remains on this instance, so re-enabling resumes rather than stranding it.
    }

    private void OnDestroy()
    {
        if (baseDetached && brokenBase != null) Destroy(brokenBase);
    }

    private void ApplyDamageStage(int stage)
    {
        if (pillarVisual != null && damageStages != null && damageStages.Length > 0)
            pillarVisual.sprite = damageStages[Mathf.Clamp(stage, 0, damageStages.Length - 1)];
    }
}
