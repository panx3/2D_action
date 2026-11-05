using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;

/// <summary>Restyles the existing theatre in place. Input/state/transition references stay with their owners.</summary>
public static class StageSelectVisualUpgrade
{
    private const string Art = "Assets/Art/StageSelect/DarkRetro/";

    [MenuItem("鉄球少女/Stage Select/暗いレトロ空間のビジュアルを適用")]
    public static string Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        Scene previous = SceneManager.GetActiveScene();
        Scene scene = SceneManager.GetSceneByPath(StageSelectSceneBuilder.ScenePath);
        bool opened = !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(StageSelectSceneBuilder.ScenePath, OpenSceneMode.Additive);
        try
        {
            if (scene.isDirty) throw new InvalidOperationException("StageSelectScene has unsaved edits. It was not modified.");
            ApplyToScene(scene);
            StageSelectExperienceUpgrade.ApplyToScene(scene);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            return "Applied dark retro hardware, separate CRT/lighting, and cartridge/slot alignment to the existing StageSelectScene.";
        }
        finally
        {
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }
    }

    public static void ApplyToScene(Scene scene)
    {
        var root = scene.GetRootGameObjects().Single(g => g.GetComponent<StageSelectController>() != null);
        var controller = root.GetComponent<StageSelectController>();
        var preview = root.GetComponent<StageSelectPreview>();
        RectTransform Find(string name) => root.GetComponentsInChildren<RectTransform>(true).Single(r => r.name == name);
        var world = Find("Theatre");
        var backdrop = root.GetComponentsInChildren<RectTransform>(true).Single(r => r.name == "Room" || r.name == "DarkBackdrop");
        var console = Find("Console"); var cartridge = Find("Cartridge");
        var crystal = Find("CrystalBirth"); var screen = Find("BackgroundCRT"); var television = Find("Television");
        CreateFrame(); CreateArrow();
        foreach (string name in new[] { "Console", "Cartridge", "Television", "Arrow" }) Import(Art + name + ".png");
        var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/StageSelectSurface.shader");
        if (shader == null || !shader.isSupported) throw new InvalidOperationException("StageSelectSurface shader is unavailable.");
        var voidMaterial = Surface("DarkSpace", shader, 2, new Vector2(640, 360), Color.white);
        var glowMaterial = Surface("LightSpill", shader, 1, new Vector2(320, 180), Color.white, true);
        var shadowMaterial = Surface("ContactShadow", shader, 1, new Vector2(320, 180), Color.black);
        var consoleMaterial = Surface("ConsoleSurface", shader, 0, new Vector2(400, 156), new Color(.84f, .86f, .94f));
        var cartridgeMaterial = Surface("CartridgeSurface", shader, 0, new Vector2(320, 195), new Color(.86f, .88f, .96f));
        var frameMaterial = Surface("TelevisionSurface", shader, 0, new Vector2(432, 268), Color.white);
        var backImage = backdrop.GetComponent<UnityEngine.UI.Image>();
        backdrop.name = "DarkBackdrop"; backImage.sprite = null; backImage.material = voidMaterial; backImage.color = Color.white;

        Set(screen, new Vector2(0, 94), new Vector2(768, 432));
        Set(television, new Vector2(0, 78), new Vector2(864, 536));
        var frame = television.GetComponent<UnityEngine.UI.Image>(); frame.sprite = Sprite("Television"); frame.material = frameMaterial;
        var screenGlow = Glow("ScreenLightSpill", world, new Vector2(0, 80), new Vector2(1120, 780), glowMaterial);
        screenGlow.transform.SetSiblingIndex(backdrop.GetSiblingIndex() + 1);
        var floorGlow = Glow("ReflectedCRTLight", world, new Vector2(0, -295), new Vector2(1060, 170), glowMaterial);
        floorGlow.transform.SetSiblingIndex(screen.GetSiblingIndex());

        // Transparent source margins stay in the original generated files; layout accounts for them.
        Set(cartridge, new Vector2(0, 16), new Vector2(540, 329));
        var shell = Find("Shell").GetComponent<UnityEngine.UI.Image>();
        Set(shell.rectTransform, Vector2.zero, new Vector2(540, 329)); shell.sprite = Sprite("Cartridge"); shell.material = cartridgeMaterial;
        var cartridgeGlow = Glow("CartridgePinkHalo", cartridge, Vector2.zero, new Vector2(690, 410), glowMaterial);
        cartridgeGlow.transform.SetAsFirstSibling();
        Set(Find("StageName"), new Vector2(0, 32), new Vector2(380, 85));
        Set(Find("StageNameText"), new Vector2(0, 32), new Vector2(380, 85));
        Find("StageNameText").GetComponent<TMP_Text>().color = new Color(.86f, .93f, 1);
        Set(Find("InsertButton"), new Vector2(0, 16), new Vector2(470, 230));
        Set(crystal, new Vector2(0, 16), crystal.sizeDelta);
        var crystalGlow = Glow("CrystalPinkHalo", crystal, Vector2.zero, new Vector2(630, 480), glowMaterial);
        crystalGlow.transform.SetAsFirstSibling();
        Find("CrystalFlash").anchoredPosition = new Vector2(0, 16);
        Set(console, new Vector2(0, -208), new Vector2(660, 258));
        var consoleImage = console.GetComponent<UnityEngine.UI.Image>(); consoleImage.sprite = Sprite("Console"); consoleImage.material = consoleMaterial;
        var shadow = Glow("ConsoleContactShadow", world, new Vector2(0, -308), new Vector2(800, 90), shadowMaterial);
        shadow.color = new Color(1, 1, 1, .7f); shadow.transform.SetSiblingIndex(console.GetSiblingIndex());
        Find("PowerLamp").GetComponent<UnityEngine.UI.Image>().enabled = false;
        var powerGlow = Glow("PinkPowerLight", console, new Vector2(193, -47), new Vector2(64, 24), glowMaterial);

        foreach (string name in new[] { "Heading", "Subtitle", "PreviewLabel", "LibraryMark", "LibraryCaption", "ConsoleBrand" })
            Find(name).gameObject.SetActive(false);
        foreach (string name in new[] { "Previous", "Next" })
        {
            var arrow = Find(name); Set(arrow, new Vector2(name == "Previous" ? -452 : 452, 16), new Vector2(48, 64));
            var image = arrow.GetComponent<UnityEngine.UI.Image>(); image.sprite = Sprite("Arrow");
            var button = arrow.GetComponent<UnityEngine.UI.Button>(); var colors = button.colors;
            colors.normalColor = Color.white; colors.highlightedColor = new Color(.7f, .9f, 1f); colors.pressedColor = new Color(1f, .58f, .8f);
            colors.selectedColor = colors.normalColor; button.colors = colors;
        }
        Find("BackButton").GetComponent<UnityEngine.UI.Image>().color = Color.clear;
        Find("BackLabel").GetComponent<TMP_Text>().color = new Color(.62f, .65f, .77f);
        Set(Find("Availability"), new Vector2(484, -302), new Vector2(300, 30));
        Set(Find("InputHints"), new Vector2(0, -343), new Vector2(1080, 26));
        Find("InputHints").GetComponent<TMP_Text>().color = new Color(.66f, .68f, .78f);
        var lighting = root.GetComponent<StageSelectLighting>();
        if (lighting == null) lighting = root.AddComponent<StageSelectLighting>();
        var motes = new UnityEngine.UI.Image[10];
        for (int i = 0; i < motes.Length; i++)
        {
            float side = i % 2 == 0 ? -1 : 1;
            // Glint's visible pixels occupy only 8x7 of its 32x32 source; retain those margins in the source asset.
            motes[i] = Glow("AmbientCrystal_" + i, world, new Vector2(side * (275 + i % 4 * 29), -60 - i / 2 * 31), new Vector2(32 + i % 3 * 8, 40 + i % 3 * 10), null);
            motes[i].sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/PinkJewel/Glint.png");
            motes[i].preserveAspect = true;
            motes[i].transform.SetSiblingIndex(cartridge.GetSiblingIndex());
        }
        Edit(lighting, s =>
        {
            Ref(s, "controller", controller); Ref(s, "screenGlow", screenGlow); Ref(s, "floorGlow", floorGlow);
            Ref(s, "cartridgeGlow", cartridgeGlow); Ref(s, "crystalGlow", crystalGlow); Ref(s, "powerGlow", powerGlow);
            var array = s.FindProperty("motes"); array.arraySize = motes.Length;
            for (int i = 0; i < motes.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = motes[i];
        });
        Edit(controller, s =>
        {
            Ref(s, "defaultCartridge", shell.sprite);
            s.FindProperty("insertPosition").vector2Value = new Vector2(0, -200);
            s.FindProperty("insertScale").floatValue = .68f;
        });
        Edit(preview, s =>
        {
            s.FindProperty("softness").floatValue = .8f; s.FindProperty("scanlines").floatValue = .16f;
            s.FindProperty("vignette").floatValue = .26f; s.FindProperty("noise").floatValue = .012f;
            s.FindProperty("brightness").floatValue = .9f;
        });
        var machine = AssetDatabase.LoadAssetAtPath<StageSelectStage>("Assets/Data/StageSelect/MachineCity.asset");
        if (machine != null)
        {
            Edit(machine, s => s.FindProperty("cartridgeTint").colorValue = Color.white);
            AssetDatabase.SaveAssetIfDirty(machine);
        }
    }

    private static void Set(RectTransform rect, Vector2 position, Vector2 size) { rect.anchoredPosition = position; rect.sizeDelta = size; }
    private static Sprite Sprite(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(Art + name + ".png");
    private static void Edit(UnityEngine.Object target, Action<SerializedObject> action)
    { var s = new SerializedObject(target); action(s); s.ApplyModifiedPropertiesWithoutUndo(); }
    private static void Ref(SerializedObject s, string name, UnityEngine.Object value) => s.FindProperty(name).objectReferenceValue = value;
    private static UnityEngine.UI.Image Glow(string name, Transform parent, Vector2 position, Vector2 size, Material material)
    {
        Transform existing = parent.Find(name);
        var rect = existing != null ? (RectTransform)existing : new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f); Set(rect, position, size);
        var image = rect.GetComponent<UnityEngine.UI.Image>(); if (image == null) image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.material = material; image.raycastTarget = false; return image;
    }
    private static Material Surface(string name, Shader shader, float mode, Vector2 grid, Color tone, bool additive = false)
    {
        string path = Art + name + ".mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path); if (existing != null) return existing;
        var material = new Material(shader) { name = name };
        material.SetFloat("_Mode", mode); material.SetVector("_PixelGrid", new Vector4(grid.x, grid.y, 0, 0));
        material.SetColor("_Tone", tone); material.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
        AssetDatabase.CreateAsset(material, path); AssetDatabase.SaveAssetIfDirty(material); return material;
    }
    private static void Import(string path)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
        importer.filterMode = FilterMode.Point; importer.mipmapEnabled = false; importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp; importer.npotScale = TextureImporterNPOTScale.None;
        importer.textureCompression = TextureImporterCompression.Uncompressed; importer.maxTextureSize = 2048;
        importer.SaveAndReimport();
    }
    private static void Save(string path, int width, int height, Func<int, int, Color> pixel)
    {
        if (File.Exists(path)) return;
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false); var pixels = new Color[width * height];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) pixels[(height - 1 - y) * width + x] = pixel(x, y);
        texture.SetPixels(pixels); texture.Apply(); File.WriteAllBytes(path, texture.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(texture);
    }
    private static void CreateFrame()
    {
        Save(Art + "Television.png", 432, 268, (x, y) =>
        {
            if (x < 4 || x >= 428 || y < 2 || y >= 266 || ((x < 12 || x >= 420) && (y < 10 || y >= 258))) return Color.clear;
            if (x >= 24 && x < 408 && y >= 18 && y < 234) return Color.clear;
            float grain = ((x * 31 + y * 17) % 7 - 3) * .0015f;
            Color c = new Color(.043f + grain, .044f + grain, .066f + grain);
            if (x >= 19 && x <= 412 && y >= 13 && y <= 239) c = new Color(.018f, .023f, .04f);
            else if (y < 13) c = Color.Lerp(new Color(.06f, .064f, .096f), new Color(.1f, .145f, .23f), Mathf.Pow(Mathf.Sin(x / 432f * Mathf.PI), 2));
            else if (x < 19) c += new Color(.04f, .008f, .025f) * Mathf.Clamp01(1 - Mathf.Abs(y - 145) / 160f);
            else if (x > 412) c += new Color(.005f, .025f, .05f);
            if (y >= 242 && y < 246 && x > 30 && x < 400) c += new Color(.027f, .014f, .035f);
            if ((x < 9 || x > 423 || y > 259)) c *= .43f;
            if (y == 10 && x > 24 && x < 408) c += new Color(.05f, .045f, .075f);
            c.a = 1; return c;
        });
    }
    private static void CreateArrow()
    {
        Save(Art + "Arrow.png", 24, 32, (x, y) =>
        {
            int half = Math.Abs(y - 16);
            if (x < 3 || x > 21 - half || half > 13) return Color.clear;
            if (x == 3 || x >= 19 - half || half > 11) return new Color(.12f, .12f, .2f);
            return x < 7 ? new Color(1f, .88f, .96f) : new Color(1f, .58f, .79f);
        });
    }
}
