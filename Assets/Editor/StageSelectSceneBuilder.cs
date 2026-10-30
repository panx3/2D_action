using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;

/// <summary>Creates the theatre once; never regenerates an existing artist-edited scene or cartridge.</summary>
public static class StageSelectSceneBuilder
{
    public const string ScenePath = "Assets/Scenes/StageSelectScene.unity";
    private const string Art = "Assets/Art/StageSelect/";
    private const string Data = "Assets/Data/StageSelect/";
    private static readonly Color Ink = Hex("242035"), Cream = Hex("F4E6CB"), Pink = Hex("F4A7CB"), Muted = Hex("B6A4C5");

    [MenuItem("鉄球少女/Stage Select/新しいセレクトSceneを作成")]
    public static string Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            throw new InvalidOperationException("StageSelectScene already exists. Edit its hierarchy/Inspector; this command does not overwrite it.");
        Scene previous = SceneManager.GetActiveScene();
        Scene scene = default;
        try
        {
            CreateArt();
            CreateFont();
            StageSelectStage first = Cartridge("SkyCapital", "蒼天の浮遊王都", 0, "Stage1", true);
            StageSelectStage second = Cartridge("MachineCity", "紅蓮の機巧都市", 1, "Stage2", false);
            first.far.sprite = Load<Sprite>("Assets/image_/背景_一番後ろ.png");
            first.middle.sprite = Load<Sprite>("Assets/image_/背景_真ん中.png"); first.middle.drift = .04f;
            first.near.sprite = Load<Sprite>("Assets/image_/背景_一番手前.png"); first.near.drift = .065f;
            second.far.sprite = Load<Sprite>("Assets/image_/Stage/Stage2/Backgrounds/MachineCitySky.png");
            second.middle.sprite = Load<Sprite>("Assets/image_/Stage/Stage2/Backgrounds/MachineCitySkyline.png"); second.middle.drift = .045f;
            second.cartridgeTint = Hex("FFE4D8"); second.accent = Hex("FFBA94");
            EditorUtility.SetDirty(first); EditorUtility.SetDirty(second);
            AssetDatabase.SaveAssetIfDirty(first); AssetDatabase.SaveAssetIfDirty(second);
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            CreateView(new[] { first, second });
            StageSelectVisualUpgrade.ApplyToScene(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            ConnectTitle();
            var scenes = EditorBuildSettings.globalScenes.ToList();
            if (!scenes.Any(s => s.path == ScenePath)) scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            else scenes.First(s => s.path == ScenePath).enabled = true;
            EditorBuildSettings.globalScenes = scenes.ToArray();
            var buildSettings = Resources.FindObjectsOfTypeAll<EditorBuildSettings>().Single();
            EditorUtility.SetDirty(buildSettings);
            AssetDatabase.SaveAssetIfDirty(buildSettings);
            return "Created StageSelectScene; Title → StageSelect → Stage1. Stage2 uses its existing backgrounds and is preview-only on this branch.";
        }
        finally
        {
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static StageSelectStage Cartridge(string id, string title, int spriteIndex, string destination, bool playable)
    {
        Directory.CreateDirectory(Data);
        var stage = ScriptableObject.CreateInstance<StageSelectStage>();
        stage.stageName = title; stage.sceneName = destination; stage.playable = playable;
        var sprites = AssetDatabase.LoadAllAssetsAtPath("Assets/image_/ステージ名sprite.png").OfType<Sprite>().OrderByDescending(s => s.rect.y).ToArray();
        stage.titleSprite = sprites[spriteIndex];
        AssetDatabase.CreateAsset(stage, Data + id + ".asset");
        return stage;
    }

    private static void CreateView(StageSelectStage[] stages)
    {
        var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
        camera.tag = "MainCamera"; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Ink;
        camera.orthographic = true; camera.transform.position = new Vector3(0, 0, -10);
        var root = new GameObject("StageSelectPresentation");
        var controller = root.AddComponent<StageSelectController>();
        var preview = root.AddComponent<StageSelectPreview>();
        var audio = root.AddComponent<AudioSource>(); audio.playOnAwake = false; audio.spatialBlend = 0;
        var canvasRect = Rect("StageSelectCanvas", root.transform, Vector2.zero, new Vector2(1280, 720));
        var canvas = canvasRect.gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.pixelPerfect = true; canvas.sortingOrder = 20;
        var scaler = canvasRect.gameObject.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720); scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.Expand;
        canvasRect.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        var world = Rect("Theatre", canvasRect, Vector2.zero, new Vector2(1280, 720));
        var worldGroup = world.gameObject.AddComponent<CanvasGroup>();
        Picture("Room", world, "Room", Vector2.zero, new Vector2(1280, 720));
        var raw = Rect("BackgroundCRT", world, new Vector2(-24, 80), new Vector2(704, 396)).gameObject.AddComponent<UnityEngine.UI.RawImage>();
        raw.raycastTarget = false;
        Picture("Television", world, "Television", new Vector2(0, 70), new Vector2(840, 488));
        Label("Heading", world, "STAGE SELECT", new Vector2(0, 337), new Vector2(600, 28), 24, Cream, true);
        Label("Subtitle", world, "カセットをえらんで、世界のつづきへ。", new Vector2(0, 302), new Vector2(560, 24), 16, Ink);
        Label("PreviewLabel", world, "LIVE PREVIEW", new Vector2(-265, 248), new Vector2(186, 22), 12, new Color(1, 1, 1, .8f), true);

        var cartridge = Rect("Cartridge", world, new Vector2(0, -5), new Vector2(464, 256));
        var cartridgeGroup = cartridge.gameObject.AddComponent<CanvasGroup>();
        var shell = Picture("Shell", cartridge, "Cartridge", Vector2.zero, new Vector2(464, 256));
        var title = Picture("StageName", cartridge, null, new Vector2(0, -3), new Vector2(420, 94)); title.preserveAspect = true;
        var fallbackTitle = Label("StageNameText", cartridge, "", Vector2.zero, new Vector2(410, 94), 32, Ink);
        fallbackTitle.enableAutoSizing = true; fallbackTitle.fontSizeMin = 18; fallbackTitle.fontSizeMax = 32;
        // Console comes after the cartridge: the front lip naturally hides it as it enters the slot.
        var console = Picture("Console", world, "Console", new Vector2(0, -222), new Vector2(544, 192));
        Label("ConsoleBrand", console.transform, "MORNING STAR", new Vector2(85, -17), new Vector2(210, 24), 12, Hex("80708D"), true);
        var lamp = Solid("PowerLamp", console.transform, new Vector2(-170, -5), new Vector2(8, 4), Hex("6B525F"));

        var controls = Rect("Controls", world, Vector2.zero, new Vector2(1280, 720));
        var controlsGroup = controls.gameObject.AddComponent<CanvasGroup>();
        Arrow("Previous", controls, -1, controller);
        Arrow("Next", controls, 1, controller);
        Label("LibraryMark", world, "鉄球少女", new Vector2(-448, -246), new Vector2(250, 30), 24, Cream);
        Label("LibraryCaption", world, "CASSETTE LIBRARY", new Vector2(-448, -274), new Vector2(270, 20), 12, Muted, true);
        var status = Label("Availability", controls, "", new Vector2(448, -256), new Vector2(300, 32), 16, Hex("B9E8F2"));
        var hint = Label("InputHints", controls, "", new Vector2(0, -340), new Vector2(1080, 28), 16, Cream);
        // A clear mouse target over the cartridge; keyboard/gamepad actions have one owner.
        var hit = Solid("InsertButton", controls, new Vector2(0, -5), new Vector2(464, 256), Color.clear);
        hit.raycastTarget = true;
        var insert = hit.gameObject.AddComponent<UnityEngine.UI.Button>(); insert.targetGraphic = hit;
        insert.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
        UnityEditor.Events.UnityEventTools.AddPersistentListener(insert.onClick, controller.Confirm);
        var back = Solid("BackButton", controls, new Vector2(-522, 337), new Vector2(172, 32), Hex("332B45"));
        back.raycastTarget = true;
        Label("BackLabel", back.transform, "もどる", Vector2.zero, new Vector2(172, 28), 16, Muted);
        var backButton = back.gameObject.AddComponent<UnityEngine.UI.Button>(); backButton.targetGraphic = back;
        backButton.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
        UnityEditor.Events.UnityEventTools.AddPersistentListener(backButton.onClick, controller.Back);

        var crystal = Rect("CrystalBirth", world, new Vector2(0, -5), new Vector2(220, 240));
        var crystalGroup = crystal.gameObject.AddComponent<CanvasGroup>();
        var jewel = Picture("ExistingCrystal", crystal, null, Vector2.zero, new Vector2(220, 240));
        jewel.sprite = Load<Sprite>("Assets/Art/PinkJewel/PinkJewel.png"); jewel.preserveAspect = true;
        var cracks = Picture("Cracks", crystal, "Cracks", Vector2.zero, new Vector2(150, 190)).gameObject.AddComponent<CanvasGroup>();
        var shardRoot = Rect("CrystalFragments", world, Vector2.zero, Vector2.zero);
        var shardSprite = Load<Sprite>("Assets/Art/PinkJewel/Diamond.png");
        var shards = new RectTransform[24];
        for (int i = 0; i < shards.Length; i++)
        {
            var shard = Picture("Shard_" + i, shardRoot, null, Vector2.zero, new Vector2(18 + i % 3 * 8, 28 + i % 3 * 10));
            shard.sprite = shardSprite; shard.preserveAspect = true;
            shards[i] = shard.rectTransform;
        }
        var flash = Picture("CrystalFlash", world, null, new Vector2(0, -5), new Vector2(920, 620));
        flash.sprite = Load<Sprite>("Assets/Art/PinkJewel/Flash.png"); flash.preserveAspect = true;
        var loading = Label("Loading", world, "よみこみ中…", new Vector2(-24, 80), new Vector2(340, 50), 20, Cream);
        var actions = Load<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
        var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        var module = eventSystem.GetComponent<InputSystemUIInputModule>(); module.AssignDefaultActions();
        module.move = null; module.submit = null; module.cancel = null;
        Configure(preview, s =>
        {
            Ref(s, "screen", raw); Ref(s, "compositionShader", Load<Shader>("Assets/StageSelectPreview.shader"));
            Ref(s, "crtShader", Load<Shader>("Assets/CRTFilter.shader"));
        });
        Configure(controller, s =>
        {
            var array = s.FindProperty("stages"); array.arraySize = stages.Length;
            for (int i = 0; i < stages.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = stages[i];
            Ref(s, "inputActions", actions); Ref(s, "world", world); Ref(s, "canvas", canvas); Ref(s, "worldGroup", worldGroup);
            Ref(s, "preview", preview); Ref(s, "screen", raw); Ref(s, "cartridge", cartridge); Ref(s, "cartridgeGroup", cartridgeGroup);
            Ref(s, "cartridgeImage", shell); Ref(s, "defaultCartridge", shell.sprite); Ref(s, "stageTitle", title); Ref(s, "fallbackTitle", fallbackTitle);
            Ref(s, "status", status); Ref(s, "hints", hint); Ref(s, "loadingLabel", loading); Ref(s, "controlsGroup", controlsGroup);
            Ref(s, "powerLamp", lamp); Ref(s, "flash", flash); Ref(s, "console", console.rectTransform); Ref(s, "crystal", crystal);
            Ref(s, "crystalGroup", crystalGroup); Ref(s, "cracks", cracks); Ref(s, "audioSource", audio);
            Ref(s, "gatherClip", Load<AudioClip>("Assets/Audio/SFX/Crystal/CheckpointActivate.wav"));
            Ref(s, "crackClip", Load<AudioClip>("Assets/Audio/SFX/Crystal/ひびがはいる音.mp3"));
            Ref(s, "burstClip", Load<AudioClip>("Assets/Audio/SFX/Crystal/砕ける音.mp3"));
            var fragmentArray = s.FindProperty("shards"); fragmentArray.arraySize = shards.Length;
            for (int i = 0; i < shards.Length; i++) fragmentArray.GetArrayElementAtIndex(i).objectReferenceValue = shards[i];
        });
        // Reuse the existing BGM owner and title recording; do not create a global music singleton.
        var music = new GameObject("StageSelectAudio", typeof(AudioSource), typeof(GameBgmController));
        Configure(music.GetComponent<GameBgmController>(), s =>
        {
            Ref(s, "bgmSource", music.GetComponent<AudioSource>());
            Ref(s, "titleClip", Load<AudioClip>("Assets/Audio/BGM/TitleBGM.mp3"));
            s.FindProperty("initialState").enumValueIndex = (int)GameBgmController.BgmState.Title;
        });
    }

    private static void Arrow(string name, Transform parent, int direction, StageSelectController controller)
    {
        var image = Picture(name, parent, "Arrow", new Vector2(direction * 495, -5), new Vector2(72, 88));
        image.rectTransform.localScale = new Vector3(direction, 1, 1); image.raycastTarget = true;
        var button = image.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = image;
        button.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
        UnityEditor.Events.UnityEventTools.AddIntPersistentListener(button.onClick, controller.Move, direction);
        var colors = button.colors; colors.normalColor = Cream; colors.highlightedColor = Pink; colors.pressedColor = Hex("D281AA"); button.colors = colors;
    }

    private static void ConnectTitle()
    {
        Scene title = SceneManager.GetSceneByPath("Assets/Scenes/TitleScene.unity"); bool opened = !title.isLoaded;
        if (opened) title = EditorSceneManager.OpenScene("Assets/Scenes/TitleScene.unity", OpenSceneMode.Additive);
        try
        {
            if (title.isDirty) throw new InvalidOperationException("TitleScene has unsaved edits; its route was not changed.");
            var controller = title.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<TitleScreenController>(true)).Single();
            Configure(controller, s => s.FindProperty("stageSceneName").stringValue = "StageSelectScene");
            EditorSceneManager.MarkSceneDirty(title); EditorSceneManager.SaveScene(title);
        }
        finally { if (opened) EditorSceneManager.CloseScene(title, true); }
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = position; rect.sizeDelta = size; return rect;
    }
    private static UnityEngine.UI.Image Picture(string name, Transform parent, string sprite, Vector2 position, Vector2 size)
    {
        var image = Rect(name, parent, position, size).gameObject.AddComponent<UnityEngine.UI.Image>();
        if (sprite != null) image.sprite = Load<Sprite>(Art + sprite + ".png");
        image.raycastTarget = false; return image;
    }
    private static UnityEngine.UI.Image Solid(string name, Transform parent, Vector2 position, Vector2 size, Color color)
    { var image = Picture(name, parent, null, position, size); image.color = color; return image; }
    private static TMP_Text Label(string name, Transform parent, string text, Vector2 position, Vector2 size, int fontSize, Color color, bool latin = false)
    {
        var label = Rect(name, parent, position, size).gameObject.AddComponent<TextMeshProUGUI>();
        label.font = Load<TMP_FontAsset>(latin ? "Assets/Fonts/Silkscreen CRT.asset" : Art + "StageSelectJapanese.asset");
        label.text = text; label.fontSize = fontSize; label.color = color; label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap; label.raycastTarget = false;
        return label;
    }
    private static void Configure(UnityEngine.Object target, Action<SerializedObject> edit)
    { var s = new SerializedObject(target); edit(s); s.ApplyModifiedPropertiesWithoutUndo(); }
    private static void Ref(SerializedObject s, string name, UnityEngine.Object value) => s.FindProperty(name).objectReferenceValue = value;
    private static T Load<T>(string path) where T : UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new FileNotFoundException(path);
    private static Color Hex(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out Color c); return c; }

