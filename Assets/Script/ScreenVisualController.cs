using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

/// <summary>Scene単位の演出。共有アセットを書き換えず、OFF/破棄時は自分の変更だけを復元する。</summary>
[DisallowMultipleComponent, DefaultExecutionOrder(900)]
public sealed class ScreenVisualController : MonoBehaviour
{
    public bool visualsEnabled = true;
    public ScreenVisualProfile profile;
    [SerializeField] private Camera targetCamera;
    [SerializeField] private Shader spriteLightingShader;
    [SerializeField] private Shader vignetteShader;
    [SerializeField] private Material particleMaterial;
    [SerializeField] private Shader localGlowShader;

    private sealed class Binding
    {
        public Renderer renderer;
        public Material original, visual;
        public ParallaxBackgroundLayer parallax;
        public bool background;
    }
    private readonly List<Binding> bindings = new List<Binding>();
    private readonly Dictionary<(Material, int), Material> litMaterials = new Dictionary<(Material, int), Material>();
    private readonly HashSet<Renderer> registered = new HashSet<Renderer>();
    private readonly List<Renderer> scanBuffer = new List<Renderer>();
    private readonly List<GameObject> roots = new List<GameObject>();
    private readonly Vector4[] lightPositions = new Vector4[8];
    private readonly Vector4[] lightColors = new Vector4[8];
    private readonly Vector4[] lightMasks = new Vector4[8];
    private readonly ScreenLocalLight2D[] selectedLights = new ScreenLocalLight2D[8];
    private readonly float[] lightDistances = new float[8];
    private ScreenVisualProfile fallback;
    private Canvas vignetteCanvas;
    private Material vignetteMaterial;
    private ParticleSystem particles;
    private float nextScan;
    private bool ownsScene;
    private Gradient particleGradient;
    private float lastParticleOpacity = -1;
    private Color lastParticleColor;
    private Vector2 lastParticleArea;
    private Material localGlowMaterial;
    private Sprite localGlowSprite;

    public ScreenVisualProfile Settings
    {
        get
        {
            if (profile != null) return profile;
            if (fallback == null)
            {
                fallback = ScriptableObject.CreateInstance<ScreenVisualProfile>();
                fallback.hideFlags = HideFlags.HideAndDontSave;
            }
            return fallback;
        }
    }
    public int ActiveLightCount { get; private set; }
    public int ParticleCount => particles != null ? particles.particleCount : 0;
    public int BoundRendererCount => bindings.Count;
    public Camera TargetCamera => targetCamera;

