using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TextCore.LowLevel;

/// <summary>Rebuilds CRT menus and the original goal logo while preserving prefab GUIDs.</summary>
public static class CRTOverlayInstaller
{
    private static readonly Color White = new Color32(239, 232, 247, 255);
    private static readonly Color Pink = new Color32(255, 82, 158, 255);
    private static readonly Color Cyan = new Color32(115, 216, 220, 255);
    private const string FontPath = "Assets/Fonts/Silkscreen CRT.asset";
    private static TMP_FontAsset font;

    public static void RebuildPausePrefab() => RebuildPrefab("Assets/Prefabs/UI/PauseMenu.prefab", 0);
    public static void RebuildResultPrefab() => RebuildPrefab("Assets/Prefabs/UI/GoalMenu.prefab", 1);
    public static void RebuildAcquiredPrefab() => GoalCelebrationLogoSetup.Apply();

    [MenuItem("Tools/鉄球少女/UI/Rebuild Game Over CRT Prefab")]
    public static void RebuildGameOverPrefab()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        const string path = "Assets/Resources/UI/GameOverMenu.prefab";
        GameObject root = PrefabUtility.LoadPrefabContents("Assets/Prefabs/UI/GoalMenu.prefab");
        try
        {
            root.name = "GameOverMenu";
            UnityEngine.Object.DestroyImmediate(root.GetComponent<GoalMenuController>());
            var overlay = root.GetComponent<CRTOverlayController>();
            var controller = root.AddComponent<GameOverMenuController>();
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 710;
            var scaler = root.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            root.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            Transform safe = root.transform.Find("CRTPresentation/PhosphorUI/SafeArea");
            Transform menu = safe.Find("Menu");
            UnityEngine.Object.DestroyImmediate(menu.Find("NextButton").gameObject);
            UnityEngine.Object.DestroyImmediate(safe.Find("ClearTime").gameObject);
            var retry = menu.Find("RetryButton").GetComponent<UnityEngine.UI.Button>();
            var title = menu.Find("TitleButton").GetComponent<UnityEngine.UI.Button>();
            retry.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -24);
            title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -98);
            CRTMenuNavigation.Configure(retry, title);
            overlay.SetMessage("GAME OVER", "TRY AGAIN");
            safe.Find("Signal").GetComponent<TMP_Text>().text = "SIGNAL LOST";
            SetArray(overlay, "menuItems", new UnityEngine.Object[] { retry.GetComponent<CRTMenuItem>(), title.GetComponent<CRTMenuItem>() });
            Set(controller, "overlay", overlay);
            Set(controller, "retryButton", retry);
            Set(controller, "titleButton", title);
            root.transform.Find("CRTPresentation").gameObject.SetActive(false);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static void RebuildPrefab(string path, int kind)
    {
        font = CreateFont();
        BuildPrefab(path, kind);
        AssetDatabase.SaveAssetIfDirty(font);
    }

    [MenuItem("Tools/鉄球少女/UI/Rebuild CRT Overlays")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/CompletScene.unity" || scene.isDirty)
            throw new InvalidOperationException("Open a saved CompletScene first; existing unsaved edits are preserved.");
        font = CreateFont();
        BuildPrefab("Assets/Prefabs/UI/PauseMenu.prefab", 0);
        BuildPrefab("Assets/Prefabs/UI/GoalMenu.prefab", 1);
        RebuildAcquiredPrefab();
        AssetDatabase.SaveAssetIfDirty(font);
        foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include))
        {
            if (canvas.name == "PauseCanvas") canvas.sortingOrder = 700;
            if (canvas.name == "GoalCanvas") canvas.sortingOrder = 710;
            if (canvas.name == "CrystalAcquiredUI") canvas.sortingOrder = 650;
        }
        var pause = UnityEngine.Object.FindAnyObjectByType<PauseMenuController>(FindObjectsInactive.Include);
        foreach (Component controller in new Component[] {
            UnityEngine.Object.FindAnyObjectByType<GoalMenuController>(FindObjectsInactive.Include),
            UnityEngine.Object.FindAnyObjectByType<CrystalAcquiredUI>(FindObjectsInactive.Include) })
        {
            Set(controller, "pauseMenu", pause);
            PrefabUtility.RecordPrefabInstancePropertyModifications(controller);
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[CRTOverlay] Rebuilt Pause, Result, Crystal prefabs and saved CompletScene.");
    }

    private static TMP_FontAsset CreateFont()
    {
        var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (existing != null) return existing;
        Font source = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/Silkscreen-Regular.ttf");
        var asset = TMP_FontAsset.CreateFontAsset(source, 64, 2, GlyphRenderMode.RASTER_HINTED, 1024, 1024, AtlasPopulationMode.Dynamic);
        asset.name = "Silkscreen CRT";
        asset.TryAddCharacters(new string(Enumerable.Range(32, 95).Select(c => (char)c).ToArray()), out string missing);
        if (!string.IsNullOrEmpty(missing)) throw new InvalidOperationException("Pixel font is missing: " + missing);
        asset.atlasPopulationMode = AtlasPopulationMode.Static;
        AssetDatabase.CreateAsset(asset, FontPath);
        foreach (Texture2D texture in asset.atlasTextures)
        {
            texture.filterMode = FilterMode.Point;
            AssetDatabase.AddObjectToAsset(texture, asset);
        }
        AssetDatabase.AddObjectToAsset(asset.material, asset);
        return asset;
    }

    private static void BuildPrefab(string path, int kind)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            // Keep the root and its MonoBehaviour fileIDs: GoalPoint and scene references survive.
            for (int i = root.transform.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
            var overlay = root.GetComponent<CRTOverlayController>() ?? root.AddComponent<CRTOverlayController>();
            RectTransform presentation = Rect("CRTPresentation", root.transform);
            Stretch(presentation);
            var backdrop = presentation.gameObject.AddComponent<UnityEngine.UI.RawImage>();
            backdrop.color = Color.white;
            backdrop.raycastTarget = true;
            RectTransform softenedRect = Rect("SoftenedBackdrop", presentation);
            Stretch(softenedRect);
            var softenedBackdrop = softenedRect.gameObject.AddComponent<UnityEngine.UI.RawImage>();
            softenedBackdrop.color = new Color(1f, 1f, 1f, 0f);
            softenedBackdrop.raycastTarget = false;
            RectTransform content = Rect("PhosphorUI", presentation);
            Stretch(content);
            var group = content.gameObject.AddComponent<CanvasGroup>();
            RectTransform safe = Rect("SafeArea", content);
            safe.sizeDelta = new Vector2(1250, 850);
            Text(safe, "Channel", "CH-01", new Vector2(-500, 340), new Vector2(180, 36), 26, Pink, TextAlignmentOptions.Left);
            Text(safe, "Signal", kind == 0 ? "SIGNAL OK" : "SIGNAL RECOVERED", new Vector2(-285, 340), new Vector2(360, 36), 22, Cyan, TextAlignmentOptions.Left);
            Text(safe, "System", "TEKKYU / CRT-01", new Vector2(390, 340), new Vector2(390, 36), 22, White, TextAlignmentOptions.Right);
            Line(safe, "TopRule", new Vector2(0, 294), new Vector2(1180, 2), new Color(1, 0.32f, 0.63f, 0.35f));
            string title = kind == 0 ? "PAUSE" : kind == 1 ? "STAGE CLEAR" : "CRYSTAL\nACQUIRED";
            string detail = kind == 0 ? "TAKE A BREATHER" : kind == 1 ? string.Empty : "SIGNAL RECOVERED";
            var heading = Text(safe, "Heading", title, new Vector2(0, kind == 2 ? 60 : 176),
                new Vector2(1440, kind == 2 ? 240 : 120), kind == 0 ? 112 : 86, White, TextAlignmentOptions.Center);
            heading.characterSpacing = 5;
            heading.gameObject.AddComponent<UnityEngine.UI.Shadow>().effectColor = new Color(Pink.r, Pink.g, Pink.b, 0.65f);
            heading.GetComponent<UnityEngine.UI.Shadow>().effectDistance = new Vector2(3, -3);
            var subtitle = Text(safe, "Subtitle", detail, new Vector2(0, kind == 2 ? -100 : 91), new Vector2(1100, 44), 24, Pink, TextAlignmentOptions.Center);
            subtitle.characterSpacing = 4;
            var items = new System.Collections.Generic.List<CRTMenuItem>();
            RectTransform menu = Rect("Menu", safe);
            Stretch(menu);
            UnityEngine.UI.Button primary = null, retry = null, controls = null, titleButton = null;
            ControlsPanelController controlsPanel = null;
            if (kind < 2)
            {
                primary = Button(menu, kind == 0 ? "ResumeButton" : "NextButton", "CONTINUE", new Vector2(0, -24), items);
                retry = Button(menu, "RetryButton", "RETRY", new Vector2(0, -98), items);
                titleButton = Button(menu, "TitleButton", "TITLE", new Vector2(0, kind == 0 ? -246 : -172), items);
                if (kind == 0)
                {
                    controls = Button(menu, "ControlsButton", "CONTROLS", new Vector2(0, -172), items);
                    RectTransform panel = Rect("ControlsPanel", safe);
                    Stretch(panel);
                    controlsPanel = panel.gameObject.AddComponent<ControlsPanelController>();
                    Text(panel, "ColumnLabels", "ACTION       GAMEPAD        KEY / MOUSE", new Vector2(0, 15), new Vector2(1160, 40), 23, Cyan, TextAlignmentOptions.Center);
                    Text(panel, "Bindings", "MOVE         LEFT STICK     A / D\n\nJUMP         A             SPACE\n\nLAUNCH       RIGHT STICK    LEFT CLICK\n\nCHARGE       HOLD R-STICK   HOLD CLICK", new Vector2(0, -117), new Vector2(1160, 240), 24, White, TextAlignmentOptions.Center);
                    var back = Button(panel, "BackButton", "BACK", new Vector2(0, -284), items);
                    Set(controlsPanel, "backButton", back);
                    controlsPanel.gameObject.SetActive(false);
                }
            }
            else menu.gameObject.SetActive(false);
            Line(safe, "BottomRule", new Vector2(0, -346), new Vector2(1180, 2), new Color(1, 0.32f, 0.63f, 0.35f));
            Text(safe, "DisplayLabel", "CRT DISPLAY 60HZ", new Vector2(-350, -386), new Vector2(480, 34), 22, White, TextAlignmentOptions.Left);
            Text(safe, "Footer", kind == 2 ? "READING CRYSTAL..." : "D-PAD / STICK  :  A TO SELECT", new Vector2(275, -386), new Vector2(650, 34), 20, Cyan, TextAlignmentOptions.Right);
            var sparkles = new System.Collections.Generic.List<RectTransform>();
            if (kind != 0)
            {
                // Restrained pixel stars: no modern particle burst or legacy logo layer.
                for (int i = 0; i < 8; i++)
                {
                    var star = Rect("PhosphorStar" + i, safe);
                    star.anchoredPosition = new Vector2((i % 2 == 0 ? -1 : 1) * (450 + i % 3 * 45), 120 - i / 2 * 85);
                    star.sizeDelta = new Vector2(16, 16);
                    Line(star, "Horizontal", Vector2.zero, new Vector2(16, 4), Pink);
                    Line(star, "Vertical", Vector2.zero, new Vector2(4, 16), Pink);
                    sparkles.Add(star);
                }
            }
            Set(overlay, "presentationRoot", presentation.gameObject);
            Set(overlay, "content", group);
            Set(overlay, "backdrop", backdrop);
            Set(overlay, "softenedBackdrop", softenedBackdrop);
            Set(overlay, "heading", heading);
            Set(overlay, "subtitle", subtitle);
            SetArray(overlay, "menuItems", items.Cast<UnityEngine.Object>().ToArray());
            SetArray(overlay, "sparkles", sparkles.Cast<UnityEngine.Object>().ToArray());
            if (kind == 0)
            {
                var controller = root.GetComponent<PauseMenuController>();
                Set(controller, "overlay", overlay);
                Set(controller, "menuPanel", menu.gameObject);
                Set(controller, "controlsPanel", controlsPanel);
                Set(controller, "resumeButton", primary);
                Set(controller, "retryButton", retry);
                Set(controller, "controlsButton", controls);
                Set(controller, "titleButton", titleButton);
            }
            else if (kind == 1)
            {
                var controller = root.GetComponent<GoalMenuController>();
                Set(controller, "overlay", overlay);
                Set(controller, "nextButton", primary);
                Set(controller, "retryButton", retry);
                Set(controller, "titleButton", titleButton);
                var clearTime = Text(safe, "ClearTime", "CLEAR TIME  <color=#FFE066><size=38>00:00.00</size></color>", new Vector2(0, -258),
                    new Vector2(720, 56), 32, new Color(0.94f, 0.95f, 1f, 1f), TextAlignmentOptions.Center);
                Set(controller, "clearTimeLabel", clearTime);
            }
            else
            {
                Set(root.GetComponent<CrystalAcquiredUI>(), "overlay", overlay);
                var canvas = root.GetComponent<Canvas>();
                canvas.sortingOrder = 720;
            }
            // Keep scene editing clear; ReceiveBackdrop enables the presentation at runtime.
            presentation.gameObject.SetActive(false);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
    private static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.localScale = Vector3.one;
        return rect;
    }
    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
    private static TextMeshProUGUI Text(Transform parent, string name, string text, Vector2 position, Vector2 size, float fontSize, Color color, TextAlignmentOptions alignment)
    {
        var rect = Rect(name, parent);
        rect.anchoredPosition = position; rect.sizeDelta = size;
        var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font; label.text = text; label.fontSize = fontSize;
        label.color = color; label.alignment = alignment;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;
        return label;
    }
    private static UnityEngine.UI.Image Line(Transform parent, string name, Vector2 position, Vector2 size, Color color)
    {
        var rect = Rect(name, parent); rect.anchoredPosition = position; rect.sizeDelta = size;
        var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.color = color; image.raycastTarget = false;
        return image;
    }
    private static UnityEngine.UI.Button Button(Transform parent, string name, string text, Vector2 position, System.Collections.Generic.List<CRTMenuItem> items)
    {
        var rect = Rect(name, parent); rect.anchoredPosition = position; rect.sizeDelta = new Vector2(560, 64);
        var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
        var highlight = Line(rect, "SelectedPhosphor", Vector2.zero, rect.sizeDelta, Color.clear);
        button.targetGraphic = highlight;
        button.transition = UnityEngine.UI.Selectable.Transition.None;
        var label = Text(rect, "Label", text, new Vector2(25, 0), new Vector2(420, 60), 38, White, TextAlignmentOptions.Left);
        var cursor = Text(rect, "Cursor", ">", new Vector2(-230, 0), new Vector2(50, 60), 38, Pink, TextAlignmentOptions.Center);
        var item = rect.gameObject.AddComponent<CRTMenuItem>();
        Set(item, "button", button); Set(item, "label", label); Set(item, "cursor", cursor); Set(item, "highlight", highlight);
        items.Add(item);
        return button;
    }
    private static void Set(UnityEngine.Object target, string name, UnityEngine.Object value)
    {
        var data = new SerializedObject(target);
        data.FindProperty(name).objectReferenceValue = value;
        data.ApplyModifiedPropertiesWithoutUndo();
    }
    private static void SetArray(UnityEngine.Object target, string name, UnityEngine.Object[] values)
    {
        var data = new SerializedObject(target); var array = data.FindProperty(name); array.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        data.ApplyModifiedPropertiesWithoutUndo();
    }
}
