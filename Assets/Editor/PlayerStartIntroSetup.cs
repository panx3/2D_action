using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>既存の調整値を上書きせず、CompletSceneに開始演出を設定する。</summary>
public static class PlayerStartIntroSetup
{
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

        if (settings.FindProperty("stageTitleGroup").objectReferenceValue == null)
        {
            GameObject root = new GameObject("StageIntroCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler), typeof(CanvasGroup));
            SceneManager.MoveGameObjectToScene(root, scene);
            Undo.RegisterCreatedObjectUndo(root, "Create stage intro UI");
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 400;
            UnityEngine.UI.CanvasScaler scaler = root.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            CanvasGroup group = root.GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = group.blocksRaycasts = false;

            GameObject panel = new GameObject("TitleBand", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            panel.transform.SetParent(root.transform, false);
            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0.65f);
            rect.anchorMax = new Vector2(1f, 0.85f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            UnityEngine.UI.Image background = panel.GetComponent<UnityEngine.UI.Image>();
            background.color = new Color(0.02f, 0.02f, 0.03f, 0.8f);
            background.raycastTarget = false;

            GameObject label = new GameObject("StageTitle", typeof(RectTransform), typeof(TextMeshProUGUI));
            label.transform.SetParent(panel.transform, false);
            TextMeshProUGUI text = label.GetComponent<TextMeshProUGUI>();
            text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/NotoSansJP-Bold SDF.asset");
            text.text = "STAGE 1";
            text.fontSize = 72f;
            text.characterSpacing = 8f;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
            settings.FindProperty("stageTitleGroup").objectReferenceValue = group;
            settings.FindProperty("stageTitleText").objectReferenceValue = text;
        }
        settings.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[PlayerStartIntroSetup] CompletSceneに開始演出とSTAGE 1表示を設定しました。");
    }
}
