using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>既存の調整値を上書きせず、CompletSceneに開始演出を設定する。</summary>
public static class PlayerStartIntroSetup
{
    private const string TitlePrefabPath = "Assets/Prefabs/UI/StageTitleUI.prefab";

    [MenuItem("鉄球少女/Player/スタート演出を設定")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new System.InvalidOperationException("Edit Modeで実行してください。");
        Scene scene = SceneManager.GetSceneByPath("Assets/Scenes/CompletScene.unity");
        if (!scene.isLoaded)
            scene = EditorSceneManager.OpenScene("Assets/Scenes/CompletScene.unity", OpenSceneMode.Additive);
        Player player = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Player>(true)).Single();
        CameraFollow camera = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<CameraFollow>(true)).Single();
        PlayerStartIntro intro = player.GetComponent<PlayerStartIntro>();
        bool added = intro == null;
        if (added)
            intro = Undo.AddComponent<PlayerStartIntro>(player.gameObject);
        SerializedObject settings = new SerializedObject(intro);
        if (added)
            settings.FindProperty("landingPosition").vector2Value = (Vector2)player.transform.position - Vector2.right * 1.4f;
        if (settings.FindProperty("cameraFollow").objectReferenceValue == null)
            settings.FindProperty("cameraFollow").objectReferenceValue = camera;

        settings.ApplyModifiedProperties();

        // 既存Canvasを移行する。UIが無いSceneでは共通Prefabを一つ配置する。
        GameObject root = scene.GetRootGameObjects().FirstOrDefault(candidate =>
            candidate.GetComponent<StageTitleUI>() != null || candidate.name == "StageIntroCanvas");
        if (root == null)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TitlePrefabPath);
            if (prefab != null)
                root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            else
            {
                root = new GameObject("StageIntroCanvas", typeof(RectTransform), typeof(Canvas),
                    typeof(UnityEngine.UI.CanvasScaler), typeof(CanvasGroup));
                SceneManager.MoveGameObjectToScene(root, scene);
            }
            Undo.RegisterCreatedObjectUndo(root, "Create stage intro UI");
        }
        StageTitleUI title = root.GetComponent<StageTitleUI>();
        if (title == null)
        {
            Sprite sprite = AssetDatabase.LoadAllAssetsAtPath("Assets/image_/ステージ名sprite.png")
                .OfType<Sprite>().Single(candidate => candidate.name == "ステージ名sprite_0");
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.worldCamera = null;
            canvas.sortingOrder = 400;
            UnityEngine.UI.CanvasScaler scaler = root.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            CanvasGroup group = root.GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = group.blocksRaycasts = false;

            Transform oldLabel = root.transform.Find("TitleBand/StageTitle");
            GameObject label = oldLabel != null ? oldLabel.gameObject
                : new GameObject("StageTitle", typeof(RectTransform));
            Undo.SetTransformParent(label.transform, root.transform, "Move stage title to corner");
            TMPro.TMP_Text oldText = label.GetComponent<TMPro.TMP_Text>();
            if (oldText != null)
                Undo.DestroyObjectImmediate(oldText);
            Transform oldBand = root.transform.Find("TitleBand");
            if (oldBand != null)
                Undo.DestroyObjectImmediate(oldBand.gameObject);

            UnityEngine.UI.Image image = label.GetComponent<UnityEngine.UI.Image>();
            if (image == null)
                image = Undo.AddComponent<UnityEngine.UI.Image>(label);
            image.sprite = sprite;
            image.color = Color.white;
            image.preserveAspect = true;
            image.raycastTarget = false;
            RectTransform rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one;
            rect.anchoredPosition = new Vector2(-48f, -48f);
            rect.sizeDelta = new Vector2(630f, 141f);

            title = Undo.AddComponent<StageTitleUI>(root);
            SerializedObject view = new SerializedObject(title);
            view.FindProperty("titleImage").objectReferenceValue = image;
            view.FindProperty("canvasGroup").objectReferenceValue = group;
            view.FindProperty("stageSprite").objectReferenceValue = sprite;
            view.ApplyModifiedProperties();
        }

        // PrefabにはScene内Playerへの参照を保存しない。Sceneインスタンス側だけに設定する。
        if (!PrefabUtility.IsPartOfPrefabInstance(root))
        {
            SerializedObject prefabSettings = new SerializedObject(title);
            prefabSettings.FindProperty("startIntro").objectReferenceValue = null;
            prefabSettings.ApplyModifiedProperties();
            PrefabUtility.SaveAsPrefabAssetAndConnect(root, TitlePrefabPath, InteractionMode.AutomatedAction);
        }
        SerializedObject sceneSettings = new SerializedObject(title);
        sceneSettings.FindProperty("startIntro").objectReferenceValue = intro;
        sceneSettings.ApplyModifiedProperties();
        PrefabUtility.RecordPrefabInstancePropertyModifications(title);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[PlayerStartIntroSetup] 既存Canvasを右上ステージ名Spriteの共通Prefabへ設定しました。");
    }
}
