using System.Collections.Generic;
using UnityEngine;

/// <summary>Built-in用の、Sprite/Tilemap表面を照らす軽量な円形光源。影生成は行わない。</summary>
[DisallowMultipleComponent]
public sealed class ScreenLocalLight2D : MonoBehaviour
{
    internal static readonly List<ScreenLocalLight2D> Active = new List<ScreenLocalLight2D>();
    public bool lightEnabled = true;
    public Color color = new Color(0.65f, 0.85f, 1, 1);
    [Range(0, 2)] public float intensity = 0.22f;
    [Min(0.01f)] public float radius = 4;
    [Range(0.25f, 4)] public float falloff = 1.7f;
    public bool flickerEnabled;
    [Min(0)] public float flickerSpeed = 1.5f;
    [Range(0, 0.5f)] public float flickerAmount = 0.04f;
    public LayerMask targetLayers = ~0;
    [Tooltip("任意。発光元の非表示・破壊に合わせて光も消します。")]
    public SpriteRenderer emissionSource;
    [Tooltip("任意。Tilemapなど発光元の描画を止めた場合も光を消します。")]
    public Renderer emissionOwner;
    public bool glowEnabled = true;
    [Range(0, 1)] public float glowIntensity = 0.22f;
    [Min(0.01f)] public float glowRadius = 1.5f;
    private SpriteRenderer glowRenderer;

    internal bool EmitsLight => lightEnabled && (emissionSource == null ||
        (emissionSource.enabled && emissionSource.gameObject.activeInHierarchy && emissionSource.color.a > 0.01f)) &&
        (emissionOwner == null || (emissionOwner.enabled && emissionOwner.gameObject.activeInHierarchy));

    private void OnEnable() { if (!Active.Contains(this)) Active.Add(this); }
    private void OnDisable() { Active.Remove(this); SetGlowVisible(false); }
    internal void SetGlowVisible(bool visible) { if (glowRenderer != null) glowRenderer.enabled = visible; }
    internal void UpdateGlow(Material material, Sprite sprite)
    {
        bool show = EmitsLight && glowEnabled && glowIntensity > 0 && material != null && sprite != null;
        if (!show) { SetGlowVisible(false); return; }
        if (glowRenderer == null)
        {
            var go = new GameObject("Local Light Glow") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false);
            glowRenderer = go.AddComponent<SpriteRenderer>();
            glowRenderer.sortingOrder = -710; // Behind dust, terrain, characters and every UI canvas.
        }
        glowRenderer.gameObject.layer = gameObject.layer;
        glowRenderer.sprite = sprite;
        glowRenderer.sharedMaterial = material;
        Vector3 parentScale = transform.lossyScale;
        glowRenderer.transform.localScale = new Vector3(glowRadius * 2 / Mathf.Max(0.001f, Mathf.Abs(parentScale.x)), glowRadius * 2 / Mathf.Max(0.001f, Mathf.Abs(parentScale.y)), 1);
        Color tint = color;
        tint.a = Mathf.Clamp01(glowIntensity) * (intensity > 0 ? Strength / intensity : 0);
        glowRenderer.color = tint;
        glowRenderer.enabled = true;
    }
    private void OnDestroy()
    {
        if (glowRenderer == null) return;
        if (Application.isPlaying) Destroy(glowRenderer.gameObject); else DestroyImmediate(glowRenderer.gameObject);
    }
    internal float Strength => Mathf.Max(0, intensity) * (flickerEnabled
        ? 1 - Mathf.Clamp01(flickerAmount) * Mathf.PerlinNoise(GetInstanceID() * 0.01f, Time.time * flickerSpeed) : 1);
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = color;
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
