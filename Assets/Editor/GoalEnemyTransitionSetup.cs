using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Enemy破片参照、Goal UI、Scene接続を再現可能な形で適用する。</summary>
public static class GoalEnemyTransitionSetup
{
    private const string CompletScenePath = "Assets/Scenes/CompletScene.unity";
    private const string TitleScenePath = "Assets/Scenes/TitleScene.unity";
    private const string PausePrefabPath = "Assets/Prefabs/UI/PauseMenu.prefab";
    private const string GoalPrefabPath = "Assets/Prefabs/UI/GoalMenu.prefab";
    private const string EnemyFragmentPath = "Assets/Prefabs/Enemies/EnemyFragment.prefab";

    [MenuItem("Tools/鉄球少女/Apply Enemy Fragments Goal UI And Push Transition")]
    public static void Apply()
    {
        ConfigureEnemyPrefab("Assets/Prefabs/Enemy.prefab");
        ConfigureEnemyPrefab("Assets/Prefabs/Enemies/TekkyuEnemy.prefab");
        CreateGoalMenuPrefab();
        ConfigureCompletScene();
        ConfigureTitleScene();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[GoalEnemyTransitionSetup] Enemy fragments, Goal UI and Title push transition applied.");
    }

    private static void ConfigureEnemyPrefab(string path)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        GameObject fragment = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyFragmentPath);
        if (prefab == null || fragment == null)
            throw new InvalidOperationException($"EnemyまたはEnemyFragment Prefabがありません: {path}");

        EnemyHealth health = prefab.GetComponent<EnemyHealth>();
        if (health == null)
            throw new InvalidOperationException($"EnemyHealthがありません: {path}");

        SerializedObject serialized = new SerializedObject(health);
        serialized.FindProperty("fragmentPrefab").objectReferenceValue = fragment;
        serialized.FindProperty("fragmentCount").intValue = 10;
        serialized.FindProperty("fragmentSpread").floatValue = 0.3f;
        serialized.FindProperty("minFragmentForce").floatValue = 1.5f;
        serialized.FindProperty("maxFragmentForce").floatValue = 3f;
        serialized.FindProperty("fragmentLifeTime").floatValue = 2f;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(prefab);
    }

    private static void CreateGoalMenuPrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PausePrefabPath) == null)
            throw new InvalidOperationException("PauseMenu Prefabがありません。");

        if (AssetDatabase.LoadAssetAtPath<GameObject>(GoalPrefabPath) == null
            && !AssetDatabase.CopyAsset(PausePrefabPath, GoalPrefabPath))
            throw new InvalidOperationException("PauseMenuからGoalMenuを複製できませんでした。");

        AssetDatabase.ImportAsset(GoalPrefabPath, ImportAssetOptions.ForceSynchronousImport);
        GameObject root = PrefabUtility.LoadPrefabContents(GoalPrefabPath);
        try
        {
            root.name = "GoalMenu";
            PauseMenuController oldController = root.GetComponent<PauseMenuController>();
            if (oldController != null)
                UnityEngine.Object.DestroyImmediate(oldController);

            Transform controls = FindDeep(root.transform, "ControlsPanel");
            if (controls != null)
                UnityEngine.Object.DestroyImmediate(controls.gameObject);

            RectTransform presentation = RequireRect(root.transform, "GoalPresentation", "PausePresentation");
            presentation.name = "GoalPresentation";
            RectTransform panel = RequireRect(root.transform, "GoalStonePanel", "PauseStonePanel");
            panel.name = "GoalStonePanel";
            if (panel.GetComponent<CanvasGroup>() == null)
                panel.gameObject.AddComponent<CanvasGroup>();

            TextMeshProUGUI title = RequireText(root.transform, "GoalTitle", "PauseTitle");
            title.name = "GoalTitle";
            title.text = "GOAL";

            Button next = RequireButton(root.transform, "NextButton", "ResumeButton");
            next.name = "NextButton";
            SetButtonLabel(next, "つぎへ");
            SetY(next.transform as RectTransform, 52f);

            Button retry = RequireButton(root.transform, "RetryButton", "ControlsButton");
            retry.name = "RetryButton";
            SetButtonLabel(retry, "もういちど");
            SetY(retry.transform as RectTransform, -68f);

            Button returnTitle = RequireButton(root.transform, "TitleButton");
            SetButtonLabel(returnTitle, "タイトルへ戻る");
            SetY(returnTitle.transform as RectTransform, -188f);

            Transform existingMessage = FindDeep(root.transform, "ClearMessage");
            GameObject messageObject = existingMessage != null
                ? existingMessage.gameObject
                : new GameObject(
                    "ClearMessage",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(TextMeshProUGUI));
            RectTransform messageRect = messageObject.GetComponent<RectTransform>();
            messageRect.SetParent(panel, false);
            messageRect.anchorMin = messageRect.anchorMax = new Vector2(0.5f, 0.5f);
            messageRect.pivot = new Vector2(0.5f, 0.5f);
            messageRect.anchoredPosition = new Vector2(0f, 145f);
            messageRect.sizeDelta = new Vector2(590f, 70f);
            TextMeshProUGUI message = messageObject.GetComponent<TextMeshProUGUI>();
            message.text = "ステージをクリアしました";
            message.font = title.font;
            message.fontSize = 32f;
            message.fontStyle = FontStyles.Bold;
            message.color = new Color32(239, 224, 207, 255);
            message.alignment = TextAlignmentOptions.Center;
            message.raycastTarget = false;

            GoalMenuController controller = root.GetComponent<GoalMenuController>();
            if (controller == null)
                controller = root.AddComponent<GoalMenuController>();
            SerializedObject serialized = new SerializedObject(controller);
            serialized.FindProperty("presentationRoot").objectReferenceValue = presentation.gameObject;
            serialized.FindProperty("stonePanel").objectReferenceValue = panel;
            serialized.FindProperty("nextButton").objectReferenceValue = next;
            serialized.FindProperty("retryButton").objectReferenceValue = retry;
            serialized.FindProperty("titleButton").objectReferenceValue = returnTitle;
            serialized.FindProperty("nextSceneName").stringValue = string.Empty;
            serialized.FindProperty("titleSceneName").stringValue = "TitleScene";
            serialized.FindProperty("showDuration").floatValue = 0.24f;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            presentation.gameObject.SetActive(true);
            PrefabUtility.SaveAsPrefabAsset(root, GoalPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ConfigureCompletScene()
    {
        Scene scene = EditorSceneManager.OpenScene(CompletScenePath, OpenSceneMode.Single);
        GameObject existing = scene.GetRootGameObjects().FirstOrDefault(item => item.name == "GoalCanvas");
        if (existing != null)
            UnityEngine.Object.DestroyImmediate(existing);

        GameObject canvasObject = new GameObject(
            "GoalCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        SceneManager.MoveGameObjectToScene(canvasObject, scene);
        SetLayerRecursively(canvasObject, 5);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 600;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        GameObject goalPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GoalPrefabPath);
        GameObject menuObject = PrefabUtility.InstantiatePrefab(goalPrefab, scene) as GameObject;
        menuObject.name = "GoalMenu";
        RectTransform menuRect = menuObject.GetComponent<RectTransform>();
        menuRect.SetParent(canvasObject.transform, false);
        Stretch(menuRect);

        GoalMenuController menu = menuObject.GetComponent<GoalMenuController>();
        PauseMenuController pause = FindInScene<PauseMenuController>(scene);
        SerializedObject menuSerialized = new SerializedObject(menu);
        menuSerialized.FindProperty("pauseMenu").objectReferenceValue = pause;
        menuSerialized.ApplyModifiedPropertiesWithoutUndo();

        foreach (GoalPoint goal in FindAllInScene<GoalPoint>(scene))
        {
            SerializedObject goalSerialized = new SerializedObject(goal);
            goalSerialized.FindProperty("goalMenu").objectReferenceValue = menu;
            goalSerialized.FindProperty("oneShot").boolValue = true;
            goalSerialized.FindProperty("onGoalReached").FindPropertyRelative("m_PersistentCalls")
                .FindPropertyRelative("m_Calls").arraySize = 0;
            goalSerialized.ApplyModifiedPropertiesWithoutUndo();
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static void ConfigureTitleScene()
    {
        Scene scene = EditorSceneManager.OpenScene(TitleScenePath, OpenSceneMode.Single);
        TitleScreenController title = FindInScene<TitleScreenController>(scene);
        if (title == null)
            throw new InvalidOperationException("TitleScreenControllerがありません。");

        SerializedObject serialized = new SerializedObject(title);
        serialized.FindProperty("stageSceneName").stringValue = "CompletScene";
        serialized.FindProperty("transitionStartDelay").floatValue = 0.12f;
        serialized.FindProperty("transitionSlideDuration").floatValue = 0.8f;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name)
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform result = FindDeep(root.GetChild(i), name);
            if (result != null)
                return result;
        }
        return null;
    }

    private static RectTransform RequireRect(Transform root, params string[] names)
    {
        Transform found = names.Select(name => FindDeep(root, name)).FirstOrDefault(item => item != null);
        if (found == null || found is not RectTransform rect)
            throw new InvalidOperationException($"Goal UI element missing: {string.Join(" / ", names)}");
        return rect;
    }

    private static Button RequireButton(Transform root, params string[] names)
    {
        Transform found = names.Select(name => FindDeep(root, name)).FirstOrDefault(item => item != null);
        Button button = found != null ? found.GetComponent<Button>() : null;
        if (button == null)
            throw new InvalidOperationException($"Goal UI button missing: {string.Join(" / ", names)}");
        return button;
    }

    private static TextMeshProUGUI RequireText(Transform root, params string[] names)
    {
        Transform found = names.Select(name => FindDeep(root, name)).FirstOrDefault(item => item != null);
        TextMeshProUGUI text = found != null ? found.GetComponent<TextMeshProUGUI>() : null;
        if (text == null)
            throw new InvalidOperationException($"Goal UI text missing: {string.Join(" / ", names)}");
        return text;
    }

    private static void SetButtonLabel(Button button, string label)
    {
        TextMeshProUGUI text = button.GetComponentInChildren<TextMeshProUGUI>(true);
        if (text == null)
            throw new InvalidOperationException($"Button label missing: {button.name}");
        text.text = label;
    }

    private static void SetY(RectTransform rect, float y)
    {
        if (rect != null)
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, y);
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        root.layer = layer;
        foreach (Transform child in root.transform)
            SetLayerRecursively(child.gameObject, layer);
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
}
