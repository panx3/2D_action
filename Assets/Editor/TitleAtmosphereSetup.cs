using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;

/// <summary>Updates only the title's presentation, retaining its flow, audio and shared controls panel.</summary>
public static class TitleAtmosphereSetup
{
    public const string BackgroundPath = "Assets/image_/TitleSkyRuins.png";
    private const string LogoPath = "Assets/image_/Chモー娘。ロゴ_ドット.png";
    private const string MaterialPath = "Assets/TitleAtmosphere.mat";
    private const string JapaneseFontPath = "Assets/Fonts/Title UI Japanese.asset";

    [MenuItem("鉄球少女/Title/静かな空のタイトルを適用")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before installing.");
        Scene previous = SceneManager.GetActiveScene();
        Scene scene = SceneManager.GetSceneByPath(TitleSceneBuilder.TitleScenePath);
        bool alreadyOpen = scene.isLoaded;
        if (!alreadyOpen) scene = EditorSceneManager.OpenScene(TitleSceneBuilder.TitleScenePath, OpenSceneMode.Additive);
        if (scene.isDirty) throw new InvalidOperationException("TitleScene has unsaved edits. Save it before installing.");
        try
        {
            SceneManager.SetActiveScene(scene);
            Apply(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadAssetAtPath<Material>(MaterialPath));
        }
        finally
        {
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            if (!alreadyOpen) EditorSceneManager.CloseScene(scene, true);
        }
    }

