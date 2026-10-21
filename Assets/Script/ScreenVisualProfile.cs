using System;
using UnityEngine;

[CreateAssetMenu(menuName = "鉄球少女/Screen Visual Profile")]
public sealed class ScreenVisualProfile : ScriptableObject
{
    public enum Quality { Low, Medium, High }
    public Quality quality = Quality.High;
    public VignetteSettings vignette = new VignetteSettings();
    public BlurSettings blur = new BlurSettings();
    public AtmosphereSettings atmosphere = new AtmosphereSettings();
    public LightingSettings lighting = new LightingSettings();
    public bool parallaxEnabled = true;
    public LayerSettings[] layers = new LayerSettings[0];

    [Serializable] public sealed class VignetteSettings
    {
        public bool enabled = true;
        [Range(0, 1)] public float intensity = 0.15f;
        [Range(0.05f, 1)] public float smoothness = 0.65f;
        [Range(0, 1)] public float roundness = 0.65f;
        public Color color = new Color(0.055f, 0.07f, 0.14f, 1);
    }
    [Serializable] public sealed class BlurSettings
    {
        public bool backgroundBlurEnabled = true;
        [Range(0, 1)] public float blurStrength = 0.18f;
        [Range(0, 3)] public float blurRadius = 1;
        public bool peripheralBlurEnabled = true;
        [Range(0, 1)] public float peripheralBlurStrength = 0.12f;
        [Range(0.1f, 0.95f)] public float peripheralBlurRange = 0.6f;
    }
    [Serializable] public sealed class AtmosphereSettings
    {
        public bool atmosphereEnabled = true;
        public Color atmosphereColor = new Color(0.70f, 0.83f, 0.96f, 1);
        [Range(0, 1)] public float distanceFade = 0.12f;
        public bool fogEnabled = true;
        public Color fogColor = new Color(0.73f, 0.84f, 0.95f, 1);
        [Range(0, 0.3f)] public float fogOpacity = 0.045f;
        public Vector2 fogScrollSpeed = new Vector2(0.025f, 0.006f);
        [Range(0, 1)] public float fogVariation = 0.55f;
        public bool particleEnabled = true;
        [Range(0, 40)] public float particleDensity = 5;
        [Range(0, 1)] public float particleSpeed = 0.12f;
        [Range(0, 1)] public float particleOpacity = 0.48f;
        [Range(0.01f, 0.3f), Tooltip("希望ワールドサイズ。余白除去後の描画面を6〜10画面ピクセルに補正し、中心の絵柄を約3〜6ピクセルに保ちます。")]
        public float particleSize = 0.09f;
        public Color particleColor = Color.white;
    }
    [Serializable] public sealed class LightingSettings
    {
        public bool enabled = true;
        public bool localLightsEnabled = true;
        public Color ambientColor = new Color(0.96f, 0.98f, 1, 1);
        [Range(0.5f, 1.5f)] public float ambientIntensity = 1;
    }
    [Serializable] public sealed class LayerSettings
    {
        [Tooltip("SceneのParallaxBackgroundLayerと同名。未登録の層は元の設定を保持。")]
        public string layerName;
        [Range(0, 1)] public float depthFactor = 0.5f;
        public bool overrideScrollFactor;
        [Range(0, 0.5f)] public float scrollFactor = 0.14f;
        public Color colorTint = Color.white;
        [Range(0.5f, 1.5f)] public float contrast = 1;
        [Range(0, 1.5f)] public float saturation = 1;
        [Range(0, 1)] public float blurStrength = 1;
        [Range(0, 1)] public float fogInfluence = 1;
        [Range(0, 1)] public float lightingInfluence = 0.1f;
    }
}
