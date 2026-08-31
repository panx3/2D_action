using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.U2D;

public static class TitleSceneBuilder
{
    public const string TitleScenePath = "Assets/Scenes/TitleScene.unity";
    public const string StageScenePath = "Assets/Scenes/CompletScene.unity";
    public const string BuildRequestPath = "Temp/TitleSceneBuild.request";
    public const string PreviewPath = "Temp/TitleScreenPreview.png";

    private const string HeroPath = "Assets/image_/立ち絵(sprite).png";
    private const string BallPath = "Assets/image_/鉄球.png";
    private const string ChainPath = "Assets/image_/鎖.png";
    private const string ShadowPath = "Assets/Pixel Adventure 1/Assets/Other/Shadow.png";
    private const string FarBackgroundPath = "Assets/image_/背景_一番後ろ.png";
    private const string MidBackgroundPath = "Assets/image_/背景_真ん中.png";
    private const string NearBackgroundPath = "Assets/image_/背景_一番手前.png";
    private const string BoldFontPath = "Assets/Fonts/NotoSansJP-Bold SDF.asset";
    private const string LatinFontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
    private const string BgmPath = "Assets/Audio/BGM/Peritune_Winds_Embrace.ogg";
    private const string LaunchSfxPath = "Assets/Audio/SFX/tekkyu_launch.wav";
    private const string ChainSfxPath = "Assets/Audio/SFX/Imported/打撃6.mp3";
    private const string ControlsPanelPrefabPath = "Assets/Prefabs/UI/ControlsPanel.prefab";

    private static readonly Color IronBlack = new Color32(23, 28, 42, 245);
    private static readonly Color IronMid = new Color32(48, 58, 78, 245);
    private static readonly Color IronLight = new Color32(96, 111, 135, 255);
    private static readonly Color Pink = new Color32(255, 74, 181, 255);
    private static readonly Color PinkSoft = new Color32(255, 142, 211, 255);
    private static readonly Color Ivory = new Color32(255, 249, 226, 255);

    [MenuItem("鉄球少女/Title/タイトルSceneを再生成")]
    public static void Build()
    {
        EditorBuildSettingsScene[] currentBuildScenes = EditorBuildSettings.scenes;
        if (!File.Exists(StageScenePath))
            throw new FileNotFoundException("完成Stage Sceneが見つかりません。", StageScenePath);

        ConfigurePixelArtTexture(BallPath);
        ValidateAssets();

        Scene previousActive = SceneManager.GetActiveScene();
        Scene titleScene = default;
        Scene loadedTitle = SceneManager.GetSceneByPath(TitleScenePath);
        bool keepGeneratedTitleOpen = loadedTitle.IsValid() && loadedTitle.isLoaded && loadedTitle == previousActive;
        try
        {
            titleScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            if (loadedTitle.IsValid() && loadedTitle.isLoaded)
            {
                if (loadedTitle.isDirty)
                    throw new InvalidOperationException("開いているTitleSceneに未保存変更があるため、再生成を中止しました。");
                EditorSceneManager.CloseScene(loadedTitle, true);
            }

            SceneManager.SetActiveScene(titleScene);
            CreateTitleScene(Path.GetFileNameWithoutExtension(StageScenePath));

            if (!EditorSceneManager.SaveScene(titleScene, TitleScenePath))
                throw new InvalidOperationException("TitleSceneを保存できませんでした。");

            UpdateBuildSettings(currentBuildScenes);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            RenderStaticPreview(titleScene);
            EditorSceneManager.SaveScene(titleScene, TitleScenePath);
            Debug.Log($"[TitleSceneBuilder] TitleSceneを生成しました。Stage={StageScenePath}, Preview={PreviewPath}");
        }
        finally
        {
            if (keepGeneratedTitleOpen && titleScene.IsValid() && titleScene.isLoaded && !string.IsNullOrEmpty(titleScene.path))
                SceneManager.SetActiveScene(titleScene);
            else if (titleScene.IsValid() && titleScene.isLoaded)
                EditorSceneManager.CloseScene(titleScene, true);
            if (previousActive.IsValid() && previousActive.isLoaded)
                SceneManager.SetActiveScene(previousActive);
        }
    }

