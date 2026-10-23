using UnityEngine;

/// <summary>Player-body-only collectible. Physics stays fixed while the original artwork floats.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider2D))]
public sealed class PinkJewelPickup : MonoBehaviour
{
    [Header("Collection")]
    [SerializeField, Range(1, 3), Tooltip("Placement identity, not pickup order. Use each number once per stage.")]
    private int slot = 1;

    [Header("Original artwork / separate light layers")]
    [SerializeField] private Transform visualRoot;
    [SerializeField] private SpriteRenderer jewelRenderer;
    [SerializeField] private SpriteRenderer glowRenderer;
    [SerializeField] private SpriteRenderer reflection;
    [SerializeField] private SpriteRenderer surfaceShine;
    [SerializeField] private ParticleSystem idleStars;
    [SerializeField] private ParticleSystem idleDiamonds;
    [SerializeField, Min(0f), Tooltip("World units; 0.125 = 4 source pixels at 32 PPU.")]
    private float floatAmplitude = 0.125f;
    [SerializeField, Min(0.1f)] private float floatPeriod = 1.8f;
    [SerializeField, Range(0f, 1f)] private float glowIntensity = 0.4f;
    [SerializeField, Range(0f, 20f)] private float particleRate = 8f;
    [SerializeField, Min(0.2f)] private float reflectionPeriod = 2.6f;

    [Header("Pixel surface reflection")]
    [SerializeField, Min(0.5f)] private float shinePeriod = 3.2f;
    [SerializeField, Min(0.1f)] private float shineDuration = 0.65f;
    [SerializeField, Range(0f, 1f)] private float shineIntensity = 0.85f;

    [Header("Ground shadow")]
    [SerializeField] private SpriteRenderer groundShadow;
    [SerializeField, Range(0f, 1f)] private float shadowOpacity = 0.52f;
    [SerializeField, Min(0.1f)] private float shadowProbeDistance = 8f;

    [Header("Detached pickup effect")]
    [SerializeField] private PinkJewelPickupVfx pickupEffect;
    [SerializeField, Range(0.8f, 1.2f)] private float pickupDuration = 1f;
    [SerializeField, Range(0, 80)] private int burstCount = 28;
    [SerializeField, Min(0f)] private float burstSpeed = 2.5f;
    [SerializeField, Range(0f, 1f)] private float flashIntensity = 0.85f;

    [Header("Pickup sound")]
    [SerializeField] private AudioClip pickupClip;
    [SerializeField, Range(0f, 1f)] private float soundVolume = 0.7f;
    [SerializeField, Tooltip("Unassigned uses the existing WorldImpactSfx source, with a detached 2D fallback.")]
    private AudioSource soundSource;

    private BoxCollider2D pickupTrigger;
    private PinkJewelCollection collection;
    private Vector3 restPosition, shadowScale;
    private float idleTime, nextGlint, shineTime;
    private MaterialPropertyBlock shineProperties;
    private static readonly int ShinePosition = Shader.PropertyToID("_ShinePosition");
    private static readonly int ShineStrength = Shader.PropertyToID("_ShineStrength");
    private bool collected;
    public int Slot => slot;
    public bool IsCollected => collected;

    private void Awake()
    {
        pickupTrigger = GetComponent<BoxCollider2D>();
        pickupTrigger.isTrigger = true;
        restPosition = visualRoot.localPosition;
        idleTime = Mathf.Repeat(transform.position.x * 0.37f, floatPeriod);
        nextGlint = reflectionPeriod * 0.5f;
        // Offset each placed jewel so the stage does not flash in unison.
        shineTime = Mathf.Repeat(transform.position.x * 0.17f, shinePeriod);
        shineProperties = new MaterialPropertyBlock();
        if (soundSource == null) soundSource = OneShotAudioUtility.FindWorldImpactSource();
        ApplyParticleRate();
        PlaceGroundShadow();
    }

    private void Start()
    {
        collection = PinkJewelCollection.Current;
        if (collection == null)
        {
            Debug.LogError("[PinkJewel] Add PinkJewelCollection to the stage before placing jewels.", this);
            enabled = false;
            return;
        }
        if (collection.IsCollected(slot)) HideCollected();
    }

    private void OnValidate()
    {
        slot = Mathf.Clamp(slot, 1, 3);
        floatPeriod = Mathf.Max(0.1f, floatPeriod);
        reflectionPeriod = Mathf.Max(0.2f, reflectionPeriod);
        shinePeriod = Mathf.Max(0.5f, shinePeriod);
        shineDuration = Mathf.Clamp(shineDuration, 0.1f, shinePeriod);
        if (Application.isPlaying) ApplyParticleRate();
    }

    private void ApplyParticleRate()
    {
        foreach (ParticleSystem ps in new[] { idleStars, idleDiamonds })
        {
            if (ps == null) continue;
            var emission = ps.emission;
            emission.rateOverTime = particleRate * 0.5f;
        }
    }

    private void OnTriggerEnter2D(Collider2D other) => TryCollect(other);
    private void OnTriggerStay2D(Collider2D other) => TryCollect(other);

