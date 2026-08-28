using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class RecoveryAndUiIntegrator
{
    // Keeps generated Scene/Prefab values aligned with the current production specification.
    // Asset生成はEditorのdomain reload後にrequest runnerから実行する。
    public const string RequestPath = "Temp/RecoveryAndUiIntegration.request";
    public const string UiInteractionRequestMode = "ui-interaction";

    private const string CompletScenePath = "Assets/Scenes/CompletScene.unity";
    private const string ControlsPrefabPath = "Assets/Prefabs/UI/ControlsPanel.prefab";
    private const string PausePrefabPath = "Assets/Prefabs/UI/PauseMenu.prefab";
    private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";
    private const string EnemyIdleClipPath = "Assets/Animation/EnemyIdleVisual.anim";
    private const string EnemyWalkClipPath = "Assets/Animation/EnemyWalkVisual.anim";
    private const string EnemyControllerPath = "Assets/Animation/EnemyVisual.controller";
    private const string TekkyuEnemyPrefabPath = "Assets/Prefabs/Enemies/TekkyuEnemy.prefab";
    private const string BoldFontPath = "Assets/Fonts/NotoSansJP-Bold SDF.asset";
    private const string RegularFontPath = "Assets/Fonts/NotoSansJP-Regular SDF.asset";
    private const string ChainSpritePath = "Assets/image_/鎖.png";
    private const string StoneTexturePath = "Assets/image_/ステージ地面素材めっちゃ石.png";

    private static readonly Color IronBlack = new Color32(20, 24, 36, 248);
    private static readonly Color IronMid = new Color32(49, 57, 74, 248);
    private static readonly Color IronLight = new Color32(116, 128, 146, 255);
    private static readonly Color Stone = new Color32(78, 82, 91, 250);
    private static readonly Color StoneDark = new Color32(39, 43, 52, 252);
    private static readonly Color Pink = new Color32(255, 72, 179, 255);
    private static readonly Color PinkSoft = new Color32(255, 158, 215, 255);
    private static readonly Color Moss = new Color32(74, 118, 73, 220);
    private static readonly Color Ivory = new Color32(255, 247, 224, 255);

    [MenuItem("鉄球少女/Recovery UI/復旧・Enemy・共通UIを適用")]
    public static void ApplyAll()
    {
        Directory.CreateDirectory("Assets/Prefabs/UI");
        ConfigurePauseInput();
        CreateEnemyAnimationAssets();
        ConfigureEnemyPrefab("Assets/Prefabs/Enemy.prefab");
        ConfigureEnemyPrefab(TekkyuEnemyPrefabPath);
        CreateControlsPanelPrefab();
        CreatePauseMenuPrefab();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        GameplayFeatureIntegrator.ApplyAll();
        TitleSceneBuilder.Build();
        ConfigureCompletScene();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[RecoveryAndUiIntegrator] Respawn, Enemy Visual, Title connection, Controls and Pause configured.");
    }

    [MenuItem("鉄球少女/Recovery UI/UIクリック・Navigation修正のみ適用")]
    public static void ApplyUiInteractionFix()
    {
        Directory.CreateDirectory("Assets/Prefabs/UI");
        CreateControlsPanelPrefab();
        CreatePauseMenuPrefab();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        TitleSceneBuilder.Build();
        ConfigurePauseSceneOnly();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[RecoveryAndUiIntegrator] Title/Controls/Pause UI interaction fix applied without gameplay changes.");
    }

    [MenuItem("鉄球少女/Recovery UI/Controls・Pause Previewを出力")]
    public static void RenderUiPreviews()
    {
        RenderPrefabPreview(ControlsPrefabPath, "Temp/ControlsPanelPreview.png", false);
        RenderPrefabPreview(PausePrefabPath, "Temp/PauseMenuPreview.png", true);
        Debug.Log("[RecoveryAndUiIntegrator] UI previews rendered to Temp.");
    }

    private static void RenderPrefabPreview(string prefabPath, string outputPath, bool pausePresentation)
    {
        Scene previous = SceneManager.GetActiveScene();
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);

        const int width = 1920;
        const int height = 1080;
        RenderTexture renderTexture = null;
        Texture2D texture = null;
        try
        {
            GameObject cameraObject = new GameObject("PreviewCamera", typeof(Camera));
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(41, 83, 111, 255);
            camera.orthographic = true;
            camera.transform.position = new Vector3(0f, 0f, -10f);

            GameObject canvasObject = new GameObject(
                "PreviewCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(width, height);
            scaler.matchWidthOrHeight = 0.5f;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            GameObject instance = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
            RectTransform instanceRect = instance.GetComponent<RectTransform>();
            instanceRect.SetParent(canvasObject.transform, false);
            Stretch(instanceRect);

            if (pausePresentation)
            {
                Transform presentation = instance.transform.Find("PausePresentation");
                if (presentation != null)
                    presentation.gameObject.SetActive(true);
                Transform controls = presentation != null ? presentation.Find("ControlsPanel") : null;
                if (controls != null)
                    controls.gameObject.SetActive(false);
            }
            else
            {
                instance.SetActive(true);
            }

            renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            camera.targetTexture = renderTexture;
            RenderTexture.active = renderTexture;
            Canvas.ForceUpdateCanvases();
            camera.Render();
            texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            texture.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? "Temp");
            File.WriteAllBytes(Path.GetFullPath(outputPath), texture.EncodeToPNG());
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
        }
        finally
        {
            if (texture != null)
                UnityEngine.Object.DestroyImmediate(texture);
            if (renderTexture != null)
                UnityEngine.Object.DestroyImmediate(renderTexture);
            if (scene.IsValid() && scene.isLoaded)
                EditorSceneManager.CloseScene(scene, true);
            if (previous.IsValid() && previous.isLoaded)
                SceneManager.SetActiveScene(previous);
        }
    }

    private static void ConfigurePauseInput()
    {
        InputActionAsset asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
        if (asset == null)
            throw new FileNotFoundException("InputActionAssetが見つかりません。", InputActionsPath);

        InputActionMap playerMap = asset.FindActionMap("Player", true);
        InputAction pause = playerMap.FindAction("Pause", false);
        if (pause == null)
        {
            pause = playerMap.AddAction("Pause", InputActionType.Button);
            pause.AddBinding("<Keyboard>/escape");
            pause.AddBinding("<Gamepad>/start");
            EditorUtility.SetDirty(asset);
        }
    }

    private static void CreateEnemyAnimationAssets()
    {
        AnimationClip idle = LoadOrCreateClip(EnemyIdleClipPath, "EnemyIdleVisual");
        SetTransformCurves(idle,
            new[] { new Keyframe(0f, 0f), new Keyframe(0.32f, 0f) },
            new[] { new Keyframe(0f, 0f), new Keyframe(0.32f, 0f) },
            new[] { new Keyframe(0f, 1f), new Keyframe(0.32f, 1f) },
            new[] { new Keyframe(0f, 1f), new Keyframe(0.32f, 1f) });

        AnimationClip walk = LoadOrCreateClip(EnemyWalkClipPath, "EnemyWalkVisual");
        SetTransformCurves(walk,
            new[]
            {
                new Keyframe(0f, 0f),
                new Keyframe(0.08f, 0.03f),
                new Keyframe(0.16f, 0f),
                new Keyframe(0.24f, 0.025f),
                new Keyframe(0.32f, 0f)
            },
            new[]
            {
                new Keyframe(0f, 0f),
                new Keyframe(0.08f, -1.5f),
                new Keyframe(0.16f, 1.3f),
                new Keyframe(0.24f, -1.2f),
                new Keyframe(0.32f, 0f)
            },
            new[]
            {
                new Keyframe(0f, 1f),
                new Keyframe(0.08f, 1.012f),
                new Keyframe(0.16f, 0.992f),
                new Keyframe(0.24f, 1.008f),
                new Keyframe(0.32f, 1f)
            },
            new[]
            {
                new Keyframe(0f, 1f),
                new Keyframe(0.08f, 0.988f),
                new Keyframe(0.16f, 1.015f),
                new Keyframe(0.24f, 0.992f),
                new Keyframe(0.32f, 1f)
            });

        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(EnemyControllerPath);
        if (controller == null)
            controller = AnimatorController.CreateAnimatorControllerAtPath(EnemyControllerPath);

        if (!controller.parameters.Any(parameter => parameter.name == "IsMoving"))
            controller.AddParameter("IsMoving", AnimatorControllerParameterType.Bool);

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        AnimatorState idleState = machine.states.Select(child => child.state)
            .FirstOrDefault(state => state.name == "Idle") ?? machine.AddState("Idle", new Vector3(260f, 100f));
        AnimatorState walkState = machine.states.Select(child => child.state)
            .FirstOrDefault(state => state.name == "Walk") ?? machine.AddState("Walk", new Vector3(520f, 100f));
        idleState.motion = idle;
        walkState.motion = walk;
        machine.defaultState = idleState;

        foreach (AnimatorStateTransition transition in idleState.transitions.ToArray())
            idleState.RemoveTransition(transition);
        foreach (AnimatorStateTransition transition in walkState.transitions.ToArray())
            walkState.RemoveTransition(transition);

        AnimatorStateTransition toWalk = idleState.AddTransition(walkState);
        toWalk.hasExitTime = false;
        toWalk.duration = 0.05f;
        toWalk.AddCondition(AnimatorConditionMode.If, 0f, "IsMoving");
        AnimatorStateTransition toIdle = walkState.AddTransition(idleState);
        toIdle.hasExitTime = false;
        toIdle.duration = 0.05f;
        toIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsMoving");

        EditorUtility.SetDirty(controller);
    }

    private static AnimationClip LoadOrCreateClip(string path, string name)
    {
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null)
        {
            clip = new AnimationClip { name = name, frameRate = 60f };
            AssetDatabase.CreateAsset(clip, path);
        }

        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            AnimationUtility.SetEditorCurve(clip, binding, null);

        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        return clip;
    }

    private static void SetTransformCurves(
        AnimationClip clip,
        Keyframe[] y,
        Keyframe[] rotation,
        Keyframe[] scaleX,
        Keyframe[] scaleY)
    {
        SetSteppedCurve(clip, "m_LocalPosition.y", y);
        SetSteppedCurve(clip, "localEulerAnglesRaw.z", rotation);
        SetSteppedCurve(clip, "m_LocalScale.x", scaleX);
        SetSteppedCurve(clip, "m_LocalScale.y", scaleY);
        EditorUtility.SetDirty(clip);
    }

    private static void SetSteppedCurve(AnimationClip clip, string propertyName, Keyframe[] keys)
    {
        AnimationCurve curve = new AnimationCurve(keys);
        for (int i = 0; i < curve.length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Constant);
            AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Constant);
        }
        AnimationUtility.SetEditorCurve(
            clip,
            EditorCurveBinding.FloatCurve(string.Empty, typeof(Transform), propertyName),
            curve);
    }

    private static void ConfigureEnemyPrefab(string prefabPath)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
            return;

        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            Transform visual = root.transform.Find("Visual");
            if (visual == null)
            {
                GameObject visualObject = new GameObject("Visual", typeof(SpriteRenderer), typeof(Animator));
                visual = visualObject.transform;
                visual.SetParent(root.transform, false);
            }

            visual.localPosition = Vector3.zero;
            visual.localRotation = Quaternion.identity;
            visual.localScale = Vector3.one;

            SpriteRenderer childRenderer = visual.GetComponent<SpriteRenderer>();
            if (childRenderer == null)
                childRenderer = visual.gameObject.AddComponent<SpriteRenderer>();

            SpriteRenderer rootRenderer = root.GetComponent<SpriteRenderer>();
            if (rootRenderer != null)
            {
                EditorUtility.CopySerialized(rootRenderer, childRenderer);
                UnityEngine.Object.DestroyImmediate(rootRenderer);
            }

            Animator animator = visual.GetComponent<Animator>();
            if (animator == null)
                animator = visual.gameObject.AddComponent<Animator>();
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(EnemyControllerPath);
            animator.applyRootMotion = false;

            Enemy enemy = root.GetComponent<Enemy>();
            if (enemy != null)
            {
                SerializedObject serialized = new SerializedObject(enemy);
                if (string.Equals(prefabPath, TekkyuEnemyPrefabPath, StringComparison.Ordinal))
                    serialized.FindProperty("_moveSpeed").floatValue = 5f;
                serialized.FindProperty("_detectionRange").floatValue = 7f;
                serialized.FindProperty("_loseSightDelay").floatValue = 0.5f;
                serialized.FindProperty("_lineOfSightMask").intValue = (1 << 0) | (1 << 3) | (1 << 6);
                serialized.FindProperty("_eyePoint").objectReferenceValue = null;
                serialized.FindProperty("_eyeOffset").vector2Value = new Vector2(0f, 0.15f);
                serialized.FindProperty("_visualAnimator").objectReferenceValue = animator;
                serialized.FindProperty("_visualRenderer").objectReferenceValue = childRenderer;
                serialized.FindProperty("_isMovingParameter").stringValue = "IsMoving";
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void CreateControlsPanelPrefab()
    {
        GameObject root = new GameObject("ControlsPanel", typeof(RectTransform), typeof(ControlsPanelController));
        RectTransform rootRect = root.GetComponent<RectTransform>();
        Stretch(rootRect);
        SetLayerRecursively(root, 5);

        TMP_FontAsset bold = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BoldFontPath);
        TMP_FontAsset regular = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(RegularFontPath);
        Sprite chain = LoadLargestSprite(ChainSpritePath);
        Sprite stoneSprite = LoadLargestSprite(StoneTexturePath);

        Image overlay = CreateImage("DarkOverlay", rootRect, new Color32(7, 10, 17, 190));
        Stretch(overlay.rectTransform);
        overlay.raycastTarget = true;

        RectTransform panel = CreateRect("StoneTablet", rootRect, Vector2.zero, new Vector2(1380f, 860f));
        CreateFramedPanel(panel, stoneSprite);

        TextMeshProUGUI title = CreateText("Title", panel, "操作方法", bold, 58f, Ivory, TextAlignmentOptions.Center);
        SetRect(title.rectTransform, new Vector2(0f, 350f), new Vector2(760f, 90f));
        title.outlineColor = IronBlack;
        title.outlineWidth = 0.16f;

        Image titleAccent = CreateImage("TitleAccent", panel, Pink);
        SetRect(titleAccent.rectTransform, new Vector2(0f, 304f), new Vector2(620f, 5f));
        AddChainDecoration(panel, chain, new Vector2(-510f, 348f), 250f, -7f);
        AddChainDecoration(panel, chain, new Vector2(510f, 348f), 250f, 187f);

        CreateControlRow(panel, bold, regular, "Move", "移動", "左スティック", "A・Dキー", 208f);
        CreateControlRow(panel, bold, regular, "Jump", "ジャンプ", "Aボタン", "Spaceキー", 72f);
        CreateControlRow(panel, bold, regular, "Throw", "鉄球を投げる", "右スティック", "左クリック", -64f);
        CreateControlRow(panel, bold, regular, "Charge", "回転チャージ", "右スティック長押し", "左クリック長押し", -200f);

        Button backButton = CreateMenuButton(panel, "BackButton", "戻る", bold, new Vector2(0f, -350f), new Vector2(340f, 74f));

        ControlsPanelController controller = root.GetComponent<ControlsPanelController>();
        SerializedObject serialized = new SerializedObject(controller);
        serialized.FindProperty("backButton").objectReferenceValue = backButton;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        PrefabUtility.SaveAsPrefabAsset(root, ControlsPrefabPath);
        UnityEngine.Object.DestroyImmediate(root);
    }

    private static void CreatePauseMenuPrefab()
    {
        GameObject controlsPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ControlsPrefabPath);
        if (controlsPrefab == null)
            throw new FileNotFoundException("ControlsPanel Prefabが見つかりません。", ControlsPrefabPath);

        TMP_FontAsset bold = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BoldFontPath);
        Sprite chain = LoadLargestSprite(ChainSpritePath);
        Sprite stoneSprite = LoadLargestSprite(StoneTexturePath);

        GameObject root = new GameObject("PauseMenu", typeof(RectTransform), typeof(PauseMenuController));
        RectTransform rootRect = root.GetComponent<RectTransform>();
        Stretch(rootRect);
        SetLayerRecursively(root, 5);

        RectTransform presentation = CreateRect("PausePresentation", rootRect, Vector2.zero, Vector2.zero);
        Stretch(presentation);
        Image overlay = CreateImage("DarkOverlay", presentation, new Color32(5, 8, 14, 190));
        Stretch(overlay.rectTransform);
        overlay.raycastTarget = true;

        RectTransform panel = CreateRect("PauseStonePanel", presentation, Vector2.zero, new Vector2(720f, 740f));
        CreateFramedPanel(panel, stoneSprite);
        TextMeshProUGUI title = CreateText("PauseTitle", panel, "PAUSE", bold, 70f, Ivory, TextAlignmentOptions.Center);
        SetRect(title.rectTransform, new Vector2(0f, 260f), new Vector2(560f, 100f));
        title.characterSpacing = 7f;
        title.outlineColor = IronBlack;
        title.outlineWidth = 0.16f;
        AddChainDecoration(panel, chain, new Vector2(0f, 198f), 510f, 0f);

        Button resume = CreateMenuButton(panel, "ResumeButton", "つづける", bold, new Vector2(0f, 90f), new Vector2(430f, 86f));
        Button controls = CreateMenuButton(panel, "ControlsButton", "操作方法", bold, new Vector2(0f, -30f), new Vector2(430f, 86f));
        Button titleButton = CreateMenuButton(panel, "TitleButton", "タイトルへ戻る", bold, new Vector2(0f, -150f), new Vector2(430f, 86f));

        GameObject controlsObject = PrefabUtility.InstantiatePrefab(controlsPrefab) as GameObject;
        controlsObject.name = "ControlsPanel";
        RectTransform controlsRect = controlsObject.GetComponent<RectTransform>();
        controlsRect.SetParent(presentation, false);
        Stretch(controlsRect);
        controlsObject.SetActive(false);

        PauseMenuController controller = root.GetComponent<PauseMenuController>();
        SerializedObject serialized = new SerializedObject(controller);
        serialized.FindProperty("inputActions").objectReferenceValue = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
        serialized.FindProperty("pauseActionName").stringValue = "Player/Pause";
        serialized.FindProperty("presentationRoot").objectReferenceValue = presentation.gameObject;
        serialized.FindProperty("menuPanel").objectReferenceValue = panel.gameObject;
        serialized.FindProperty("controlsPanel").objectReferenceValue = controlsObject.GetComponent<ControlsPanelController>();
        serialized.FindProperty("resumeButton").objectReferenceValue = resume;
        serialized.FindProperty("controlsButton").objectReferenceValue = controls;
        serialized.FindProperty("titleButton").objectReferenceValue = titleButton;
        serialized.FindProperty("titleSceneName").stringValue = "TitleScene";
        serialized.ApplyModifiedPropertiesWithoutUndo();

        PrefabUtility.SaveAsPrefabAsset(root, PausePrefabPath);
        UnityEngine.Object.DestroyImmediate(root);
    }

    private static void ConfigureCompletScene()
    {
        Scene scene = SceneManager.GetSceneByPath(CompletScenePath);
        bool wasAlreadyLoaded = scene.IsValid() && scene.isLoaded;
        if (!wasAlreadyLoaded)
            scene = EditorSceneManager.OpenScene(CompletScenePath, OpenSceneMode.Additive);
        try
        {
            Player player = FindInScene<Player>(scene);
            PlayerHealth health = FindInScene<PlayerHealth>(scene);
            MorningStarLauncher launcher = FindInScene<MorningStarLauncher>(scene);
            GimmickRespawnController gimmick = FindInScene<GimmickRespawnController>(scene);
            DeathRespawnManager death = FindInScene<DeathRespawnManager>(scene);
            Transform initial = FindTransform(scene, "InitialRespawnPoint");

            if (gimmick == null || death == null || player == null || launcher == null || initial == null)
                throw new InvalidOperationException("CompletSceneのRespawn必須Objectが不足しています。");

            SerializedObject gimmickSerialized = new SerializedObject(gimmick);
            gimmickSerialized.FindProperty("player").objectReferenceValue = player.transform;
            gimmickSerialized.FindProperty("playerRigidbody").objectReferenceValue = player.GetComponent<Rigidbody2D>();
            gimmickSerialized.FindProperty("playerHealth").objectReferenceValue = health;
            gimmickSerialized.FindProperty("morningStarLauncher").objectReferenceValue = launcher;
            gimmickSerialized.FindProperty("initialRespawnPoint").objectReferenceValue = initial;
            gimmickSerialized.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject playerSerialized = new SerializedObject(player);
            playerSerialized.FindProperty("_jumpVolume").floatValue = 1f;
            playerSerialized.FindProperty("_footstepVolume").floatValue = 0.39f;
            playerSerialized.FindProperty("_jumpVoiceVolume").floatValue = 1f;
            playerSerialized.FindProperty("_landingVolume").floatValue = 0.55f;
            playerSerialized.FindProperty("_footstepMinHorizontalSpeed").floatValue = 0.1f;
            playerSerialized.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject launcherSerialized = new SerializedObject(launcher);
            launcherSerialized.FindProperty("groundImpactVolume").floatValue = 0.56f;
            launcherSerialized.FindProperty("morningStarLaunchVolume").floatValue = 0.55f;
            launcherSerialized.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject deathSerialized = new SerializedObject(death);
            deathSerialized.FindProperty("_defaultSpawnPoint").objectReferenceValue = initial;
            deathSerialized.FindProperty("_player").objectReferenceValue = player;
            deathSerialized.FindProperty("_playerHealth").objectReferenceValue = health;
            deathSerialized.FindProperty("_gimmickRespawnController").objectReferenceValue = gimmick;
            deathSerialized.ApplyModifiedPropertiesWithoutUndo();

            foreach (Checkpoint checkpoint in FindAllInScene<Checkpoint>(scene))
            {
                SerializedObject serialized = new SerializedObject(checkpoint);
                serialized.FindProperty("gimmickRespawnController").objectReferenceValue = gimmick;
                serialized.FindProperty("deathRespawnManager").objectReferenceValue = death;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            foreach (RespawnZone zone in FindAllInScene<RespawnZone>(scene))
            {
                SerializedObject serialized = new SerializedObject(zone);
                serialized.FindProperty("gimmickRespawnController").objectReferenceValue = gimmick;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            ConfigurePauseCanvas(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        finally
        {
            if (!wasAlreadyLoaded && scene.IsValid() && scene.isLoaded && SceneManager.sceneCount > 1)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static void ConfigurePauseCanvas(Scene scene)
    {
        GameObject existing = scene.GetRootGameObjects().FirstOrDefault(root => root.name == "PauseCanvas");
        if (existing != null)
            UnityEngine.Object.DestroyImmediate(existing);

        GameObject canvasObject = new GameObject(
            "PauseCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        SceneManager.MoveGameObjectToScene(canvasObject, scene);
        SetLayerRecursively(canvasObject, 5);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        GameObject pausePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PausePrefabPath);
        GameObject pause = PrefabUtility.InstantiatePrefab(pausePrefab, scene) as GameObject;
        pause.name = "PauseMenu";
        RectTransform rect = pause.GetComponent<RectTransform>();
        rect.SetParent(canvasObject.transform, false);
        Stretch(rect);

        NormalizeEventSystem(scene);
    }

    private static void ConfigurePauseSceneOnly()
    {
        Scene scene = SceneManager.GetSceneByPath(CompletScenePath);
        bool wasAlreadyLoaded = scene.IsValid() && scene.isLoaded;
        if (!wasAlreadyLoaded)
            scene = EditorSceneManager.OpenScene(CompletScenePath, OpenSceneMode.Additive);

        try
        {
            ConfigurePauseCanvas(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        finally
        {
            if (!wasAlreadyLoaded && scene.IsValid() && scene.isLoaded && SceneManager.sceneCount > 1)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static void NormalizeEventSystem(Scene scene)
    {
        EventSystem[] eventSystems = FindAllInScene<EventSystem>(scene);
        EventSystem eventSystem;
        if (eventSystems.Length == 0)
        {
            GameObject eventSystemObject = new GameObject("EventSystem", typeof(EventSystem));
            SceneManager.MoveGameObjectToScene(eventSystemObject, scene);
            eventSystem = eventSystemObject.GetComponent<EventSystem>();
        }
        else
        {
            eventSystem = eventSystems[0];
            for (int i = 1; i < eventSystems.Length; i++)
                UnityEngine.Object.DestroyImmediate(eventSystems[i].gameObject);
        }

        StandaloneInputModule[] legacyModules = FindAllInScene<StandaloneInputModule>(scene);
        foreach (StandaloneInputModule legacyModule in legacyModules)
            UnityEngine.Object.DestroyImmediate(legacyModule);

        InputSystemUIInputModule module = eventSystem.GetComponent<InputSystemUIInputModule>();
        if (module == null)
            module = eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
        module.enabled = true;
        eventSystem.sendNavigationEvents = true;

        if (module.actionsAsset == null)
        {
            MethodInfo assignDefaults = typeof(InputSystemUIInputModule).GetMethod(
                "AssignDefaultActions",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            assignDefaults?.Invoke(module, null);
        }
    }

    private static void CreateFramedPanel(RectTransform panel, Sprite stoneSprite)
    {
        Image shadow = CreateImage("Shadow", panel, new Color32(5, 7, 12, 170));
        StretchWithMargin(shadow.rectTransform, -18f);
        shadow.rectTransform.anchoredPosition = new Vector2(10f, -12f);
        Image pinkGlow = CreateImage("PinkGlow", panel, new Color32(255, 62, 180, 45));
        StretchWithMargin(pinkGlow.rectTransform, -10f);
        Image outer = CreateImage("IronOuter", panel, IronBlack);
        Stretch(outer.rectTransform);
        Image rim = CreateImage("SteelRim", panel, IronLight);
        StretchWithMargin(rim.rectTransform, 10f);
        Image stone = CreateImage("StoneSurface", panel, Stone);
        StretchWithMargin(stone.rectTransform, 18f);
        if (stoneSprite != null)
        {
            Image texture = CreateImage("StoneTexture", panel, new Color32(255, 255, 255, 28), stoneSprite);
            StretchWithMargin(texture.rectTransform, 22f);
        }

        Image mossTop = CreateImage("MossTop", panel, Moss);
        SetAnchored(mossTop.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(310f, 14f), new Vector2(174f, -24f));
        Image mossBottom = CreateImage("MossBottom", panel, new Color32(74, 118, 73, 160));
        SetAnchored(mossBottom.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(220f, 10f), new Vector2(-140f, 24f));
        CreateBolt(panel, new Vector2(-panel.sizeDelta.x * 0.5f + 34f, panel.sizeDelta.y * 0.5f - 34f));
        CreateBolt(panel, new Vector2(panel.sizeDelta.x * 0.5f - 34f, panel.sizeDelta.y * 0.5f - 34f));
        CreateBolt(panel, new Vector2(-panel.sizeDelta.x * 0.5f + 34f, -panel.sizeDelta.y * 0.5f + 34f));
        CreateBolt(panel, new Vector2(panel.sizeDelta.x * 0.5f - 34f, -panel.sizeDelta.y * 0.5f + 34f));
    }

    private static void CreateControlRow(
        RectTransform parent,
        TMP_FontAsset bold,
        TMP_FontAsset regular,
        string name,
        string operation,
        string gamepad,
        string keyboard,
        float y)
    {
        RectTransform row = CreateRect($"ControlRow_{name}", parent, new Vector2(0f, y), new Vector2(1160f, 116f));
        Image background = CreateImage("RowBackground", row, new Color32(24, 29, 41, 225));
        Stretch(background.rectTransform);
        Image leftAccent = CreateImage("PinkAccent", row, Pink);
        SetAnchored(leftAccent.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(7f, 84f), new Vector2(18f, 0f));

        TextMeshProUGUI operationText = CreateText("Operation", row, operation, bold, 31f, Ivory, TextAlignmentOptions.MidlineLeft);
        SetRect(operationText.rectTransform, new Vector2(-430f, 0f), new Vector2(245f, 80f));
        TextMeshProUGUI gamepadText = CreateText("Gamepad", row, $"GAMEPAD\n{gamepad}", regular, 24f, PinkSoft, TextAlignmentOptions.Center);
        SetRect(gamepadText.rectTransform, new Vector2(-90f, 0f), new Vector2(340f, 90f));
        TextMeshProUGUI keyboardText = CreateText("KeyboardMouse", row, $"KEYBOARD / MOUSE\n{keyboard}", regular, 24f, new Color32(220, 233, 241, 255), TextAlignmentOptions.Center);
        SetRect(keyboardText.rectTransform, new Vector2(330f, 0f), new Vector2(430f, 90f));
    }

    private static Button CreateMenuButton(
        RectTransform parent,
        string name,
        string label,
        TMP_FontAsset font,
        Vector2 position,
        Vector2 size)
    {
        RectTransform root = CreateRect(name, parent, position, size);
        Image glow = CreateImage("PinkGlow", root, new Color32(255, 65, 181, 55));
        StretchWithMargin(glow.rectTransform, -7f);
        Image outer = CreateImage("Outer", root, IronBlack);
        Stretch(outer.rectTransform);
        Image rim = CreateImage("Rim", root, IronLight);
        StretchWithMargin(rim.rectTransform, 5f);
        Image inner = CreateImage("Inner", root, StoneDark);
        StretchWithMargin(inner.rectTransform, 4f);
        // Button Root自体にGraphicは無いため、表面ImageをマウスRaycast対象にする。
        inner.raycastTarget = true;
        Image accent = CreateImage("Accent", root, Pink);
        SetAnchored(accent.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(7f, size.y - 18f), new Vector2(16f, 0f));
        TextMeshProUGUI text = CreateText("Label", root, label, font, 31f, Ivory, TextAlignmentOptions.Center);
        Stretch(text.rectTransform);

        Button button = root.gameObject.AddComponent<Button>();
        button.targetGraphic = inner;
        button.transition = Selectable.Transition.ColorTint;
        button.colors = new ColorBlock
        {
            normalColor = Color.white,
            highlightedColor = new Color(1f, 0.82f, 0.94f, 1f),
            pressedColor = new Color(1f, 0.62f, 0.84f, 1f),
            selectedColor = new Color(1f, 0.82f, 0.94f, 1f),
            disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.6f),
            colorMultiplier = 1f,
            fadeDuration = 0.08f
        };
        button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
        return button;
    }

    private static void AddChainDecoration(RectTransform parent, Sprite sprite, Vector2 position, float width, float rotation)
    {
        if (sprite == null)
            return;
        Image image = CreateImage("ChainDecoration", parent, new Color32(225, 232, 240, 170), sprite);
        SetRect(image.rectTransform, position, new Vector2(width, 16f));
        image.rectTransform.localRotation = Quaternion.Euler(0f, 0f, rotation);
    }

    private static void CreateBolt(RectTransform parent, Vector2 position)
    {
        Image bolt = CreateImage("Bolt", parent, new Color32(182, 194, 208, 255));
        SetRect(bolt.rectTransform, position, new Vector2(15f, 15f));
        bolt.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
    }

    private static RectTransform CreateRect(string name, RectTransform parent, Vector2 position, Vector2 size)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform));
        RectTransform rect = gameObject.GetComponent<RectTransform>();
        if (parent != null)
            rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * 0.5f;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
        return rect;
    }

    private static Image CreateImage(string name, RectTransform parent, Color color, Sprite sprite = null)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rect = gameObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        Image image = gameObject.GetComponent<Image>();
        image.sprite = sprite != null
            ? sprite
            : AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        image.type = sprite == null ? Image.Type.Sliced : Image.Type.Simple;
        image.color = color;
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
        text.textWrappingMode = TextWrappingModes.Normal;
        text.raycastTarget = false;
        return text;
    }

    private static void SetRect(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * 0.5f;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
    }

    private static void SetAnchored(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 size, Vector2 position)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = (anchorMin + anchorMax) * 0.5f;
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        rect.localScale = Vector3.one;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = Vector2.one * 0.5f;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
    }

    private static void StretchWithMargin(RectTransform rect, float margin)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = Vector2.one * 0.5f;
        rect.offsetMin = Vector2.one * margin;
        rect.offsetMax = Vector2.one * -margin;
        rect.localScale = Vector3.one;
    }

    private static Sprite LoadLargestSprite(string path)
    {
        return AssetDatabase.LoadAllAssetsAtPath(path)
            .OfType<Sprite>()
            .OrderByDescending(sprite => sprite.rect.width * sprite.rect.height)
            .FirstOrDefault();
    }

    private static T FindInScene<T>(Scene scene) where T : Component
    {
        return FindAllInScene<T>(scene).FirstOrDefault();
    }

    private static T[] FindAllInScene<T>(Scene scene) where T : Component
    {
        return scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<T>(true))
            .ToArray();
    }

    private static Transform FindTransform(Scene scene, string name)
    {
        return scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .FirstOrDefault(transform => transform.name == name);
    }

    private static void SetLayerRecursively(GameObject gameObject, int layer)
    {
        gameObject.layer = layer;
        foreach (Transform child in gameObject.transform)
            SetLayerRecursively(child.gameObject, layer);
    }
}

[InitializeOnLoad]
public static class RecoveryAndUiIntegrationRequestRunner
{
    static RecoveryAndUiIntegrationRequestRunner()
    {
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Directory.GetCurrentDirectory();
        string requestPath = Path.Combine(projectRoot, RecoveryAndUiIntegrator.RequestPath);
        if (!File.Exists(requestPath))
            return;

        string mode = File.ReadAllText(requestPath);
        File.Delete(requestPath);
        if (mode.IndexOf(RecoveryAndUiIntegrator.UiInteractionRequestMode, StringComparison.OrdinalIgnoreCase) >= 0)
            EditorApplication.delayCall += RecoveryAndUiIntegrator.ApplyUiInteractionFix;
        else if (mode.IndexOf("preview", StringComparison.OrdinalIgnoreCase) >= 0)
            EditorApplication.delayCall += RecoveryAndUiIntegrator.RenderUiPreviews;
        else
            EditorApplication.delayCall += RecoveryAndUiIntegrator.ApplyAll;
    }
}