    private static void CreateTitleScene(string stageSceneName)
    {
        Camera camera = CreateCamera();
        RectTransform canvasRoot = CreateCanvas(camera);

        GameObject controllerObject = new GameObject("TitleScreenController");
        TitleScreenController controller = controllerObject.AddComponent<TitleScreenController>();
        AudioSource bgmSource = controllerObject.AddComponent<AudioSource>();
        bgmSource.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(BgmPath);
        bgmSource.playOnAwake = true;
        bgmSource.loop = true;
        bgmSource.volume = 0.2f;
        bgmSource.spatialBlend = 0f;
        AudioSource sfxSource = controllerObject.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false;
        sfxSource.loop = false;
        sfxSource.spatialBlend = 0f;

        RectTransform sceneRoot = CreateRect("SceneRoot", canvasRoot, Vector2.zero, Vector2.zero);
        Stretch(sceneRoot);

        CanvasGroup backgroundGroup = CreateGroup("Background", sceneRoot, out RectTransform backgroundRoot);
        Stretch(backgroundRoot);
        RectTransform far = CreateBackgroundLayer(
            "Far_SkySea", backgroundRoot, AssetDatabase.LoadAssetAtPath<Texture2D>(FarBackgroundPath),
            new Vector2(3240f, 1080f), Vector2.zero, Color.white);
        RectTransform mid = CreateBackgroundLayer(
            "Middle_FloatingRuins", backgroundRoot, AssetDatabase.LoadAssetAtPath<Texture2D>(MidBackgroundPath),
            new Vector2(3260f, 1088f), new Vector2(0f, 22f), new Color(1f, 1f, 1f, 0.82f));
        RectTransform near = CreateBackgroundLayer(
            "Near_Ruins", backgroundRoot, AssetDatabase.LoadAssetAtPath<Texture2D>(NearBackgroundPath),
            new Vector2(3300f, 1100f), new Vector2(0f, -30f), new Color(1f, 1f, 1f, 0.87f));

        Image skyTint = CreateSolidImage("SkyTint", backgroundRoot, new Color32(75, 178, 245, 26));
        Stretch(skyTint.rectTransform);
        Image lowerContrast = CreateSolidImage("LowerContrast", backgroundRoot, new Color32(9, 17, 33, 55));
        SetAnchored(lowerContrast.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(1920f, 310f), new Vector2(0f, 155f));

        Image crystalAura = CreateSolidImage("CentralCrystalAura", sceneRoot, new Color32(255, 92, 207, 16));
        SetAnchored(crystalAura.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(82f, 82f), new Vector2(388f, 404f));
        crystalAura.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        Image crystalCore = CreateSolidImage("CentralCrystalPulse", sceneRoot, new Color32(255, 170, 230, 24));
        SetAnchored(crystalCore.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(22f, 62f), new Vector2(388f, 404f));
        crystalCore.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);

        CanvasGroup logoGroup = CreateGroup("TitleLogo", sceneRoot, out RectTransform logoRoot);
        Stretch(logoRoot);
        BuildLogo(logoRoot);

        CanvasGroup characterGroup = CreateGroup("TitleCharacterDisplay", sceneRoot, out RectTransform displayRoot);
        Stretch(displayRoot);
        CharacterDisplay character = BuildCharacterDisplay(displayRoot);

        CanvasGroup startGroup = CreateGroup("StartPanel", sceneRoot, out RectTransform startPanel);
        SetAnchored(startPanel, new Vector2(1f, 0f), new Vector2(1f, 0f),
            new Vector2(520f, 204f), new Vector2(-344f, 230f));
        StartPanelDisplay start = BuildStartPanel(startPanel, controller);
        TitleControlsDisplay controls = BuildTitleControls(canvasRoot, startPanel, controller);

        CanvasGroup flashGroup = CreateGroup("FlashPanel", canvasRoot, out RectTransform flashRoot);
        Stretch(flashRoot);
        flashGroup.alpha = 0f;
        Image flash = CreateSolidImage("Flash", flashRoot, new Color32(255, 236, 250, 255));
        Stretch(flash.rectTransform);
        flash.raycastTarget = false;

