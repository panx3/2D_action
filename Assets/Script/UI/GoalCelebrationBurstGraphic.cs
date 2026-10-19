using UnityEngine;

/// <summary>One small UI mesh: faceted shockwaves and jewel fragments, driven by the goal's unscaled clock.</summary>
[DisallowMultipleComponent]
public sealed class GoalCelebrationBurstGraphic : UnityEngine.UI.MaskableGraphic
{
    [Header("Impact / reward burst (reference Canvas pixels)")]
    [SerializeField, Min(0.05f)] private float impactDuration = 0.32f;
    [SerializeField, Min(0.05f)] private float rewardDuration = 0.78f;
    [SerializeField, Min(0f)] private float impactRadius = 220f;
    [SerializeField] private Vector2 rewardRadius = new Vector2(690f, 365f);
    [SerializeField, Range(8, 48)] private int shardCount = 28;
    [SerializeField] private Color crystalPink = new Color(1f, 0.16f, 0.65f, 1f);
    [SerializeField] private Color crystalViolet = new Color(0.65f, 0.38f, 1f, 1f);

    private Vector2 impactOrigin, rewardOrigin;
    private float age, rewardAt, opacity = 1f;
    public bool IsAnimating { get; private set; }

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
    }

    public void Begin(Vector2 impact, Vector2 reward, float revealDelay)
    {
        impactOrigin = impact;
        rewardOrigin = reward;
        rewardAt = revealDelay;
        age = 0f;
        opacity = 1f;
        IsAnimating = true;
        SetVerticesDirty();
    }

    public void Advance(float elapsed, float visibility)
    {
        if (!IsAnimating) return;
        age = elapsed;
        opacity = visibility;
        if (age > Mathf.Max(impactDuration, rewardAt + rewardDuration))
            IsAnimating = false;
        SetVerticesDirty();
    }

    public void Stop()
    {
        IsAnimating = false;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh)
    {
        mesh.Clear();
        if (!IsAnimating || opacity <= 0f) return;
        DrawImpact(mesh);
        if (age >= rewardAt) DrawReward(mesh);
    }

    private void DrawImpact(UnityEngine.UI.VertexHelper mesh)
    {
        float t = Mathf.Clamp01(age / impactDuration);
        if (t >= 1f) return;
        float travel = 1f - (1f - t) * (1f - t);
        float radius = Mathf.Lerp(18f, impactRadius, travel);
        Color edge = Color.Lerp(Color.white, crystalPink, t);
        Ring(mesh, impactOrigin, Vector2.one * radius, Mathf.Lerp(8f, 2f, t), Tint(edge, (1f - t) * 0.8f), false);
        Ring(mesh, impactOrigin, Vector2.one * radius * 0.8f, 3f, Tint(crystalViolet, (1f - t) * 0.48f), true);
        for (int i = 0; i < 16; i++)
        {
            float angle = i * Mathf.PI * 2f / 16f;
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 point = impactOrigin + direction * radius * (0.6f + (i % 3) * 0.16f);
            Gem(mesh, point, 4f + (i % 3) * 2f, Tint(i % 2 == 0 ? edge : crystalViolet, 1f - t));
        }
    }

    private void DrawReward(UnityEngine.UI.VertexHelper mesh)
    {
        float t = Mathf.Clamp01((age - rewardAt) / rewardDuration);
        float travel = 1f - Mathf.Pow(1f - t, 3f);
        float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.28f, 1f, t));
        float ringFade = Mathf.Pow(1f - Mathf.Clamp01(t / 0.65f), 2f);
        Vector2 extent = new Vector2(Mathf.Min(rewardRadius.x, rectTransform.rect.width * 0.45f),
            Mathf.Min(rewardRadius.y, rectTransform.rect.height * 0.41f));
        Ring(mesh, rewardOrigin, extent * Mathf.Lerp(0.48f, 1.04f, travel), 5f,
            Tint(crystalPink, ringFade * 0.65f), true);
        for (int i = 0; i < shardCount; i++)
        {
            // Deterministic decoration: never consume Unity's gameplay random state.
            float angle = (i + 0.35f) * Mathf.PI * 2f / shardCount;
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            float spread = 0.84f + (i * 7 % 9) * 0.035f;
            Vector2 offset = Vector2.Scale(direction, extent) * spread;
            Vector2 point = rewardOrigin + offset * Mathf.Lerp(0.26f, 1f, travel);
            point.y -= t * t * (35f + i % 4 * 12f);
            Color tint = i % 3 == 0 ? Color.white : i % 3 == 1 ? crystalPink : crystalViolet;
            float twinkle = 0.7f + 0.3f * Mathf.Sin(t * 18f + i * 1.7f);
            if (t < 0.55f)
            {
                float length = (28f + i % 4 * 12f) * (1f - t / 0.55f);
                Strip(mesh, point - direction * length, point, 3f, Tint(tint, fade * 0.55f));
            }
            float size = (6f + i % 4 * 2f) * Mathf.Lerp(1f, 0.35f, t);
            Gem(mesh, point, size, Tint(tint, fade * twinkle));
            if (i % 5 == 0)
            {
                Rect(mesh, point, new Vector2(size * 3.6f, 2f), Tint(Color.white, fade * 0.7f));
                Rect(mesh, point, new Vector2(2f, size * 3.6f), Tint(Color.white, fade * 0.7f));
            }
        }
    }

    private Color Tint(Color tint, float alpha)
    {
        tint *= color;
        tint.a *= Mathf.Clamp01(alpha) * opacity;
        return tint;
    }

    private static Vector2 Snap(Vector2 point) => new Vector2(Mathf.Round(point.x / 2f), Mathf.Round(point.y / 2f)) * 2f;

    private static void Ring(UnityEngine.UI.VertexHelper mesh, Vector2 center, Vector2 radius, float thickness, Color tint, bool broken)
    {
        const int sides = 16;
        for (int i = 0; i < sides; i++)
        {
            if (broken && i % 2 != 0) continue;
            float a = i * Mathf.PI * 2f / sides, b = (i + 1) * Mathf.PI * 2f / sides;
            Vector2 da = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            Vector2 db = new Vector2(Mathf.Cos(b), Mathf.Sin(b));
            Quad(mesh, center + Vector2.Scale(da, radius), center + Vector2.Scale(db, radius),
                center + Vector2.Scale(db, radius - Vector2.one * thickness),
                center + Vector2.Scale(da, radius - Vector2.one * thickness), tint);
        }
    }

    private static void Gem(UnityEngine.UI.VertexHelper mesh, Vector2 point, float size, Color tint)
    {
        Quad(mesh, point + Vector2.up * size * 1.5f, point + Vector2.right * size,
            point - Vector2.up * size * 1.5f, point - Vector2.right * size, tint);
        Color facet = Color.Lerp(tint, Color.white, 0.55f); facet.a = tint.a;
        Quad(mesh, point + Vector2.up * size * 1.5f, point, point - Vector2.up * size * 0.5f,
            point - Vector2.right * size, facet);
    }

    private static void Strip(UnityEngine.UI.VertexHelper mesh, Vector2 start, Vector2 end, float width, Color tint)
    {
        Vector2 along = (end - start).normalized;
        Vector2 normal = new Vector2(-along.y, along.x) * (width * 0.5f);
        Quad(mesh, start - normal, end - normal, end + normal, start + normal, tint);
    }

    private static void Rect(UnityEngine.UI.VertexHelper mesh, Vector2 point, Vector2 size, Color tint)
    {
        Vector2 half = size * 0.5f;
        Quad(mesh, point + new Vector2(-half.x, -half.y), point + new Vector2(half.x, -half.y),
            point + half, point + new Vector2(-half.x, half.y), tint);
    }

    private static void Quad(UnityEngine.UI.VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color tint)
    {
        int start = mesh.currentVertCount;
        mesh.AddVert(Snap(a), tint, Vector2.zero); mesh.AddVert(Snap(b), tint, Vector2.zero);
        mesh.AddVert(Snap(c), tint, Vector2.zero); mesh.AddVert(Snap(d), tint, Vector2.zero);
        mesh.AddTriangle(start, start + 1, start + 2); mesh.AddTriangle(start + 2, start + 3, start);
    }
}
