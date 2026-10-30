using UnityEngine;

/// <summary>Small background-only render target. Never changes the gameplay CRT or camera.</summary>
[DisallowMultipleComponent]
public sealed class StageSelectPreview : MonoBehaviour
{
    [SerializeField] private UnityEngine.UI.RawImage screen;
    [SerializeField] private Shader compositionShader;
    [SerializeField] private Shader crtShader;
    [SerializeField] private Vector2Int resolution = new Vector2Int(640, 360);
    [SerializeField, Range(0f, 2f)] private float softness = .55f;
    [SerializeField, Range(0f, 1f)] private float scanlines = .13f;
    [SerializeField, Range(0f, 1f)] private float vignette = .22f;
    [SerializeField, Range(0f, .1f)] private float noise = .015f;
    [SerializeField, Range(.3f, 1.5f)] private float brightness = 1.06f;
    [SerializeField, Range(0f, .15f)] private float curvature = .075f;
    [SerializeField, Range(0f, .3f)] private float glassReflection = .12f;

    private Material composition, crt;
    private RenderTexture source, output;
    private float disturbance, clearBlend, clock;
    private StageSelectStage stage;
    public RenderTexture Output => output;
    public bool SignalLost { get; set; }
    public float Disturbance { get => disturbance; set => disturbance = Mathf.Clamp01(value); }
    public float ClearBlend { get => clearBlend; set => clearBlend = Mathf.Clamp01(value); }

    private void Awake()
    {
        if (screen == null || compositionShader == null || crtShader == null
            || !compositionShader.isSupported || !crtShader.isSupported)
        {
            Debug.LogError("[StageSelect] Preview screen/shaders are missing or unsupported.", this);
            enabled = false;
            return;
        }
        composition = new Material(compositionShader) { name = "Cartridge background (runtime)" };
        crt = new Material(crtShader) { name = "Cartridge CRT only (runtime)" };
        int width = Mathf.Clamp(resolution.x, 160, 1280), height = Mathf.Clamp(resolution.y, 90, 720);
        source = Target("Stage preview layers", width, height);
        output = Target("Stage preview CRT", width, height);
        screen.texture = output;
        // Controller/preview Awake order is intentionally not an initialization dependency.
        if (stage != null) Show(stage);
    }

    public void Show(StageSelectStage value)
    {
        stage = value;
        SignalLost = stage != null && (!stage.IsUnlocked || stage.NeedsReveal);
        clock = 0;
        if (composition == null || stage == null) return;
        Bind("_Far", stage.far);
        Bind("_Middle", stage.middle);
        Bind("_Near", stage.near);
        composition.SetColor("_Background", stage.screenColor);
        RenderNow();
    }

    private void Bind(string prefix, StageSelectStage.BackgroundLayer layer)
    {
        Sprite sprite = layer != null ? layer.sprite : null;
        composition.SetTexture(prefix + "Tex", sprite != null ? sprite.texture : Texture2D.blackTexture);
        composition.SetColor(prefix + "Tint", sprite != null ? layer.tint : Color.clear);
        Vector4 uv = sprite != null ? UnityEngine.Sprites.DataUtility.GetOuterUV(sprite) : new Vector4(0, 0, 1, 1);
        composition.SetVector(prefix + "Rect", new Vector4(uv.x, uv.y, uv.z - uv.x, uv.w - uv.y));
        float aspect = sprite != null ? sprite.rect.width / sprite.rect.height : 16f / 9f;
        float targetAspect = screen.rectTransform.rect.width / Mathf.Max(1, screen.rectTransform.rect.height);
        composition.SetVector(prefix + "Crop", new Vector4(Mathf.Min(1f, targetAspect / aspect),
            Mathf.Min(1f, aspect / targetAspect), layer != null ? layer.drift : 0f, 0));
    }

    private void LateUpdate()
    {
        clock += Time.unscaledDeltaTime;
        disturbance = Mathf.MoveTowards(disturbance, 0, Time.unscaledDeltaTime * 3f);
        RenderNow();
    }

    public void RenderNow()
    {
        if (composition == null || stage == null) return;
        composition.SetFloat("_PreviewTime", clock);
        Graphics.Blit(Texture2D.blackTexture, source, composition);
        crt.SetFloat("_PreviewTime", clock); crt.SetFloat("_Curvature", curvature);
        crt.SetFloat("_Softness", softness); crt.SetFloat("_Brightness", brightness);
        crt.SetFloat("_Scanlines", scanlines); crt.SetFloat("_Noise", noise);
        crt.SetFloat("_Vignette", vignette); crt.SetFloat("_Glass", glassReflection);
        crt.SetFloat("_SignalLost", SignalLost ? 1 : 0); crt.SetFloat("_Disturbance", disturbance);
        crt.SetFloat("_Clear", clearBlend);
        Vector2 size = screen.rectTransform.rect.size;
        crt.SetVector("_GlassSize", new Vector4(size.x, size.y, 58, 0));
        Graphics.Blit(source, output, crt, 0);
    }

    private static RenderTexture Target(string label, int width, int height)
    {
        var rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            { name = label, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        rt.Create();
        return rt;
    }

    private void OnDestroy()
    {
        if (screen != null) screen.texture = null;
        foreach (var rt in new[] { source, output })
            if (rt != null) { rt.Release(); Destroy(rt); }
        if (composition != null) Destroy(composition);
        if (crt != null) Destroy(crt);
    }
}
