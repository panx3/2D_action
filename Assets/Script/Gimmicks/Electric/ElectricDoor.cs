using UnityEngine;

/// <summary>固定枠の内側で二枚の扉を動かす。通路が塞がっている間は閉じない。</summary>
public sealed class ElectricDoor : MonoBehaviour
{
    [SerializeField] private Transform leftPanel;
    [SerializeField] private Transform rightPanel;
    [SerializeField] private BoxCollider2D passageCollider;
    [SerializeField] private ElectricReceiverCable receiver;
    [SerializeField] private Vector2 openDirection = Vector2.right;
    [SerializeField, Min(0f)] private float openDistance = 0.75f;
    [SerializeField, Min(0.01f)] private float speed = 1.5f;
    [SerializeField] private bool stayOpenOnceActivated;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip openSound;
    [SerializeField] private AudioClip closeSound;
    [SerializeField, Min(0f)] private float activationEffectIntensity = 1f;
    [Header("Grand gate (opt-in)")]
    [SerializeField] private bool heavyOpening;
    [SerializeField, Min(0f)] private float unlockDuration = 0.55f;
    private Object presentationOwner;
    private float unlockElapsed;
    private bool driveStarted;
    private GrandElectricDoorVisuals grandVisuals;
    private Vector3 leftClosed, rightClosed;
    private bool requestedOpen, openedOnce, subscribed;
    private float progress, effectRemaining;
    private ElectricArcEffect activationArc, activationArcRight;
    public bool IsFullyOpen => progress >= 0.9999f;
    public bool IsGrandGate => heavyOpening;
    public bool IsPresentationControlled => presentationOwner != null;
    public float OpeningProgress => progress;
    public float UnlockProgress => !heavyOpening || unlockDuration <= 0f ? (requestedOpen ? 1f : 0f) : Mathf.Clamp01(unlockElapsed / unlockDuration);
    public float ChargeProgress => receiver != null ? receiver.ChargeProgress : openedOnce ? 1f : 0f;
    public Bounds RevealBounds => grandVisuals != null ? grandVisuals.ArtworkBounds : passageCollider != null ? passageCollider.bounds : new Bounds(transform.position, Vector3.one);
    public event System.Action FullyOpened;

