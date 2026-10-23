using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class GroundTileSeamSetup
{
    [MenuItem("Tools/Stage1/Set Up Ground Seam Blending")]
    public static void Apply()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (Application.isPlaying || scene.path != "Assets/Scenes/Stage1.unity")
            throw new InvalidOperationException("Open Stage1 in Edit Mode first.");
        const string materialPath = "Assets/GroundTileSeam.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            Shader shader = Shader.Find("IronBallGirl/Ground Tile Seam");
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("Ground seam shader is unavailable.");
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, materialPath);
        }
        int count = 0;
        foreach (var root in scene.GetRootGameObjects())
        foreach (var map in root.GetComponentsInChildren<Tilemap>(true))
        {
            if (!map.name.StartsWith("Ground", StringComparison.OrdinalIgnoreCase)) continue;
            if (map.GetComponent<TilemapCollider2D>() == null) continue;
            bool ground = false;
            foreach (Vector3Int p in map.cellBounds.allPositionsWithin)
            {
                Sprite sprite = map.GetSprite(p);
                if (sprite != null && AssetDatabase.GetAssetPath(sprite) == "Assets/image_/Stage/地面ドット絵.png")
                { ground = true; break; }
            }
            if (!ground) continue;
            var renderer = map.GetComponent<TilemapRenderer>();
            if (renderer.sharedMaterial != material && renderer.sharedMaterial != null && renderer.sharedMaterial.shader.name != "Sprites/Default")
            {
                Debug.LogWarning("Existing custom ground material preserved: " + map.name, map);
                continue;
            }
            var blender = map.GetComponent<GroundTileSeamBlender>();
            if (blender == null) blender = Undo.AddComponent<GroundTileSeamBlender>(map.gameObject);
            Undo.RecordObject(renderer, "Ground seam material");
            renderer.sharedMaterial = material;
            EditorUtility.SetDirty(renderer);
            blender.RebuildNow();
            count++;
        }
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("[GroundTileSeamSetup] Applied to " + count + " ground Tilemaps. Tile placement and colliders preserved.");
    }
}
