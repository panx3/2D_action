using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>添付ロゴを使うGoal祝福Prefabを再現可能な形で構築する。</summary>
public static class GoalCelebrationLogoSetup
{
    public const string RequestPath = "Temp/GoalCelebrationLogoSetup.request";
    private const string SetupVersion = "GoalCelebrationLogoSetup_v1";
    private const string LogoPath = "Assets/Resources/GoalCelebration/GoalCelebrationLogo.png";
    private const string PrefabPath = "Assets/Prefabs/UI/CrystalAcquiredUI.prefab";

    [MenuItem("Tools/鉄球少女/Apply Goal Celebration Logo")]
    public static void Apply()
    {
        ConfigureLogoImporter();
        ConfigurePrefab();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[GoalCelebrationLogoSetup] Goal logo, flash, dimmer and pixel sparkles applied.");
    }

    [InitializeOnLoadMethod]
    private static void QueueRequestedApply()
    {
        TextureImporter importer = AssetImporter.GetAtPath(LogoPath) as TextureImporter;
        if (File.Exists(RequestPath) || (importer != null && importer.userData != SetupVersion))
            EditorApplication.delayCall += RunRequestedApply;
    }

    private static void RunRequestedApply()
    {
        TextureImporter importer = AssetImporter.GetAtPath(LogoPath) as TextureImporter;
        bool requested = File.Exists(RequestPath);
        if (!requested && (importer == null || importer.userData == SetupVersion))
            return;
        if (requested)
            File.Delete(RequestPath);
        Apply();
    }

    private static void ConfigureLogoImporter()
    {
        TextureImporter importer = AssetImporter.GetAtPath(LogoPath) as TextureImporter;
        if (importer == null)
            throw new InvalidOperationException($"Goal celebration logo was not found: {LogoPath}");

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 100f;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Point;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.userData = SetupVersion;
        importer.SaveAndReimport();
    }

