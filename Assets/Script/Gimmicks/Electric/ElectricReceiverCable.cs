using UnityEngine;
using UnityEngine.Events;

/// <summary>受電判定とイベント。接続先の装置の実装には依存しない。</summary>
[DefaultExecutionOrder(75)]
public sealed class ElectricReceiverCable : MonoBehaviour
{
    internal const float ChainArrivalProgress = 0.5f;
    private const float TerminalArrivalProgress = 0.8f;
    private const float PowerFlashDuration = 0.25f;
    private static readonly int PowerId = Shader.PropertyToID("_GatePower");
    private static readonly int PulseId = Shader.PropertyToID("_GatePulse");
    [SerializeField] private TeslaCoil source;
    [SerializeField] private MorningStarLauncher launcher;
    [SerializeField] private Vector2 tip = new Vector2(-1.35f, 0.3f);
    [SerializeField, Min(0.05f)] private float radius = 0.8f;
    [SerializeField, Min(0f)] private float activationDelay = 0.8f;
    [SerializeField, Min(0f)] private float contactGrace = 0.12f;
    [SerializeField, Min(0f)] private float effectIntensity = 1f;
    [SerializeField] private bool latchAfterActivation;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip activationSound;
    [SerializeField] private AudioClip deactivationSound;
    [SerializeField] private SpriteRenderer body;
    [SerializeField] private UnityEvent onPowerStarted = new UnityEvent();
    [SerializeField] private UnityEvent onPowerEnded = new UnityEvent();
    private float lostTime, heldTime;
    private bool conducting, powered, ballContact;
    [SerializeField, Min(0.1f)] private float actorEffectRadius = 0.65f;
    private float flashRemaining;
    private ElectricArcEffect tipArc, ballArc, playerArc, chainArc, contactArc;
    private PlayerHealth playerHealth;
    private Player actor;
    private ElectricActivationSequence sequence;
    private SpriteRenderer ballVisual;
    private MaterialPropertyBlock clampLight;
    public event System.Action ConductionStarted;
    public event System.Action ConductionEnded;
    public event System.Action PowerStarted;
    public event System.Action PowerEnded;
    public bool IsConducting => conducting;
    public bool IsPowered => powered;
    public float ChargeProgress => powered ? 1f : activationDelay <= 0f ? (conducting ? 1f : 0f) : Mathf.Clamp01(heldTime / activationDelay);
    internal bool UsesConnectionLeadIn => sequence != null && sequence.UsesConnectionLeadIn;
    public MorningStarLauncher Launcher => launcher;
    public Vector2 TipWorld => transform.TransformPoint(tip);

    internal void ApplyClampAppearance(Sprite sprite, Material material)
    {
        if (!UsesConnectionLeadIn || body == null || sprite == null || material == null)
            return;

        body.sprite = sprite;
        body.sharedMaterial = material;
        body.color = Color.white;
        SpriteRenderer playerSprite = actor != null ? actor.BodySprite : null;
        if (playerSprite != null && body.sortingLayerID == playerSprite.sortingLayerID)
            body.sortingOrder = Mathf.Min(body.sortingOrder, playerSprite.sortingOrder - 1);

        clampLight ??= new MaterialPropertyBlock();
        UpdateClampLight(ballContact, conducting ? ChargeProgress : 0f);
    }

