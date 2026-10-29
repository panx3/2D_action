using UnityEngine;
using UnityEngine.Events;

/// <summary>受電判定とイベント。接続先の装置の実装には依存しない。</summary>
public sealed class ElectricReceiverCable : MonoBehaviour
{
    [SerializeField] private TeslaCoil source;
    [SerializeField] private MorningStarLauncher launcher;
    [SerializeField] private Vector2 tip = new Vector2(-1.35f, 0.3f);
    [SerializeField, Min(0.05f)] private float radius = 0.8f;
    [SerializeField, Min(0f)] private float activationDelay = 0.8f;
    [SerializeField, Min(0f)] private float contactGrace = 0.12f;
    [SerializeField, Min(0f)] private float effectIntensity = 1f;
    [SerializeField] private bool latchAfterActivation = true;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip activationSound;
    [SerializeField] private AudioClip deactivationSound;
    [SerializeField] private SpriteRenderer body;
    [SerializeField] private UnityEvent onPowerStarted = new UnityEvent();
    [SerializeField] private UnityEvent onPowerEnded = new UnityEvent();
    private float lostTime, heldTime;
    private bool conducting, powered, ballContact;
    private ElectricArcEffect tipArc, ballArc, playerArc, chainArc;
    public bool IsConducting => conducting;
    public bool IsPowered => powered;

    private void Awake()
    {
        tipArc = gameObject.AddComponent<ElectricArcEffect>();
        ballArc = gameObject.AddComponent<ElectricArcEffect>();
        playerArc = gameObject.AddComponent<ElectricArcEffect>();
        chainArc = gameObject.AddComponent<ElectricArcEffect>();
        if (launcher == null) launcher = FindAnyObjectByType<MorningStarLauncher>();
    }
    private bool PlayerInRange()
    {
        if (launcher == null) return false;
        Vector2 point = transform.TransformPoint(tip);
        float worldRadius = radius * Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y));
        foreach (Collider2D c in launcher.GetComponentsInChildren<Collider2D>())
        {
            if (!c.enabled || c.isTrigger || !c.gameObject.activeInHierarchy) continue;
            Player player = c.GetComponentInParent<Player>();
            if (player != null && player.gameObject == launcher.gameObject &&
                (c.ClosestPoint(point) - point).sqrMagnitude <= worldRadius * worldRadius) return true;
        }
        return false;
    }
    private void Update()
    {
        bool connected = launcher != null && launcher.isActiveAndEnabled && launcher.gameObject.activeInHierarchy &&
            launcher.BallBody != null && launcher.BallBody.simulated && launcher.IsRopeLineVisible &&
            launcher.State != MorningStarLauncher.MorningStarState.StartIntro &&
            launcher.State != MorningStarLauncher.MorningStarState.Respawning;
        ballContact = connected && source != null && source.Contains(launcher.BallBody);
        if (source != null) source.SetBallInRange(ballContact);
        bool raw = ballContact && PlayerInRange();
        if (!connected) lostTime = contactGrace + 1f;
        else if (raw) lostTime = 0f;
        else lostTime += Time.deltaTime;
        conducting = raw || (conducting && connected && lostTime <= contactGrace);
        if (conducting)
        {
            heldTime += Time.deltaTime;
            if (!powered && heldTime >= activationDelay)
            {
                powered = true;
                if (audioSource != null && activationSound != null) audioSource.PlayOneShot(activationSound);
                onPowerStarted.Invoke();
            }
        }
        else
        {
            heldTime = 0f;
            if (powered && !latchAfterActivation) EndPower();
        }
    }
    private void EndPower()
    {
        powered = false;
        if (audioSource != null && deactivationSound != null) audioSource.PlayOneShot(deactivationSound);
        onPowerEnded.Invoke();
    }
    private void OnDisable()
    {
        if (source != null) source.SetBallInRange(false);
        conducting = false; heldTime = 0f;
        if (tipArc != null) tipArc.Hide();
        if (ballArc != null) ballArc.Hide();
        if (playerArc != null) playerArc.Hide();
        if (chainArc != null) chainArc.Hide();
        if (powered && !latchAfterActivation) EndPower();
    }
    private void LateUpdate()
    {
        if (body != null) body.color = conducting ? new Color(1f, 0.84f, 0.48f) : Color.white;
        tipArc.DrawBurst(transform.TransformPoint(tip), radius * 0.65f, effectIntensity * (conducting ? 1.8f : 0.8f));
        bool charged = ballContact && launcher != null;
        ballArc.DrawBurst(charged ? launcher.BallBody.position : Vector2.zero, 0.5f, charged ? effectIntensity : 0f);
        playerArc.DrawBurst(charged ? (Vector2)launcher.transform.position : Vector2.zero, 0.45f, charged ? effectIntensity * 0.6f : 0f);
        chainArc.DrawChain(charged ? launcher : null, charged ? effectIntensity : 0f);
    }
    private void OnDrawGizmosSelected() { Gizmos.color = Color.yellow; Gizmos.matrix = transform.localToWorldMatrix; Gizmos.DrawWireSphere(tip, radius); }
}


