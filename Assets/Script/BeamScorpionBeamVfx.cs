using UnityEngine;

/// <summary>差し替え可能なピクセルビーム。判定・ダメージは持たない。</summary>
public sealed class BeamScorpionBeamVfx : MonoBehaviour
{
    [SerializeField] private SpriteRenderer halo, shell, core, upperRail, lowerRail;
    private Vector2 start, end;
    private float thickness, fadeDuration, fadeElapsed;
    private Color tint;
    public void SetBeam(Vector2 from, Vector2 to, float width, Color color)
    {
        start = from; end = to; thickness = Mathf.Max(0.01f, width); tint = color;
        Render(1f);
    }
    public void FadeOut(float seconds) { fadeDuration = Mathf.Max(0.01f, seconds); }
    private void Update()
    {
        if (fadeDuration <= 0f) return;
        fadeElapsed += Time.deltaTime;
        Render(1f - Mathf.Clamp01(fadeElapsed / fadeDuration));
        if (fadeElapsed >= fadeDuration) Destroy(gameObject);
    }
    private void Render(float alpha)
    {
        Vector2 delta = end - start;
        transform.SetPositionAndRotation((start + end) * 0.5f,
            Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg));
        // 親の敵を拡縮しても、判定と同じワールド寸法で描く。
        transform.localScale = Vector3.one;
        Vector3 inherited = transform.lossyScale;
        transform.localScale = new Vector3(1f / Mathf.Max(0.0001f, Mathf.Abs(inherited.x)),
            1f / Mathf.Max(0.0001f, Mathf.Abs(inherited.y)), 1f);
        float pulse = 0.92f + 0.08f * Mathf.Sin(Mathf.Floor(Time.time * 18f));
        Set(halo, delta.magnitude, thickness * 2.6f, 0f, tint, 0.18f * alpha);
        Set(shell, delta.magnitude, thickness, 0f, tint, 0.9f * alpha);
        Set(core, delta.magnitude, thickness * 0.38f * pulse, 0f, Color.Lerp(tint, Color.white, 0.88f), alpha);
        Set(upperRail, delta.magnitude, thickness * 0.12f, thickness * 0.82f, tint, 0.5f * alpha);
        Set(lowerRail, delta.magnitude, thickness * 0.12f, -thickness * 0.82f, tint, 0.5f * alpha);
    }
    private static void Set(SpriteRenderer renderer, float length, float height, float y, Color color, float alpha)
    {
        if (renderer == null || renderer.sprite == null) return;
        renderer.transform.localPosition = new Vector3(0f, y, 0f);
        renderer.transform.localScale = new Vector3(length / renderer.sprite.bounds.size.x, height / renderer.sprite.bounds.size.y, 1f);
        color.a *= alpha;
        renderer.color = color;
    }
}