    private void OnEnable()
    {
        // Additive scenes may each own a controller, but never duplicate an owner within a scene.
        foreach (ScreenVisualController other in FindObjectsByType<ScreenVisualController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (other != this && other.isActiveAndEnabled && other.gameObject.scene == gameObject.scene)
            { enabled = false; return; }
        ownsScene = true;
        if (spriteLightingShader == null) spriteLightingShader = Shader.Find("IronBallGirl/Screen Visual Sprite");
        if (vignetteShader == null) vignetteShader = Shader.Find("IronBallGirl/Screen Visual Vignette");
        if (localGlowShader == null) localGlowShader = Shader.Find("IronBallGirl/Screen Visual Glow");
    }

    private void Start()
    {
        RefreshBindings();
        CreateVignette();
        CreateParticles();
        if (localGlowShader != null && localGlowShader.isSupported)
        {
            localGlowMaterial = new Material(localGlowShader) { hideFlags = HideFlags.HideAndDontSave };
            localGlowSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height), Vector2.one * 0.5f, Texture2D.whiteTexture.width, 0, SpriteMeshType.FullRect);
            localGlowSprite.hideFlags = HideFlags.HideAndDontSave;
        }
        ApplyNow();
    }

    private void LateUpdate()
    {
        if (targetCamera == null || !targetCamera.isActiveAndEnabled)
        {
            Camera candidate = Camera.main;
            if (candidate != null && candidate.gameObject.scene == gameObject.scene) targetCamera = candidate;
        }
        // Reuse the lists; this also picks up spawned enemies/debris without per-frame scene searches.
        if (Time.unscaledTime >= nextScan)
        {
            nextScan = Time.unscaledTime + 2;
            RefreshBindings();
        }
        ApplyNow();
    }

    public void RefreshBindings()
    {
        if (!Application.isPlaying || !ownsScene) return;
        roots.Clear();
        gameObject.scene.GetRootGameObjects(roots);
        foreach (GameObject root in roots)
        {
            scanBuffer.Clear();
            root.GetComponentsInChildren(true, scanBuffer);
            foreach (Renderer r in scanBuffer)
            {
                if (r == null || registered.Contains(r) || !(r is SpriteRenderer || r is TilemapRenderer)) continue;
                Material original = r.sharedMaterial;
                if (original == null || original.shader == null) continue;
                var parallax = r.GetComponent<ParallaxBackgroundLayer>();
                if (parallax == null && original.shader.name != "Sprites/Default") continue;
                if (parallax != null && original.shader.name != "IronBallGirl/Background Aerial Perspective") continue;
                if (r.GetComponentInParent<Canvas>() != null) continue;
                // Runtime copies inherit the source material; the source owner updates these itself.
                if (r.gameObject.hideFlags != HideFlags.None) continue;
                Material visual;
                if (parallax != null)
                    visual = new Material(original) { hideFlags = HideFlags.HideAndDontSave, name = original.name + " (Screen Visual)" };
                else
                {
                    if (spriteLightingShader == null || !spriteLightingShader.isSupported) continue;
                    var key = (original, r.gameObject.layer);
                    if (!litMaterials.TryGetValue(key, out visual))
                    {
                        visual = new Material(original) { shader = spriteLightingShader, hideFlags = HideFlags.HideAndDontSave, name = original.name + " (Screen Light)" };
                        visual.SetFloat("_SV_Layer", r.gameObject.layer);
                        litMaterials.Add(key, visual);
                    }
                }
                bindings.Add(new Binding { renderer = r, original = original, visual = visual, parallax = parallax, background = parallax != null });
                registered.Add(r);
            }
        }
    }

    public void ApplyNow()
    {
        if (!Application.isPlaying || !ownsScene) return;
        var p = Settings;
        bool active = isActiveAndEnabled && visualsEnabled;
        bool background = active && (p.atmosphere.atmosphereEnabled || p.atmosphere.fogEnabled ||
            p.blur.backgroundBlurEnabled || p.blur.peripheralBlurEnabled || p.lighting.enabled || p.parallaxEnabled);
        for (int i = bindings.Count - 1; i >= 0; i--)
        {
            Binding b = bindings[i];
            if (b.renderer == null)
            {
                if (b.background) DestroyOwned(b.visual);
                registered.Remove(b.renderer);
                bindings.RemoveAt(i);
                continue;
            }
            bool use = b.parallax != null ? background : active && p.lighting.enabled;
            // Feedback/animation may temporarily replace a material. Respect that owner.
            Material current = b.renderer.sharedMaterial;
            if (use && current == b.original) b.renderer.sharedMaterial = b.visual;
            else if (!use && current == b.visual) b.renderer.sharedMaterial = b.original;
            if (b.parallax != null)
            {
                var layer = FindLayer(p, b.parallax.name);
                b.parallax.SetVisualScrollFactor(active && p.parallaxEnabled && layer != null && layer.overrideScrollFactor ? (float?)layer.scrollFactor : null);
                ConfigureBackground(b, p, layer, active);
            }
        }
        ApplyLights(p, active);
        ApplyVignette(p, active);
        ApplyParticles(p, active);
        Shader.SetGlobalFloat("_SV_Time", Time.time);
    }

    private static ScreenVisualProfile.LayerSettings FindLayer(ScreenVisualProfile p, string name)
    {
        if (p.layers == null) return null;
        foreach (var l in p.layers) if (l != null && l.layerName == name) return l;
        return null;
    }

    private void ConfigureBackground(Binding b, ScreenVisualProfile p, ScreenVisualProfile.LayerSettings l, bool active)
    {
        Material m = b.visual;
        bool known = l != null;
        float depth = known ? Mathf.Clamp01(l.depthFactor) : 0;
        bool tone = active && p.parallaxEnabled && known;
        float blur = active && known && p.quality != ScreenVisualProfile.Quality.Low ? 1 : 0;
        float quality = p.quality == ScreenVisualProfile.Quality.Medium ? 0.5f : 1;
        var a = p.atmosphere;
        m.SetFloat("_SV_Active", active ? 1 : 0);
        m.SetFloat("_SV_Layer", b.renderer.gameObject.layer);
        m.SetColor("_SV_Tint", tone ? l.colorTint : Color.white);
        m.SetVector("_SV_Tone", new Vector4(tone ? l.contrast : 1, tone ? l.saturation : 1,
            active && a.atmosphereEnabled ? a.distanceFade * depth : 0, known ? l.lightingInfluence : 0));
        m.SetVector("_SV_Blur", new Vector4(p.blur.backgroundBlurEnabled && known ? p.blur.blurStrength * l.blurStrength * blur * quality : 0,
            p.blur.blurRadius, p.blur.peripheralBlurEnabled && known ? p.blur.peripheralBlurStrength * l.blurStrength * blur * quality : 0, p.blur.peripheralBlurRange));
        m.SetVector("_SV_Fog", new Vector4(active && a.fogEnabled && known ? a.fogOpacity * l.fogInfluence : 0,
            a.fogVariation, a.fogScrollSpeed.x, a.fogScrollSpeed.y));
        m.SetColor("_SV_Atmosphere", a.atmosphereColor);
        m.SetColor("_SV_FogColor", a.fogColor);
    }

    private void ApplyLights(ScreenVisualProfile p, bool active)
    {
        bool use = active && p.lighting.enabled;
        Shader.SetGlobalFloat("_SV_LightingEnabled", use ? 1 : 0);
        Color ambient = p.lighting.ambientColor * Mathf.Clamp(p.lighting.ambientIntensity, 0.5f, 1.5f);
        Shader.SetGlobalVector("_SV_Ambient", new Vector4(ambient.r, ambient.g, ambient.b, 1));
        int max = p.quality == ScreenVisualProfile.Quality.Low ? 2 : p.quality == ScreenVisualProfile.Quality.Medium ? 4 : 8;
        int count = 0;
        Vector2 cameraPosition = targetCamera != null ? (Vector2)targetCamera.transform.position : Vector2.zero;
        if (use && p.lighting.localLightsEnabled)
        foreach (ScreenLocalLight2D light in ScreenLocalLight2D.Active)
        {
            if (light == null || !light.EmitsLight || light.gameObject.scene != gameObject.scene || light.intensity <= 0) continue;
            float d = Mathf.Max(0, Vector2.Distance(cameraPosition, light.transform.position) - light.radius);
            if (targetCamera != null && d > targetCamera.orthographicSize * (1 + targetCamera.aspect)) continue;
            int slot = count;
            while (slot > 0 && lightDistances[slot - 1] > d) slot--;
            if (slot >= max) continue;
            int last = Mathf.Min(count, max - 1);
            for (int j = last; j > slot; j--) { selectedLights[j] = selectedLights[j - 1]; lightDistances[j] = lightDistances[j - 1]; }
            selectedLights[slot] = light; lightDistances[slot] = d; count = Mathf.Min(count + 1, max);
        }
        for (int i = 0; i < count; i++)
        {
            var light = selectedLights[i];
            Vector3 pos = light.transform.position;
            lightPositions[i] = new Vector4(pos.x, pos.y, Mathf.Max(0.01f, light.radius), Mathf.Max(0.25f, light.falloff));
            Color c = light.color * light.Strength;
            lightColors[i] = new Vector4(c.r, c.g, c.b, 1);
            uint mask = unchecked((uint)light.targetLayers.value);
            lightMasks[i] = new Vector4(mask & 65535, mask >> 16, 0, 0);
            light.UpdateGlow(localGlowMaterial, localGlowSprite);
        }
        ActiveLightCount = count;
        foreach (ScreenLocalLight2D light in ScreenLocalLight2D.Active)
        {
            if (light == null || light.gameObject.scene != gameObject.scene) continue;
            bool selected = false;
            for (int i = 0; i < count; i++) if (selectedLights[i] == light) { selected = true; break; }
            if (!selected) light.SetGlowVisible(false);
        }
        Shader.SetGlobalInt("_SV_LightCount", count);
        Shader.SetGlobalVectorArray("_SV_LightPositions", lightPositions);
        Shader.SetGlobalVectorArray("_SV_LightColors", lightColors);
        Shader.SetGlobalVectorArray("_SV_LightMasks", lightMasks);
    }

    private void CreateVignette()
    {
        if (vignetteShader == null || !vignetteShader.isSupported) return;
        var go = new GameObject("Screen Visual Vignette", typeof(RectTransform), typeof(Canvas));
        go.hideFlags = HideFlags.DontSave;
        go.transform.SetParent(transform, false);
        vignetteCanvas = go.GetComponent<Canvas>();
        vignetteCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        vignetteCanvas.sortingOrder = 50; // HP=100 / StageIntro=400. Also survives the existing CRT canvas conversion.
        var imageGo = new GameObject("Vignette", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        imageGo.transform.SetParent(go.transform, false);
        RectTransform rect = (RectTransform)imageGo.transform;
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        var image = imageGo.GetComponent<Image>();
        image.raycastTarget = false;
        vignetteMaterial = new Material(vignetteShader) { hideFlags = HideFlags.HideAndDontSave };
        image.material = vignetteMaterial;
    }

    private void ApplyVignette(ScreenVisualProfile p, bool active)
    {
        if (vignetteCanvas == null) return;
        vignetteCanvas.enabled = active && p.vignette.enabled && p.vignette.intensity > 0;
        if (vignetteCanvas.renderMode == RenderMode.ScreenSpaceCamera && targetCamera != null)
            vignetteCanvas.worldCamera = targetCamera;
        vignetteMaterial.SetColor("_Color", p.vignette.color);
        vignetteMaterial.SetVector("_Vignette", new Vector4(p.vignette.intensity, p.vignette.smoothness, p.vignette.roundness,
            targetCamera != null ? targetCamera.aspect : (float)Screen.width / Mathf.Max(1, Screen.height)));
    }

    private void CreateParticles()
    {
        if (particleMaterial == null) return;
        var go = new GameObject("Screen Visual Ambient Dust") { hideFlags = HideFlags.DontSave };
        go.transform.SetParent(transform, false);
        particles = go.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.playOnAwake = false; main.loop = true; main.maxParticles = 96;
        main.duration = 10; main.prewarm = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(7, 13);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        main.startSpeed = 0;
        var shape = particles.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Box;
        var velocity = particles.velocityOverLifetime; velocity.enabled = true; velocity.space = ParticleSystemSimulationSpace.World;
        var noise = particles.noise; noise.enabled = true; noise.strength = 0.08f; noise.frequency = 0.2f; noise.scrollSpeed = 0.08f;
        var sheet = particles.textureSheetAnimation; sheet.enabled = true; sheet.numTilesX = 2; sheet.numTilesY = 2;
        sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0); sheet.startFrame = new ParticleSystem.MinMaxCurve(0, 1);
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = particleMaterial; renderer.sortingOrder = -700;
        particleGradient = new Gradient();
    }

    private void ApplyParticles(ScreenVisualProfile p, bool active)
    {
        if (particles == null) return;
        var a = p.atmosphere;
        bool use = active && a.particleEnabled && targetCamera != null && a.particleDensity > 0 && a.particleOpacity > 0;
        if (!use)
        {
            if (particles.isPlaying || particles.particleCount > 0) particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return;
        }
        Vector3 cameraPosition = targetCamera.transform.position;
        Vector3 center = new Vector3(cameraPosition.x, cameraPosition.y, 2);
        if ((particles.transform.position - center).sqrMagnitude > 1600) particles.Clear();
        particles.transform.position = center;
        var main = particles.main;
        // Atlas UVs now crop the transparent padding. Keep the visible fleck readable
        // at wide/portrait views without turning it into a large foreground sprite.
        float worldPerPixel = targetCamera.orthographicSize * 2 / Mathf.Max(1, targetCamera.pixelHeight);
        // The cropped cell still includes satellites and irregular edges: a 6-10px
        // quad gives the actual main fleck about 3-6px, rather than another 1px speck.
        float visibleSize = Mathf.Clamp(a.particleSize, worldPerPixel * 6, worldPerPixel * 10);
        main.startSize = new ParticleSystem.MinMaxCurve(visibleSize * 0.75f, visibleSize * 1.25f);
        var emission = particles.emission;
        float q = p.quality == ScreenVisualProfile.Quality.Low ? 0.2f : p.quality == ScreenVisualProfile.Quality.Medium ? 0.5f : 1;
        emission.rateOverTime = a.particleDensity * q;
        Vector2 area = new Vector2(targetCamera.orthographicSize * targetCamera.aspect * 2.4f, targetCamera.orthographicSize * 2.4f);
        if (area != lastParticleArea)
        { var shape = particles.shape; shape.scale = new Vector3(area.x, area.y, 0.1f); lastParticleArea = area; }
        var velocity = particles.velocityOverLifetime;
        velocity.x = new ParticleSystem.MinMaxCurve(a.particleSpeed * 0.2f, a.particleSpeed);
        velocity.y = new ParticleSystem.MinMaxCurve(-a.particleSpeed * 0.15f, a.particleSpeed * 0.4f);
        velocity.z = new ParticleSystem.MinMaxCurve(0, 0);
        if (lastParticleOpacity != a.particleOpacity || lastParticleColor != a.particleColor)
        {
            // Only allocate gradient keys when the Inspector values change.
            particleGradient.SetKeys(new[] { new GradientColorKey(a.particleColor, 0), new GradientColorKey(a.particleColor, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(a.particleOpacity, 0.2f), new GradientAlphaKey(a.particleOpacity, 0.75f), new GradientAlphaKey(0, 1) });
            var colors = particles.colorOverLifetime; colors.enabled = true; colors.color = particleGradient;
            lastParticleColor = a.particleColor; lastParticleOpacity = a.particleOpacity;
        }
        if (!particles.isPlaying) particles.Play();
    }

    private void OnDisable()
    {
        if (!ownsScene) return;
        RestoreBindings();
        Shader.SetGlobalFloat("_SV_LightingEnabled", 0);
        Shader.SetGlobalInt("_SV_LightCount", 0);
        foreach (ScreenLocalLight2D light in ScreenLocalLight2D.Active)
            if (light != null && light.gameObject.scene == gameObject.scene) light.SetGlowVisible(false);
        if (vignetteCanvas != null) vignetteCanvas.enabled = false;
        if (particles != null) particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void RestoreBindings()
    {
        foreach (Binding b in bindings)
        {
            if (b.renderer != null && b.renderer.sharedMaterial == b.visual) b.renderer.sharedMaterial = b.original;
            if (b.parallax != null) b.parallax.SetVisualScrollFactor(null);
        }
    }
    private void OnDestroy()
    {
        RestoreBindings();
        foreach (Binding b in bindings) if (b.background) DestroyOwned(b.visual);
        foreach (Material m in litMaterials.Values) DestroyOwned(m);
        DestroyOwned(vignetteMaterial);
        DestroyOwned(localGlowMaterial);
        DestroyOwned(localGlowSprite);
        if (vignetteCanvas != null) DestroyOwned(vignetteCanvas.gameObject);
        if (particles != null) DestroyOwned(particles.gameObject);
        DestroyOwned(fallback);
    }
    private static void DestroyOwned(Object value)
    {
        if (value == null) return;
        if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
    }
}
