using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 添付HP素材を編集可能なUnity UI階層へ組み立て、Camera/Respawn参照を接続する。
/// </summary>
public static class HpCameraFeatureSetup
{
    private const string ScenePath = "Assets/Scenes/CompletScene.unity";

    [MenuItem("Tools/鉄球少女/Apply HP Gauge And Camera Follow")]
    public static void Apply()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        SegmentHpBarUI hpUi = UnityEngine.Object.FindAnyObjectByType<SegmentHpBarUI>(FindObjectsInactive.Include);
        PlayerHealth health = UnityEngine.Object.FindAnyObjectByType<PlayerHealth>(FindObjectsInactive.Include);
        CameraFollow cameraFollow = UnityEngine.Object.FindAnyObjectByType<CameraFollow>(FindObjectsInactive.Include);
        if (hpUi == null || health == null || cameraFollow == null)
            throw new InvalidOperationException("CompletScene is missing SegmentHpBarUI, PlayerHealth or CameraFollow.");

        Canvas canvas = hpUi.GetComponentInChildren<Canvas>(true);
        if (canvas == null)
            throw new InvalidOperationException("PlayerHUD/HpCanvas is missing.");

        canvas.pixelPerfect = true;
        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler != null)
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
        }

        RectTransform root = FindRect(canvas.transform, "HPBarRoot");
        Image frame = FindImage(root, "Frame");
        Image emptyBar = FindImage(root, "EmptyBar");
        RectTransform damageMask = FindRect(root, "DamageMask");
        Image damageFill = FindImage(damageMask, "DamageFill");
        RectTransform hpMask = FindRect(root, "HpMask");
        Image hpFill = FindImage(hpMask, "HpFill");
        if (root == null || frame == null || emptyBar == null || damageMask == null
            || damageFill == null || hpMask == null || hpFill == null)
            throw new InvalidOperationException("既存HP Gauge hierarchyが不足しています。Frameは再生成しません。");

        SerializedObject hpSerialized = new SerializedObject(hpUi);
        hpSerialized.FindProperty("_playerHealth").objectReferenceValue = health;
        hpSerialized.FindProperty("_hpBarRoot").objectReferenceValue = root;
        hpSerialized.FindProperty("_emptyBar").objectReferenceValue = emptyBar;
        hpSerialized.FindProperty("_damageMask").objectReferenceValue = damageMask;
        hpSerialized.FindProperty("_damageFill").objectReferenceValue = damageFill;
        hpSerialized.FindProperty("_hpMask").objectReferenceValue = hpMask;
        hpSerialized.FindProperty("_hpFill").objectReferenceValue = hpFill;
        hpSerialized.FindProperty("_frame").objectReferenceValue = frame;
        hpSerialized.FindProperty("_hpSmoothDuration").floatValue = 0.25f;
        hpSerialized.FindProperty("_damageDelay").floatValue = 0.10f;
        hpSerialized.FindProperty("_damageSmoothDuration").floatValue = 0.25f;
        hpSerialized.FindProperty("_damageShakeDuration").floatValue = 0.14f;
        hpSerialized.FindProperty("_damageShakeAmount").vector2Value = new Vector2(4f, 1.5f);
        hpSerialized.ApplyModifiedPropertiesWithoutUndo();
        hpUi.AlignVisualsToFrame();
        EditorUtility.SetDirty(hpUi);

        SerializedObject cameraSerialized = new SerializedObject(cameraFollow);
        cameraSerialized.FindProperty("_horizontalLookAhead").floatValue = 2.2f;
        cameraSerialized.FindProperty("_lookAheadSmoothTime").floatValue = 0.15f;
        cameraSerialized.FindProperty("_lookAheadVelocityThreshold").floatValue = 0.1f;
        cameraSerialized.FindProperty("_swingLookAheadMultiplier").floatValue = 0.35f;
        cameraSerialized.ApplyModifiedPropertiesWithoutUndo();

        GimmickRespawnController gimmickRespawn = UnityEngine.Object.FindAnyObjectByType<GimmickRespawnController>(FindObjectsInactive.Include);
        if (gimmickRespawn != null)
            AssignObject(gimmickRespawn, "cameraFollow", cameraFollow);

        DeathRespawnManager deathRespawn = UnityEngine.Object.FindAnyObjectByType<DeathRespawnManager>(FindObjectsInactive.Include);
        if (deathRespawn != null)
            AssignObject(deathRespawn, "_cameraFollow", cameraFollow);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[HpCameraFeatureSetup] HPBarRoot hierarchy and CameraFollow/Respawn integration applied.");
    }

    private static RectTransform FindRect(Transform root, string name)
    {
        if (root == null)
            return null;
        RectTransform[] rects = root.GetComponentsInChildren<RectTransform>(true);
        foreach (RectTransform rect in rects)
        {
            if (rect.name == name)
                return rect;
        }
        return null;
    }

    private static Image FindImage(Transform root, string name)
    {
        if (root == null)
            return null;
        Image[] images = root.GetComponentsInChildren<Image>(true);
        foreach (Image image in images)
        {
            if (image.name == name)
                return image;
        }
        return null;
    }

    private static void AssignObject(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null)
            throw new InvalidOperationException($"{target.GetType().Name}.{propertyName} was not found.");
        property.objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