        CanvasGroup fadeGroup = CreateGroup("FadePanel", canvasRoot, out RectTransform fadeRoot);
        Stretch(fadeRoot);
        fadeGroup.alpha = 0f;
        Image fade = CreateSolidImage("Fade", fadeRoot, Color.black);
        Stretch(fade.rectTransform);
        fade.raycastTarget = false;

        CreateEventSystem(start.button);
        ConfigureController(
            controller,
            stageSceneName,
            backgroundGroup,
            logoGroup,
            characterGroup,
            startGroup,
            flashGroup,
            fadeGroup,
            sceneRoot,
            character.heroMotionRoot,
            character.ballRoot,
            startPanel,
            start.glow,
            start.button,
            controls.button,
            controls.panel,
            character.chain,
            new[] { far, mid, near },
            new Graphic[] { crystalAura, crystalCore, start.glowImage, start.electricTop, start.electricBottom },
            bgmSource,
            sfxSource);

        Canvas.ForceUpdateCanvases();
        character.chain.RefreshNow();
        Canvas.ForceUpdateCanvases();
    }

    private static Camera CreateCamera()
    {
        GameObject cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener), typeof(PixelPerfectCamera));
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.GetComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color32(54, 154, 225, 255);
        camera.orthographic = true;
        camera.orthographicSize = 5.625f;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 100f;
        camera.transform.position = new Vector3(0f, 0f, -10f);

        PixelPerfectCamera pixelPerfect = cameraObject.GetComponent<PixelPerfectCamera>();
        pixelPerfect.assetsPPU = 32;
        pixelPerfect.refResolutionX = 640;
        pixelPerfect.refResolutionY = 360;
        pixelPerfect.upscaleRT = true;
        pixelPerfect.pixelSnapping = true;
        pixelPerfect.cropFrameX = true;
        pixelPerfect.cropFrameY = true;
        return camera;
    }

    private static RectTransform CreateCanvas(Camera camera)
    {
        GameObject canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 1f;
        canvas.sortingOrder = 0;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        return canvasObject.GetComponent<RectTransform>();
    }

    private static void BuildLogo(RectTransform root)
    {
        Image plate = CreateSolidImage("LogoContrastPlate", root, new Color32(13, 25, 44, 120));
        SetAnchored(plate.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(760f, 310f), new Vector2(448f, -194f));

        Image accent = CreateSolidImage("PinkAccent", root, Pink);
        SetAnchored(accent.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(12f, 214f), new Vector2(83f, -184f));

        TMP_FontAsset bold = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BoldFontPath);
        TMP_FontAsset latin = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(LatinFontPath);
        TextMeshProUGUI shadow = CreateText("JapaneseTitleShadow", root, "鉄球少女", bold, 132f,
            new Color32(6, 11, 22, 220), TextAlignmentOptions.Center);
        SetAnchored(shadow.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(720f, 170f), new Vector2(443f, -145f));

        TextMeshProUGUI title = CreateText("JapaneseTitle", root, "鉄球少女", bold, 132f, Ivory, TextAlignmentOptions.Center);
        SetAnchored(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(720f, 170f), new Vector2(435f, -137f));
        title.fontStyle = FontStyles.Bold;
        title.outlineColor = new Color32(22, 29, 44, 255);
        title.outlineWidth = 0.2f;

        TextMeshProUGUI english = CreateText("EnglishTitle", root, "T E K K Y U   S H O J O", latin, 35f,
            new Color32(255, 218, 240, 255), TextAlignmentOptions.Center);
        SetAnchored(english.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(650f, 58f), new Vector2(430f, -245f));
        english.characterSpacing = 2f;

        Sprite chainSprite = LoadSprite(ChainPath, "鎖_0");
        Image chainRule = CreateSpriteImage("ChainRule", root, chainSprite, new Color32(220, 226, 235, 230), false);
        SetAnchored(chainRule.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(510f, 20f), new Vector2(440f, -288f));
        Image ballMark = CreateSpriteImage("BallMark", root, LoadSprite(BallPath, "鉄球_0"), Color.white, true);
        SetAnchored(ballMark.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(60f, 60f), new Vector2(725f, -288f));
    }

    private static CharacterDisplay BuildCharacterDisplay(RectTransform root)
    {
        Sprite shadowSprite = LoadSprite(ShadowPath);
        Sprite ballSprite = LoadSprite(BallPath, "鉄球_0");
        Sprite chainSprite = LoadSprite(ChainPath, "鎖_0");
        Sprite[] heroFrames = AssetDatabase.LoadAllAssetsAtPath(HeroPath)
            .OfType<Sprite>()
            .OrderBy(sprite => sprite.name, StringComparer.Ordinal)
            .ToArray();

        Image heroShadow = CreateSpriteImage("HeroShadow", root, shadowSprite, new Color32(7, 12, 20, 115), false);
        SetAnchored(heroShadow.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f),
            new Vector2(300f, 62f), new Vector2(372f, 150f));
        Image ballShadow = CreateSpriteImage("BallShadow", root, shadowSprite, new Color32(7, 12, 20, 145), false);
        SetAnchored(ballShadow.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f),
            new Vector2(390f, 84f), new Vector2(817f, 138f));

        RectTransform chainRoot = CreateRect("Chain", root, Vector2.zero, Vector2.zero);
        Stretch(chainRoot);

        RectTransform heroMotion = CreateRect("Hero", root, Vector2.zero, Vector2.zero);
        SetAnchored(heroMotion, Vector2.zero, Vector2.zero, new Vector2(340f, 360f), new Vector2(372f, 300f));
        RectTransform heroVisual = CreateRect("HeroVisual", heroMotion, Vector2.zero, new Vector2(340f, 340f));
        Image heroImage = CreateSpriteImage("HeroSprite", heroVisual, heroFrames.FirstOrDefault(), Color.white, true);
        Stretch(heroImage.rectTransform);
        heroImage.raycastTarget = false;

        TitleHeroIdleDisplay idle = heroVisual.gameObject.AddComponent<TitleHeroIdleDisplay>();
        SerializedObject idleSerialized = new SerializedObject(idle);
        idleSerialized.FindProperty("heroImage").objectReferenceValue = heroImage;
        SetObjectArray(idleSerialized.FindProperty("idleFrames"), heroFrames.Cast<UnityEngine.Object>().ToArray());
        idleSerialized.FindProperty("frameDuration").floatValue = 0.13f;
        idleSerialized.FindProperty("bobDistance").floatValue = 3f;
        idleSerialized.FindProperty("bobPeriod").floatValue = 2.2f;
        idleSerialized.FindProperty("breathingScale").floatValue = 0.012f;
        idleSerialized.ApplyModifiedPropertiesWithoutUndo();

        // Title用Player Spriteには左手から下へ伸びる棒が描き込まれている。
        // HeroHandAnchorは実際の手元、TitleChainStartはその棒先へ合わせる。
        // 別Rod Imageを重ねると二重表示になるため追加しない。
        Vector2 handPosition = new Vector2(-56f, -72f);
        Vector2 rodTipPosition = new Vector2(-90f, -111f);
        CreateRect("HeroHandAnchor", heroVisual, handPosition, new Vector2(12f, 12f));
        RectTransform chainStart = CreateRect("TitleChainStart", heroVisual, rodTipPosition, new Vector2(12f, 12f));

        RectTransform ballRoot = CreateRect("MorningStar", root, Vector2.zero, Vector2.zero);
        SetAnchored(ballRoot, Vector2.zero, Vector2.zero, new Vector2(310f, 310f), new Vector2(700f, 250f));
        Image ball = CreateSpriteImage("MorningStarSprite", ballRoot, ballSprite, Color.white, true);
        Stretch(ball.rectTransform);
        ball.raycastTarget = false;

        Image ballRim = CreateSpriteImage("MorningStarHighlight", ballRoot, ballSprite, new Color32(255, 179, 223, 35), true);
        Stretch(ballRim.rectTransform);
        ballRim.rectTransform.localScale = Vector3.one * 1.045f;
        ballRim.raycastTarget = false;
        ballRim.transform.SetAsFirstSibling();

        RectTransform ballAnchor = CreateRect("BallChainAnchor", ballRoot, new Vector2(-155f, 14f), new Vector2(12f, 12f));

        const int segmentCount = 18;
        RectTransform[] segments = new RectTransform[segmentCount];
        for (int i = 0; i < segmentCount; i++)
        {
            Image segment = CreateSpriteImage($"Link_{i:00}", chainRoot, chainSprite, new Color32(235, 239, 246, 255), false);
            segment.raycastTarget = false;
            segments[i] = segment.rectTransform;
        }

        TitleChainDisplay chain = chainRoot.gameObject.AddComponent<TitleChainDisplay>();
        SerializedObject chainSerialized = new SerializedObject(chain);
        chainSerialized.FindProperty("startAnchor").objectReferenceValue = chainStart;
        chainSerialized.FindProperty("endAnchor").objectReferenceValue = ballAnchor;
        SetObjectArray(chainSerialized.FindProperty("segments"), segments.Cast<UnityEngine.Object>().ToArray());
        chainSerialized.FindProperty("idleSag").floatValue = 46f;
        chainSerialized.FindProperty("thickness").floatValue = 15f;
        chainSerialized.FindProperty("idleSway").floatValue = 3f;
        chainSerialized.ApplyModifiedPropertiesWithoutUndo();

        return new CharacterDisplay
        {
            heroMotionRoot = heroMotion,
            ballRoot = ballRoot,
            chain = chain
        };
    }

    private static StartPanelDisplay BuildStartPanel(RectTransform root, TitleScreenController controller)
    {
        Image glow = CreateSolidImage("PinkOuterGlow", root, new Color32(255, 55, 181, 62));
        StretchWithMargin(glow.rectTransform, -20f);
        glow.raycastTarget = false;

        Image outer = CreateSolidImage("OuterFrame", root, IronBlack);
        Stretch(outer.rectTransform);
        outer.raycastTarget = true;
        Image rim = CreateSolidImage("SteelRim", outer.rectTransform, IronLight);
        StretchWithMargin(rim.rectTransform, 8f);
        Image inner = CreateSolidImage("InnerPanel", rim.rectTransform, IronMid);
        StretchWithMargin(inner.rectTransform, 5f);
        Image darkFace = CreateSolidImage("DarkFace", inner.rectTransform, new Color32(18, 24, 38, 250));
        StretchWithMargin(darkFace.rectTransform, 7f);

        Image leftCrystal = CreateSolidImage("PinkCrystal", root, Pink);
        SetAnchored(leftCrystal.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(28f, 66f), new Vector2(28f, 0f));
        leftCrystal.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        Image electricTop = CreateSolidImage("ElectricTop", root, PinkSoft);
        SetAnchored(electricTop.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(390f, 5f), new Vector2(0f, -20f));
        Image electricBottom = CreateSolidImage("ElectricBottom", root, Pink);
        SetAnchored(electricBottom.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(310f, 4f), new Vector2(0f, 18f));

        CreateBolt(root, new Vector2(20f, 78f));
        CreateBolt(root, new Vector2(500f, 78f));
        CreateBolt(root, new Vector2(20f, -78f));
        CreateBolt(root, new Vector2(500f, -78f));

        TMP_FontAsset latin = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(LatinFontPath);
        TextMeshProUGUI startText = CreateText("START", root, "START", latin, 72f, Ivory, TextAlignmentOptions.Center);
        SetAnchored(startText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(420f, 92f), new Vector2(14f, 22f));
        startText.fontStyle = FontStyles.Bold;
        startText.characterSpacing = 5f;
        startText.outlineColor = new Color32(9, 12, 20, 255);
        startText.outlineWidth = 0.14f;

        TextMeshProUGUI hint = CreateText("InputHint", root, "A  /  ENTER  /  CLICK", latin, 25f,
            new Color32(255, 175, 221, 255), TextAlignmentOptions.Center);
        SetAnchored(hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(430f, 46f), new Vector2(14f, 38f));
        hint.characterSpacing = 2f;

        Button button = root.gameObject.AddComponent<Button>();
        button.targetGraphic = outer;
        button.transition = Selectable.Transition.None;
        button.navigation = new Navigation { mode = Navigation.Mode.Automatic };

        return new StartPanelDisplay
        {
            button = button,
            glow = glow.rectTransform,
            glowImage = glow,
            electricTop = electricTop,
            electricBottom = electricBottom
        };
    }

    private static TitleControlsDisplay BuildTitleControls(
        RectTransform canvasRoot,
        RectTransform startPanel,
        TitleScreenController controller)
    {
        RectTransform buttonRoot = CreateRect(
            "ControlsButton",
            startPanel,
            new Vector2(0f, -150f),
            new Vector2(390f, 72f));

        Image glow = CreateSolidImage("PinkGlow", buttonRoot, new Color32(255, 55, 181, 58));
        StretchWithMargin(glow.rectTransform, -9f);
        Image outer = CreateSolidImage("OuterFrame", buttonRoot, IronBlack);
        Stretch(outer.rectTransform);
        outer.raycastTarget = true;
        Image rim = CreateSolidImage("SteelRim", outer.rectTransform, IronLight);
        StretchWithMargin(rim.rectTransform, 5f);
        Image inner = CreateSolidImage("InnerPanel", rim.rectTransform, IronMid);
        StretchWithMargin(inner.rectTransform, 4f);

        TextMeshProUGUI label = CreateText(
            "Label",
            buttonRoot,
            "操作方法",
            AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BoldFontPath),
            31f,
            Ivory,
            TextAlignmentOptions.Center);
        Stretch(label.rectTransform);
        label.outlineColor = new Color32(9, 12, 20, 255);
        label.outlineWidth = 0.12f;

        Button button = buttonRoot.gameObject.AddComponent<Button>();
        button.targetGraphic = outer;
        button.transition = Selectable.Transition.ColorTint;
        button.navigation = new Navigation { mode = Navigation.Mode.Automatic };

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ControlsPanelPrefabPath);
        if (prefab == null)
            throw new FileNotFoundException("共通ControlsPanel Prefabが見つかりません。", ControlsPanelPrefabPath);

        GameObject panelObject = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
        if (panelObject == null)
            throw new InvalidOperationException("ControlsPanel PrefabをTitleSceneへ配置できませんでした。");
        panelObject.name = "ControlsPanel";
        RectTransform panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.SetParent(canvasRoot, false);
        Stretch(panelRect);
        ControlsPanelController panel = panelObject.GetComponent<ControlsPanelController>();
        panelObject.SetActive(false);

        return new TitleControlsDisplay { button = button, panel = panel };
    }

    private static void CreateBolt(RectTransform root, Vector2 position)
    {
        Image bolt = CreateSolidImage("Bolt", root, new Color32(176, 190, 208, 255));
        SetAnchored(bolt.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(13f, 13f), position);
        bolt.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        bolt.raycastTarget = false;
    }

    private static void ConfigureController(
        TitleScreenController controller,
        string stageSceneName,
        CanvasGroup backgroundGroup,
        CanvasGroup logoGroup,
        CanvasGroup characterGroup,
        CanvasGroup startGroup,
        CanvasGroup flashGroup,
        CanvasGroup fadeGroup,
        RectTransform sceneRoot,
        RectTransform heroRoot,
        RectTransform ballRoot,
        RectTransform startPanel,
        RectTransform startGlow,
        Button startButton,
        Button controlsButton,
        ControlsPanelController controlsPanel,
        TitleChainDisplay chain,
        RectTransform[] backgrounds,
        Graphic[] ambientGraphics,
        AudioSource bgmSource,
        AudioSource sfxSource)
    {
        SerializedObject serialized = new SerializedObject(controller);
        serialized.FindProperty("stageSceneName").stringValue = stageSceneName;
        serialized.FindProperty("backgroundGroup").objectReferenceValue = backgroundGroup;
        serialized.FindProperty("logoGroup").objectReferenceValue = logoGroup;
        serialized.FindProperty("characterGroup").objectReferenceValue = characterGroup;
        serialized.FindProperty("startGroup").objectReferenceValue = startGroup;
        serialized.FindProperty("flashGroup").objectReferenceValue = flashGroup;
        serialized.FindProperty("fadeGroup").objectReferenceValue = fadeGroup;
        serialized.FindProperty("sceneRoot").objectReferenceValue = sceneRoot;
        serialized.FindProperty("heroMotionRoot").objectReferenceValue = heroRoot;
        serialized.FindProperty("ballRoot").objectReferenceValue = ballRoot;
        serialized.FindProperty("startPanel").objectReferenceValue = startPanel;
        serialized.FindProperty("startGlow").objectReferenceValue = startGlow;
        serialized.FindProperty("startButton").objectReferenceValue = startButton;
        serialized.FindProperty("controlsButton").objectReferenceValue = controlsButton;
        serialized.FindProperty("controlsPanel").objectReferenceValue = controlsPanel;
        serialized.FindProperty("chainDisplay").objectReferenceValue = chain;
        SetObjectArray(serialized.FindProperty("backgroundLayers"), backgrounds.Cast<UnityEngine.Object>().ToArray());
        SetObjectArray(serialized.FindProperty("ambientGlowGraphics"), ambientGraphics.Cast<UnityEngine.Object>().ToArray());
        serialized.FindProperty("loadingBallSprite").objectReferenceValue = LoadSprite(BallPath, "鉄球_0");
        serialized.FindProperty("loadingFont").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BoldFontPath);
        serialized.FindProperty("fadeOutDuration").floatValue = 0.25f;
        serialized.FindProperty("minimumLoadingDuration").floatValue = 0.6f;
        serialized.FindProperty("loadingFadeOutDuration").floatValue = 0.18f;
        serialized.FindProperty("stageFadeInDuration").floatValue = 0.3f;
        serialized.FindProperty("loadingRotationSpeed").floatValue = 220f;
        serialized.FindProperty("bgmSource").objectReferenceValue = bgmSource;
        serialized.FindProperty("sfxSource").objectReferenceValue = sfxSource;
        serialized.FindProperty("startConfirmClip").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(LaunchSfxPath);
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void CreateEventSystem(Button firstSelected)
    {
        GameObject eventSystemObject = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        EventSystem eventSystem = eventSystemObject.GetComponent<EventSystem>();
        eventSystem.firstSelectedGameObject = firstSelected != null ? firstSelected.gameObject : null;
        InputSystemUIInputModule module = eventSystemObject.GetComponent<InputSystemUIInputModule>();
        MethodInfo assignDefaults = typeof(InputSystemUIInputModule).GetMethod(
            "AssignDefaultActions", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        assignDefaults?.Invoke(module, null);
    }

    private static RectTransform CreateBackgroundLayer(
        string name,
        RectTransform parent,
        Texture2D texture,
        Vector2 size,
        Vector2 position,
        Color color)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        RectTransform rect = gameObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        SetAnchored(rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), size, position);
        RawImage image = gameObject.GetComponent<RawImage>();
        image.texture = texture;
        image.color = color;
        image.raycastTarget = false;
        return rect;
    }

    private static CanvasGroup CreateGroup(string name, RectTransform parent, out RectTransform rect)
    {
        rect = CreateRect(name, parent, Vector2.zero, Vector2.zero);
        CanvasGroup group = rect.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 1f;
        group.interactable = true;
        group.blocksRaycasts = true;
        return group;
    }

    private static RectTransform CreateRect(string name, RectTransform parent, Vector2 anchoredPosition, Vector2 size)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform));
        RectTransform rect = gameObject.GetComponent<RectTransform>();
        if (parent != null)
            rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
        return rect;
    }

    private static Image CreateSolidImage(string name, RectTransform parent, Color color)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rect = gameObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        Image image = gameObject.GetComponent<Image>();
        image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        image.type = image.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static Image CreateSpriteImage(string name, RectTransform parent, Sprite sprite, Color color, bool preserveAspect)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rect = gameObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        Image image = gameObject.GetComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.preserveAspect = preserveAspect;
        image.useSpriteMesh = false;
        image.raycastTarget = false;
        return image;
    }

    private static TextMeshProUGUI CreateText(
        string name,
        RectTransform parent,
        string value,
        TMP_FontAsset font,
        float size,
        Color color,
        TextAlignmentOptions alignment)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        RectTransform rect = gameObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        TextMeshProUGUI text = gameObject.GetComponent<TextMeshProUGUI>();
        text.text = value;
        text.font = font;
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        return text;
    }

    private static Sprite LoadSprite(string path, string preferredName = null)
    {
        Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToArray();
        if (!string.IsNullOrWhiteSpace(preferredName))
        {
            Sprite preferred = sprites.FirstOrDefault(sprite => sprite.name == preferredName);
            if (preferred != null)
                return preferred;
        }

        Sprite direct = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (direct != null)
            return direct;
        return sprites.OrderByDescending(sprite => sprite.rect.width * sprite.rect.height).FirstOrDefault();
    }

    private static void SetAnchored(
        RectTransform rect,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 size,
        Vector2 position)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        rect.localScale = Vector3.one;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
    }

    private static void StretchWithMargin(RectTransform rect, float margin)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(margin, margin);
        rect.offsetMax = new Vector2(-margin, -margin);
        rect.localScale = Vector3.one;
    }

    private static void SetObjectArray(SerializedProperty property, UnityEngine.Object[] values)
    {
        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    private static void ValidateAssets()
    {
        string[] requiredPaths =
        {
            HeroPath, BallPath, ChainPath, ShadowPath,
            FarBackgroundPath, MidBackgroundPath, NearBackgroundPath,
            BoldFontPath, LatinFontPath, BgmPath, LaunchSfxPath, ChainSfxPath,
            ControlsPanelPrefabPath
        };

        foreach (string path in requiredPaths)
        {
            if (AssetDatabase.LoadMainAssetAtPath(path) == null)
                throw new FileNotFoundException("タイトルで使用する既存Assetが見つかりません。", path);
        }
    }

    private static void ConfigurePixelArtTexture(string path)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
            return;

        bool changed = importer.filterMode != FilterMode.Point
            || importer.mipmapEnabled
            || importer.textureCompression != TextureImporterCompression.Uncompressed;
        if (!changed)
            return;

        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
    }

    private static void UpdateBuildSettings(EditorBuildSettingsScene[] currentScenes)
    {
        List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>
        {
            new EditorBuildSettingsScene(TitleScenePath, true)
        };
        scenes.Add(new EditorBuildSettingsScene(StageScenePath, true));
        scenes.AddRange(currentScenes.Where(scene =>
            !string.Equals(scene.path, TitleScenePath, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(scene.path, StageScenePath, StringComparison.OrdinalIgnoreCase)));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    private static void RenderStaticPreview(Scene scene)
    {
        Camera camera = scene.GetRootGameObjects()
            .Select(root => root.GetComponent<Camera>())
            .FirstOrDefault(candidate => candidate != null);
        if (camera == null)
            return;

        const int width = 1920;
        const int height = 1080;
        RenderTexture previousTarget = camera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGB24, false);
        try
        {
            Canvas.ForceUpdateCanvases();
            camera.targetTexture = renderTexture;
            RenderTexture.active = renderTexture;
            // ScreenSpaceCamera Canvasは最初の描画で最終座標が確定する。
            // その後に鎖を再配置してから本番プレビューを描画する。
            camera.Render();
            foreach (TitleChainDisplay chain in scene.GetRootGameObjects()
                         .SelectMany(root => root.GetComponentsInChildren<TitleChainDisplay>(true)))
                chain.RefreshNow();
            Canvas.ForceUpdateCanvases();
            camera.Render();
            texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            texture.Apply();
            string fullPath = Path.GetFullPath(PreviewPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? "Temp");
            File.WriteAllBytes(fullPath, texture.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(renderTexture);
        }
    }

    private sealed class CharacterDisplay
    {
        public RectTransform heroMotionRoot;
        public RectTransform ballRoot;
        public TitleChainDisplay chain;
    }

    private sealed class StartPanelDisplay
    {
        public Button button;
        public RectTransform glow;
        public Image glowImage;
        public Image electricTop;
        public Image electricBottom;
    }

    private sealed class TitleControlsDisplay
    {
        public Button button;
        public ControlsPanelController panel;
    }
}

[InitializeOnLoad]
public static class TitleSceneBuildRequestRunner
{
    static TitleSceneBuildRequestRunner()
    {
        EditorApplication.delayCall += TryBuild;
    }

    private static void TryBuild()
    {
        string requestPath = Path.GetFullPath(TitleSceneBuilder.BuildRequestPath);
        if (!File.Exists(requestPath))
            return;

        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += TryBuild;
            return;
        }

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.isPlaying = false;
            return;
        }

        try
        {
            TitleSceneBuilder.Build();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
        finally
        {
            if (File.Exists(requestPath))
                File.Delete(requestPath);
        }
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode)
            return;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.delayCall += TryBuild;
    }
}
