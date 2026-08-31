using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Scene単位の常時CRT表示を一括管理する。
/// カメラへの描画コンポーネントは実行時だけ追加する。
/// </summary>
[DisallowMultipleComponent]
public sealed class CRTFilterController : MonoBehaviour
{
    private const string EnabledPrefsKey = "IronBallGirl.CRT.Enabled";
    private const string StrengthPrefsKey = "IronBallGirl.CRT.Strength";

    [Header("Master")]
    [SerializeField, InspectorName("CRT Enabled")]
    private bool crtEnabled = true;

    [SerializeField, Range(0f, 1f)]
    private float masterStrength = 0.4f;

    [Header("Scanline")]
    [SerializeField, Range(0f, 1f)]
    private float scanlineStrength = 0.45f;

    [Header("Noise")]
    [SerializeField, Range(0f, 1f)]
    private float noiseStrength = 0.18f;

    [Header("Vignette")]
    [SerializeField, Range(0f, 1f)]
    private float vignetteStrength = 0.35f;

    [Header("Chromatic Aberration")]
    [SerializeField, Range(0f, 1f)]
    private float chromaticStrength = 0.22f;

    [Header("Color")]
    [SerializeField, Range(0f, 1f)]
    private float contrastStrength = 0.3f;

    [Header("Optional")]
    [SerializeField]
    private bool enableHotkey;

    [SerializeField, Tooltip("ONの場合のみ、起動時に共通PlayerPrefsをScene設定へ反映します。OFFではInspector設定を優先します。")]
    private bool loadSavedPreferences;

    [SerializeField, Tooltip("Screen Space - OverlayのUIを一時的にCamera描画へ含め、CRTを画面全体へ適用します。")]
    private bool includeOverlayCanvases = true;

#if ENABLE_INPUT_SYSTEM
    [SerializeField]
    private Key hotkey = Key.F8;
#endif

    [SerializeField, HideInInspector]
    private Camera targetCamera;

    [SerializeField, HideInInspector]
    private Shader crtShader;

    private CRTFilterRenderer filterRenderer;
    private bool ownsRenderer;
    private bool ownsCameraRenderer;
    private readonly Dictionary<Canvas, CanvasState> convertedCanvases = new Dictionary<Canvas, CanvasState>();

    private struct CanvasState
    {
        public RenderMode RenderMode;
        public Camera WorldCamera;
        public float PlaneDistance;
    }

    private void Awake()
    {
        if (loadSavedPreferences)
            LoadPreferences();
    }

    private void OnEnable()
    {
        InitializeRenderer();
        ApplySettings();
    }

    private void OnDisable()
    {
        StopRendering();
    }

    private void Update()
    {
        if (!enableHotkey)
            return;

#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard[hotkey].wasPressedThisFrame)
            ToggleCRT();
#endif
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
            PlayerPrefs.Save();
    }

    private void OnApplicationQuit()
    {
        PlayerPrefs.Save();
    }

    private void OnDestroy()
    {
        StopRendering();

        if (ownsRenderer && filterRenderer != null && filterRenderer.CanBeDestroyedBy(this))
            Destroy(filterRenderer);
    }

    private void OnValidate()
    {
        masterStrength = Mathf.Clamp01(masterStrength);
        scanlineStrength = Mathf.Clamp01(scanlineStrength);
        noiseStrength = Mathf.Clamp01(noiseStrength);
        vignetteStrength = Mathf.Clamp01(vignetteStrength);
        chromaticStrength = Mathf.Clamp01(chromaticStrength);
        contrastStrength = Mathf.Clamp01(contrastStrength);

        if (Application.isPlaying)
            ApplySettings();
    }

    /// <summary>Unity UI Toggle から直接呼び出せる。</summary>
    public void SetCRTEnabled(bool enabled)
    {
        crtEnabled = enabled;
        PlayerPrefs.SetInt(EnabledPrefsKey, enabled ? 1 : 0);
        ApplySettings();
    }

    /// <summary>Unity UI Slider から直接呼び出せる。</summary>
    public void SetCRTStrength(float value)
    {
        masterStrength = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(StrengthPrefsKey, masterStrength);
        ApplySettings();
    }

    public void ToggleCRT()
    {
        SetCRTEnabled(!crtEnabled);
    }

    private void LoadPreferences()
    {
        if (PlayerPrefs.HasKey(EnabledPrefsKey))
            crtEnabled = PlayerPrefs.GetInt(EnabledPrefsKey, crtEnabled ? 1 : 0) != 0;

        if (PlayerPrefs.HasKey(StrengthPrefsKey))
            masterStrength = Mathf.Clamp01(PlayerPrefs.GetFloat(StrengthPrefsKey, masterStrength));
    }

    private void InitializeRenderer()
    {
        if (filterRenderer != null && ownsCameraRenderer)
            return;

        Camera cameraToUse = targetCamera != null ? targetCamera : Camera.main;
        if (cameraToUse == null || crtShader == null || !crtShader.isSupported)
        {
            Debug.LogWarning("[CRTFilter] 描画準備に失敗したため、通常表示を継続します。", this);
            return;
        }

        targetCamera = cameraToUse;

        filterRenderer = cameraToUse.GetComponent<CRTFilterRenderer>();
        bool createdRenderer = false;
        if (filterRenderer == null)
        {
            filterRenderer = cameraToUse.gameObject.AddComponent<CRTFilterRenderer>();
            filterRenderer.hideFlags = HideFlags.HideInInspector;
            ownsRenderer = true;
            createdRenderer = true;
        }

        if (!filterRenderer.TryClaim(this, crtShader))
        {
            Debug.LogWarning(
                "[CRTFilter] CRTFilterController already exists for this camera. Duplicate CRT filter disabled.",
                this);
            if (createdRenderer)
                Destroy(filterRenderer);
            filterRenderer = null;
            enabled = false;
            return;
        }

        ownsCameraRenderer = true;
    }