    private void Awake()
    {
        tipArc = gameObject.AddComponent<ElectricArcEffect>();
        ballArc = gameObject.AddComponent<ElectricArcEffect>();
        playerArc = gameObject.AddComponent<ElectricArcEffect>();
        chainArc = gameObject.AddComponent<ElectricArcEffect>();
        contactArc = gameObject.AddComponent<ElectricArcEffect>();
        if (launcher == null) launcher = FindAnyObjectByType<MorningStarLauncher>();
        if (launcher != null) playerHealth = launcher.GetComponent<PlayerHealth>();
        if (launcher != null) actor = launcher.GetComponent<Player>();
        if (launcher != null) ballVisual = ElectricArcEffect.BallVisual(launcher.BallBody);
        sequence = GetComponent<ElectricActivationSequence>();
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
        if (Time.deltaTime <= 0f) return;
        bool connected = launcher != null && launcher.isActiveAndEnabled && launcher.gameObject.activeInHierarchy &&
            launcher.BallBody != null && launcher.BallBody.simulated && launcher.IsRopeLineVisible &&
            launcher.State != MorningStarLauncher.MorningStarState.StartIntro &&
            launcher.State != MorningStarLauncher.MorningStarState.Respawning &&
            (playerHealth == null || (!playerHealth.IsDead && !playerHealth.IsRespawning));
        ballContact = connected && source != null && source.Contains(launcher.BallBody);
        if (source != null) source.SetBallInRange(ballContact, ballContact ? launcher.BallBody.transform : null);
        bool raw = ballContact && launcher.CanBeginElectricStun && PlayerInRange();
        if (!connected) lostTime = contactGrace + 1f;
        else if (raw) lostTime = 0f;
        else lostTime += Time.deltaTime;
        SetConducting(raw || (conducting && connected && lostTime <= contactGrace));
        if (conducting)
        {
            heldTime += Time.deltaTime;
            if (!powered && heldTime >= activationDelay)
            {
                powered = true; flashRemaining = PowerFlashDuration;
                if (audioSource != null && activationSound != null) audioSource.PlayOneShot(activationSound);
                onPowerStarted.Invoke();
                PowerStarted?.Invoke();
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
        PowerEnded?.Invoke();
    }
    private void SetConducting(bool value)
    {
        if (conducting == value) return;
        conducting = value;
        if (value) ConductionStarted?.Invoke();
        else ConductionEnded?.Invoke();
    }
    private void OnDisable()
    {
        if (source != null) source.SetBallInRange(false);
        SetConducting(false); heldTime = 0f; lostTime = 0f; flashRemaining = 0f; ballContact = false;
        if (body != null) body.color = Color.white;
        if (tipArc != null) tipArc.Hide();
        if (ballArc != null) ballArc.Hide();
        if (playerArc != null) playerArc.Hide();
        if (chainArc != null) chainArc.Hide();
        if (contactArc != null) contactArc.Hide();
        if (powered && !latchAfterActivation) EndPower();
        UpdateClampLight(false, 0f);
    }
    private void LateUpdate()
    {
        bool useLeadIn = UsesConnectionLeadIn;
        float deltaTime = useLeadIn && sequence.IsRevealingDoor ? Time.unscaledDeltaTime : Time.deltaTime;
        flashRemaining = Mathf.Max(0f, flashRemaining - deltaTime);
        bool actorsAvailable = !useLeadIn || (actor != null && actor.isActiveAndEnabled
            && launcher != null && launcher.isActiveAndEnabled && launcher.BallBody != null
            && launcher.BallBody.simulated && (playerHealth == null || (!playerHealth.IsDead && !playerHealth.IsRespawning)));
        bool charged = actorsAvailable && ballContact && launcher != null;
        float charge = actorsAvailable && conducting ? ChargeProgress : 0f;
        UpdateClampLight(charged, charge);
        ballArc.DrawOutline(ballVisual, charged ? effectIntensity * (conducting ? 1.4f : 1f) : 0f);

        if (useLeadIn && (!powered || !actorsAvailable))
        {
            DrawConnectionLeadIn(charge, charged);
            return;
        }

        if (body != null && clampLight == null)
            body.color = flashRemaining > 0f ? new Color(1f, 1f, 0.7f) : conducting ? new Color(1f, 0.84f, 0.48f) : Color.white;
        if (useLeadIn && flashRemaining > 0f)
            tipArc.DrawTerminalCharge(TipWorld, effectIntensity * flashRemaining / PowerFlashDuration);
        else
            tipArc.DrawOutline(body, conducting ? effectIntensity * 1.8f : 0f, TipWorld, null, 0.55f);
        SpriteRenderer playerVisual = sequence != null ? sequence.EffectSprite : actor != null ? actor.BodySprite : null;
        playerArc.DrawOutline(playerVisual, charged ? effectIntensity : 0f);
        contactArc.DrawBetweenOutlines(playerVisual, body, conducting ? effectIntensity : 0f);
        chainArc.DrawChain(charged ? launcher : null, charged ? effectIntensity * (conducting ? 1.4f : 1f) : 0f);
    }

    private void UpdateClampLight(bool charged, float progress)
    {
        if (clampLight == null || body == null)
            return;

        float light = 0f;
        if (powered)
            light = 1f;
        else if (charged || progress > 0f)
            light = Mathf.Lerp(0.30f, 0.70f, progress);
        float flash = 0.22f * (flashRemaining / PowerFlashDuration);
        body.GetPropertyBlock(clampLight);
        clampLight.SetFloat(PowerId, light);
        clampLight.SetFloat(PulseId, flash);
        body.SetPropertyBlock(clampLight);
    }

    private void DrawConnectionLeadIn(float progress, bool charged)
    {
        float terminalCharge = Mathf.InverseLerp(TerminalArrivalProgress, 1f, progress);
        if (body != null && clampLight == null)
            body.color = Color.Lerp(Color.white, new Color(1f, 0.84f, 0.48f), terminalCharge);
        playerArc.Hide();

        if (progress > 0f && progress < ChainArrivalProgress)
            chainArc.DrawChainPulse(launcher, progress / ChainArrivalProgress, effectIntensity);
        else
            chainArc.Hide();

        if (progress >= ChainArrivalProgress)
        {
            float travel = Mathf.InverseLerp(ChainArrivalProgress, TerminalArrivalProgress, progress);
            float strength = Mathf.Lerp(1f, 0.5f, terminalCharge);
            contactArc.DrawConnectionPulse(launcher.VisualRopeAnchorWorld, TipWorld, travel, strength * effectIntensity);
        }
        else
            contactArc.Hide();

        if (terminalCharge > 0f)
            tipArc.DrawTerminalCharge(TipWorld, Mathf.Lerp(0.4f, 1f, terminalCharge) * effectIntensity);
        else if (clampLight != null && charged && progress <= 0f)
        {
            // Point out the still-open receiver when the coil has picked up the ball.
            float strength = (Mathf.FloorToInt(Time.time * 6f) & 1) == 0 ? 0.4f : 0.7f;
            tipArc.DrawTerminalCharge(TipWorld, strength * effectIntensity);
        }
        else
            tipArc.Hide();
    }
    private void OnDrawGizmosSelected() { Gizmos.color = Color.yellow; Gizmos.matrix = transform.localToWorldMatrix; Gizmos.DrawWireSphere(tip, radius); }
}