    private static void CreateFont()
    {
        var font = TMP_FontAsset.CreateFontAsset(Load<Font>("Assets/Fonts/DotGothic16-Regular.ttf"),
            16, 1, GlyphRenderMode.RASTER_HINTED, 1024, 1024, AtlasPopulationMode.Dynamic);
        font.name = "Stage Select Japanese";
        const string text = "カセットをえらんで、世界のつづきへ。このあそぶただいま制作中もどる鉄球少女蒼天浮遊王都紅蓮機巧都市月影氷晶起動できませんよみこに失敗したうちどお試くださいタイトルSceneが登録されていません←→十字キーいれるはじめるもどるよみこみ中… /";
        string characters = new string((text + new string(Enumerable.Range(32, 95).Select(c => (char)c).ToArray())).Distinct().ToArray());
        font.TryAddCharacters(characters, out string missing);
        if (!string.IsNullOrEmpty(missing)) throw new InvalidOperationException("Stage select font is missing: " + missing);
        AssetDatabase.CreateAsset(font, Art + "StageSelectJapanese.asset");
        foreach (var texture in font.atlasTextures) { texture.filterMode = FilterMode.Point; AssetDatabase.AddObjectToAsset(texture, font); }
        AssetDatabase.AddObjectToAsset(font.material, font); AssetDatabase.SaveAssetIfDirty(font);
    }

