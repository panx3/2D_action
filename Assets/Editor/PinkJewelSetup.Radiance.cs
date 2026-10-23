using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class PinkJewelSetup
{
    private const string RadianceSource = Art + "/Source/RadianceAtlas.png";

    [MenuItem("Tools/Tekkyu/Pink Jewels/Apply Radiance Artwork")]
    public static void UpgradeRadiance()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
        AudioClip pickupClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SFX/Items/コインGET.mp3");
        if (pickupClip == null) throw new InvalidOperationException("The supplied jewel pickup sound is missing.");
        MakeRadianceTextures();
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/PinkJewelShine.shader");
        if (shader == null) throw new InvalidOperationException("Pink Jewel shine shader is missing.");
        string materialPath = Materials + "/SurfaceShine.mat";
        Material shine = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (shine == null)
        {
            shine = new Material(shader);
            AssetDatabase.CreateAsset(shine, materialPath);
        }
        GameObject root = PrefabUtility.LoadPrefabContents(PickupPath);
        try
        {
            Transform visual = root.transform.Find("Visual");
            visual.localScale = Vector3.one * 0.85f;
            SpriteRenderer jewel = visual.Find("OriginalJewel").GetComponent<SpriteRenderer>();
            Transform existingShine = visual.Find("SurfaceShine");
            SpriteRenderer overlay = existingShine == null
                ? Layer("SurfaceShine", visual, "PinkJewel", 1f, 15)
                : existingShine.GetComponent<SpriteRenderer>();
            overlay.sprite = jewel.sprite;
            overlay.sharedMaterial = shine;
            overlay.sortingLayerID = jewel.sortingLayerID;
            overlay.sortingOrder = jewel.sortingOrder + 1;
            // Each layer draws the same original alpha silhouette; no replacement of the artwork.
            var reflection = visual.Find("FacetReflection").GetComponent<SpriteRenderer>();
            reflection.sprite = Sprite("RadianceGlint");
            reflection.transform.localScale = Vector3.one * 0.7f;
            ConfigureRadianceParticles(visual.Find("IdleStars").GetComponent<ParticleSystem>(), "RadianceStars", true);
            ConfigureRadianceParticles(visual.Find("IdleDiamonds").GetComponent<ParticleSystem>(), "RadianceDiamonds", true);
            PinkJewelPickup pickup = root.GetComponent<PinkJewelPickup>();
            Set(pickup, "surfaceShine", overlay);
            Set(pickup, "pickupClip", pickupClip);
            var settings = new SerializedObject(pickup);
            // Preserve values already tuned away from the old defaults.
            if (Mathf.Approximately(settings.FindProperty("particleRate").floatValue, 5f))
                settings.FindProperty("particleRate").floatValue = 8f;
            if (Mathf.Approximately(settings.FindProperty("glowIntensity").floatValue, 0.32f))
                settings.FindProperty("glowIntensity").floatValue = 0.4f;
            settings.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PickupPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        root = PrefabUtility.LoadPrefabContents(EffectPath);
        try
        {
            ConfigureRadianceParticles(root.transform.Find("RadiantStars").GetComponent<ParticleSystem>(), "RadianceStars", false);
            ConfigureRadianceParticles(root.transform.Find("RadiantDiamonds").GetComponent<ParticleSystem>(), "RadianceDiamonds", false);
            PrefabUtility.SaveAsPrefabAsset(root, EffectPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssetIfDirty(shine);
    }

    private static void MakeRadianceTextures()
    {
        Texture2D source = ReadTexture(RadianceSource);
        try
        {
            // Generated 4x2 atlas -> two 4-frame strips on a real 32px grid.
            // Nearest sampling and binary alpha remove generation edge haze, without redrawing art.
            int cellW = source.width / 4, cellH = source.height / 2;
            for (int row = 0; row < 2; row++)
            {
                string name = row == 0 ? "RadianceStars" : "RadianceDiamonds";
                var pixels = new Color32[128 * 32];
                for (int frame = 0; frame < 4; frame++)
                    for (int y = 0; y < 32; y++)
                        for (int x = 0; x < 32; x++)
                        {
                            Color32 color = source.GetPixel(frame * cellW + (int)((x + 0.5f) * cellW / 32),
                                (1 - row) * cellH + (int)((y + 0.5f) * cellH / 32));
                            color.a = color.a >= 160 ? (byte)255 : (byte)0;
                            pixels[y * 128 + frame * 32 + x] = color;
                        }
                WriteRadianceTexture(name, 128, 32, pixels);
                if (row == 0)
                {
                    var peak = new Color32[32 * 32];
                    for (int y = 0; y < 32; y++) Array.Copy(pixels, y * 128 + 64, peak, y * 32, 32);
                    WriteRadianceTexture("RadianceGlint", 32, 32, peak);
                }
            }
        }
        finally { Object.DestroyImmediate(source); }
    }

    private static void WriteRadianceTexture(string name, int width, int height, Color32[] pixels)
    {
        string path = Art + "/" + name + ".png";
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        try
        {
            texture.SetPixels32(pixels); texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
        }
        finally { Object.DestroyImmediate(texture); }
        ConfigureSprite(path, 32f);
    }

    private static void ConfigureRadianceParticles(ParticleSystem particles, string textureName, bool idle)
    {
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.startSize = new ParticleSystem.MinMaxCurve(idle ? 0.5f : 0.4f, idle ? 0.85f : 0.7f);
        main.maxParticles = idle ? 24 : 48;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.startColor = Color.white; // The generated sprite already supplies its pink/lilac palette.
        if (idle)
        {
            main.startLifetime = new ParticleSystem.MinMaxCurve(1f, 1.6f);
            var emission = particles.emission; emission.rateOverTime = 4f;
            var shape = particles.shape; shape.radius = 1.1f;
        }
        var animation = particles.textureSheetAnimation;
        animation.enabled = true;
        animation.mode = ParticleSystemAnimationMode.Grid;
        animation.numTilesX = 4; animation.numTilesY = 1;
        animation.animation = ParticleSystemAnimationType.WholeSheet;
        animation.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 0.999f));
        animation.cycleCount = 1;
        animation.startFrame = 0f;
        var size = particles.sizeOverLifetime;
        // The authored frames already grow and shrink; keep their peak large enough to read.
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.8f));
        string path = Materials + "/" + textureName + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Sprites/Default"))
                { mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "/" + textureName + ".png") };
            AssetDatabase.CreateAsset(material, path);
        }
        particles.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
    }
}