    private void Awake()
    {
        grandVisuals = GetComponent<GrandElectricDoorVisuals>();
        if (leftPanel != null) leftClosed = leftPanel.localPosition;
        if (rightPanel != null) rightClosed = rightPanel.localPosition;
        activationArc = gameObject.AddComponent<ElectricArcEffect>();
        activationArcRight = gameObject.AddComponent<ElectricArcEffect>();
    }
    private void OnEnable()
    {
        if (receiver == null || subscribed) return;
        receiver.PowerStarted += Open; receiver.PowerEnded += Close; subscribed = true;
        if (receiver.IsPowered) Open();
    }
    private void OnDisable()
    {
        presentationOwner = null;
        if (heavyOpening && audioSource != null) audioSource.Stop();
        if (!IsFullyOpen) driveStarted = false;
        if (receiver != null && subscribed) { receiver.PowerStarted -= Open; receiver.PowerEnded -= Close; }
        subscribed = false;
        if (activationArc != null) activationArc.Hide();
        if (activationArcRight != null) activationArcRight.Hide();
    }
    public void Open()
    {
        if (requestedOpen) return;
        requestedOpen = true; openedOnce = true; effectRemaining = 0.35f;
        if (!heavyOpening && audioSource != null && openSound != null) audioSource.PlayOneShot(openSound);
    }
    public void Close()
    {
        if ((stayOpenOnceActivated && openedOnce) || !requestedOpen) return;
        requestedOpen = false;
        if (audioSource != null && closeSound != null) audioSource.PlayOneShot(closeSound);
    }
    private void Update()
    {
        if (presentationOwner != null) return;
        Advance(Time.deltaTime);
    }
    public bool BeginPresentation(Object owner)
    {
        if (!heavyOpening || owner == null || !isActiveAndEnabled || IsFullyOpen
            || (presentationOwner != null && presentationOwner != owner)) return false;
        presentationOwner = owner;
        return true;
    }
    public void AdvancePresentation(Object owner, float deltaTime)
    {
        if (owner != null && presentationOwner == owner && isActiveAndEnabled)
            Advance(Mathf.Max(0f, deltaTime));
    }
    public void EndPresentation(Object owner)
    {
        if (presentationOwner == owner) presentationOwner = null;
    }
    private void Advance(float deltaTime)
    {
        if (leftPanel == null || rightPanel == null || passageCollider == null) return;
        bool wasFullyOpen = IsFullyOpen;
        bool occupied = !requestedOpen && (progress > 0.0001f || !passageCollider.enabled) && PassageOccupied();
        float target = requestedOpen || occupied ? 1f : 0f;
        float step = Mathf.Max(0f, deltaTime);
        if (heavyOpening && target > progress && unlockElapsed < unlockDuration)
        {
            float spent = Mathf.Min(step, unlockDuration - unlockElapsed);
            unlockElapsed += spent;
            step -= spent;
        }
        if (heavyOpening && target > progress && step > 0f && !driveStarted)
        {
            driveStarted = true;
            if (audioSource != null && openSound != null)
            {
                audioSource.clip = openSound;
                audioSource.loop = true;
                audioSource.Play();
            }
        }
        progress = Mathf.MoveTowards(progress, target, speed * step / Mathf.Max(0.01f, openDistance));
        Vector2 direction = openDirection.sqrMagnitude > 0.0001f ? openDirection.normalized : Vector2.right;
        float eased = heavyOpening ? progress * progress * progress * (progress * (progress * 6f - 15f) + 10f)
            : progress * progress * (3f - 2f * progress);
        leftPanel.localPosition = leftClosed - (Vector3)(direction * (openDistance * eased));
        rightPanel.localPosition = rightClosed + (Vector3)(direction * (openDistance * eased));
        // Do not enable a collider while either actor overlaps the passage, including during reversal.
        passageCollider.enabled = progress < 0.9999f && (passageCollider.enabled || (progress <= 0f && !occupied));
        effectRemaining = Mathf.Max(0f, effectRemaining - deltaTime);
        if (!wasFullyOpen && IsFullyOpen)
        {
            if (heavyOpening && audioSource != null) audioSource.Stop();
            if (grandVisuals != null) grandVisuals.PlayStopImpact();
            FullyOpened?.Invoke();
        }
    }
    private bool PassageOccupied()
    {
        Vector2 center = transform.TransformPoint(passageCollider.offset);
        Vector3 scale = transform.lossyScale;
        Vector2 size = Vector2.Scale(passageCollider.size, new Vector2(Mathf.Abs(scale.x), Mathf.Abs(scale.y)));
        foreach (Collider2D hit in Physics2D.OverlapBoxAll(center, size + Vector2.one * 0.1f, transform.eulerAngles.z))
        {
            if (hit == null || hit == passageCollider || hit.isTrigger) continue;
            if (hit.GetComponentInParent<Player>() != null) return true;
            Rigidbody2D body = hit.attachedRigidbody;
            if (body != null && (body.GetComponent<MorningStarCollisionReporter>() != null || body.CompareTag("morningstar"))) return true;
        }
        return false;
    }

    internal Vector2 KeepOnApproachSide(Collider2D hit, Vector2 approach, Vector2 target, float clearance)
    {
        if (hit != passageCollider || !passageCollider.enabled)
            return target;

        Transform blocker = passageCollider.transform;
        Vector2 center = blocker.TransformPoint(passageCollider.offset);
        Vector2 normal = blocker.right;
        if (Vector2.Dot(approach - center, normal) < 0f)
            normal = -normal;

        float halfWidth = (passageCollider.size.x * 0.5f + passageCollider.edgeRadius)
            * Mathf.Abs(blocker.lossyScale.x);
        float distance = Vector2.Dot(target - center, normal);
        return target + normal * Mathf.Max(0f, halfWidth + clearance - distance);
    }

    private void LateUpdate()
    {
        float intensity = effectRemaining > 0f ? activationEffectIntensity : 0f;
        if (activationArc != null) activationArc.DrawOutline(leftPanel != null ? leftPanel.GetComponent<SpriteRenderer>() : null, intensity);
        if (activationArcRight != null) activationArcRight.DrawOutline(rightPanel != null ? rightPanel.GetComponent<SpriteRenderer>() : null, intensity);
    }
}

