using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Applies curved hardware, real slot occlusion and the clear-earned reveal to existing scene objects.</summary>
public static class StageSelectExperienceUpgrade
{
    private const string Art = "Assets/Art/StageSelect/Unlock/";
    public static string Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        Scene previous = SceneManager.GetActiveScene();
        try
        {
            WithScene(StageSelectSceneBuilder.ScenePath, ApplyToScene);
            WithScene("Assets/Scenes/Stage1.unity", s =>
            {
                var goal = s.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<GoalMenuController>(true)).Single();
                Edit(goal, so => { so.FindProperty("nextSceneName").stringValue = "StageSelectScene"; so.FindProperty("showDemoEnding").boolValue = false; });
            });
        }
        finally { if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous); }
        return "Applied curved CRT / slot slide / new crystal art; Stage1 clear earns Stage2 reveal. Stage1 NEXT returns to select.";
    }
    private static void WithScene(string path, Action<Scene> edit)
    {
        var s = SceneManager.GetSceneByPath(path); bool opened = !s.isLoaded;
        if (opened) s = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        try { if (s.isDirty) throw new InvalidOperationException(path + " has unsaved edits."); edit(s); EditorSceneManager.MarkSceneDirty(s); EditorSceneManager.SaveScene(s); }
        finally { if (opened) EditorSceneManager.CloseScene(s, true); }
    }
    public static void ApplyToScene(Scene s)
    {
        CreateCracks(); CreateInsertSound();
        foreach (string name in new[] { "CurvedTelevision", "CrystalMass", "CrystalShard", "Fractures" }) Import(Art + name + ".png");
        var root = s.GetRootGameObjects().Single(g => g.GetComponent<StageSelectController>() != null);
        RectTransform Find(string n) => root.GetComponentsInChildren<RectTransform>(true).Single(r => r.name == n);
        var world = Find("Theatre"); var console = Find("Console"); var card = Find("Cartridge");
        var tv = Find("Television").GetComponent<UnityEngine.UI.Image>();
        tv.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Art + "CurvedTelevision.png");
        tv.rectTransform.anchoredPosition = new Vector2(0, 58); tv.rectTransform.sizeDelta = new Vector2(840, 600);
        tv.material = Material("CurvedCase", 3, new Vector2(432, 324), new Color(.55f, .55f, .6f));
        Find("BackgroundCRT").anchoredPosition = new Vector2(0, 70);
        Find("BackgroundCRT").sizeDelta = new Vector2(706, 480);
        card.anchoredPosition = new Vector2(0, 38); card.sizeDelta = new Vector2(500, 305);
        Find("Shell").sizeDelta = card.sizeDelta;
        Find("StageName").anchoredPosition = Find("StageNameText").anchoredPosition = new Vector2(0, 30);
        Find("StageName").sizeDelta = Find("StageNameText").sizeDelta = new Vector2(350, 78);
        Find("InsertButton").anchoredPosition = card.anchoredPosition; Find("InsertButton").sizeDelta = new Vector2(430, 220);
        Find("CrystalBirth").anchoredPosition = Find("CrystalFlash").anchoredPosition = card.anchoredPosition;
        var mass = Find("ExistingCrystal").GetComponent<UnityEngine.UI.Image>();
        mass.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Art + "CrystalMass.png"); mass.rectTransform.sizeDelta = new Vector2(290, 290);
        mass.material = Material("CrystalFacets", 0, new Vector2(96, 88), Color.white);
        var fracture = Find("Cracks").GetComponent<UnityEngine.UI.Image>();
        fracture.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Art + "Fractures.png"); fracture.rectTransform.sizeDelta = new Vector2(235, 235);
        foreach (var image in root.GetComponentsInChildren<UnityEngine.UI.Image>(true).Where(i => i.name.StartsWith("Shard_")))
        {
            image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Art + "CrystalShard.png");
            image.material = Material("ShardFacets", 0, new Vector2(32, 48), Color.white);
            int index = int.Parse(image.name.Substring("Shard_".Length));
            image.rectTransform.sizeDelta = new Vector2(18 + index % 3 * 8, 28 + index % 3 * 10) * 1.35f;
        }
        foreach (var image in root.GetComponentsInChildren<UnityEngine.UI.Image>(true).Where(i => i.name.StartsWith("AmbientCrystal_"))) image.gameObject.SetActive(false);
        Find("CartridgePinkHalo").gameObject.SetActive(false);
        var back = world.Find("ConsoleBackDeck") as RectTransform;
        if (back == null) { back = new GameObject("ConsoleBackDeck", typeof(RectTransform), typeof(UnityEngine.UI.Image)).GetComponent<RectTransform>(); back.SetParent(world, false); }
        back.anchorMin = back.anchorMax = back.pivot = new Vector2(.5f, .5f); back.sizeDelta = console.sizeDelta; back.anchoredPosition = console.anchoredPosition;
        var front = console.GetComponent<UnityEngine.UI.Image>(); var backImage = back.GetComponent<UnityEngine.UI.Image>();
        backImage.sprite = front.sprite; backImage.material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/StageSelect/DarkRetro/ConsoleSurface.mat"); backImage.raycastTarget = false;
        // Move the cartridge relative to the existing back deck. Moving the deck to a stale
        // cartridge index can leave the deck in front once Unity removes its old array slot.
        card.SetSiblingIndex(back.GetSiblingIndex() + 1);
        console.SetSiblingIndex(card.GetSiblingIndex() + 1);
        var lip = Material("SlotFrontLip", 0, new Vector2(400, 156), new Color(.84f, .86f, .94f));
        lip.SetFloat("_SurfaceClipY", .72f); EditorUtility.SetDirty(lip); AssetDatabase.SaveAssetIfDirty(lip); front.material = lip;
        Vector4 uv = UnityEngine.Sprites.DataUtility.GetOuterUV(front.sprite);
        lip.SetVector("_SpriteUVRect", new Vector4(uv.x,uv.y,uv.z-uv.x,uv.w-uv.y));
        EditorUtility.SetDirty(lip); AssetDatabase.SaveAssetIfDirty(lip);
        Edit(root.GetComponent<StageSelectLighting>(), so => { so.FindProperty("consoleBack").objectReferenceValue = back; so.FindProperty("consoleFront").objectReferenceValue = console; });
        Edit(root.GetComponent<StageSelectPreview>(), so => { so.FindProperty("curvature").floatValue = .085f; so.FindProperty("glassReflection").floatValue = .14f; });
        Edit(root.GetComponent<StageSelectController>(), so =>
        {
            so.FindProperty("alignDuration").floatValue = .16f; so.FindProperty("insertDuration").floatValue = .42f;
            so.FindProperty("insertScale").floatValue = .8f; so.FindProperty("insertPosition").vector2Value = new Vector2(0, -210);
            so.FindProperty("gatherDuration").floatValue = .85f; so.FindProperty("crackDuration").floatValue = .42f; so.FindProperty("burstDuration").floatValue = .4f;
            so.FindProperty("insertClip").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(Art + "CartridgeClack.wav");
        });
        var stage = AssetDatabase.LoadAssetAtPath<StageSelectStage>("Assets/Data/StageSelect/MachineCity.asset");
        Edit(stage, so => { so.FindProperty("unlockAfterScene").stringValue = "Stage1"; so.FindProperty("lockedMessage").stringValue = "Stage1クリアで解放"; }); AssetDatabase.SaveAssetIfDirty(stage);
        ApplyRichFinish(root);
    }

    private static void ApplyRichFinish(GameObject root)
    {
        CreateRichCracks(); Import(Art + "JaggedFractures.png");
        RectTransform Find(string n) => root.GetComponentsInChildren<RectTransform>(true).Single(r => r.name == n);
        var screen = Find("BackgroundCRT"); screen.anchoredPosition = new Vector2(0, 78); screen.sizeDelta = new Vector2(720, 440);
        var tv = Find("Television").GetComponent<UnityEngine.UI.Image>();
        tv.sprite = null; tv.rectTransform.anchoredPosition = new Vector2(0, 62); tv.rectTransform.sizeDelta = new Vector2(840, 548);
        tv.material = Material("SmoothCurvedHousing", 4, new Vector2(840,572), Color.white);
        tv.material.SetVector("_HousingSize",new Vector4(840,548,720,440));
        EditorUtility.SetDirty(tv.material); AssetDatabase.SaveAssetIfDirty(tv.material);
        Import(Art + "JaggedCartridgeCrystal.png");
        var mass = Find("ExistingCrystal").GetComponent<UnityEngine.UI.Image>();
        mass.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Art + "JaggedCartridgeCrystal.png");
        mass.rectTransform.sizeDelta = new Vector2(510,285);
        mass.material = Material("JaggedCrystalFacets", 0, new Vector2(192,112), Color.white);
        // A separate additive white core hides the joins as large foreground splinters fuse.
        var world = Find("Theatre");
        var fusion = world.Find("CrystalFusionLight") as RectTransform;
        if (fusion == null) { fusion = new GameObject("CrystalFusionLight",typeof(RectTransform),typeof(UnityEngine.UI.Image)).GetComponent<RectTransform>(); fusion.SetParent(world,false); }
        fusion.anchorMin = fusion.anchorMax = fusion.pivot = new Vector2(.5f,.5f);
        fusion.anchoredPosition = Find("CrystalBirth").anchoredPosition; fusion.sizeDelta = new Vector2(670,380);
        var whiteLight = Material("CrystalFusionLight",1,new Vector2(320,180),Color.white);
        whiteLight.SetFloat("_DstBlend",(float)UnityEngine.Rendering.BlendMode.One);
        EditorUtility.SetDirty(whiteLight); AssetDatabase.SaveAssetIfDirty(whiteLight);
        var fusionImage = fusion.GetComponent<UnityEngine.UI.Image>(); fusionImage.material = whiteLight;
        fusionImage.raycastTarget = false; fusionImage.color = new Color(1,1,1,0);
        fusion.SetSiblingIndex(Find("CrystalFragments").GetSiblingIndex()+1);
        Find("Cracks").sizeDelta = new Vector2(445,230);
        Find("Cracks").GetComponent<UnityEngine.UI.Image>().sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Art + "JaggedFractures.png");
        Edit(root.GetComponent<StageSelectPreview>(), so =>
        {
            so.FindProperty("crtShader").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Shader>("Assets/StageSelectGlass.shader");
            so.FindProperty("resolution").vector2IntValue = new Vector2Int(960,600);
            so.FindProperty("curvature").floatValue = .18f; so.FindProperty("glassReflection").floatValue = .24f;
            so.FindProperty("softness").floatValue = 1.2f; so.FindProperty("scanlines").floatValue = .32f;
            so.FindProperty("scanlineCount").floatValue = 240; so.FindProperty("rgbSeparation").floatValue = .9f;
            so.FindProperty("vignette").floatValue = .26f;
        });
        Edit(root.GetComponent<StageSelectController>(), so =>
        {
            so.FindProperty("insertPosition").vector2Value = new Vector2(0,-119);
            so.FindProperty("insertScale").floatValue = .78f;
            so.FindProperty("gatherDuration").floatValue = 1.65f; so.FindProperty("crackDuration").floatValue = .48f;
            so.FindProperty("burstDuration").floatValue = .45f; so.FindProperty("bootDuration").floatValue = .38f;
            so.FindProperty("fusionLight").objectReferenceValue = fusionImage;
        });
        foreach (var image in root.GetComponentsInChildren<UnityEngine.UI.Image>(true).Where(i => i.name.StartsWith("Shard_")))
        {
            int index=int.Parse(image.name.Substring("Shard_".Length));
            image.rectTransform.sizeDelta = new Vector2(42+index%4*10,66+index%4*12);
        }
    }
    private static void CreateRichCracks()
    {
        string path = Art + "JaggedFractures.png"; if (File.Exists(path)) return;
        const int width = 192, height = 112; var pixels = new Color[width * height];
        int[][] lines = {
            new[] {96,5,94,22,102,36,95,52,101,64,96,80,105,96,102,106},
            new[] {100,35,73,43,67,54,45,48,28,59},
            new[] {99,67,121,64,133,75,149,69,170,78},
            new[] {94,80,78,84,69,97}, new[] {94,22,115,19,124,9}
        };
        void Dot(int x, int y, int radius, Color color)
        {
            for (int oy = -radius; oy <= radius; oy++) for (int ox = -radius; ox <= radius; ox++)
                if (x+ox >= 0 && x+ox < width && y+oy >= 0 && y+oy < height) pixels[(y+oy)*width+x+ox] = color;
        }
        foreach (int pass in new[] {0,1}) foreach (var line in lines)
        for (int i = 0; i < line.Length-2; i += 2)
        {
            int steps = Mathf.Max(Mathf.Abs(line[i+2]-line[i]),Mathf.Abs(line[i+3]-line[i+1]));
            for (int j=0;j<=steps;j++)
            {
                float t=j/(float)Mathf.Max(steps,1);
                Dot(Mathf.RoundToInt(Mathf.Lerp(line[i],line[i+2],t)) + (pass==1 ? 1 : 0),Mathf.RoundToInt(Mathf.Lerp(line[i+1],line[i+3],t)),
                    pass==0 ? 1 : 0,pass==0 ? new Color(.28f,.015f,.15f,.95f) : new Color(1,.91f,.98f));
            }
        }
        var tex=new Texture2D(width,height,TextureFormat.RGBA32,false);tex.SetPixels(pixels);tex.Apply();
        File.WriteAllBytes(path,tex.EncodeToPNG());UnityEngine.Object.DestroyImmediate(tex);
    }
    private static Material Material(string name, int mode, Vector2 grid, Color tone)
    {
        var path = Art + name + ".mat"; var m = AssetDatabase.LoadAssetAtPath<Material>(path); if (m != null) return m;
        m = new Material(Shader.Find("UI/IronBallGirl/StageSelectSurface")) { name = name };
        m.SetFloat("_Mode", mode); m.SetVector("_PixelGrid", new Vector4(grid.x, grid.y, 0, 0)); m.SetColor("_Tone", tone);
        AssetDatabase.CreateAsset(m, path); AssetDatabase.SaveAssetIfDirty(m); return m;
    }
    private static void Import(string path)
    {
        AssetDatabase.ImportAsset(path); var i = (TextureImporter)AssetImporter.GetAtPath(path);
        i.textureType = TextureImporterType.Sprite; i.spriteImportMode = SpriteImportMode.Single; i.filterMode = FilterMode.Point;
        i.mipmapEnabled = false; i.alphaIsTransparency = true; i.npotScale = TextureImporterNPOTScale.None; i.maxTextureSize = 2048;
        i.textureCompression = TextureImporterCompression.Uncompressed; i.SaveAndReimport();
    }
    private static void Edit(UnityEngine.Object o, Action<SerializedObject> edit) { var s = new SerializedObject(o); edit(s); s.ApplyModifiedPropertiesWithoutUndo(); }
    private static void CreateCracks()
    {
        string path = Art + "Fractures.png"; if (File.Exists(path)) return;
        var t = new Texture2D(96, 96, TextureFormat.RGBA32, false); var p = new Color[96 * 96];
        int[] xs = { 44, 47, 43, 49, 45, 53, 50, 56, 51, 57 };
        for (int y = 5; y < 90; y++)
        {
            int x = xs[Mathf.Min(xs.Length - 1, (y - 5) / 9)]; p[y * 96 + x] = new Color(1, .94f, .98f);
            if (y > 30 && y < 53) p[y * 96 + Mathf.Clamp(x - (y - 30), 0, 95)] = new Color(1, .84f, .96f);
            if (y > 52 && y < 74) p[y * 96 + Mathf.Clamp(x + (y - 52), 0, 95)] = new Color(1, .84f, .96f);
        }
        t.SetPixels(p); t.Apply(); File.WriteAllBytes(path, t.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(t);
    }
    private static void CreateInsertSound()
    {
        string path = Art + "CartridgeClack.wav"; if (File.Exists(path)) return;
        const int rate = 44100, count = 10584; var data = new byte[count * 2]; uint seed = 371;
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)rate; seed = seed * 1664525u + 1013904223u; float noise = (seed >> 8) / 8388607.5f - 1;
            float first = Mathf.Exp(-t * 70), second = t < .035f ? 0 : Mathf.Exp(-(t - .035f) * 45);
            float v = noise * (first * .46f + second * .23f) + Mathf.Sin(t * 118 * Mathf.PI * 2) * Mathf.Exp(-t * 24) * .26f
                + Mathf.Sin(t * 730 * Mathf.PI * 2) * first * .1f;
            short pcm = (short)(Mathf.Clamp(v, -.95f, .95f) * short.MaxValue); data[i * 2] = (byte)pcm; data[i * 2 + 1] = (byte)(pcm >> 8);
        }
        using (var w = new BinaryWriter(File.Create(path)))
        { w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + data.Length); w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16); w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(data.Length); w.Write(data); }
        AssetDatabase.ImportAsset(path);
    }
}
