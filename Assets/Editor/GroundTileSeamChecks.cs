using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

/// <summary>Isolated render checks. Never edits production tiles, cameras or colliders.</summary>
public static class GroundTileSeamChecks
{
    [MenuItem("Tools/Stage1/Check Ground Seam Blending")]
    public static void CheckFromMenu()
    {
        string path = Path.Combine(Application.temporaryCachePath, "GroundSeamChecks");
        Debug.Log(Run(path) + "\nImages and report: " + path);
    }

    public static string Run(string outputDirectory)
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run isolated checks in Edit Mode.");
        Directory.CreateDirectory(outputDirectory);
        Scene preview = EditorSceneManager.NewPreviewScene();
        var resources = new List<Object>();
        var report = new System.Text.StringBuilder();
        var errors = new List<string>();
        Application.LogCallback captureErrors = (message, stack, type) => {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message);
        };
        Application.logMessageReceived += captureErrors;
        try
        {
            var root = new GameObject("Seam check grid", typeof(Grid));
            SceneManager.MoveGameObjectToScene(root, preview);
            var go = new GameObject("Seam check tiles", typeof(Tilemap), typeof(TilemapRenderer));
            go.transform.SetParent(root.transform, false);
            var map = go.GetComponent<Tilemap>();
            var renderer = go.GetComponent<TilemapRenderer>();
            var texture = new Texture2D(16, 16, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            resources.Add(texture);
            var pattern = new Color[256];
            for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                float c = 0.22f + x * 0.018f + y * 0.009f + ((x + y) % 2) * 0.04f;
                pattern[x + y * 16] = new Color(c, c * 0.85f, c * 0.7f, x == 6 && y == 6 ? 0 : 1);
            }
            texture.SetPixels(pattern); texture.Apply();
            var sprite = Sprite.Create(texture, new Rect(0, 0, 16, 16), Vector2.one * 0.5f, 16, 0, SpriteMeshType.FullRect);
            resources.Add(sprite);
            var tile = ScriptableObject.CreateInstance<Tile>(); tile.sprite = sprite; resources.Add(tile);
            for (int y = 0; y < 2; y++) for (int x = 0; x < 2; x++) map.SetTile(new Vector3Int(x, y, 0), tile);
            var plain = new Material(Shader.Find("Sprites/Default")); resources.Add(plain);
            var seam = new Material(Shader.Find("IronBallGirl/Ground Tile Seam")); resources.Add(seam);
            var cameraGO = new GameObject("Seam check camera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraGO, preview);
            var camera = cameraGO.GetComponent<Camera>(); camera.scene = preview;
            camera.orthographic = true; camera.orthographicSize = 1;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
            camera.allowHDR = camera.allowMSAA = false; camera.enabled = false;
            camera.transform.position = new Vector3(1, 1, -10);
            renderer.sharedMaterial = plain;
            Color32[] original = Render(camera, 32, 32);
            renderer.sharedMaterial = seam;
            var blender = go.AddComponent<GroundTileSeamBlender>();
            blender.enableBlending = false; blender.RebuildNow();
            Require(Equal(original, Render(camera, 32, 32)), "OFF must exactly match Sprites/Default");
            blender.enableBlending = true; blender.RebuildNow();
            Color32[] corrected = Render(camera, 32, 32);
            int horizontal = 0, vertical = 0, corner = 0, total = 0;
            for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                int i = x + y * 32;
                Require(original[i].a == corrected[i].a, "Alpha changed");
                if (original[i].Equals(corrected[i])) continue;
                total++;
                Require(x != 0 && y != 0 && x != 31 && y != 31, "Outer contour changed");
                int px = x % 16, py = y % 16;
                Require(px < 2 || px >= 14 || py < 2 || py >= 14, "Tile interior changed");
                if (x >= 14 && x <= 17) vertical++;
                if (y >= 14 && y <= 17) horizontal++;
                if (x >= 14 && x <= 17 && y >= 14 && y <= 17) corner++;
            }
            Require(horizontal > 0 && vertical > 0 && corner > 0, "Both seam directions and 2x2 corner must be corrected");
            blender.RebuildNow();
            Require(Equal(corrected, Render(camera, 32, 32)), "Rebuild must be deterministic");
            map.SetTile(new Vector3Int(1, 1, 0), null);
            Require((bool)typeof(GroundTileSeamBlender).GetField("dirty", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(blender), "Tile deletion must invalidate cache");
            blender.RebuildNow();
            Color32[] removed = Render(camera, 32, 32);
            for (int y = 16; y < 32; y++) for (int x = 16; x < 32; x++) Require(removed[x + y * 32].a == 0, "Deleted tile has residue");
            map.SetTile(new Vector3Int(1, 1, 0), tile); blender.RebuildNow();
            Require(Equal(corrected, Render(camera, 32, 32)), "Adding tile must restore deterministic result");
            blender.enabled = false;
            Require(Equal(original, Render(camera, 32, 32)), "Disabling component must restore original rendering");
            blender.enabled = true; blender.RebuildNow();
            camera.orthographicSize = 2;
            Require(Equal(corrected, Render(camera, 64, 64, 16, 16, 32, 32)), "Integer zoom changed world-anchored correction");
            camera.orthographicSize = 1; camera.transform.position += Vector3.right;
            Color32[] moved = Render(camera, 32, 32);
            for (int y = 0; y < 32; y++) for (int x = 0; x < 16; x++)
                Require(moved[x + y * 32].Equals(corrected[x + 16 + y * 32]), "Camera translation changed correction");
            report.AppendLine("PASS synthetic: horizontal/vertical/corner, alpha + silhouette + interior protection, OFF/disable exact restoration, deterministic rebuild, add/remove events, integer camera translation/zoom. Changed pixels=" + total);
            Object.DestroyImmediate(root);

            // Real Stage1 art and tile transforms, rendered in a separate preview scene.
            foreach (GroundTileSeamBlender source in Object.FindObjectsByType<GroundTileSeamBlender>())
            {
                if (source.gameObject.scene.path != "Assets/Scenes/Stage1.unity" || !source.name.StartsWith("Ground")) continue;
                Tilemap sourceMap = source.GetComponent<Tilemap>();
                var gridGO = new GameObject("Real ground check", typeof(Grid));
                SceneManager.MoveGameObjectToScene(gridGO, preview);
                gridGO.GetComponent<Grid>().cellSize = sourceMap.layoutGrid.cellSize;
                var mapGO = new GameObject("Real ground", typeof(Tilemap), typeof(TilemapRenderer));
                mapGO.transform.SetParent(gridGO.transform, false);
                mapGO.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
                mapGO.transform.localScale = source.transform.lossyScale;
                var clone = mapGO.GetComponent<Tilemap>(); clone.tileAnchor = sourceMap.tileAnchor; clone.color = sourceMap.color;
                Vector3Int chosen = Vector3Int.zero; float score = float.MaxValue;
                foreach (Vector3Int p in sourceMap.cellBounds.allPositionsWithin)
                {
                    if (!sourceMap.HasTile(p)) continue;
                    clone.SetTile(p, sourceMap.GetTile(p)); clone.SetTileFlags(p, TileFlags.None);
                    clone.SetTransformMatrix(p, sourceMap.GetTransformMatrix(p)); clone.SetColor(p, sourceMap.GetColor(p));
                    if (sourceMap.HasTile(p + Vector3Int.right) && sourceMap.HasTile(p + Vector3Int.up) && sourceMap.HasTile(p + Vector3Int.right + Vector3Int.up))
                    {
                        float distance = Mathf.Abs(p.x) + Mathf.Abs(p.y);
                        if (distance < score) { chosen = p; score = distance; }
                    }
                }
                var cr = mapGO.GetComponent<TilemapRenderer>(); cr.sharedMaterial = plain;
                Vector3 centre = clone.CellToWorld(chosen + new Vector3Int(1, 1, 0));
                camera.transform.position = new Vector3(centre.x, centre.y, -10); camera.orthographicSize = 4;
                Color32[] before = Render(camera, 504, 336);
                Save(before, 504, 336, Path.Combine(outputDirectory, source.name + "-before.png"));
                cr.sharedMaterial = seam;
                var cb = mapGO.AddComponent<GroundTileSeamBlender>(); cb.RebuildNow();
                Color32[] after = Render(camera, 504, 336);
                int changed = 0;
                for (int i = 0; i < before.Length; i++)
                {
                    Require(before[i].a == after[i].a, "Real art alpha changed");
                    if (!before[i].Equals(after[i])) changed++;
                }
                Require(changed > 0, "No visible correction on " + source.name);
                Save(after, 504, 336, Path.Combine(outputDirectory, source.name + "-after.png"));
                cb.enableBlending = false; cb.RebuildNow();
                Require(Equal(before, Render(camera, 504, 336)), "Real art OFF differs from original");
                report.AppendLine("PASS " + source.name + ": source alpha retained, OFF pixel-identical; changed screen pixels=" + changed + "; corrected source pixels=" + cb.CorrectedPixelCount + "; tiles=" + cb.SupportedTileCount + "; cache=" + cb.CacheBytes + " bytes; rebuild=" + cb.LastBuildMilliseconds.ToString("F1") + " ms");
                Object.DestroyImmediate(gridGO);
            }
            string result = report.ToString(); File.WriteAllText(Path.Combine(outputDirectory, "checks.txt"), result);
            return result;
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(preview);
            foreach (Object resource in resources) if (resource != null) Object.DestroyImmediate(resource);
            Application.logMessageReceived -= captureErrors;
            Require(errors.Count == 0, "Unexpected Unity Error/Exception: " + string.Join("; ", errors));
        }
    }

    private static Color32[] Render(Camera camera, int width, int height, int x = 0, int y = 0, int cropWidth = 0, int cropHeight = 0)
    {
        RenderTexture old = RenderTexture.active;
        var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var texture = new Texture2D(cropWidth == 0 ? width : cropWidth, cropHeight == 0 ? height : cropHeight, TextureFormat.RGBA32, false);
        try
        {
            camera.targetTexture = rt; camera.aspect = (float)width / height; camera.Render(); RenderTexture.active = rt;
            texture.ReadPixels(new Rect(x, y, texture.width, texture.height), 0, 0); texture.Apply();
            return texture.GetPixels32();
        }
        finally { camera.targetTexture = null; RenderTexture.active = old; RenderTexture.ReleaseTemporary(rt); Object.DestroyImmediate(texture); }
    }

    private static void Save(Color32[] pixels, int width, int height, string path)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        try { texture.SetPixels32(pixels); texture.Apply(); File.WriteAllBytes(path, texture.EncodeToPNG()); }
        finally { Object.DestroyImmediate(texture); }
    }
    private static bool Equal(Color32[] a, Color32[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (!a[i].Equals(b[i])) return false;
        return true;
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