    private static void CreateArt()
    {
        Directory.CreateDirectory(Art);
        var room = new Pixels(640, 360);
        for (int y = 0; y < 360; y++) for (int x = 0; x < 640; x++)
        {
            float light = Mathf.Max(0, 1 - Vector2.Distance(new Vector2(x, y), new Vector2(320, 160)) / 390f);
            room.Dot(x, y, Color.Lerp(Hex("201D31"), Hex("51405D"), light * .8f + (((x + y) % 4 == 0) ? .015f : 0)));
        }
        room.Fill(0, 268, 640, 3, Hex("746071")); room.Fill(0, 271, 640, 89, Hex("3B2C40"));
        for (int y = 280; y < 360; y += 18) room.Fill(0, y, 640, 1, Hex("493348"));
        for (int i = 0; i < 2; i++)
        {
            int x = i == 0 ? 20 : 566;
            room.Fill(x, 100, 54, 4, Hex("1B1929")); room.Fill(x - 3, 97, 60, 3, Hex("7A6279"));
            for (int j = 0; j < 4; j++)
            {
                Color c = new[] { Hex("887685"), Hex("B98A96"), Hex("8E99A1"), Hex("AD9A86") }[j];
                room.Fill(x + 3 + j * 12, 69 + j % 2 * 5, 10, 28 - j % 2 * 5, c);
                room.Fill(x + 5 + j * 12, 74 + j % 2 * 5, 6, 3, Hex("D8C8B7"));
            }
            room.Fill(x + 14, 172, 26, 34, Hex("74536D")); room.Fill(x + 17, 175, 20, 27, Hex("A88497"));
            room.Fill(x + 24, 180, 5, 16, Hex("E2B1BF")); room.Fill(x + 19, 185, 15, 5, Hex("E2B1BF"));
        }
        room.Ellipse(320, 294, 164, 18, Hex("221C31")); room.Ellipse(320, 310, 115, 12, Hex("624058"));
        room.Save("Room");

        var tv = new Pixels(420, 244);
        tv.Fill(8, 0, 402, 242, Ink); tv.Fill(0, 8, 420, 224, Ink);
        tv.Fill(8, 4, 400, 230, Hex("C8B3B5")); tv.Fill(4, 10, 412, 218, Hex("C8B3B5"));
        tv.Fill(10, 6, 396, 218, Cream); tv.Fill(8, 12, 400, 207, Cream);
        tv.Fill(14, 8, 386, 3, Hex("FFF6DC")); tv.Fill(8, 223, 402, 11, Hex("9B839B"));
        tv.Fill(16, 12, 364, 210, Hex("8E798C")); tv.Fill(19, 15, 358, 204, Ink);
        tv.Fill(22, 18, 352, 198, Color.clear);
        tv.Fill(30, 226, 120, 2, Hex("6D576F"));
        for (int i = 0; i < 9; i++) tv.Fill(178 + i * 14, 227, 8, 2, Hex("8C778B"));
        tv.Ellipse(395, 53, 11, 11, Hex("A38A9C")); tv.Ellipse(394, 51, 8, 8, Hex("E4D2BB")); tv.Fill(393, 44, 2, 14, Hex("7D677D"));
        tv.Ellipse(395, 91, 9, 9, Hex("A38A9C")); tv.Ellipse(394, 89, 6, 6, Hex("E4D2BB"));
        for (int i = 0; i < 12; i++) tv.Fill(387, 124 + i * 4, 16, 2, Hex("B29CA9"));
        tv.Fill(389, 191, 10, 5, Hex("334C4C")); tv.Fill(391, 192, 6, 2, Hex("A7E4BD"));
        tv.Save("Television");

        var cart = new Pixels(232, 128);
        cart.Fill(7, 2, 218, 122, Ink); cart.Fill(2, 10, 228, 108, Ink);
        cart.Fill(8, 6, 216, 114, Hex("D1C9C5")); cart.Fill(5, 12, 222, 100, Hex("D1C9C5"));
        cart.Fill(10, 7, 212, 106, Hex("F3E9D6")); cart.Fill(12, 10, 208, 3, Hex("FFFAE9"));
        cart.Fill(66, 2, 100, 12, Hex("B3ABB9")); cart.Fill(72, 5, 88, 5, Hex("DBD1CF"));
        cart.Fill(9, 23, 214, 82, Hex("A49BAD")); cart.Fill(11, 25, 210, 78, Hex("E7DCCB"));
        cart.Fill(13, 27, 206, 73, Hex("FFF2D9"));
        cart.Fill(16, 28, 200, 2, Hex("FFFFFF")); cart.Fill(12, 100, 208, 2, Hex("C5B8BD"));
        for (int i = 0; i < 4; i++) {cart.Fill(7, 32 + i * 15, 2, 7, Hex("9F96A6")); cart.Fill(223, 32 + i * 15, 2, 7, Hex("9F96A6"));}
        cart.Fill(20, 113, 192, 5, Hex("B7ADBA")); cart.Fill(28, 120, 176, 3, Hex("8C8195")); cart.Save("Cartridge");

        var machine = new Pixels(272, 96);
        machine.Poly(new[] { new Vector2Int(30, 2), new Vector2Int(242, 2), new Vector2Int(270, 30), new Vector2Int(270, 79), new Vector2Int(258, 91), new Vector2Int(14, 91), new Vector2Int(2, 79), new Vector2Int(2, 30) }, Ink);
        machine.Poly(new[] { new Vector2Int(31, 5), new Vector2Int(241, 5), new Vector2Int(265, 30), new Vector2Int(7, 30) }, Hex("DFD0BC"));
        machine.Fill(8, 31, 256, 47, Hex("C8B3B5")); machine.Fill(12, 34, 248, 36, Cream); machine.Fill(12, 72, 248, 8, Hex("AA91A5"));
        machine.Fill(33, 7, 206, 2, Hex("FFF3D7")); machine.Fill(79, 13, 116, 13, Hex("AC8EA1")); machine.Fill(82, 15, 110, 8, Ink); machine.Fill(86, 16, 102, 2, Hex("66536D"));
        machine.Fill(27, 41, 28, 15, Hex("9E5C7E")); machine.Fill(29, 41, 24, 11, Hex("E19AB7")); machine.Fill(31, 42, 20, 2, Hex("FFD2DB"));
        machine.Fill(62, 43, 16, 9, Hex("A397AA")); machine.Fill(64, 43, 12, 6, Hex("D1C6CE"));
        machine.Fill(83, 63, 40, 11, Ink); machine.Fill(147, 63, 40, 11, Ink);
        for (int i = 0; i < 4; i++) { machine.Fill(87 + i * 8, 67, 3, 3, Hex("AD95A0")); machine.Fill(151 + i * 8, 67, 3, 3, Hex("AD95A0")); }
        machine.Fill(22, 87, 35, 6, Ink); machine.Fill(215, 87, 35, 6, Ink);
        for (int i = 0; i < 5; i++) {machine.Fill(31 + i * 6, 17, 3, 7, Hex("AF9EAE")); machine.Fill(213 + i * 6, 17, 3, 7, Hex("AF9EAE"));}
        machine.Save("Console");
        var arrow = new Pixels(36, 44);
        arrow.Poly(new[] { new Vector2Int(7, 4), new Vector2Int(32, 21), new Vector2Int(32, 24), new Vector2Int(7, 41), new Vector2Int(3, 38), new Vector2Int(3, 7) }, Ink);
        arrow.Poly(new[] { new Vector2Int(8, 10), new Vector2Int(27, 23), new Vector2Int(8, 35) }, Color.white); arrow.Save("Arrow");
        var cracks = new Pixels(64, 80);
        int[] line = { 29, 31, 29, 35, 32, 37, 36, 41, 38, 44 };
        for (int i = 0; i < line.Length - 1; i++)
        { cracks.Fill(line[i], 5 + i * 7, 2, 9, Hex("FFFBE7")); cracks.Fill(Math.Min(line[i], line[i + 1]), 12 + i * 7, Math.Abs(line[i + 1] - line[i]) + 2, 2, Hex("FFFBE7")); }
        cracks.Fill(13, 28, 18, 2, Hex("FFFBE7")); cracks.Fill(39, 54, 15, 2, Hex("FFFBE7")); cracks.Save("Cracks");
        AssetDatabase.Refresh();
    }