    private static void ConfigurePrefab()
    {
        Sprite logoSprite = AssetDatabase.LoadAssetAtPath<Sprite>(LogoPath);
        if (logoSprite == null)
            throw new InvalidOperationException("The imported goal celebration Sprite could not be loaded.");

        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            CrystalAcquiredUI controller = root.GetComponent<CrystalAcquiredUI>();
            Canvas canvas = root.GetComponent<Canvas>();
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            if (controller == null || canvas == null || scaler == null)
                throw new InvalidOperationException("CrystalAcquiredUI prefab is missing its controller or Canvas.");

            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.localScale = Vector3.one;
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 650;
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            while (root.transform.childCount > 0)
                UnityEngine.Object.DestroyImmediate(root.transform.GetChild(0).gameObject);

            RectTransform presentation = CreateRect("GoalCelebrationPresentation", root.transform);
            Stretch(presentation);
            CanvasGroup presentationGroup = presentation.gameObject.AddComponent<CanvasGroup>();
            presentationGroup.alpha = 0f;
            presentationGroup.interactable = false;
            presentationGroup.blocksRaycasts = false;

            Image darkOverlay = CreateImage("DarkOverlay", presentation, Color.black);
            Stretch(darkOverlay.rectTransform);
            darkOverlay.raycastTarget = false;

            RectTransform sparkleLayer = CreateRect("PixelSparkleLayer", presentation);
            Stretch(sparkleLayer);
            RectTransform[] sparkles = CreateSparkles(sparkleLayer);

            Image logo = CreateImage("CongratulationLogo", presentation, Color.white);
            RectTransform logoRect = logo.rectTransform;
            Center(logoRect, new Vector2(705f, 516f));
            logo.sprite = logoSprite;
            logo.preserveAspect = true;
            logo.raycastTarget = false;

            Image flash = CreateImage("WhiteFlash", presentation, Color.white);
            Stretch(flash.rectTransform);
            flash.raycastTarget = false;

            SerializedObject serialized = new SerializedObject(controller);
            serialized.FindProperty("presentationRoot").objectReferenceValue = presentation.gameObject;
            serialized.FindProperty("canvasGroup").objectReferenceValue = presentationGroup;
            serialized.FindProperty("darkOverlay").objectReferenceValue = darkOverlay;
            serialized.FindProperty("flashOverlay").objectReferenceValue = flash;
            serialized.FindProperty("celebrationLogo").objectReferenceValue = logoRect;
            serialized.FindProperty("logoImage").objectReferenceValue = logo;
            SetObjectArray(serialized.FindProperty("sparkleVisuals"), sparkles.Cast<UnityEngine.Object>().ToArray());
            serialized.FindProperty("pauseMenu").objectReferenceValue = null;

            serialized.FindProperty("logoStartDelay").floatValue = 0.16f;
            serialized.FindProperty("popDuration").floatValue = 0.42f;
            serialized.FindProperty("logoDisplayDuration").floatValue = 1.15f;
            serialized.FindProperty("fadeOutDuration").floatValue = 0.16f;
            serialized.FindProperty("clearScreenDelay").floatValue = 0.08f;
            serialized.FindProperty("logoFinalScale").floatValue = 1f;
            serialized.FindProperty("logoStartScale").floatValue = 0.25f;
            serialized.FindProperty("overshootScale").floatValue = 1.16f;
            serialized.FindProperty("undershootScale").floatValue = 0.95f;
            serialized.FindProperty("logoStartYOffset").floatValue = -34f;
            serialized.FindProperty("logoStartRotation").floatValue = -4f;
            serialized.FindProperty("flashDuration").floatValue = 0.1f;
            serialized.FindProperty("slowMotionScale").floatValue = 0.18f;
            serialized.FindProperty("slowMotionDuration").floatValue = 0.24f;
            serialized.FindProperty("darkenAlpha").floatValue = 0.42f;
            serialized.FindProperty("darkenDelay").floatValue = 0.1f;
            serialized.FindProperty("darkenFadeDuration").floatValue = 0.18f;
            serialized.FindProperty("particlesEnabled").boolValue = true;
            serialized.FindProperty("sparkleBurstDuration").floatValue = 0.46f;
            serialized.FindProperty("sparkleRotationSpeed").floatValue = 95f;
            serialized.FindProperty("sparklePulseSpeed").floatValue = 3.2f;
            serialized.FindProperty("sparklePulseAmount").floatValue = 0.18f;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            presentation.gameObject.SetActive(false);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static RectTransform[] CreateSparkles(Transform parent)
    {
        Vector2[] positions =
        {
            new Vector2(-420f, 225f), new Vector2(-330f, 120f), new Vector2(-245f, 270f),
            new Vector2(-155f, 165f), new Vector2(-72f, 305f), new Vector2(72f, 305f),
            new Vector2(155f, 165f), new Vector2(245f, 270f), new Vector2(330f, 120f),
            new Vector2(420f, 225f), new Vector2(-455f, -30f), new Vector2(-325f, -165f),
            new Vector2(-185f, -245f), new Vector2(185f, -245f), new Vector2(325f, -165f),
            new Vector2(455f, -30f), new Vector2(-515f, 95f), new Vector2(515f, 95f)
        };
        Color[] colors =
        {
            new Color(1f, 0.24f, 0.72f, 1f),
            new Color(1f, 0.94f, 1f, 1f),
            new Color(0.72f, 0.42f, 1f, 1f)
        };

        List<RectTransform> results = new List<RectTransform>();
        for (int i = 0; i < positions.Length; i++)
        {
            RectTransform sparkle = CreateRect($"PixelSpark_{i + 1:00}", parent);
            Center(sparkle, new Vector2(28f, 28f));
            sparkle.anchoredPosition = positions[i];
            sparkle.localRotation = Quaternion.Euler(0f, 0f, i % 3 == 0 ? 45f : 0f);
            sparkle.gameObject.AddComponent<CanvasGroup>();

            float length = 12f + (i % 4) * 4f;
            float thickness = i % 5 == 0 ? 5f : 3f;
            Image horizontal = CreateImage("Horizontal", sparkle, colors[i % colors.Length]);
            Center(horizontal.rectTransform, new Vector2(length, thickness));
            Image vertical = CreateImage("Vertical", sparkle, colors[i % colors.Length]);
            Center(vertical.rectTransform, new Vector2(thickness, length));
            horizontal.raycastTarget = false;
            vertical.raycastTarget = false;
            results.Add(sparkle);
        }
        return results.ToArray();
    }

    private static Image CreateImage(string name, Transform parent, Color color)
    {
        RectTransform rect = CreateRect(name, parent);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        return image;
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject child = new GameObject(name, typeof(RectTransform));
        int uiLayer = LayerMask.NameToLayer("UI");
        child.layer = uiLayer >= 0 ? uiLayer : 0;
        RectTransform rect = child.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        return rect;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void Center(RectTransform rect, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = size;
    }

    private static void SetObjectArray(SerializedProperty property, UnityEngine.Object[] values)
    {
        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }
}
