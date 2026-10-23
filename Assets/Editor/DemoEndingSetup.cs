using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

/// <summary>Adds the demo page to the existing result prefab without rebuilding its CRT template.</summary>
public static class DemoEndingSetup
{
    private const string PrefabPath = "Assets/Prefabs/UI/GoalMenu.prefab";
    private const string LogoPath = "Assets/Art/DemoEnding/ThankYouForPlaying.png";
    private const string FontPath = "Assets/Fonts/DotGothic16 CRT.asset";
    private const string Message = "体験版はここまでです。\n遊んでくれてありがとうございます。";

    [MenuItem("Tools/鉄球少女/UI/Setup Demo Ending")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play Mode first.");
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            ApplyTo(root);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    public static void ApplyTo(GameObject root)
    {
        Transform safe = root.transform.Find("CRTPresentation/PhosphorUI/SafeArea");
        if (safe == null) throw new InvalidOperationException("Goal CRT SafeArea is missing.");
        var controller = root.GetComponent<GoalMenuController>();
        if (controller == null) throw new InvalidOperationException("GoalMenuController is missing.");
        if (safe.Find("DemoEnding") != null) return;

        var importer = (TextureImporter)AssetImporter.GetAtPath(LogoPath);
        if (importer == null) throw new InvalidOperationException("Demo ending logo is missing.");
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.SaveAndReimport();
        Sprite logo = AssetDatabase.LoadAssetAtPath<Sprite>(LogoPath);
        TMP_FontAsset font = GetFont();

        RectTransform page = Rect("DemoEnding", safe, Vector2.zero, new Vector2(1180f, 600f));
        // Integer enlargement retains the supplied image's original pixel grid and aspect.
        RectTransform logoRect = Rect("ThankYouLogo", page, new Vector2(0f, 108f), logo.rect.size * 6f);
        var image = logoRect.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.sprite = logo;
        image.preserveAspect = true;
        image.raycastTarget = false;
        var labelRect = Rect("ThankYouMessage", page, new Vector2(0f, -112f), new Vector2(1120f, 112f));
        var label = labelRect.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font;
        label.text = Message;
        label.fontSize = 32f;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.color = new Color32(239, 232, 247, 255);
        label.raycastTarget = false;
        page.gameObject.SetActive(false);

        var data = new SerializedObject(controller);
        data.FindProperty("demoEndingContent").objectReferenceValue = page.gameObject;
        var resultItems = data.FindProperty("resultOnlyContent");
        string[] names = { "Heading", "Subtitle", "ClearTime" };
        resultItems.arraySize = names.Length;
        for (int i = 0; i < names.Length; i++)
            resultItems.GetArrayElementAtIndex(i).objectReferenceValue = safe.Find(names[i])?.gameObject;
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    private static TMP_FontAsset GetFont()
    {
        TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (existing != null) return existing;
        Font source = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/DotGothic16-Regular.ttf");
        if (source == null) throw new InvalidOperationException("DotGothic16 font is missing.");
        var font = TMP_FontAsset.CreateFontAsset(source, 16, 2, GlyphRenderMode.RASTER_HINTED,
            512, 512, AtlasPopulationMode.Dynamic);
        font.name = "DotGothic16 CRT";
        string characters = new string(Message.Where(c => !char.IsControl(c)).Distinct().ToArray());
        if (!font.TryAddCharacters(characters, out string missing))
            throw new InvalidOperationException("Demo font is missing characters: " + missing);
        font.atlasPopulationMode = AtlasPopulationMode.Static;
        AssetDatabase.CreateAsset(font, FontPath);
        foreach (Texture2D atlas in font.atlasTextures)
        {
            atlas.filterMode = FilterMode.Point;
            AssetDatabase.AddObjectToAsset(atlas, font);
        }
        AssetDatabase.AddObjectToAsset(font.material, font);
        EditorUtility.SetDirty(font);
        AssetDatabase.SaveAssetIfDirty(font);
        return font;
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }
}
