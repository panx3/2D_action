using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>EnemyHealthの確定被弾を表示する共通Component。HP・移動・攻撃は変更しない。</summary>
[DisallowMultipleComponent, DefaultExecutionOrder(100)]
[RequireComponent(typeof(EnemyHealth))]
public sealed class EnemyDamageFeedback : MonoBehaviour
{
    [Header("HP gauge")]
    [SerializeField] private bool gaugeEnabled = true;
    [SerializeField] private EnemyHealthGaugeStyle gaugeStyle;
    [Tooltip("空欄の場合は共通の MiniHealthGauge Prefab を使います。")]
    [SerializeField] private EnemyMiniHealthGauge gaugePrefab;
    [Tooltip("ON は見た目の上端から、OFF は敵ルートからのオフセット。")]
    [SerializeField] private bool gaugeAboveBody;
    [SerializeField] private Vector3 gaugeOffset = new Vector3(0f, 0.36f, 0f);
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

    private static EnemyMiniHealthGauge sharedGaugePrefab;
    private EnemyHealth health;
    private EnemyMiniHealthGauge gauge;
    private SpriteRenderer[] overlays;
    private GameObject critical;
    private float flashUntil;
    private int previousHp;
    private bool gaugeDefeatStarted;
    public bool GaugeVisible => gauge != null && gauge.gameObject.activeSelf;
    public bool CriticalActive => critical != null;
    public bool FlashActive => flashUntil > Time.time;

    internal static void AddWithAutomaticGaugePosition(GameObject owner)
    {
        owner.AddComponent<EnemyDamageFeedback>().gaugeAboveBody = true;
    }

    private void Reset() => gaugeAboveBody = true;

    private void Awake()
    {
        health = GetComponent<EnemyHealth>();
        if (bodyRenderers == null || bodyRenderers.Length == 0)
        {
            Transform visual = transform.Find("Visual");
            bodyRenderers = (visual != null ? visual : transform).GetComponentsInChildren<SpriteRenderer>(true);
        }
        overlays = flashMaterial != null ? new SpriteRenderer[bodyRenderers.Length] : System.Array.Empty<SpriteRenderer>();
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
        if (gauge != null && !gauge.IsDefeating) Destroy(gauge.gameObject);
        if (overlays != null) foreach (var item in overlays) if (item != null) Destroy(item.gameObject);
    }
    private void LateUpdate()
    {
        if (GaugeVisible && !gauge.IsDefeating)
            gauge.transform.position = GaugePosition();
        if (health.CurrentHp <= 0) { ClearBodyEffects(); return; }
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
    private SpriteRenderer Body => bodyRenderers.Length > 0 ? bodyRenderers[0] : null;
    private int SortingLayer => Body != null ? Body.sortingLayerID : 0;
    private int SortingOrder => Body != null ? Body.sortingOrder + 20 : 25;
    private Vector3 WorldPoint(Vector3 offset) => transform.position + Vector3.Scale(offset,
        new Vector3(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y), 1f));

    private Vector3 GaugePosition()
    {
        if (!gaugeAboveBody) return WorldPoint(gaugeOffset);
        Vector3 point = transform.position;
        if (Body != null && Body.sprite != null) point.y = Mathf.Max(point.y, Body.bounds.max.y);
        return point + gaugeOffset;
    }

    private void OnHit(MorningStarHitContext context, bool lethal)
    {
        if (!isActiveAndEnabled) return;
        int prior = previousHp;
        previousHp = health.CurrentHp;
        if (gaugeEnabled && health.MaxHp >= 2 && (previousHp < prior || lethal))
            ShowGauge(prior, lethal);

        if (hitEffectEnabled) Spawn(hitEffectPrefab, (Vector3)context.ImpactPoint + hitOffset);
        if (lethal)
        {
            ClearBodyEffects();
            if (defeatEffectEnabled) Spawn(defeatEffectPrefab, WorldPoint(defeatOffset));
            return;
        }
        flashUntil = Time.time + flashDuration;
        RefreshCritical();
    }

    private void ShowGauge(int prior, bool lethal)
    {
        if (gaugeDefeatStarted) return;
        if (gauge == null)
        {
            if (gaugePrefab == null && sharedGaugePrefab == null)
            {
                var asset = Resources.Load<GameObject>(EnemyMiniHealthGauge.ResourcePath);
                if (asset != null) sharedGaugePrefab = asset.GetComponent<EnemyMiniHealthGauge>();
            }
            var prefab = gaugePrefab != null ? gaugePrefab : sharedGaugePrefab;
            if (prefab == null) return;
            gauge = Instantiate(prefab);
            SceneManager.MoveGameObjectToScene(gauge.gameObject, gameObject.scene);
            gauge.Initialize(gaugeStyle, SortingLayer, SortingOrder);
        }

        gauge.transform.position = GaugePosition();
        float max = health.MaxHp;
        // サソリの遅延死亡中は空のバーを表示し、死亡確定時に一度だけ砕く。
        gaugeDefeatStarted = lethal;
        gauge.Impact(Mathf.Clamp01(prior / max), Mathf.Clamp01(previousHp / max), lethal);
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
        if (gauge != null && !gauge.IsDefeating) gauge.Hide();
        ClearBodyEffects();
    }
    private void ClearBodyEffects()
    {
        flashUntil = 0f;
        if (critical != null) { critical.SetActive(false); Destroy(critical); critical = null; }
        if (overlays != null) foreach (var item in overlays) if (item != null) item.enabled = false;
    }
    private void ResetPresentation()
    {
        previousHp = health.CurrentHp;
        gaugeDefeatStarted = false;
        if (gauge != null && gauge.IsDefeating)
        {
            gauge.gameObject.SetActive(false);
            Destroy(gauge.gameObject);
            gauge = null;
        }
        ClearTransient();
        if (isActiveAndEnabled) RefreshCritical();
    }
}
