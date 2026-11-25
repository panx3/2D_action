using UnityEngine;

/// <summary>差し替え可能なピクセルビーム。判定・ダメージは持たない。</summary>
public sealed class BeamScorpionBeamVfx : MonoBehaviour
{
    [SerializeField] private SpriteRenderer core;

    private static readonly int Span = Shader.PropertyToID("_Span");
    private static readonly int Height = Shader.PropertyToID("_Height");
    private static readonly int Contact = Shader.PropertyToID("_Contact");
    private static readonly int Progress = Shader.PropertyToID("_Progress");
    private MaterialPropertyBlock properties;
    private Vector2 start, end;
    private float thickness, age, fadeDuration, fadeElapsed;
    private bool contact;
    private Color tint;

    public void SetBeam(Vector2 from, Vector2 to, float width, Color color, bool atContact = false)
    {
        start = from;
        end = to;
        thickness = Mathf.Max(0.01f, width);
        tint = color;
        contact = atContact;
        Render(1f);
    }

    public void FadeOut(float seconds) { fadeDuration = Mathf.Max(0.01f, seconds); }

    private void Update()
    {
        age += Time.deltaTime;
        if (fadeDuration <= 0f) return;
        fadeElapsed += Time.deltaTime;
        Render(1f - Mathf.SmoothStep(0f, 1f, fadeElapsed / fadeDuration));
        if (fadeElapsed >= fadeDuration) Destroy(gameObject);
    }

    private void Render(float alpha)
    {
        if (core == null || core.sprite == null) return;
        Vector2 delta = end - start;
        float length = delta.magnitude;
        core.enabled = length > 0.001f;
        if (!core.enabled) return;
        transform.SetPositionAndRotation((start + end) * 0.5f,
            Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg));
        // 親の敵を拡縮しても、判定と同じワールド寸法で描く。
        transform.localScale = Vector3.one;
        Vector3 inherited = transform.lossyScale;
        transform.localScale = new Vector3(1f / Mathf.Max(0.0001f, Mathf.Abs(inherited.x)),
            1f / Mathf.Max(0.0001f, Mathf.Abs(inherited.y)), 1f);
        float height = thickness * Mathf.Lerp(1.1f, 6f, alpha);
        core.transform.localScale = new Vector3(length / core.sprite.bounds.size.x, height / core.sprite.bounds.size.y, 1f);
        Color color = tint;
        color.a *= alpha;
        core.color = color;
        properties ??= new MaterialPropertyBlock();
        properties.SetFloat(Span, length);
        properties.SetFloat(Height, height);
        properties.SetFloat(Contact, contact ? 1f : 0f);
        properties.SetFloat(Progress, Mathf.Clamp01(age / 0.12f));
        core.SetPropertyBlock(properties);
    }
}
