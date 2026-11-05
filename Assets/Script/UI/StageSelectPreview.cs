using UnityEngine;

/// <summary>Small background-only render target. Never changes the gameplay CRT or camera.</summary>
[DisallowMultipleComponent]
public sealed class StageSelectPreview : MonoBehaviour
{
    [SerializeField] private UnityEngine.UI.RawImage screen;
    [SerializeField] private UnityEngine.UI.Image televisionHousing;
    [SerializeField] private UnityEngine.UI.RawImage cartridgeArtwork;
    [SerializeField] private UnityEngine.UI.RawImage floorReflection;
    [SerializeField] private Shader compositionShader;
    [SerializeField] private Shader crtShader;
    [SerializeField] private Vector2Int resolution = new Vector2Int(640, 360);
    [SerializeField, Range(0f, 2f)] private float softness = .55f;
    [SerializeField, Range(0f, 1f)] private float scanlines = .13f;
    [SerializeField, Range(0f, 1f)] private float vignette = .22f;
    [SerializeField, Range(0f, .1f)] private float noise = .015f;
    [SerializeField, Range(.3f, 1.5f)] private float brightness = 1.06f;
    [SerializeField, Range(0f, .5f)] private float curvature = .12f;
    [SerializeField, Range(0f, .15f)] private float edgeBow = .06f;
    [SerializeField, Range(1f, 60f)] private float cornerRadius = 30f;
    [SerializeField, Range(80f,400f)] private float scanlineCount = 240f;
    [SerializeField, Range(0f,3f)] private float rgbSeparation = .9f;
    [SerializeField, Range(0f, .3f)] private float glassReflection = .12f;
    [SerializeField, Range(0f, 1f)] private float phosphorBloom = .22f;

    private Material composition, crt, housingSource, housingRuntime;
    private RenderTexture source, output, previous, printedArtwork;
    private bool captureArtwork;
    private float disturbance, clearBlend, clock, stageBlend = 1;
    private StageSelectStage stage;
    public RenderTexture Output => output;
    public bool SignalLost { get; set; }
    public float Disturbance { get => disturbance; set => disturbance = Mathf.Clamp01(value); }
    public float ClearBlend { get => clearBlend; set => clearBlend = Mathf.Clamp01(value); }
    public float FocusDepth { get; set; }
    public float SignalRecovery { get; set; } = 1;
    public float EntryProgress { get; set; }

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
        if (televisionHousing != null)
        {
            housingSource = televisionHousing.material;
            housingRuntime = new Material(housingSource) { name = "Convex CRT bezel (runtime)" };
            televisionHousing.material = housingRuntime;
        }
        int width = Mathf.Clamp(resolution.x, 160, 1280), height = Mathf.Clamp(resolution.y, 90, 720);
        source = Target("Stage preview layers", width, height);
        output = Target("Stage preview CRT", width, height);
        previous = Target("Previous cartridge signal", width, height);
        screen.texture = output;
        if (cartridgeArtwork != null)
        {
            printedArtwork = Target("Printed cartridge artwork", 512, 320);
            printedArtwork.filterMode = FilterMode.Point;
            cartridgeArtwork.texture = printedArtwork;
        }
        if (floorReflection != null) floorReflection.texture = output;
        // Controller/preview Awake order is intentionally not an initialization dependency.
        if (stage != null) Show(stage);
    }

    public void Show(StageSelectStage value)
    {
        if (composition != null && stage != null && stage != value)
        {
            Graphics.Blit(source, previous);
            stageBlend = 0;
        }
        stage = value;
        SignalLost = stage != null && (!stage.IsUnlocked || stage.NeedsReveal);
        SignalRecovery = 1;
        clock = 0;
        if (composition == null || stage == null) return;
        Bind("_Far", stage.far);
        Bind("_Middle", stage.middle);
        Bind("_Near", stage.near);
        composition.SetColor("_Background", stage.screenColor);
        captureArtwork = true;
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
        stageBlend = Mathf.MoveTowards(stageBlend, 1, Time.unscaledDeltaTime / .38f);
        RenderNow();
    }

    public void RenderNow()
    {
        if (composition == null || stage == null) return;
        composition.SetFloat("_PreviewTime", clock);
        Graphics.Blit(Texture2D.blackTexture, source, composition);
        if (captureArtwork && printedArtwork != null)
        {
            Graphics.Blit(source, printedArtwork);
            captureArtwork = false;
        }
        crt.SetFloat("_PreviewTime", clock); crt.SetFloat("_Curvature", curvature);
        crt.SetFloat("_Softness", softness); crt.SetFloat("_Brightness", brightness);
        crt.SetFloat("_FocusDepth", Mathf.Clamp01(FocusDepth));
        crt.SetFloat("_Bloom", phosphorBloom);
        crt.SetFloat("_Entry", Mathf.Clamp01(EntryProgress));
        crt.SetTexture("_PreviousTex", previous);
        crt.SetFloat("_StageBlend", Mathf.SmoothStep(0, 1, stageBlend));
        crt.SetFloat("_Scanlines", scanlines); crt.SetFloat("_Noise", noise);
        crt.SetFloat("_ScanlineCount",scanlineCount); crt.SetFloat("_RGB",rgbSeparation);
        crt.SetFloat("_Vignette", vignette); crt.SetFloat("_Glass", glassReflection);
        crt.SetFloat("_SignalLost", SignalLost ? 1 : 1 - Mathf.Clamp01(SignalRecovery)); crt.SetFloat("_Disturbance", disturbance);
        crt.SetFloat("_Clear", clearBlend);
        Vector4 shape = new Vector4(edgeBow, cornerRadius, 0, 0);
        crt.SetVector("_FaceShape", shape);
        if (housingRuntime != null)
        {
            housingRuntime.SetVector("_FaceShape", shape);
            housingRuntime.SetFloat("_FaceClear", clearBlend);
        }
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
        if (cartridgeArtwork != null) cartridgeArtwork.texture = null;
        if (floorReflection != null) floorReflection.texture = null;
        foreach (var rt in new[] { source, output, previous, printedArtwork })
            if (rt != null) { rt.Release(); Destroy(rt); }
        if (composition != null) Destroy(composition);
        if (crt != null) Destroy(crt);
        if (housingRuntime != null)
        {
            if (televisionHousing != null) televisionHousing.material = housingSource;
            Destroy(housingRuntime);
        }
    }
}
