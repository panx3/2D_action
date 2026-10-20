using UnityEngine;
using UnityEngine.UI;

/// <summary>One background material and one small particle mesh; no per-frame objects or textures.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class TitleAtmosphere : MaskableGraphic
{
    [SerializeField] private UnityEngine.UI.RawImage background;
    [SerializeField, Range(0f, 2f)] private float motionStrength = 1.5f;
    [SerializeField, Range(0f, 0.1f)] private float scanlineStrength;
    [SerializeField, Range(0f, 0.04f)] private float grainStrength;
    [SerializeField, Range(0, 40)] private int moteCount = 26;
    [SerializeField, Range(0, 10)] private int petalCount = 9;
    [SerializeField, Tooltip("16px四方の花びら4種類を下段に並べた64x32のPointフィルタ画像")]
    private Texture2D petalAtlas;
    [SerializeField, Range(0, 8)] private int birdCount = 4;
    [SerializeField] private TMPro.TMP_Text idlePrompt;
    private Material backgroundMaterial;
    private Material originalMaterial;
    private float response;
    private double startedAt;
    private bool departing;
    private float menuVisibility = 1f;
    private static readonly int ClockId = Shader.PropertyToID("_TitleTime");
    private static readonly int AspectId = Shader.PropertyToID("_ViewAspect");
    private static readonly int MotionId = Shader.PropertyToID("_Motion");
    private static readonly int ScanId = Shader.PropertyToID("_Scanlines");
    private static readonly int GrainId = Shader.PropertyToID("_Grain");
    private static readonly int ResponseId = Shader.PropertyToID("_Response");
    private static readonly int MenuVisibilityId = Shader.PropertyToID("_MenuVisibility");

    public override Texture mainTexture => petalAtlas != null ? petalAtlas : Texture2D.whiteTexture;
    private Vector2 WhiteUv => petalAtlas != null ? new Vector2(1.5f / 64f, 25.5f / 32f) : Vector2.zero;

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
        startedAt = Time.unscaledTimeAsDouble;
        if (background == null || !Application.isPlaying) return;
        originalMaterial = background.material;
        backgroundMaterial = new Material(originalMaterial);
        background.material = backgroundMaterial;
    }

    public void Confirm() => response = 1f;
    public void SetDeparting(bool value) => departing = value;
    public void SetMenuVisibility(float value)
    {
        menuVisibility = Mathf.Clamp01(value);
        if (backgroundMaterial != null) backgroundMaterial.SetFloat(MenuVisibilityId, menuVisibility);
    }

    private void Update()
    {
        response = Mathf.MoveTowards(response, 0f, Time.unscaledDeltaTime * 2.5f);
        if (idlePrompt != null)
        {
            Color promptColor = idlePrompt.color;
            promptColor.a = .6f + .25f * (.5f + .5f * Mathf.Sin(Time.unscaledTime * 1.7f));
            idlePrompt.color = promptColor;
        }
        if (backgroundMaterial != null)
        {
            // All scene motion uses unscaled time; bounded clock stays precise during long idle sessions.
            backgroundMaterial.SetFloat(ClockId, (float)((Time.unscaledTimeAsDouble - startedAt) % 12240d));
            Rect view = background.rectTransform.rect;
            backgroundMaterial.SetFloat(AspectId, view.width / Mathf.Max(1f, view.height));
            backgroundMaterial.SetFloat(MotionId, departing ? 0 : motionStrength);
            backgroundMaterial.SetFloat(ScanId, scanlineStrength);
            backgroundMaterial.SetFloat(GrainId, grainStrength);
            backgroundMaterial.SetFloat(ResponseId, response);
            backgroundMaterial.SetFloat(MenuVisibilityId, menuVisibility);
        }
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        double time = Application.isPlaying ? Time.unscaledTimeAsDouble - startedAt : 3d;
        for (int i = 0; i < moteCount; i++)
        {
            float seed = Mathf.Repeat(i * 0.618034f + 0.17f, 1f);
            float period = 12f + i % 7;
            float phase = (float)((time / period + seed) % 1d);
            float fade = Mathf.SmoothStep(0f, 1f, Mathf.Min(phase, 1f - phase) * 7f);
            float x = Mathf.Lerp(-0.06f, 1.06f, phase);
            float y = Mathf.Repeat(seed * 7.31f, 1f) + Mathf.Sin(phase * Mathf.PI * 2f) * 0.016f;
            Vector2 center = new Vector2(r.xMin + x * r.width, r.yMin + y * r.height);
            float size = (1.5f + seed * 2.2f) * r.height / 1080f;
            float angle = phase * Mathf.PI * 2f + seed * 6f;
            Vector2 axis = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * size;
            Vector2 side = new Vector2(-axis.y, axis.x);
            Color tint = new Color(1f, 0.97f, 0.83f, 0.5f * fade);
            int n = vh.currentVertCount;
            vh.AddVert(center - axis, tint, WhiteUv);
            vh.AddVert(center + side, tint, WhiteUv);
            vh.AddVert(center + axis, tint, WhiteUv);
            vh.AddVert(center - side, tint, WhiteUv);
            vh.AddTriangle(n, n + 1, n + 2);
            vh.AddTriangle(n, n + 2, n + 3);
        }
        if (petalAtlas != null)
            for (int i = 0; i < petalCount; i++) AddPetal(vh, r, time, i);
        // Distant silhouettes cross the open sky in small, staggered groups.
        for (int i = 0; i < birdCount; i++)
        {
            float phase = (float)((time / 20d + i * .037d) % 1d);
            float fade = Mathf.SmoothStep(0f, 1f, Mathf.Min(phase, 1f - phase) * 9f);
            Vector2 center = new Vector2(r.xMin + Mathf.Lerp(.32f, 1.05f, phase) * r.width,
                r.yMin + (.84f + .023f * (i % 3) + Mathf.Sin(phase * 6.28f) * .018f) * r.height);
            float wing = (3f + 3f * Mathf.Sin((float)(time % 20d) * (Mathf.PI * 2f * .7f) + i * .8f)) * r.height / 1080f;
            float span = (8f + i * 1.5f) * r.height / 1080f;
            Color tint = new Color(.82f, .91f, 1f, fade * .7f);
            Stroke(vh, center + new Vector2(-span, wing), center, 1.5f * r.height / 1080f, tint);
            Stroke(vh, center, center + new Vector2(span, wing), 1.5f * r.height / 1080f, tint);
        }
    }

    private void AddPetal(VertexHelper vh, Rect r, double time, int index)
    {
        float seed = Mathf.Repeat(index * 0.618034f + 0.29f, 1f);
        float period = 17f + index % 5 * 2.3f;
        float phase = (float)((time / period + seed) % 1d);
        float flutter = (float)(time % 120d) * (1.1f + seed * 0.65f) + index * 2.4f;
        float fade = Mathf.SmoothStep(0f, 1f, Mathf.Min(phase, 1f - phase) * 10f);
        float x = Mathf.Lerp(-0.06f, 1.06f, phase);
        float y = Mathf.Repeat(seed * 4.31f, 1f) + 0.09f * (0.5f - phase) + Mathf.Sin(flutter * 0.75f) * 0.024f;
        float pixel = Mathf.Max(0.5f, r.height / 540f);
        Vector2 center = new Vector2(r.xMin + x * r.width, r.yMin + y * r.height);
        center.x = Mathf.Round(center.x / pixel) * pixel;
        center.y = Mathf.Round(center.y / pixel) * pixel;
        float size = (26f + seed * 16f) * r.height / 1080f;
        float angle = (Mathf.Sin(flutter * 0.55f) * 0.75f + seed * 2f - 1f);
        Vector2 axis = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        // 平面の回転に加えて幅を変え、表裏が返る瞬間の細いシルエットを作る。
        Vector2 halfX = axis * (size * 0.5f * Mathf.Lerp(0.22f, 1f, Mathf.Abs(Mathf.Cos(flutter))));
        Vector2 halfY = new Vector2(-axis.y, axis.x) * (size * 0.5f);
        int variant = index % 4;
        float u0 = variant * 0.25f;
        float u1 = u0 + 0.25f;
        Color tint = new Color(1f, Mathf.Lerp(0.85f, 1f, seed), 1f, fade * Mathf.Lerp(0.72f, 0.94f, seed));
        int n = vh.currentVertCount;
        vh.AddVert(center - halfX - halfY, tint, new Vector2(u0, 0f));
        vh.AddVert(center - halfX + halfY, tint, new Vector2(u0, 0.5f));
        vh.AddVert(center + halfX + halfY, tint, new Vector2(u1, 0.5f));
        vh.AddVert(center + halfX - halfY, tint, new Vector2(u1, 0f));
        vh.AddTriangle(n, n + 1, n + 2);
        vh.AddTriangle(n, n + 2, n + 3);
    }

    private void Stroke(VertexHelper vh, Vector2 from, Vector2 to, float width, Color tint)
    {
        Vector2 d = (to - from).normalized;
        Vector2 side = new Vector2(-d.y, d.x) * width * .5f;
        int n = vh.currentVertCount;
        vh.AddVert(from - side, tint, WhiteUv); vh.AddVert(from + side, tint, WhiteUv);
        vh.AddVert(to + side, tint, WhiteUv); vh.AddVert(to - side, tint, WhiteUv);
        vh.AddTriangle(n, n + 1, n + 2); vh.AddTriangle(n, n + 2, n + 3);
    }

    protected override void OnDestroy()
    {
        if (background != null && backgroundMaterial != null) background.material = originalMaterial;
        if (backgroundMaterial != null)
        {
            if (Application.isPlaying) Destroy(backgroundMaterial);
            else DestroyImmediate(backgroundMaterial);
        }
        base.OnDestroy();
    }
}
