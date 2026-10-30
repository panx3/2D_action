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
    private Vector3 leftClosed, rightClosed;
    private bool requestedOpen, openedOnce, subscribed;
    private float progress, effectRemaining;
    private ElectricArcEffect activationArc;
    public bool IsFullyOpen => progress >= 0.9999f;

    private void Awake()
    {
        if (leftPanel != null) leftClosed = leftPanel.localPosition;
        if (rightPanel != null) rightClosed = rightPanel.localPosition;
        activationArc = gameObject.AddComponent<ElectricArcEffect>();
    }
    private void OnEnable()
    {
        if (receiver == null || subscribed) return;
        receiver.PowerStarted += Open; receiver.PowerEnded += Close; subscribed = true;
        if (receiver.IsPowered) Open();
    }
    private void OnDisable()
    {
        if (receiver != null && subscribed) { receiver.PowerStarted -= Open; receiver.PowerEnded -= Close; }
        subscribed = false;
        if (activationArc != null) activationArc.Hide();
    }
    public void Open()
    {
        if (requestedOpen) return;
        requestedOpen = true; openedOnce = true; effectRemaining = 0.35f;
        if (audioSource != null && openSound != null) audioSource.PlayOneShot(openSound);
    }
    public void Close()
    {
        if ((stayOpenOnceActivated && openedOnce) || !requestedOpen) return;
        requestedOpen = false;
        if (audioSource != null && closeSound != null) audioSource.PlayOneShot(closeSound);
    }
    private void Update()
    {
        if (leftPanel == null || rightPanel == null || passageCollider == null) return;
        bool occupied = !requestedOpen && (progress > 0.0001f || !passageCollider.enabled) && PassageOccupied();
        float target = requestedOpen || occupied ? 1f : 0f;
        progress = Mathf.MoveTowards(progress, target, speed * Time.deltaTime / Mathf.Max(0.01f, openDistance));
        Vector2 direction = openDirection.sqrMagnitude > 0.0001f ? openDirection.normalized : Vector2.right;
        float eased = progress * progress * (3f - 2f * progress);
        leftPanel.localPosition = leftClosed - (Vector3)(direction * (openDistance * eased));
        rightPanel.localPosition = rightClosed + (Vector3)(direction * (openDistance * eased));
        // Do not enable a collider while either actor overlaps the passage, including during reversal.
        passageCollider.enabled = progress < 0.9999f && (passageCollider.enabled || (progress <= 0f && !occupied));
        effectRemaining = Mathf.Max(0f, effectRemaining - Time.deltaTime);
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
    private void LateUpdate()
    {
        if (activationArc != null) activationArc.DrawBurst(transform.TransformPoint(new Vector2(0f, 2.3f)), 0.3f, effectRemaining > 0f ? activationEffectIntensity : 0f);
    }
}

