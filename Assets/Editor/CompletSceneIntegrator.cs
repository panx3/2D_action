using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CompletSceneIntegrator
{
    private const string SamplePath = "Assets/Scenes/SampleScene.unity";
    private const string TestPath = "Assets/Scenes/TestScene_Gimmicks.unity";
    private const string CompletePath = "Assets/Scenes/CompletScene.unity";

    [MenuItem("鉄球少女/CompletScene/最終Sceneを統合")]
    public static void Build()
    {
        Scene complete = EditorSceneManager.OpenScene(TestPath, OpenSceneMode.Single);
        EditorSceneManager.SaveScene(complete, CompletePath, true);

        GameObject oldPlayer = FindRoot(complete, "Player");
        GameObject oldBall = FindRoot(complete, "morningstar");
        GameObject oldChain = FindRoot(complete, "ChainLine");
        GameObject oldSpawn = FindRoot(complete, "SpawnPoint");
        Vector3 spawnPosition = oldPlayer.transform.position;

        Scene sample = EditorSceneManager.OpenScene(SamplePath, OpenSceneMode.Additive);
        Camera sampleCamera = FindRoot(sample, "Main Camera").GetComponent<Camera>();
        Camera completeCamera = FindRoot(complete, "Main Camera").GetComponent<Camera>();
        completeCamera.clearFlags = sampleCamera.clearFlags;
        completeCamera.backgroundColor = sampleCamera.backgroundColor;
        GameObject transfer = new GameObject("__SampleTransfer");
        SceneManager.MoveGameObjectToScene(transfer, sample);
        MoveUnder(FindRoot(sample, "Player"), transfer.transform);
        MoveUnder(FindRoot(sample, "morningstar"), transfer.transform);
        MoveUnder(FindRoot(sample, "ChainLine"), transfer.transform);
        MoveUnder(FindRoot(sample, "SpawnPoint"), transfer.transform);
        MoveUnder(FindRoot(sample, "PlayerHUD"), transfer.transform);

        GameObject imported = UnityEngine.Object.Instantiate(transfer);
        imported.name = "__ImportedFromSampleScene";
        SceneManager.MoveGameObjectToScene(imported, complete);
        EditorSceneManager.CloseScene(sample, true);

        Dictionary<UnityEngine.Object, UnityEngine.Object> replacements = BuildReplacementMap(oldPlayer, imported.transform.Find("Player"));
        AddReplacementMap(replacements, oldBall, imported.transform.Find("morningstar"));
        AddReplacementMap(replacements, oldChain, imported.transform.Find("ChainLine"));
        AddReplacementMap(replacements, oldSpawn, imported.transform.Find("SpawnPoint"));
        RemapSceneReferences(complete, replacements);

        Scene discarded = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        DeactivateHierarchy(oldPlayer);
        DeactivateHierarchy(oldBall);
        DeactivateHierarchy(oldChain);
        DeactivateHierarchy(oldSpawn);
        SceneManager.MoveGameObjectToScene(oldPlayer, discarded);
        SceneManager.MoveGameObjectToScene(oldBall, discarded);
        SceneManager.MoveGameObjectToScene(oldChain, discarded);
        SceneManager.MoveGameObjectToScene(oldSpawn, discarded);
        EditorSceneManager.CloseScene(discarded, true);

        Transform player = imported.transform.Find("Player");
        Transform ball = imported.transform.Find("morningstar");
        Transform sampleSpawn = imported.transform.Find("SpawnPoint");
        Transform hpCanvas = imported.transform.Find("PlayerHUD/HpCanvas");
        Vector3 ballOffset = ball.position - player.position;
        player.position = spawnPosition;
        ball.position = spawnPosition + ballOffset;
        sampleSpawn.position = spawnPosition;
        bool hpCanvasWasActive = hpCanvas.gameObject.activeSelf;
        hpCanvas.gameObject.SetActive(false);
        GameObjectUtility.RemoveMonoBehavioursWithMissingScript(hpCanvas.gameObject);
        hpCanvas.gameObject.SetActive(hpCanvasWasActive);

        Player newPlayer = UnityEngine.Object.FindAnyObjectByType<Player>(FindObjectsInactive.Include);
        CameraFollow cameraFollow = UnityEngine.Object.FindAnyObjectByType<CameraFollow>(FindObjectsInactive.Include);
        SetObjectReference(cameraFollow, "_target", newPlayer.transform);

        EditorSceneManager.MarkSceneDirty(complete);
        if (!EditorSceneManager.SaveScene(complete, CompletePath))
            throw new InvalidOperationException("CompletSceneを保存できませんでした。");

        Debug.Log("[CompletScene] TestScene_Gimmicksを土台にSampleSceneのPlayer・背景・HUDを統合しました。");
    }

    private static void MoveUnder(GameObject child, Transform parent)
    {
        if (child == null)
            throw new InvalidOperationException("SampleSceneの移植対象が見つかりません。");
        child.transform.SetParent(parent, true);
    }

    private static GameObject FindRoot(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root.name == name)
                return root;
        throw new InvalidOperationException(scene.path + " に " + name + " がありません。");
    }

    private static Dictionary<UnityEngine.Object, UnityEngine.Object> BuildReplacementMap(GameObject oldRoot, Transform newRoot)
    {
        Dictionary<UnityEngine.Object, UnityEngine.Object> map = new Dictionary<UnityEngine.Object, UnityEngine.Object>();
        AddReplacementMap(map, oldRoot, newRoot);
        return map;
    }

    private static void AddReplacementMap(Dictionary<UnityEngine.Object, UnityEngine.Object> map, GameObject oldRoot, Transform newRoot)
    {
        if (oldRoot == null || newRoot == null)
            return;
        map[oldRoot] = newRoot.gameObject;
        foreach (Component oldComponent in oldRoot.GetComponents<Component>())
        {
            if (oldComponent == null)
                continue;
            Component replacement = newRoot.GetComponent(oldComponent.GetType());
            if (replacement != null)
                map[oldComponent] = replacement;
        }
    }

    private static void RemapSceneReferences(Scene scene, Dictionary<UnityEngine.Object, UnityEngine.Object> replacements)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        foreach (Component component in root.GetComponentsInChildren<Component>(true))
        {
            if (component == null)
                continue;
            SerializedObject serialized = new SerializedObject(component);
            SerializedProperty property = serialized.GetIterator();
            bool enterChildren = true;
            bool changed = false;
            while (property.Next(enterChildren))
            {
                enterChildren = false;
                if (property.propertyType != SerializedPropertyType.ObjectReference
                    || property.propertyPath == "m_GameObject"
                    || property.propertyPath == "m_Script")
                    continue;
                if (property.objectReferenceValue != null
                    && replacements.TryGetValue(property.objectReferenceValue, out UnityEngine.Object replacement))
                {
                    property.objectReferenceValue = replacement;
                    changed = true;
                }
            }
            if (changed)
                serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static void SetObjectReference(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
    {
        if (target == null)
            throw new InvalidOperationException(propertyName + " の設定対象が見つかりません。");
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null)
            throw new InvalidOperationException(target.GetType().Name + "." + propertyName + " が見つかりません。");
        property.objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void DeactivateHierarchy(GameObject root)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            child.gameObject.SetActive(false);
    }

}
