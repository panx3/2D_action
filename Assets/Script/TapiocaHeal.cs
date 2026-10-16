using UnityEngine;

/// <summary>HPが減っているPlayerだけが取得できる、配置型の回復アイテム。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer), typeof(BoxCollider2D))]
public sealed class TapiocaHeal : MonoBehaviour
{
    [SerializeField, Min(1), Tooltip("取得時のHP回復量。最大HPを超えた分は回復しない")]
    private int healAmount = 3;

    [Header("Pickup Feedback")]
    [SerializeField] private TapiocaHealEffects pickupEffects;
    [SerializeField, Tooltip("未設定なら無音。アイテム消滅後も既存の2D音源で最後まで再生")]
    private AudioClip healClip;
    [SerializeField, Range(0f, 1f)] private float healVolume = 0.65f;

    private BoxCollider2D _pickupCollider;
    private SpriteRenderer _spriteRenderer;
    private bool _collected;

    private void Awake()
    {
        _pickupCollider = GetComponent<BoxCollider2D>();
        _spriteRenderer = GetComponent<SpriteRenderer>();
        if (pickupEffects == null)
            pickupEffects = GetComponentInChildren<TapiocaHealEffects>();
    }

    private void Reset()
    {
        GetComponent<BoxCollider2D>().isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other) => TryCollect(other);

    // 満タンで残したアイテムに重なったままHPが減った場合も取得できる。
    private void OnTriggerStay2D(Collider2D other) => TryCollect(other);

    private void TryCollect(Collider2D other)
    {
        if (_collected || !isActiveAndEnabled || healAmount <= 0 || !PlayerColliderUtility.IsPlayerBody(other))
            return;

        PlayerHealth health = other.GetComponentInParent<PlayerHealth>();
        if (health == null || health.IsDead || health.CurrentHp >= health.MaxHp)
            return;

        int amount = Mathf.Min(healAmount, health.MaxHp - health.CurrentHp);
        // Heal内の通知より前に確定し、複数Colliderや同フレームの再入による二重取得を防ぐ。
        _collected = true;
        _pickupCollider.enabled = false;
        _spriteRenderer.enabled = false;
        health.Heal(amount); // 既存のHP更新通知で体力ゲージも更新される。
        foreach (var bar in FindObjectsByType<SegmentHpBarUI>(FindObjectsSortMode.None))
            bar.PlayHealFlash(health);
        if (pickupEffects != null)
            pickupEffects.PlayPickup();
        OneShotAudioUtility.Play2D(OneShotAudioUtility.FindWorldImpactSource(), healClip, healVolume, transform.position);
        Destroy(gameObject);
    }
}
