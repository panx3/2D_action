using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>EnemyHealthの確定被弾を表示する共通Component。HP・移動・攻撃は変更しない。</summary>
[DisallowMultipleComponent, DefaultExecutionOrder(100)]
[RequireComponent(typeof(EnemyHealth))]
public sealed class EnemyDamageFeedback : MonoBehaviour
{
    [Header("Mini HP gauge")]
    [SerializeField] private bool gaugeEnabled = true;
    [SerializeField] private EnemyMiniHealthGauge gaugePrefab;
    [SerializeField, Min(0.05f)] private float gaugeDisplayTime = 1.5f;
    [SerializeField] private Vector3 gaugeOffset = new Vector3(0f, 2.95f, 0f);
    [Header("Flash (alpha-mask overlays, no source-material changes)")]
    [SerializeField] private bool flashEnabled = true;
    [SerializeField] private SpriteRenderer[] bodyRenderers;
    [SerializeField] private Material flashMaterial;
    [SerializeField] private Color flashColor = Color.white;
    [SerializeField, Min(0f)] private float flashDuration = 0.1f;
    [Header("Replaceable effects")]
    [SerializeField] private bool hitEffectEnabled = true;
    [SerializeField] private GameObject hitEffectPrefab;
    [SerializeField] private Vector3 hitOffset;
    [SerializeField] private bool criticalEffectEnabled = true;
    [SerializeField, Min(1)] private int criticalHpThreshold = 1;
    [SerializeField] private GameObject criticalEffectPrefab;
    [SerializeField] private Vector3 criticalOffset = new Vector3(0f, 1.1f, 0f);
    [SerializeField] private bool defeatEffectEnabled = true;
    [SerializeField] private GameObject defeatEffectPrefab;
    [SerializeField] private Vector3 defeatOffset = new Vector3(0f, 1.1f, 0f);

    private EnemyHealth health;
    private EnemyMiniHealthGauge gauge;
    private SpriteRenderer[] overlays;
    private GameObject critical;
    private float gaugeUntil, flashUntil;
    public bool GaugeVisible => gauge != null && gauge.gameObject.activeSelf;
    public bool CriticalActive => critical != null;
    public bool FlashActive => flashUntil > Time.time;

