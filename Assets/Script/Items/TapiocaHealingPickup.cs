using UnityEngine;

/// <summary>回復量はPlayerHealth、表示は既存HP UIに委譲する、接触取得用の回復アイテム。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider2D))]
public sealed class TapiocaHealingPickup : MonoBehaviour
{
    [Header("回復")]
    [SerializeField, Min(1), Tooltip("最大HP14に対して初期値4。満タンでも取得・消費します。死亡中は取得しません。")]
    private int healAmount = 4;

    [Header("常時演出")]
    [SerializeField] private Transform visualRoot;
    [SerializeField] private SpriteRenderer drinkRenderer;
    [SerializeField] private SpriteRenderer glowRenderer;
    [SerializeField] private ParticleSystem idleParticles;
    [SerializeField, Min(0f), Tooltip("上下の片振幅（world units）。初期値0.18は元画像の約5px分。")]
    private float floatAmplitude = 0.18f;
    [SerializeField, Min(0.1f), Tooltip("浮遊の1周期（秒）")]
    private float floatPeriod = 1.6f;
    [SerializeField, Range(0f, 1f)] private float glowIntensity = 0.38f;
    [SerializeField, Range(0f, 8f), Tooltip("1秒あたりの常時粒子数")]
    private float particleRate = 3.2f;

    [Header("接地影")]
    [SerializeField] private SpriteRenderer groundShadow;
    [SerializeField, Range(0f, 1f)] private float shadowOpacity = 0.60f;
    [SerializeField, Min(0.1f), Tooltip("下方のFloorを探す距離。床がない場合は影を表示しません。")]
    private float shadowProbeDistance = 8f;

    [Header("取得演出")]
    [SerializeField, Range(0.2f, 0.5f)] private float pickupDuration = 0.28f;
    [SerializeField] private ParticleSystem pickupBurst;
    [SerializeField] private ParticleSystem playerHealingEffect;
    [SerializeField] private AudioClip pickupClip;
    [SerializeField, Range(0f, 1f)] private float soundVolume = 0.5f;
    [SerializeField, Min(0f), Tooltip("0なら取得音の長さだけ飲んでから回復。秒を指定して調整も可能。")]
    private float drinkingDuration;
    [SerializeField] private AudioClip recoveryClip;
    [SerializeField, Range(0f, 1f)] private float recoverySoundVolume = 0.65f;
    [SerializeField, Range(0.5f, 2f)] private float recoveryEffectDuration = 1.1f;
    [SerializeField, Tooltip("未設定時は既存WorldImpactSfxへルーティングします。")]
    private AudioSource soundSource;

    private BoxCollider2D pickupTrigger;
    private Vector3 restPosition;
    private Vector3 pickupStart;
    private Vector3 pickupEnd;
    private float idleTime;
    private float pickupTime;
    private bool collected;
    private Player pickupPlayer;
    private ParticleSystem[] idleEmitters;
    private Vector3 shadowBaseScale;
    private PlayerHealth receiverHealth;
    private Collider2D receiverCollider;
    private int receiverVersion;
    private float recoveryStartsAt;
    private bool recoveryStarted;
    private ParticleSystem recoveryEffect;
    private static float nextSoundTime;
    private static float nextRecoverySoundTime;

    public bool IsCollected => collected;
    public int HealAmount => healAmount;
    public bool HasRecovered => recoveryStarted;
    public float DrinkingDuration => drinkingDuration > 0f ? drinkingDuration
        : pickupClip != null ? pickupClip.length : 0.94f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSoundCooldown()
    {
        nextSoundTime = nextRecoverySoundTime = 0f;
    }

    private void Awake()
    {
        pickupTrigger = GetComponent<BoxCollider2D>();
        pickupTrigger.isTrigger = true;
        if (visualRoot == null) visualRoot = transform;
        restPosition = visualRoot.localPosition;
        // 複数配置しても完全に同期させない。物理判定は固定位置のまま。
        idleTime = Mathf.Repeat(transform.position.x * 0.37f, floatPeriod);
        if (soundSource == null) soundSource = OneShotAudioUtility.FindWorldImpactSource();
        ApplyParticleRate();
        PlaceGroundShadow();
    }