    private void ApplySettings()
    {
        if (filterRenderer == null)
            return;

        bool shouldRender = isActiveAndEnabled && crtEnabled && masterStrength > 0f;
        if (!shouldRender)
        {
            StopVisualEffect();
            return;
        }

        if (includeOverlayCanvases)
            ConvertOverlayCanvases();

        filterRenderer.SetSettings(
            masterStrength,
            scanlineStrength,
            noiseStrength,
            vignetteStrength,
            chromaticStrength,
            contrastStrength);
        filterRenderer.enabled = true;
    }

    private void StopVisualEffect()
    {
        if (filterRenderer != null && ownsCameraRenderer)
            filterRenderer.enabled = false;

        RestoreOverlayCanvases();
    }

    private void StopRendering()
    {
        StopVisualEffect();

        if (filterRenderer != null && ownsCameraRenderer)
            filterRenderer.Release(this);

        ownsCameraRenderer = false;
    }

    private void ConvertOverlayCanvases()
    {
        Canvas[] sceneCanvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include);
        foreach (Canvas canvas in sceneCanvases)
            TryConvertCanvas(canvas);
    }

    private void TryConvertCanvas(Canvas canvas)
    {
        if (canvas == null
            || targetCamera == null
            || canvas.gameObject.scene != gameObject.scene
            || !canvas.isRootCanvas
            || canvas.renderMode != RenderMode.ScreenSpaceOverlay
            || convertedCanvases.ContainsKey(canvas))
            return;

        convertedCanvases.Add(canvas, new CanvasState
        {
            RenderMode = canvas.renderMode,
            WorldCamera = canvas.worldCamera,
            PlaneDistance = canvas.planeDistance
        });

        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = targetCamera;
        canvas.planeDistance = Mathf.Clamp(
            canvas.planeDistance,
            targetCamera.nearClipPlane + 0.01f,
            targetCamera.farClipPlane - 0.01f);
    }

    private void RestoreOverlayCanvases()
    {
        foreach (KeyValuePair<Canvas, CanvasState> entry in convertedCanvases)
        {
            if (entry.Key == null)
                continue;

            entry.Key.renderMode = entry.Value.RenderMode;
            entry.Key.worldCamera = entry.Value.WorldCamera;
            entry.Key.planeDistance = entry.Value.PlaneDistance;
        }

        convertedCanvases.Clear();
    }
}

/// <summary>CRTFilterController が実行時にカメラへ追加する内部描画コンポーネント。</summary>
[AddComponentMenu("")]
[DisallowMultipleComponent]
internal sealed class CRTFilterRenderer : MonoBehaviour
{
    private static readonly int MasterStrengthId = Shader.PropertyToID("_MasterStrength");
    private static readonly int ScanlineStrengthId = Shader.PropertyToID("_ScanlineStrength");
    private static readonly int NoiseStrengthId = Shader.PropertyToID("_NoiseStrength");
    private static readonly int VignetteStrengthId = Shader.PropertyToID("_VignetteStrength");
    private static readonly int ChromaticStrengthId = Shader.PropertyToID("_ChromaticStrength");
    private static readonly int ContrastStrengthId = Shader.PropertyToID("_ContrastStrength");
    private static readonly int CrtTimeId = Shader.PropertyToID("_CRTTime");

    private Material material;
    private float masterStrength;
    private float scanlineStrength;
    private float noiseStrength;
    private float vignetteStrength;
    private float chromaticStrength;
    private float contrastStrength;
    private CRTFilterController owner;

    public bool TryClaim(CRTFilterController requestedOwner, Shader shader)
    {
        if (requestedOwner == null || (owner != null && owner != requestedOwner))
            return false;

        owner = requestedOwner;
        if (material != null)
            return true;

        if (shader == null || !shader.isSupported)
        {
            owner = null;
            return false;
        }

        material = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        return true;
    }

    public void Release(CRTFilterController requestedOwner)
    {
        if (owner != requestedOwner)
            return;

        enabled = false;
        owner = null;
    }

    public bool CanBeDestroyedBy(CRTFilterController requestedOwner)
    {
        return owner == null || owner == requestedOwner;
    }

    public void SetSettings(
        float master,
        float scanline,
        float noise,
        float vignette,
        float chromatic,
        float contrast)
    {
        masterStrength = Mathf.Clamp01(master);
        scanlineStrength = Mathf.Clamp01(scanline);
        noiseStrength = Mathf.Clamp01(noise);
        vignetteStrength = Mathf.Clamp01(vignette);
        chromaticStrength = Mathf.Clamp01(chromatic);
        contrastStrength = Mathf.Clamp01(contrast);
    }

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (material == null)
        {
            Graphics.Blit(source, destination);
            return;
        }

        material.SetFloat(MasterStrengthId, masterStrength);
        material.SetFloat(ScanlineStrengthId, scanlineStrength);
        material.SetFloat(NoiseStrengthId, noiseStrength);
        material.SetFloat(VignetteStrengthId, vignetteStrength);
        material.SetFloat(ChromaticStrengthId, chromaticStrength);
        material.SetFloat(ContrastStrengthId, contrastStrength);
        material.SetFloat(CrtTimeId, Time.unscaledTime);
        Graphics.Blit(source, destination, material, 0);
    }

    private void OnDestroy()
    {
        if (material != null)
            Destroy(material);
    }
}