    public static void Apply(Scene scene)
    {
        T Find<T>() where T : Component => scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true)).FirstOrDefault();
        var controller = Find<TitleScreenController>();
        var canvas = Find<Canvas>();
        var controls = Find<ControlsPanelController>();
        var camera = Find<Camera>();
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/Silkscreen CRT.asset");
        var japaneseFont = CreateJapaneseFont();
        var logo = AssetDatabase.LoadAllAssetsAtPath(LogoPath).OfType<Sprite>().OrderByDescending(s => s.rect.width * s.rect.height).FirstOrDefault();
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(BackgroundPath);
        var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/TitleAtmosphere.shader");
        if (controller == null || canvas == null || controls == null || camera == null || logo == null || texture == null || font == null || shader == null)
            throw new InvalidOperationException("Required title scene references or art assets are missing.");

        var importer = (TextureImporter)AssetImporter.GetAtPath(BackgroundPath);
        importer.textureType = TextureImporterType.Default;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 2048;
        importer.SaveAndReimport();
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, MaterialPath); }

        var serialized = new SerializedObject(controller);
        var root = (RectTransform)serialized.FindProperty("sceneRoot").objectReferenceValue;
        // These are presentation-only objects owned by the previous title composition.
        for (int i = root.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(root.GetChild(i).gameObject);
        foreach (string oldName in new[] { "Title", "Title background" })
        {
            var old = canvas.transform.Find(oldName);
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
        }
        var filters = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<CRTFilterController>(true)).ToArray();
        if (filters.Length == 0) filters = new[] { new GameObject("CRT_Filter").AddComponent<CRTFilterController>() };
        foreach (var filter in filters)
        {
            var crt = new SerializedObject(filter);
            crt.FindProperty("crtEnabled").boolValue = true;
            crt.FindProperty("masterStrength").floatValue = .88f;
            crt.FindProperty("scanlineStrength").floatValue = .95f;
            crt.FindProperty("noiseStrength").floatValue = .18f;
            crt.FindProperty("vignetteStrength").floatValue = .5f;
            crt.FindProperty("chromaticStrength").floatValue = .24f;
            crt.FindProperty("contrastStrength").floatValue = 0f;
            crt.FindProperty("targetCamera").objectReferenceValue = camera;
            crt.FindProperty("crtShader").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Shader>("Assets/CRTFilter.shader");
            crt.ApplyModifiedPropertiesWithoutUndo();
            filter.enabled = true;
        }
        var pixelCamera = camera.GetComponent<UnityEngine.U2D.PixelPerfectCamera>();
        if (pixelCamera != null) pixelCamera.enabled = false;
        camera.targetTexture = null;
        camera.rect = new Rect(0, 0, 1, 1);
        camera.backgroundColor = new Color(0.025f, 0.05f, 0.09f);
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 1f;
        var scaler = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.Expand;

        var backgroundRoot = Rect("Background", root); Stretch(backgroundRoot);
        var backgroundGroup = backgroundRoot.gameObject.AddComponent<CanvasGroup>();
        var art = Rect("SkyRuins", backgroundRoot); Stretch(art);
        var raw = art.gameObject.AddComponent<UnityEngine.UI.RawImage>();
        raw.texture = AssetDatabase.LoadAssetAtPath<Texture2D>(BackgroundPath);
        raw.material = material;
        raw.raycastTarget = false;
        var particles = Rect("DriftingLightAndPetals", backgroundRoot); Stretch(particles);
        var atmosphere = particles.gameObject.AddComponent<TitleAtmosphere>();
        atmosphere.raycastTarget = false;
        AtmosphereEffectsSetup.ConfigureTitle(atmosphere);
        Set(atmosphere, "background", raw);

        // The design remains 16:9 inside wider/taller windows, while the painting fills the frame.
        var composition = Rect("Composition", root);
        var compositionGroup = composition.gameObject.AddComponent<CanvasGroup>();
        var fitter = composition.gameObject.AddComponent<UnityEngine.UI.AspectRatioFitter>();
        fitter.aspectMode = UnityEngine.UI.AspectRatioFitter.AspectMode.FitInParent;
        fitter.aspectRatio = 16f / 9f;
        var logoRoot = Rect("TitleLogo", composition, new Vector2(.724f, .755f), new Vector2(760, 486));
        var logoGroup = logoRoot.gameObject.AddComponent<CanvasGroup>();
        var logoImage = logoRoot.gameObject.AddComponent<UnityEngine.UI.Image>();
        logoImage.sprite = logo; logoImage.preserveAspect = true; logoImage.raycastTarget = false;

        var menu = Rect("TitleMenu", composition, new Vector2(.745f, .295f), new Vector2(480, 272));
        var menuGroup = menu.gameObject.AddComponent<CanvasGroup>();
        var start = MenuButton("Start", "START", "冒険をはじめる", "01", menu, 88, font, japaneseFont, controller);
        var how = MenuButton("Controls", "CONTROLS", "操作方法", "02", menu, 0, font, japaneseFont, controller);
        var exit = MenuButton("Exit", "EXIT", "ゲームを終了", "03", menu, -88, font, japaneseFont, controller);
        var caption = Text("MenuCaption", composition, "SELECT YOUR JOURNEY", font, 15);
        Place(caption.rectTransform, new Vector2(.745f, .47f), new Vector2(650, 30));
        caption.characterSpacing = 6; caption.color = new Color(.88f, .93f, 1f, .8f);
        var hint = Text("InputHint", composition, "D-PAD / ARROWS   SELECT      A / ENTER   OK", font, 15);
        Place(hint.rectTransform, new Vector2(.745f, .105f), new Vector2(680, 30));
        hint.color = new Color(.88f, .93f, 1f, 1f);
        var prompt = Text("PressStart", composition, "PRESS START", font, 18);
        Place(prompt.rectTransform, new Vector2(.745f, .067f), new Vector2(500, 25));
        prompt.characterSpacing = 10; prompt.color = new Color(1f, .75f, .85f, .8f);
        Set(atmosphere, "idlePrompt", prompt);
        var foot = Text("Footer", composition, "MORNING STAR GIRL", font, 12);
        Place(foot.rectTransform, new Vector2(.745f, .031f), new Vector2(650, 24));
        foot.characterSpacing = 8; foot.color = new Color(.2f, .3f, .4f, 1f);
        CornerMarks(composition);

        var previousControls = controls;
        controls = BuildControls(canvas.transform, font, japaneseFont, controller);

        serialized.Update();
        SetReference(serialized, "backgroundGroup", backgroundGroup);
        SetReference(serialized, "logoGroup", logoGroup);
        SetReference(serialized, "startGroup", menuGroup);
        SetReference(serialized, "startPanel", menu);
        SetReference(serialized, "startButton", start);
        SetReference(serialized, "controlsButton", how);
        SetReference(serialized, "controlsPanel", controls);
        SetReference(serialized, "exitButton", exit);
        SetReference(serialized, "atmosphere", atmosphere);
        SetReference(serialized, "titleCompositionGroup", compositionGroup);
        SetReference(serialized, "transitionShader", AssetDatabase.LoadAssetAtPath<Shader>("Assets/TitleDotWipe.shader"));
        SetReference(serialized, "loadingBallSprite", AssetDatabase.LoadAssetAtPath<Sprite>("Assets/image_/TitleLoadingSilhouette.png"));
        SetReference(serialized, "loadingFont", font);
        foreach (string field in new[] { "characterGroup", "heroMotionRoot", "ballRoot", "startGlow", "chainDisplay" }) SetReference(serialized, field, null);
        serialized.FindProperty("backgroundLayers").arraySize = 0;
        serialized.FindProperty("ambientGlowGraphics").arraySize = 0;
        serialized.FindProperty("startPulseScale").floatValue = 0;
        serialized.FindProperty("backgroundDrift").floatValue = 0;
        serialized.FindProperty("startConfirmVolume").floatValue = .22f;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        var eventSystem = Find<EventSystem>();
        if (eventSystem != null) eventSystem.firstSelectedGameObject = start.gameObject;

        UnityEngine.Object.DestroyImmediate(previousControls.gameObject);
        controls.gameObject.SetActive(false);
        ApplyQuietPresentation(controller);
        Canvas.ForceUpdateCanvases();
        EditorSceneManager.MarkSceneDirty(scene);
    }

    /// <summary>Refines existing objects in place, preserving button events and scene ownership.</summary>
    public static void ApplyQuietPresentation(TitleScreenController controller)
    {
        var serif = CreateEtherealFont();
        var serialized = new SerializedObject(controller);
        var menu = (RectTransform)serialized.FindProperty("startPanel").objectReferenceValue;
        var composition = ((CanvasGroup)serialized.FindProperty("titleCompositionGroup").objectReferenceValue).transform;
        menu.sizeDelta = new Vector2(380, 240);
        int row = 0;
        foreach (var item in menu.GetComponentsInChildren<TitleMenuItem>(true))
        {
            var rect = (RectTransform)item.transform;
            rect.sizeDelta = new Vector2(380, 64); rect.anchoredPosition = new Vector2(0, 80 - row++ * 80);
            QuietButton(item, serif);
        }
        var controls = (ControlsPanelController)serialized.FindProperty("controlsPanel").objectReferenceValue;
        foreach (var item in controls.GetComponentsInChildren<TitleMenuItem>(true)) QuietButton(item, serif);
        foreach (Transform child in composition)
            if (child.name == "MenuCaption" || child.name == "PressStart" || child.name == "Footer" || child.name == "MonitorCorner")
                child.gameObject.SetActive(false);
        var hint = composition.Find("InputHint").GetComponent<TMP_Text>();
        hint.text = "D-PAD / ARROWS     A / ENTER"; hint.font = serif; hint.fontSize = 18; hint.characterSpacing = 5;
        hint.color = new Color(.84f, .86f, .88f, .6f);
        Place(hint.rectTransform, new Vector2(.745f, .11f), new Vector2(560, 30));
        var atmosphere = (TitleAtmosphere)serialized.FindProperty("atmosphere").objectReferenceValue;
        Set(atmosphere, "idlePrompt", null);
        SetReference(serialized, "loadingFont", serif);
        SetReference(serialized, "loadingBackdrop", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/image_/TitleInterludeRuins.png"));
        var profile = serialized.FindProperty("transitionSettings");
        profile.FindPropertyRelative("minimumLoadingDuration").floatValue = 1.8f;
        profile.FindPropertyRelative("loadingUiFadeDuration").floatValue = .18f;
        profile.FindPropertyRelative("revealDirection").enumValueIndex = (int)TitleStageTransition.WipeDirection.LeftToRight;
        profile.FindPropertyRelative("pendulumPeriod").floatValue = 2.15f;
        profile.FindPropertyRelative("pendulumAmplitude").floatValue = 18f;
        profile.FindPropertyRelative("patternStrength").floatValue = .85f;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
    }

    private static void QuietButton(TitleMenuItem item, TMP_FontAsset font)
    {
        foreach (string name in new[] { "Description", "Number", "SelectionFill", "SelectionLine" })
        {
            var old = item.transform.Find(name);
            if (old != null) old.gameObject.SetActive(false);
        }
        var label = item.transform.Find("Label").GetComponent<TMP_Text>();
        label.font = font; label.fontSize = 34; label.characterSpacing = 10; label.margin = Vector4.zero;
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(.80f, .83f, .86f);
        Stretch(label.rectTransform);
        var cursor = (RectTransform)item.transform.Find("Cursor");
        cursor.anchoredPosition = new Vector2(-154, 0); cursor.sizeDelta = new Vector2(10, 1.5f);
        cursor.localRotation = Quaternion.identity; cursor.localScale = Vector3.one;
        cursor.GetComponent<UnityEngine.UI.Image>().color = new Color(.94f, .93f, .88f, 0);
    }

    private static TMP_FontAsset CreateEtherealFont()
    {
        const string path = "Assets/Fonts/Title Ethereal Latin.asset";
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (font != null) return font;
        var source = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/Spectral-Light.ttf");
        if (source == null) throw new InvalidOperationException("Spectral Light is missing.");
        font = TMP_FontAsset.CreateFontAsset(source, 64, 6, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic);
        font.name = "Title Ethereal Latin";
        font.TryAddCharacters(new string(Enumerable.Range(32, 95).Select(c => (char)c).ToArray()), out string missing);
        if (!string.IsNullOrEmpty(missing)) throw new InvalidOperationException("Latin font is missing glyphs: " + missing);
        font.atlasPopulationMode = AtlasPopulationMode.Static;
        AssetDatabase.CreateAsset(font, path);
        foreach (var texture in font.atlasTextures)
        { texture.filterMode = FilterMode.Bilinear; AssetDatabase.AddObjectToAsset(texture, font); }
        AssetDatabase.AddObjectToAsset(font.material, font);
        AssetDatabase.SaveAssetIfDirty(font);
        return font;
    }

    private static ControlsPanelController BuildControls(Transform parent, TMP_FontAsset font, TMP_FontAsset jp, TitleScreenController owner)
    {
        var root = Rect("ControlsPanel", parent); Stretch(root);
        root.gameObject.AddComponent<CanvasGroup>();
        var panel = root.gameObject.AddComponent<ControlsPanelController>();
        var scrim = root.gameObject.AddComponent<UnityEngine.UI.Image>();
        scrim.color = new Color(.055f, .09f, .14f, 1f); scrim.raycastTarget = true;
        var fit = Rect("GuideFrame", root);
        var aspect = fit.gameObject.AddComponent<UnityEngine.UI.AspectRatioFitter>();
        aspect.aspectMode = UnityEngine.UI.AspectRatioFitter.AspectMode.FitInParent; aspect.aspectRatio = 16f / 9f;
        var content = Rect("GuideContent", fit, null, new Vector2(1540, 920));
        Solid("DiagramSurface", content, new Vector2(-481, 5), new Vector2(464, 610), new Color(.08f, .12f, .18f, .65f));
        Solid("TableSurface", content, new Vector2(250, 0), new Vector2(950, 564), new Color(.07f, .11f, .17f, .7f));
        var header = AtText("Header", content, "HOW TO PLAY", font, 42, new Vector2(-400, 385), new Vector2(620, 60));
        header.alignment = TextAlignmentOptions.MidlineLeft; header.characterSpacing = 5;
        var jpTitle = AtText("JapaneseTitle", content, "操作方法", jp, 27, new Vector2(574, 382), new Vector2(300, 48));
        jpTitle.alignment = TextAlignmentOptions.MidlineRight; jpTitle.color = new Color(.74f, .82f, .91f);
        Solid("HeaderRule", content, new Vector2(0, 337), new Vector2(1440, 2), new Color(.75f, .65f, .78f, .6f));
        Solid("ColumnRule", content, new Vector2(-222, 10), new Vector2(1, 570), new Color(.56f, .7f, .86f, .25f));

        var lead = AtText("Lead", content, "THROW.\nFOLLOW.\nFLY.", font, 46, new Vector2(-481, 205), new Vector2(430, 188));
        lead.alignment = TextAlignmentOptions.TopLeft; lead.lineSpacing = 8;
        lead.color = new Color(.94f, .91f, .91f);
        var diagramRoot = Rect("LaunchDiagram", content, null, new Vector2(430, 270));
        diagramRoot.anchoredPosition = new Vector2(-481, -58);
        var diagram = diagramRoot.gameObject.AddComponent<TitleControlsDiagram>(); diagram.raycastTarget = false;
        var girl = Sprite("Girl", diagramRoot, "Assets/image_/立ち絵(sprite).png", new Vector2(92, 128));
        var ball = Sprite("Ball", diagramRoot, "Assets/image_/鉄球.png", new Vector2(56, 56));
        var phase = AtText("ExamplePhase", content, "01  AIM", font, 20, new Vector2(-481, -237), new Vector2(430, 32));
        phase.alignment = TextAlignmentOptions.MidlineLeft; phase.color = new Color(1f, .73f, .83f);
        Set(diagram, "girl", girl.rectTransform); Set(diagram, "ball", ball.rectTransform); Set(diagram, "stepLabel", phase);
        var tip = AtText("Tip", content, "鉄球を投げて、空を渡る。\n鎖が張ると、少女が鉄球に引かれます。", jp, 22, new Vector2(-481, -305), new Vector2(440, 90));
        tip.alignment = TextAlignmentOptions.TopLeft; tip.textWrappingMode = TextWrappingModes.Normal;
        tip.color = new Color(.72f, .81f, .9f); tip.lineSpacing = 7;
        var padHeading = AtText("PadHeading", content, "GAMEPAD", font, 18, new Vector2(165, 282), new Vector2(250, 36));
        padHeading.color = new Color(1f, .74f, .84f);
        var keyboardHeading = AtText("KeyboardHeading", content, "KEYBOARD / MOUSE", font, 18, new Vector2(528, 282), new Vector2(360, 36));
        keyboardHeading.color = new Color(.73f, .84f, 1f);

        string[] actions = { "移動", "ジャンプ", "鉄球を投げる", "回転チャージ", "スイング", "鎖を離す", "ポーズ" };
        string[] pad = { "L STICK", "A", "R STICK", "R3  HOLD / RELEASE", "LB / LT  HOLD", "B", "MENU" };
        string[] keyboard = { "A / D", "SPACE", "LEFT CLICK", "LEFT CLICK  HOLD", "LEFT CLICK  HOLD", "RIGHT CLICK / B", "ESC" };
        string[] notes = { "左右に移動", "押す長さで高さを調整", "投げたい方向へ倒す / マウスで狙う", "長押しで溜めて、離すと投げる", "鉄球が引っ掛かっている時", "引っ掛かりを解除", "メニューを開く" };
        for (int i = 0; i < actions.Length; i++)
        {
            float y = 218 - i * 78;
            Solid("Rule_" + i, content, new Vector2(250, y - 41), new Vector2(930, 1), new Color(.5f, .65f, .79f, .18f));
            var action = AtText("Action_" + i, content, actions[i], jp, 22, new Vector2(-89, y + 5), new Vector2(235, 35));
            action.alignment = TextAlignmentOptions.MidlineLeft;
            var padKey = AtText("Gamepad_" + i, content, pad[i], font, 18, new Vector2(165, y + 8), new Vector2(315, 32));
            padKey.color = new Color(.96f, .85f, .9f);
            var keyboardKey = AtText("Keyboard_" + i, content, keyboard[i], font, 18, new Vector2(528, y + 8), new Vector2(365, 32));
            keyboardKey.color = new Color(.81f, .89f, 1f);
            var note = AtText("Note_" + i, content, notes[i], jp, 16, new Vector2(354, y - 20), new Vector2(700, 26));
            note.alignment = TextAlignmentOptions.MidlineLeft; note.color = new Color(.55f, .68f, .8f);
        }

        Solid("FooterRule", content, new Vector2(0, -365), new Vector2(1440, 1), new Color(.56f, .7f, .86f, .28f));
        var back = MenuButton("Back", "BACK", "タイトルへ戻る", "", content, 0, font, jp, owner);
        back.GetComponent<RectTransform>().anchoredPosition = new Vector2(480, -417);
        var backHint = AtText("BackHint", content, "B / ESC   BACK", font, 18, new Vector2(-480, -417), new Vector2(450, 40));
        backHint.alignment = TextAlignmentOptions.MidlineLeft; backHint.color = new Color(.7f, .8f, .9f);
        Set(panel, "backButton", back);
        root.gameObject.SetActive(false);
        return panel;
    }

    private static UnityEngine.UI.Button MenuButton(string name, string value, string subtitle, string index, RectTransform parent, float y, TMP_FontAsset font, TMP_FontAsset jp, TitleScreenController owner)
    {
        var rect = Rect(name, parent, new Vector2(.5f, .5f), new Vector2(480, 78));
        rect.anchoredPosition = new Vector2(0, y);
        var hit = rect.gameObject.AddComponent<UnityEngine.UI.Image>(); hit.color = Color.clear; hit.raycastTarget = true;
        var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = hit;
        button.transition = UnityEngine.UI.Selectable.Transition.None;
        var highlight = Solid("SelectionFill", rect, Vector2.zero, new Vector2(480, 78), Color.clear);
        var label = Text("Label", rect, value, font, 32); Stretch(label.rectTransform);
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.margin = new Vector4(70, 0, 0, 24); label.characterSpacing = 4;
        var secondary = AtText("Description", rect, subtitle, jp, 16, new Vector2(25, -21), new Vector2(390, 28));
        secondary.alignment = TextAlignmentOptions.MidlineLeft;
        var number = AtText("Number", rect, index, font, 15, new Vector2(198, 14), new Vector2(50, 25));
        var cursor = Solid("Cursor", rect, new Vector2(-210, 0), new Vector2(8, 8), new Color(1f, .7f, .83f));
        cursor.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
        var lineRect = Rect("SelectionLine", rect, new Vector2(0, 0), new Vector2(408, 1));
        lineRect.pivot = new Vector2(0, .5f); lineRect.anchoredPosition = new Vector2(70, 0);
        var line = lineRect.gameObject.AddComponent<UnityEngine.UI.Image>(); line.raycastTarget = false;
        var item = rect.gameObject.AddComponent<TitleMenuItem>();
        Set(item, "label", label); Set(item, "cursor", cursor); Set(item, "line", line); Set(item, "owner", owner);
        Set(item, "highlight", highlight); Set(item, "description", secondary); Set(item, "number", number);
        return button;
    }

    private static void CornerMarks(RectTransform parent)
    {
        foreach (Vector2 corner in new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) })
        {
            var mark = Rect("MonitorCorner", parent, corner, new Vector2(40, 40));
            mark.anchoredPosition = new Vector2(corner.x == 0 ? 38 : -38, corner.y == 0 ? 38 : -38);
            var tint = new Color(.86f, .9f, 1f, .3f);
            Solid("Horizontal", mark, new Vector2(corner.x == 0 ? 10 : -10, 0), new Vector2(22, 1), tint);
            Solid("Vertical", mark, new Vector2(0, corner.y == 0 ? 10 : -10), new Vector2(1, 22), tint);
        }
    }
    private static TMP_FontAsset CreateJapaneseFont()
    {
        var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(JapaneseFontPath);
        if (existing != null) return existing;
        var source = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/NotoSansJP-Regular.ttf");
        var font = TMP_FontAsset.CreateFontAsset(source, 32, 2, GlyphRenderMode.RASTER_HINTED, 1024, 1024, AtlasPopulationMode.Dynamic);
        font.name = "Title UI Japanese";
        const string japanese = "冒険をはじめる操作方法ゲームを終了タイトルへ戻る鉄球を投げて、空を渡る。鎖が張ると、少女が鉄球に引かれます。移動ジャンプ鉄球を投げる回転チャージスイング鎖を離すポーズ左右に移動押す長さで高さを調整投げたい方向へ倒す / マウスで狙う長押しで溜めて、離すと投げる鉄球が引っ掛かっている時引っ掛かりを解除メニューを開く";
        string characters = new string((japanese + new string(Enumerable.Range(32, 95).Select(c => (char)c).ToArray())).Distinct().ToArray());
        font.TryAddCharacters(characters, out string missing);
        if (!string.IsNullOrEmpty(missing)) throw new InvalidOperationException("Title font is missing glyphs: " + missing);
        font.atlasPopulationMode = AtlasPopulationMode.Static;
        AssetDatabase.CreateAsset(font, JapaneseFontPath);
        foreach (var texture in font.atlasTextures)
        { texture.filterMode = FilterMode.Point; AssetDatabase.AddObjectToAsset(texture, font); }
        AssetDatabase.AddObjectToAsset(font.material, font);
        AssetDatabase.SaveAssetIfDirty(font);
        return font;
    }
    private static UnityEngine.UI.Image Sprite(string name, Transform parent, string path, Vector2 size)
    {
        var image = Rect(name, parent, null, size).gameObject.AddComponent<UnityEngine.UI.Image>();
        image.sprite = AssetDatabase.LoadAllAssetsAtPath(path).OfType<UnityEngine.Sprite>().FirstOrDefault();
        image.preserveAspect = true; image.raycastTarget = false; return image;
    }
    private static UnityEngine.UI.Image Solid(string name, Transform parent, Vector2 position, Vector2 size, Color tint)
    {
        var image = Rect(name, parent, null, size).gameObject.AddComponent<UnityEngine.UI.Image>();
        image.rectTransform.anchoredPosition = position; image.color = tint; image.raycastTarget = false; return image;
    }
    private static TextMeshProUGUI AtText(string name, Transform parent, string value, TMP_FontAsset font, float size, Vector2 position, Vector2 bounds)
    {
        var text = Text(name, parent, value, font, size); text.rectTransform.sizeDelta = bounds;
        text.rectTransform.anchoredPosition = position; return text;
    }

    private static RectTransform Rect(string name, Transform parent, Vector2? anchor = null, Vector2? size = null)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); Place(rect, anchor ?? new Vector2(.5f, .5f), size ?? Vector2.zero); return rect;
    }
    private static void Place(RectTransform rect, Vector2 anchor, Vector2 size)
    { rect.anchorMin = rect.anchorMax = anchor; rect.pivot = new Vector2(.5f, .5f); rect.sizeDelta = size; rect.anchoredPosition = Vector2.zero; }
    private static void Stretch(RectTransform rect)
    { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
    private static TextMeshProUGUI Text(string name, Transform parent, string value, TMP_FontAsset font, float size)
    {
        var text = Rect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        text.text = value; text.font = font; text.fontSize = size; text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center; text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false; return text;
    }
    private static void Set(UnityEngine.Object target, string field, UnityEngine.Object value)
    { var so = new SerializedObject(target); SetReference(so, field, value); so.ApplyModifiedPropertiesWithoutUndo(); }
    private static void SetReference(SerializedObject so, string field, UnityEngine.Object value) => so.FindProperty(field).objectReferenceValue = value;
}
