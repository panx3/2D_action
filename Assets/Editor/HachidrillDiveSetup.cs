using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

public static class HachidrillDiveSetup
{
    public const string ArtPath = "Assets/Art/Effects/Hachidrill/HachidrillDiveVfx.png";

    // 生成原画をゲームのピクセル密度へ合わせて取り込み、Prefabだけを更新する。
    public static string Apply(string generatedImagePath)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        if (!File.Exists(ArtPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ArtPath));
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            ImageConversion.LoadImage(source, File.ReadAllBytes(generatedImagePath));
            // 1コマ64px。最近傍で縮小し、生成時の半透明エッジの細かいノイズを取り除く。
            var texture = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            var pixels = new Color32[256 * 256];
            for (int y = 0; y < 256; y++)
                for (int x = 0; x < 256; x++)
                {
                    Color32 color = source.GetPixel(Mathf.Min(source.width - 1, (int)((x + 0.5f) * source.width / 256f)),
                        Mathf.Min(source.height - 1, (int)((y + 0.5f) * source.height / 256f)));
                    color.a = color.a >= 160 ? (byte)255 : (byte)0;
                    pixels[y * 256 + x] = color;
                }
            texture.SetPixels32(pixels); texture.Apply();
            File.WriteAllBytes(ArtPath, ImageConversion.EncodeToPNG(texture));
            UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(ArtPath, ImportAssetOptions.ForceSynchronousImport);
        }
        var importer = (TextureImporter)AssetImporter.GetAtPath(ArtPath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = 100f;
        importer.filterMode = FilterMode.Point; importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.alphaIsTransparency = true; importer.wrapMode = TextureWrapMode.Clamp;
        var factories = new SpriteDataProviderFactories(); factories.Init();
        var provider = factories.GetSpriteEditorDataProviderFromObject(importer); provider.InitSpriteEditorDataProvider();
        var previous = provider.GetSpriteRects().ToDictionary(x => x.name, x => x.spriteID);
        string[] rows = { "Dive", "Impact", "Drilling", "Extraction" };
        var rects = new SpriteRect[16];
        // 生成された各行の実際の接地ピクセルに合わせる。セル中央基準だと煙が地面へ埋まる。
        float[] pivotY = { 0.5f, 6f / 64f, 7f / 64f, 11f / 64f };
        for (int row = 0; row < 4; row++)
            for (int col = 0; col < 4; col++)
            {
                string name = rows[row] + "_" + col;
                rects[row * 4 + col] = new SpriteRect { name = name, rect = new Rect(col * 64, (3 - row) * 64, 64, 64),
                    alignment = SpriteAlignment.Custom, pivot = new Vector2(0.5f, pivotY[row]),
                    spriteID = previous.TryGetValue(name, out var id) ? id : GUID.Generate() };
            }
        provider.SetSpriteRects(rects);
        provider.GetDataProvider<ISpriteNameFileIdDataProvider>().SetNameFileIdPairs(rects.Select(x => new SpriteNameFileIdPair(x.name, x.spriteID)));
        provider.Apply(); importer.SaveAndReimport();
        Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath(ArtPath).OfType<Sprite>().ToArray();
        var root = PrefabUtility.LoadPrefabContents(HachidrillSetup.PrefabPath);
        try
        {
            var ai = new SerializedObject(root.GetComponent<HachidrillAttackController>());
            ai.FindProperty("detectionWidth").floatValue = 20f;
            ai.FindProperty("detectionHeightDown").floatValue = 12f;
            ai.FindProperty("attackWindup").floatValue = 0.6f;
            ai.FindProperty("returnSpeed").floatValue = 4f;
            ai.ApplyModifiedPropertiesWithoutUndo();
            var feedback = root.GetComponent<HachidrillDiveFeedback>();
            if (feedback == null) feedback = root.AddComponent<HachidrillDiveFeedback>();
            var data = new SerializedObject(feedback);
            string[] fields = { "diveFrames", "impactFrames", "drillingFrames", "extractionFrames" };
            for (int row = 0; row < 4; row++)
            {
                var array = data.FindProperty(fields[row]); array.arraySize = 4;
                for (int col = 0; col < 4; col++)
                    array.GetArrayElementAtIndex(col).objectReferenceValue = sprites.Single(x => x.name == rows[row] + "_" + col);
            }
            data.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, HachidrillSetup.PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        HachidrillSetup.Validate();
        return "PASS: dive prefab + 16 Point/uncompressed 64px VFX sprites. Scenes not saved.";
    }
}
