using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class RollingIronBallSetup
{
    public const string SpritePath = "Assets/Art/Gimmicks/RollingIronBall.png";
    public const string PrefabPath = "Assets/Prefabs/Gimmicks/RollingIronBall.prefab";
    public const string MaterialPath = "Assets/Prefabs/Gimmicks/RollingIronBall.physicsMaterial2D";

    public static string Create(string suppliedImagePath)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
            throw new InvalidOperationException("Prefab already exists; preserve Inspector adjustments.");
        Player player = UnityEngine.Object.FindFirstObjectByType<Player>();
        Collider2D playerBody = player != null ? player.GetComponents<Collider2D>().FirstOrDefault(c => !c.isTrigger) : null;
        if (playerBody == null) throw new InvalidOperationException("Open a production scene containing Player first.");
        float diameter = playerBody.bounds.size.y * 1.5f;

        Directory.CreateDirectory(Path.GetDirectoryName(SpritePath));
        if (!File.Exists(SpritePath)) File.Copy(suppliedImagePath, SpritePath);
        else if (!File.ReadAllBytes(SpritePath).SequenceEqual(File.ReadAllBytes(suppliedImagePath)))
            throw new InvalidOperationException("Another image already exists at " + SpritePath);
        AssetDatabase.ImportAsset(SpritePath, ImportAssetOptions.ForceSynchronousImport);
        var image = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        ImageConversion.LoadImage(image, File.ReadAllBytes(SpritePath));
        int minX = image.width, minY = image.height, maxX = -1, maxY = -1;
        for (int y = 0; y < image.height; y++)
            for (int x = 0; x < image.width; x++)
                if (image.GetPixel(x, y).a > 0.5f)
                { minX = Mathf.Min(minX, x); minY = Mathf.Min(minY, y); maxX = Mathf.Max(maxX, x); maxY = Mathf.Max(maxY, y); }
        if (maxY < minY) throw new InvalidOperationException("Image has no opaque ball.");
        Vector2 pivot = new Vector2((minX + maxX + 1f) * 0.5f / image.width, (minY + maxY + 1f) * 0.5f / image.height);
        float opaqueHeight = maxY - minY + 1;
        UnityEngine.Object.DestroyImmediate(image);
        var importer = (TextureImporter)AssetImporter.GetAtPath(SpritePath);
        importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 100f; importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false; importer.alphaIsTransparency = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.wrapMode = TextureWrapMode.Clamp;
        var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)SpriteAlignment.Custom; settings.spritePivot = pivot;
        settings.spriteMeshType = SpriteMeshType.FullRect; importer.SetTextureSettings(settings);
        importer.SaveAndReimport();

        var material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(MaterialPath);
        if (material == null)
        {
            material = new PhysicsMaterial2D("RollingIronBall") { friction = 0.65f, bounciness = 0.03f };
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        var root = new GameObject("RollingIronBall");
        try
        {
            root.layer = 0;
            var body = root.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Dynamic; body.mass = 8f; body.gravityScale = 1f;
            body.linearDamping = 0.03f; body.angularDamping = 0.18f;
            body.constraints = RigidbodyConstraints2D.None;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            var collider = root.AddComponent<CircleCollider2D>(); collider.radius = diameter * 0.5f;
            collider.sharedMaterial = material;
            var visual = new GameObject("Visual"); visual.layer = root.layer; visual.transform.SetParent(root.transform, false);
            visual.transform.localScale = Vector3.one * (diameter * 100f / opaqueHeight);
            var renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath); renderer.sortingOrder = 5;
            root.AddComponent<RollingIronBall>();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        return "Created " + PrefabPath + "; diameter=" + diameter + "; player height=" + playerBody.bounds.size.y
            + "; mass=8; friction=0.65; source pixels unchanged.";
    }

    [MenuItem("Tools/Gimmicks/Place Rolling Iron Ball Near Player")]
    public static void PlaceNearPlayer()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) throw new InvalidOperationException("Create the RollingIronBall prefab first.");
        var player = UnityEngine.Object.FindFirstObjectByType<Player>();
        if (player == null) throw new InvalidOperationException("No Player in the open scene.");
        float radius = prefab.GetComponent<CircleCollider2D>().radius;
        foreach (float offset in new[] { 10f, 13f, 16f, -10f, -13f })
        {
            Vector2 origin = (Vector2)player.transform.position + new Vector2(offset, 10f);
            foreach (RaycastHit2D hit in Physics2D.RaycastAll(origin, Vector2.down, 45f, (1 << 0) | (1 << 6)))
            {
                if (hit.collider.isTrigger || hit.normal.y < 0.8f
                    || hit.rigidbody != null && hit.rigidbody.bodyType == RigidbodyType2D.Dynamic) continue;
                Vector2 position = hit.point + Vector2.up * (radius + 0.06f);
                if (Physics2D.OverlapCircleAll(position, radius + 0.01f)
                    .Any(c => !c.isTrigger && c.gameObject.scene == player.gameObject.scene)) continue;
                var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, player.gameObject.scene);
                Undo.RegisterCreatedObjectUndo(root, "Place Rolling Iron Ball"); root.transform.position = position;
                EditorSceneManager.MarkSceneDirty(root.scene); Selection.activeGameObject = root;
                return;
            }
        }
        throw new InvalidOperationException("No clear floor near Player. Drag the prefab into the desired location.");
    }
}