    private void TryCollect(Collider2D other)
    {
        if (collected || !isActiveAndEnabled || other == null || collection == null || Time.timeScale <= 0f) return;
        // Production Player has its solid capsule on the Player GameObject. Equipment children
        // are never accepted, even if a future chain collider inherits the Player's Rigidbody.
        Player player = other.GetComponent<Player>();
        PlayerHealth health = other.GetComponent<PlayerHealth>();
        if (other.isTrigger || player == null || health == null || health.IsDead || health.IsRespawning
            || player.IsGoalInputBlocked || other.attachedRigidbody == null
            || other.attachedRigidbody != player.GetComponent<Rigidbody2D>()) return;
        if (!collection.TryCollect(slot)) return;

        Vector3 position = jewelRenderer.transform.position;
        // The shared/detached source continues playing after the jewel visuals disappear.
        if (soundVolume > 0f) OneShotAudioUtility.Play2D(soundSource, pickupClip, soundVolume, position);
        if (pickupEffect != null)
        {
            // A separate root owns the full tail; hiding/removing the pickup cannot truncate it.
            PinkJewelPickupVfx effect = Instantiate(pickupEffect, position, Quaternion.identity);
            effect.Play(jewelRenderer.sprite, jewelRenderer.transform.lossyScale,
                jewelRenderer.sortingLayerID, jewelRenderer.sortingOrder,
                pickupDuration, burstCount, burstSpeed, flashIntensity);
        }
        HideCollected();
    }

    private void HideCollected()
    {
        collected = true;
        pickupTrigger.enabled = false;
        visualRoot.gameObject.SetActive(false);
        if (groundShadow != null) groundShadow.enabled = false;
        StopIdle(idleStars);
        StopIdle(idleDiamonds);
    }

    private static void StopIdle(ParticleSystem ps)
    {
        if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void Update()
    {
        if (collected) return;
        idleTime += Time.deltaTime;
        float phase = idleTime * Mathf.PI * 2f / floatPeriod;
        float bob = Mathf.Sin(phase);
        visualRoot.localPosition = restPosition + Vector3.up * (floatAmplitude * bob);
        glowRenderer.color = new Color(1f, 0.26f, 0.63f, glowIntensity * (0.82f + 0.18f * Mathf.Sin(phase + 0.6f)));
        UpdateShadow(bob);
        UpdateSurfaceShine(Time.deltaTime);

        nextGlint -= Time.deltaTime;
        if (nextGlint <= -0.24f) nextGlint = Random.Range(reflectionPeriod * 0.7f, reflectionPeriod * 1.3f);
        float glint = nextGlint > 0f ? 0f : Mathf.Sin(Mathf.Clamp01(-nextGlint / 0.24f) * Mathf.PI);
        reflection.color = new Color(1f, 0.94f, 0.98f, glint * 0.95f);
        reflection.transform.localScale = Vector3.one * Mathf.Lerp(0.35f, 0.7f, glint);
        if (glint <= 0f && nextGlint > 0f)
        {
            // Alternate the facet/tip position once per glint, without rotating the jewel.
            if (reflection.enabled) reflection.transform.localPosition = Random.value > 0.5f
                ? new Vector3(-0.25f, 0.63f, 0f) : new Vector3(0.18f, 1.05f, 0f);
            reflection.enabled = false;
        }
        else reflection.enabled = true;
    }

    private void UpdateSurfaceShine(float deltaTime)
    {
        if (surfaceShine == null) return;
        shineTime = Mathf.Repeat(shineTime + deltaTime, shinePeriod);
        surfaceShine.enabled = shineTime < shineDuration;
        if (!surfaceShine.enabled) return;
        float progress = shineTime / shineDuration;
        // Only this separate, alpha-masked layer changes; the supplied sprite stays intact.
        shineProperties.SetFloat(ShinePosition, Mathf.Lerp(-0.25f, 1.25f, progress));
        shineProperties.SetFloat(ShineStrength, shineIntensity * Mathf.Sin(progress * Mathf.PI));
        surfaceShine.SetPropertyBlock(shineProperties);
    }

    private void PlaceGroundShadow()
    {
        if (groundShadow == null) return;
        shadowScale = groundShadow.transform.localScale;
        groundShadow.enabled = false;
        foreach (RaycastHit2D hit in Physics2D.RaycastAll(transform.position + Vector3.up * 0.2f, Vector2.down, shadowProbeDistance))
        {
            if (hit.collider.isTrigger || !hit.collider.CompareTag("Floor") || hit.normal.y < 0.35f) continue;
            groundShadow.transform.position = hit.point + hit.normal * 0.025f;
            groundShadow.transform.rotation = Quaternion.FromToRotation(Vector3.up, hit.normal);
            Renderer floor = hit.collider.GetComponent<Renderer>();
            if (floor != null)
            {
                groundShadow.sortingLayerID = floor.sortingLayerID;
                groundShadow.sortingOrder = floor.sortingOrder + 1;
            }
            groundShadow.enabled = true;
            UpdateShadow(0f);
            break;
        }
    }

    private void UpdateShadow(float bob)
    {
        if (groundShadow == null || !groundShadow.enabled) return;
        float height = bob * 0.5f + 0.5f;
        groundShadow.transform.localScale = shadowScale * Mathf.Lerp(0.96f, 1.06f, height);
        groundShadow.color = new Color(0.055f, 0.04f, 0.09f, shadowOpacity * Mathf.Lerp(1f, 0.8f, height));
    }
}
