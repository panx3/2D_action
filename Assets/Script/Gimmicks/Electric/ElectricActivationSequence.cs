using UnityEngine;

/// <summary>受電の接続から扉全開までの感電演出。受電判定や扉の進捗は既存の所有者に委ねる。</summary>
[DefaultExecutionOrder(50), DisallowMultipleComponent]
public sealed class ElectricActivationSequence : MonoBehaviour
{
    [SerializeField] private ElectricReceiverCable receiver;
    [SerializeField] private ElectricDoor door;
    [SerializeField] private Player player;
    [SerializeField] private Sprite skeletonSprite;
    [SerializeField, Min(0.02f)] private float blinkInterval = 0.08f;
    private PlayerHealth health;
    private SpriteRenderer body, skeleton;
    private bool active, committed, previousForceRenderingOff;
    private float elapsed;
    public bool IsPresenting => active;
    public SpriteRenderer EffectSprite => skeleton != null && skeleton.enabled ? skeleton : player != null ? player.BodySprite : null;

    private void OnEnable()
    {
        if (receiver == null) receiver = GetComponent<ElectricReceiverCable>();
        if (player == null && receiver != null && receiver.Launcher != null) player = receiver.Launcher.GetComponent<Player>();
        if (receiver != null)
        {
            receiver.ConductionStarted += Begin;
            receiver.ConductionEnded += OnConductionEnded;
            receiver.PowerStarted += OnPowerStarted;
        }
        if (door != null) door.FullyOpened += Finish;
        if (player != null)
        {
            health = player.GetComponent<PlayerHealth>();
            if (health != null) health.OnDead += Finish;
        }
    }

    private void Begin()
    {
        if (active || receiver == null || receiver.IsPowered || door == null || !door.isActiveAndEnabled
            || door.IsFullyOpen || player == null || skeletonSprite == null || !player.TryBeginElectricStun(this)) return;
        body = player.BodySprite;
        if (body == null) { player.EndElectricStun(this); return; }
        if (skeleton == null)
        {
            var visual = new GameObject("ElectricShockSkeleton");
            // The body owns the stage filter; this runtime copy follows its material directly.
            visual.hideFlags = HideFlags.DontSave;
            visual.layer = body.gameObject.layer;
            visual.transform.SetParent(body.transform, false);
            skeleton = visual.AddComponent<SpriteRenderer>();
            skeleton.sprite = skeletonSprite;
        }
        previousForceRenderingOff = body.forceRenderingOff;
        elapsed = 0f;
        committed = false;
        active = true;
    }

    private void Update()
    {
        if (!active)
        {
            if (receiver != null && receiver.IsConducting && !receiver.IsPowered) Begin();
            return;
        }
        if (player == null || !player.IsElectricStunned || !player.isActiveAndEnabled || receiver == null || !receiver.isActiveAndEnabled
            || door == null || !door.isActiveAndEnabled || (health != null && (health.IsDead || health.IsRespawning)))
        { Finish(); return; }
        elapsed += Time.deltaTime;
        if (committed && door.IsFullyOpen) Finish();
    }

    private void LateUpdate()
    {
        if (!active || body == null || skeleton == null) return;
        bool showBones = ((int)(elapsed / blinkInterval) & 1) == 0;
        body.forceRenderingOff = previousForceRenderingOff || showBones;
        skeleton.enabled = showBones && !previousForceRenderingOff;
        skeleton.flipX = body.flipX;
        skeleton.flipY = body.flipY;
        skeleton.sharedMaterial = body.sharedMaterial;
        skeleton.sortingLayerID = body.sortingLayerID;
        skeleton.sortingOrder = body.sortingOrder + 2;
        skeleton.color = body.color;
        float pixel = 1f / skeletonSprite.pixelsPerUnit;
        skeleton.transform.localPosition = new Vector3(((int)(elapsed / blinkInterval) & 2) == 0 ? -pixel : pixel, 0f, -0.01f);
    }

    private void OnPowerStarted() { if (active) committed = true; }
    private void OnConductionEnded() { if (!committed) Finish(); }

    private void Finish()
    {
        if (!active) return;
        active = false;
        if (body != null) body.forceRenderingOff = previousForceRenderingOff;
        if (skeleton != null) skeleton.enabled = false;
        if (player != null) player.EndElectricStun(this);
    }

    private void OnDisable()
    {
        Finish();
        if (receiver != null)
        {
            receiver.ConductionStarted -= Begin;
            receiver.ConductionEnded -= OnConductionEnded;
            receiver.PowerStarted -= OnPowerStarted;
        }
        if (door != null) door.FullyOpened -= Finish;
        if (health != null) health.OnDead -= Finish;
    }

    private void OnDestroy() { if (skeleton != null) Destroy(skeleton.gameObject); }
}