    private sealed class Pixels
    {
        private readonly int width, height;
        private readonly Color[] pixels;
        public Pixels(int width, int height) { this.width = width; this.height = height; pixels = new Color[width * height]; }
        public void Dot(int x, int y, Color c) { if (x >= 0 && x < width && y >= 0 && y < height) pixels[(height - 1 - y) * width + x] = c; }
        public void Fill(int x, int y, int w, int h, Color c) { for (int j = y; j < y + h; j++) for (int i = x; i < x + w; i++) Dot(i, j, c); }
        public void Ellipse(int x, int y, int rx, int ry, Color c)
        { for (int j = -ry; j <= ry; j++) for (int i = -rx; i <= rx; i++) if (i * i / (float)(rx * rx) + j * j / (float)(ry * ry) <= 1) Dot(x + i, y + j, c); }
        public void Poly(Vector2Int[] points, Color c)
        {
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                bool inside = false;
                for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
                    if ((points[i].y > y) != (points[j].y > y) && x < (points[j].x - points[i].x) * (y - points[i].y) / (float)(points[j].y - points[i].y) + points[i].x) inside = !inside;
                if (inside) Dot(x, y, c);
            }
        }
        public void Save(string name)
        {
            string path = Art + name + ".png";
            if (File.Exists(path)) throw new InvalidOperationException("Refusing to overwrite artwork: " + path);
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.SetPixels(pixels); texture.Apply(); File.WriteAllBytes(path, texture.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
            importer.filterMode = FilterMode.Point; importer.mipmapEnabled = false; importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed; importer.npotScale = TextureImporterNPOTScale.None;
            importer.spritePixelsPerUnit = 100; importer.SaveAndReimport();
        }
    }
}