    private void Awake()
    {
        health = GetComponent<EnemyHealth>();
        if (bodyRenderers == null || bodyRenderers.Length == 0)
        {
            Transform visual = transform.Find("Visual");
            bodyRenderers = (visual != null ? visual : transform).GetComponentsInChildren<SpriteRenderer>(true);
        }
        overlays = new SpriteRenderer[bodyRenderers.Length];
        if (flashMaterial != null)
            for (int i = 0; i < bodyRenderers.Length; i++)
            {
                if (bodyRenderers[i] == null) continue;
                var overlay = new GameObject("DamageFlash").AddComponent<SpriteRenderer>();
                overlay.transform.SetParent(bodyRenderers[i].transform, false);
                overlay.sharedMaterial = flashMaterial; overlay.enabled = false; overlays[i] = overlay;
            }
        health.HitConfirmed += OnHit;
        health.HealthReset += ResetPresentation;
    }
    private void OnEnable() { if (health != null) ResetPresentation(); }
    private void OnDisable() { ClearTransient(); }
    private void OnDestroy()
    {
        if (health != null) { health.HitConfirmed -= OnHit; health.HealthReset -= ResetPresentation; }
        if (gauge != null) Destroy(gauge.gameObject);
        if (overlays != null) foreach (var item in overlays) if (item != null) Destroy(item.gameObject);
    }
    private void LateUpdate()
    {
        if (health.CurrentHp <= 0) { ClearTransient(); return; }
        if (gauge != null)
        {
            gauge.gameObject.SetActive(gaugeEnabled && Time.time < gaugeUntil);
            gauge.transform.SetPositionAndRotation(WorldPoint(gaugeOffset), Quaternion.identity);
        }
        for (int i = 0; i < overlays.Length; i++)
        {
            var overlay = overlays[i]; var source = bodyRenderers[i];
            if (overlay == null || source == null) continue;
            overlay.enabled = flashEnabled && Time.time < flashUntil && source.enabled;
            if (!overlay.enabled) continue;
            overlay.sprite = source.sprite; overlay.flipX = source.flipX; overlay.flipY = source.flipY;
            overlay.color = new Color(flashColor.r, flashColor.g, flashColor.b, flashColor.a * source.color.a);
            overlay.sortingLayerID = source.sortingLayerID; overlay.sortingOrder = source.sortingOrder + 1;
            overlay.maskInteraction = source.maskInteraction;
        }
        RefreshCritical();
    }
    private int SortingLayer => bodyRenderers.Length > 0 && bodyRenderers[0] != null ? bodyRenderers[0].sortingLayerID : 0;
    private int SortingOrder => bodyRenderers.Length > 0 && bodyRenderers[0] != null ? bodyRenderers[0].sortingOrder + 20 : 25;
    private Vector3 WorldPoint(Vector3 offset) => transform.position + Vector3.Scale(offset,
        new Vector3(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y), 1f));

    private void OnHit(MorningStarHitContext context, bool lethal)
    {
        if (!isActiveAndEnabled) return;
        if (hitEffectEnabled) Spawn(hitEffectPrefab, (Vector3)context.ImpactPoint + hitOffset);
        if (lethal)
        {
            ClearTransient();
            // HitConfirmedはEnemyHealthの既存死亡処理より先。切り離して最後まで再生する。
            if (defeatEffectEnabled) Spawn(defeatEffectPrefab, WorldPoint(defeatOffset));
            return;
        }
        flashUntil = Time.time + flashDuration;
        gaugeUntil = Time.time + gaugeDisplayTime;
        if (gaugeEnabled && gaugePrefab != null)
        {
            if (gauge == null)
            {
                gauge = Instantiate(gaugePrefab);
                SceneManager.MoveGameObjectToScene(gauge.gameObject, gameObject.scene);
            }
            gauge.SetHealth(health.CurrentHp, health.MaxHp, SortingLayer, SortingOrder);
            gauge.transform.SetPositionAndRotation(WorldPoint(gaugeOffset), Quaternion.identity);
            gauge.gameObject.SetActive(true);
        }
        RefreshCritical();
    }
    private GameObject Spawn(GameObject prefab, Vector3 position)
    {
        if (prefab == null) return null;
        var effect = Instantiate(prefab, position, Quaternion.identity);
        SceneManager.MoveGameObjectToScene(effect, gameObject.scene);
        foreach (var renderer in effect.GetComponentsInChildren<SpriteRenderer>(true))
        { renderer.sortingLayerID = SortingLayer; renderer.sortingOrder = SortingOrder - 2; }
        return effect;
    }
    private void RefreshCritical()
    {
        bool needed = criticalEffectEnabled && criticalEffectPrefab != null && health.CurrentHp > 0
            && health.CurrentHp < health.MaxHp && health.CurrentHp <= criticalHpThreshold;
        if (!needed) { if (critical != null) { Destroy(critical); critical = null; } return; }
        if (critical == null) critical = Spawn(criticalEffectPrefab, WorldPoint(criticalOffset));
        if (critical != null) critical.transform.position = WorldPoint(criticalOffset);
    }
    private void ClearTransient()
    {
        gaugeUntil = flashUntil = 0f;
        if (gauge != null) gauge.gameObject.SetActive(false);
        if (critical != null) { critical.SetActive(false); Destroy(critical); critical = null; }
        if (overlays != null) foreach (var item in overlays) if (item != null) item.enabled = false;
    }
    private void ResetPresentation() { ClearTransient(); if (isActiveAndEnabled) RefreshCritical(); }
}
