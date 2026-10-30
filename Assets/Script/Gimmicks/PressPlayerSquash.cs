using UnityEngine;

/// <summary>PlayerのTransform/Collider/剛体を変えず、足元を支点にSpriteの頂点だけを圧縮する。</summary>
[DisallowMultipleComponent, DefaultExecutionOrder(1100)]
public sealed class PressPlayerSquash : MonoBehaviour
{
    private static readonly int SquashId = Shader.PropertyToID("_PressSquash");
    private Player player;
    private PlayerHealth health;
    private SpriteRenderer body;
    private Material ownedMaterial, originalMaterial;
    private MaterialPropertyBlock block;
    private float requested = 1f, lastFixed = -1f, holdUntil, recovery = 0.18f, maximumWidth = 1.18f;
    private int originalOrder, appliedOrder;
    private bool ownsOrder;
    public float VerticalScale { get; private set; } = 1f;
    public float HorizontalScale { get; private set; } = 1f;

    private void Awake()
    {
        player = GetComponent<Player>(); health = GetComponent<PlayerHealth>();
        body = player != null ? player.BodySprite : null;
        block = new MaterialPropertyBlock();
        if (health != null) health.OnDead += ResetEffect;
    }

    public void Contact(float underside, float minimumHeight, float width, float hold, float recoveryTime, int pressOrder)
    {
        if (!enabled || body == null || body.sprite == null || health == null || health.IsDead || health.IsRespawning) return;
        Bounds sprite = body.sprite.bounds;
        Vector3 foot = new Vector3(sprite.center.x, body.flipY ? -sprite.max.y : sprite.min.y, 0f);
        float footY = body.transform.TransformPoint(foot).y;
        float height = sprite.size.y * Mathf.Abs(body.transform.lossyScale.y);
        float target = Mathf.Clamp((underside - footY) / Mathf.Max(0.01f, height), minimumHeight, 0.85f);
        if (lastFixed != Time.fixedTime) { requested = 1f; lastFixed = Time.fixedTime; }
        requested = Mathf.Min(requested, target);
        maximumWidth = width; recovery = Mathf.Max(0.01f, recoveryTime);
        holdUntil = Time.time + hold;
        if (!ownsOrder) { originalOrder = body.sortingOrder; ownsOrder = true; }
        appliedOrder = Mathf.Max(originalOrder, pressOrder + 1);
    }

    private void LateUpdate()
    {
        if (body == null || body.sprite == null || health == null || health.IsDead || health.IsRespawning)
        { ResetEffect(); return; }
        float target = Time.time <= holdUntil ? requested : 1f;
        VerticalScale = target < VerticalScale ? target : Mathf.MoveTowards(VerticalScale, target, Time.deltaTime / recovery);
        if (VerticalScale >= 0.9999f) { ResetEffect(); return; }
        HorizontalScale = Mathf.Lerp(1f, maximumWidth, 1f - VerticalScale);
        if (ownedMaterial == null || body.sharedMaterial != ownedMaterial)
        {
            Shader shader = Resources.Load<Shader>("PressPlayerSquash");
            if (shader == null || !shader.isSupported) { ResetEffect(); return; }
            originalMaterial = body.sharedMaterial;
            if (ownedMaterial == null) ownedMaterial = new Material(shader) { name = "Player press squash (Runtime)", hideFlags = HideFlags.HideAndDontSave };
            if (originalMaterial != null) ownedMaterial.CopyPropertiesFromMaterial(originalMaterial);
            ownedMaterial.SetFloat("_SV_Layer", body.gameObject.layer);
            body.sharedMaterial = ownedMaterial;
        }
        Bounds sprite = body.sprite.bounds;
        body.GetPropertyBlock(block);
        block.SetVector(SquashId, new Vector4(HorizontalScale, VerticalScale, sprite.center.x, body.flipY ? sprite.max.y : sprite.min.y));
        body.SetPropertyBlock(block);
        if (ownsOrder) body.sortingOrder = appliedOrder;
    }

    public void ResetEffect()
    {
        if (body != null)
        {
            body.GetPropertyBlock(block); block.SetVector(SquashId, new Vector4(1f, 1f, 0f, 0f)); body.SetPropertyBlock(block);
            if (ownedMaterial != null && body.sharedMaterial == ownedMaterial) body.sharedMaterial = originalMaterial;
            if (ownsOrder && body.sortingOrder == appliedOrder) body.sortingOrder = originalOrder;
        }
        ownsOrder = false; requested = VerticalScale = HorizontalScale = 1f; holdUntil = -1f;
    }

    private void OnDisable() => ResetEffect();
    private void OnDestroy()
    {
        ResetEffect();
        if (health != null) health.OnDead -= ResetEffect;
        if (ownedMaterial != null) Destroy(ownedMaterial);
    }
}
