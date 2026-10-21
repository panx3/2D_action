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

    internal bool EmitsLight => lightEnabled && (emissionSource == null ||
        (emissionSource.enabled && emissionSource.gameObject.activeInHierarchy && emissionSource.color.a > 0.01f));

    private void OnEnable() { if (!Active.Contains(this)) Active.Add(this); }
    private void OnDisable() => Active.Remove(this);
    internal float Strength => Mathf.Max(0, intensity) * (flickerEnabled
        ? 1 - Mathf.Clamp01(flickerAmount) * Mathf.PerlinNoise(GetInstanceID() * 0.01f, Time.time * flickerSpeed) : 1);
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = color;
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