    private void OnValidate()
    {
        healAmount = Mathf.Max(1, healAmount);
        floatPeriod = Mathf.Max(0.1f, floatPeriod);
        floatAmplitude = Mathf.Max(0f, floatAmplitude);
        drinkingDuration = Mathf.Max(0f, drinkingDuration);
        if (Application.isPlaying) ApplyParticleRate();
    }

    private void ApplyParticleRate()
    {
        if (idleParticles == null) return;
        if (idleEmitters == null) idleEmitters = idleParticles.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < idleEmitters.Length; i++)
        {
            var emission = idleEmitters[i].emission;
            emission.rateOverTime = particleRate / idleEmitters.Length;
        }
    }

    private void OnTriggerEnter2D(Collider2D other) => TryCollect(other);
    private void OnTriggerStay2D(Collider2D other) => TryCollect(other);

    private void TryCollect(Collider2D other)
    {
        // Unityは無効なBehaviourにもTrigger通知を送るため、enabledも確認する。
        if (collected || !isActiveAndEnabled || other == null || Time.timeScale <= 0f) return;
        Player player = other.GetComponent<Player>();
        PlayerHealth health = other.GetComponent<PlayerHealth>();
        if (other.isTrigger || player == null || health == null || health.IsDead || health.IsRespawning
            || player.IsGoalInputBlocked || !PickupContactUtility.Touches(drinkRenderer, player)) return;
        // Player配下の鉄球や鎖では拾わない。Player本人のbodyに属するColliderだけ許可。
        if (other.attachedRigidbody == null
            || other.attachedRigidbody != health.GetComponent<Rigidbody2D>()) return;

        // 1段階目: 取得確定・小さな反応・飲む音。HPはまだ回復させない。
        collected = true;
        pickupTrigger.enabled = false;
        receiverHealth = health;
        receiverCollider = other;
        receiverVersion = health.RecoveryVersion;
        recoveryStartsAt = Mathf.Max(pickupDuration, DrinkingDuration);
        pickupStart = visualRoot.position;
        // 接触した方向へ短く寄せる。高速移動するPlayerを追い続けて軌道を曲げない。
        pickupEnd = pickupStart + Vector3.ClampMagnitude(other.bounds.center - pickupStart, 0.35f);

        if (idleParticles != null)
            idleParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (pickupBurst != null)
        {
            pickupBurst.transform.position = pickupStart;
            pickupBurst.Play(true);
        }
        if (Time.unscaledTime >= nextSoundTime && soundVolume > 0f && pickupClip != null)
        {
            // 同フレームの重複音を抑え、他の戦闘SEは止めない。
            if (OneShotAudioUtility.Play2D(soundSource, pickupClip, soundVolume, transform.position))
                nextSoundTime = Time.unscaledTime + 0.09f;
        }
    }

    private bool ReceiverIsValid => receiverHealth != null && receiverHealth.isActiveAndEnabled
        && !receiverHealth.IsDead && receiverHealth.RecoveryVersion == receiverVersion;

    private void BeginRecovery()
    {
        // 2段階目: 同じフレームで回復音・HP・キャラクター・UIを同期する。
        recoveryStarted = true;
        receiverHealth.Heal(healAmount);
        if (!ReceiverIsValid) return;
        Vector3 center = receiverCollider != null ? receiverCollider.bounds.center : receiverHealth.transform.position;
        if (playerHealingEffect != null)
        {
            recoveryEffect = Instantiate(playerHealingEffect, center, Quaternion.identity, receiverHealth.transform);
            recoveryEffect.Play(true);
        }
        foreach (var bar in FindObjectsByType<SegmentHpBarUI>(FindObjectsSortMode.None))
            bar.PlayItemRecovery(receiverHealth, recoveryEffectDuration);
        if (Time.unscaledTime >= nextRecoverySoundTime && recoveryClip != null && recoverySoundVolume > 0f)
        {
            if (OneShotAudioUtility.Play2D(soundSource, recoveryClip, recoverySoundVolume, center))
                nextRecoverySoundTime = Time.unscaledTime + 0.12f;
        }
    }

    private void OnDisable()
    {
        if (!collected) return;
        if (recoveryEffect != null) Destroy(recoveryEffect.gameObject);
        // 取得後の無効化・再有効化で遅延回復を再開しない。
        Destroy(gameObject);
    }

    private void Update()
    {
        if (collected)
        {
            if (!ReceiverIsValid)
            {
                Destroy(gameObject);
                return;
            }
            pickupTime += Time.deltaTime;
            float t = Mathf.Clamp01(pickupTime / pickupDuration);
            // 冒頭は形を見せ、拡縮せず短い引き寄せとフェードで消す。
            float motion = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.12f, 1f, t));
            float opacity = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.32f, 0.95f, t));
            visualRoot.position = Vector3.Lerp(pickupStart, pickupEnd, motion);
            SetGlow(glowIntensity * 1.2f * (1f - motion));
            UpdateGroundShadow(0f, 1f - motion);
            if (drinkRenderer != null)
                drinkRenderer.color = new Color(1f, 1f, 1f, opacity);
            if (t >= 1f && drinkRenderer != null) drinkRenderer.enabled = false;
            if (!recoveryStarted && pickupTime >= recoveryStartsAt) BeginRecovery();
            // 飲み終わるまで、見えないrootが遅延回復を所有する。
            float cleanupAt = recoveryStartsAt + Mathf.Max(recoveryEffectDuration,
                recoveryClip != null ? recoveryClip.length : 0f) + 0.25f;
            if (recoveryStarted && pickupTime >= cleanupAt) Destroy(gameObject);
            return;
        }

        idleTime += Time.deltaTime;
        float phase = idleTime * (Mathf.PI * 2f / floatPeriod);
        float bob = Mathf.Sin(phase) * 0.86f + Mathf.Sin(phase * 2f + 0.6f) * 0.14f;
        visualRoot.localPosition = restPosition + Vector3.up * (bob * floatAmplitude);
        SetGlow(glowIntensity * (0.85f + Mathf.Sin(phase + 0.8f) * 0.15f));
        UpdateGroundShadow(bob, 1f);
        if (PickupContactUtility.TryGetTouchingBody(drinkRenderer, ref pickupPlayer, out Collider2D body))
            TryCollect(body);
    }

    private void PlaceGroundShadow()
    {
        if (groundShadow == null) return;
        shadowBaseScale = groundShadow.transform.localScale;
        groundShadow.enabled = false;
        // 静止配置アイテムのため床の検出は起動時だけ。Playerや他のTriggerは除外する。
        RaycastHit2D[] hits = Physics2D.RaycastAll(transform.position + Vector3.up * 0.2f,
            Vector2.down, shadowProbeDistance);
        for (int i = 0; i < hits.Length; i++)
        {
            var hit = hits[i];
            if (hit.collider.isTrigger || !hit.collider.CompareTag("Floor") || hit.normal.y < 0.35f) continue;
            groundShadow.transform.position = (Vector3)(hit.point + hit.normal * 0.025f);
            groundShadow.transform.rotation = Quaternion.FromToRotation(Vector3.up, hit.normal);
            var floorRenderer = hit.collider.GetComponent<Renderer>();
            if (floorRenderer != null)
            {
                groundShadow.sortingLayerID = floorRenderer.sortingLayerID;
                groundShadow.sortingOrder = floorRenderer.sortingOrder + 1;
            }
            groundShadow.enabled = true;
            UpdateGroundShadow(0f, 1f);
            return;
        }
    }

    private void UpdateGroundShadow(float bob, float fade)
    {
        if (groundShadow == null || !groundShadow.enabled) return;
        float height = Mathf.Clamp01(bob * 0.5f + 0.5f);
        // 高い位置では少し広く薄くする。接地位置自体は動かさない。
        groundShadow.transform.localScale = shadowBaseScale * Mathf.Lerp(0.96f, 1.06f, height);
        groundShadow.color = new Color(0.055f, 0.04f, 0.09f,
            shadowOpacity * Mathf.Lerp(1f, 0.8f, height) * fade);
    }

    private void SetGlow(float alpha)
    {
        if (glowRenderer == null) return;
        glowRenderer.color = new Color(0.55f, 0.95f, 1f, alpha);
    }
}
