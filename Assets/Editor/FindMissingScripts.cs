using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 非アクティブ・子オブジェクト・Prefab アセットを含め Missing Script を検出する。
/// メニュー: Tools / Find Missing Scripts
/// </summary>
public static class FindMissingScripts
{
    [MenuItem("Tools/Find Missing Scripts")]
    static void FindMissing()
    {
        int count = 0;
        var allObjects = Resources.FindObjectsOfTypeAll<GameObject>();

        foreach (var go in allObjects)
        {
            if (go == null)
                continue;

            // プロジェクトウィンドウ上の非シーン Prefab も対象（Resources.FindObjectsOfTypeAll の仕様）
            if (EditorUtility.IsPersistent(go) && !PrefabUtility.IsPartOfPrefabAsset(go))
                continue;

            var comps = go.GetComponents<Component>();
            foreach (var c in comps)
            {
                if (c != null)
                    continue;

                string sceneName = go.scene.IsValid() ? go.scene.name : "(Prefab/Asset)";
                string path = GetHierarchyPath(go);
                Debug.LogWarning(
                    $"Missing Script: {go.name} | Path: {path} | Scene: {sceneName}",
                    go);
                count++;
            }
        }

        if (count == 0)
            Debug.Log("Scan complete. Missing Script: none (inactive & children included).");
        else
            Debug.Log($"Scan complete. Missing Script count: {count}");
    }

    static string GetHierarchyPath(GameObject go)
    {
        string path = go.name;
        Transform t = go.transform.parent;
        while (t != null)
        {
            path = t.name + "/" + path;
            t = t.parent;
        }
        return path;
    }
}
