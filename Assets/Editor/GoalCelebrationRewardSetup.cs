using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Adds reward accents without rebuilding the existing logo, sparkle layout or scene.</summary>
public static class GoalCelebrationRewardSetup
{
    private const string PrefabPath = "Assets/Prefabs/UI/CrystalAcquiredUI.prefab";
    private const string ChimePath = "Assets/Audio/SFX/Crystal/GoalAcquireChime.wav";

    [MenuItem("Tools/鉄球少女/Apply Goal Reward Accents")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Apply goal reward accents in Edit Mode.");
        CreateChimeIfMissing();
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var controller = root.GetComponent<CrystalAcquiredUI>();
            var data = new SerializedObject(controller);
            var presentation = data.FindProperty("presentationRoot").objectReferenceValue as GameObject;
            if (presentation == null) throw new InvalidOperationException("Existing goal presentation is missing.");
            Transform existing = presentation.transform.Find("CrystalBurst");
            GoalCelebrationBurstGraphic burst;
            if (existing != null) burst = existing.GetComponent<GoalCelebrationBurstGraphic>();
            else
            {
                var child = new GameObject("CrystalBurst", typeof(RectTransform), typeof(CanvasRenderer), typeof(GoalCelebrationBurstGraphic));
                child.layer = presentation.layer;
                var rect = child.GetComponent<RectTransform>();
                rect.SetParent(presentation.transform, false);
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                rect.SetSiblingIndex(1); // Behind sparkles/logo, above the dimmed game.
                burst = child.GetComponent<GoalCelebrationBurstGraphic>();
            }
            if (burst == null) throw new InvalidOperationException("CrystalBurst has the wrong component.");
            burst.raycastTarget = false;
            var source = root.GetComponent<AudioSource>();
            if (source == null) source = root.AddComponent<AudioSource>();
            source.playOnAwake = false; source.loop = false; source.spatialBlend = 0f;
            source.volume = 1f; source.priority = 64;
            var worldSource = OneShotAudioUtility.FindWorldImpactSource();
            if (worldSource != null) source.outputAudioMixerGroup = worldSource.outputAudioMixerGroup;
            data.FindProperty("burstGraphic").objectReferenceValue = burst;
            data.FindProperty("rewardAudioSource").objectReferenceValue = source;
            data.FindProperty("acquireClip").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(ChimePath);
            Set(data, "acquireVolume", 0.28f);
            Set(data, "popDuration", 0.26f);
            Set(data, "logoDisplayDuration", 1.15f);
            Set(data, "fadeOutDuration", 0.16f);
            Set(data, "clearScreenDelay", 0.08f);
            Set(data, "logoStartScale", 0.82f);
            Set(data, "overshootScale", 1.09f);
            Set(data, "logoStartYOffset", -12f);
            Set(data, "logoStartRotation", -1f);
            Set(data, "flashStrength", 0.32f);
            var dark = data.FindProperty("darkOverlay").objectReferenceValue as UnityEngine.UI.Image;
            var flash = data.FindProperty("flashOverlay").objectReferenceValue as UnityEngine.UI.Image;
            if (dark != null) dark.color = new Color(0.055f, 0.016f, 0.09f, 1f);
            if (flash != null) flash.color = new Color(1f, 0.88f, 0.97f, 1f);
            data.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        Debug.Log("[GoalReward] Pixel impact/reward bursts, timed crystal chime and continuous result handoff saved.");
    }

    private static void Set(SerializedObject data, string name, float value) => data.FindProperty(name).floatValue = value;

    private static void CreateChimeIfMissing()
    {
        if (!File.Exists(ChimePath))
        {
            const int rate = 48000;
            var samples = new double[(int)(rate * 0.66)];
            double peak = 0;
            var random = new System.Random(20261019);
            double[] offsets = { 0, 0.045, 0.095, 0.16 };
            double[] pitches = { 880, 1320, 1760, 2640 };
            for (int i = 0; i < samples.Length; i++)
            {
                double t = i / (double)rate;
                double sample = Math.Sin(2 * Math.PI * (82 * t - 90 * t * t)) * Math.Exp(-t * 48) * 0.18;
                sample += (random.NextDouble() * 2 - 1) * Math.Exp(-t * 100) * 0.025;
                for (int note = 0; note < pitches.Length; note++)
                {
                    double local = t - offsets[note];
                    if (local < 0) continue;
                    double envelope = (1 - Math.Exp(-local * 650)) * Math.Exp(-local * (9 + note));
                    double phase = local * pitches[note] * 2 * Math.PI;
                    double bell = Math.Sin(phase) + Math.Sin(phase * 2.01) * 0.17 + Math.Sin(phase * 3.97) * 0.035;
                    sample += bell * envelope * (0.45 - note * 0.065);
                }
                sample *= Math.Min(1, t / 0.002) * Math.Min(1, (0.66 - t) / 0.06);
                samples[i] = sample; peak = Math.Max(peak, Math.Abs(sample));
            }
            using (var writer = new BinaryWriter(File.Create(ChimePath)))
            {
                int bytes = samples.Length * 2;
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + bytes);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
                writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2);
                writer.Write((short)2); writer.Write((short)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(bytes);
                foreach (double sample in samples) writer.Write((short)(sample / Math.Max(peak, 0.001) * 0.78 * short.MaxValue));
            }
        }
        AssetDatabase.ImportAsset(ChimePath, ImportAssetOptions.ForceSynchronousImport);
        var importer = AssetImporter.GetAtPath(ChimePath) as AudioImporter;
        if (importer == null) throw new InvalidOperationException("Reward chime could not be imported.");
        var settings = importer.defaultSampleSettings;
        settings.loadType = AudioClipLoadType.DecompressOnLoad;
        settings.compressionFormat = AudioCompressionFormat.PCM;
        settings.preloadAudioData = true;
        importer.defaultSampleSettings = settings;
        importer.SaveAndReimport();
    }
}
