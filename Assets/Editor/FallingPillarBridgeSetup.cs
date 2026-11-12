using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Imports the stone artwork and updates the existing Stage2 pillar prefab and placement.</summary>
public static class FallingPillarBridgeSetup
{
    public const string PrefabPath = "Assets/Prefabs/Gimmicks/FallingPillarBridge.prefab";
    private const string Art = "Assets/Art/Gimmicks/FallingPillarBridge";
    private const float Ppu = 24f;
    private const int UpperPixels = 301;
    private const int BasePixels = 56;
    private const float BaseHeight = BasePixels / Ppu;
    private const float Width = 52f / Ppu;
    private const float FallenLength = 14.2f * 21f / Ppu;

    [MenuItem("Tools/Tekkyu/Stage2/Apply Stone Pillar")]
    public static void CreateAndPlace()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != "Assets/Scenes/Stage2.unity")
            throw new InvalidOperationException("Open Stage2 in Edit Mode.");
        bool wasDirty = scene.isDirty;
        var instance = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<FallingPillarBridge>(true)).Single();
        Vector3 position = instance.transform.position;
        Physics2D.SyncTransforms();
        var shore = Physics2D.RaycastAll(new Vector2(position.x, 25f), Vector2.down, 40f, LayerMask.GetMask("Default"))
            .FirstOrDefault(hit => hit.collider is CompositeCollider2D && !hit.collider.isTrigger);
        if (!shore.collider) throw new InvalidOperationException("Stage2 shore was not found.");

        Sprite[] stages = ImportPillarArt(out Sprite stump);
        var dust = BuildDust();
        var fragment = UpdateFragment();
        var landing = BuildLandingAudio();

        GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var component = contents.GetComponent<FallingPillarBridge>();
            var settings = new SerializedObject(component);
            var renderer = (SpriteRenderer)settings.FindProperty("pillarVisual").objectReferenceValue;
            var footing = (GameObject)settings.FindProperty("brokenBase").objectReferenceValue;
            renderer.gameObject.name = "StoneColumn";
            renderer.sprite = stages[0];
            footing.GetComponent<SpriteRenderer>().sprite = stump;
            footing.transform.localPosition = Vector3.down * BaseHeight;
            footing.layer = contents.layer;
            footing.tag = contents.tag;
            if (!footing.TryGetComponent(out Rigidbody2D footBody))
                footBody = footing.AddComponent<Rigidbody2D>();
            footBody.bodyType = RigidbodyType2D.Static;
            var oldFootCollider = footing.GetComponent<BoxCollider2D>();
            if (oldFootCollider) Object.DestroyImmediate(oldFootCollider);
            if (!footing.TryGetComponent(out PolygonCollider2D footCollider))
                footCollider = footing.AddComponent<PolygonCollider2D>();
            footCollider.SetPath(0, Profile(new[] {
                new Vector2(2, 1), new Vector2(82, 1), new Vector2(82, 10),
                new Vector2(76, 18), new Vector2(70, 30), new Vector2(67, 34),
                new Vector2(67, 51), new Vector2(60, 53), new Vector2(47, 49),
                new Vector2(32, 53), new Vector2(17, 51), new Vector2(17, 34),
                new Vector2(13, 30), new Vector2(7, 18), new Vector2(2, 10)
            }, 0));
            footing.SetActive(false);

            var oldStanding = contents.GetComponent<BoxCollider2D>();
            if (oldStanding) Object.DestroyImmediate(oldStanding);
            if (!contents.TryGetComponent(out PolygonCollider2D standing))
                standing = contents.AddComponent<PolygonCollider2D>();
            standing.SetPath(0, Profile(new[] {
                new Vector2(2, 1), new Vector2(82, 1), new Vector2(82, 10),
                new Vector2(77, 18), new Vector2(70, 30), new Vector2(67, 40),
                new Vector2(67, 317), new Vector2(71, 324), new Vector2(71, 346),
                new Vector2(62, 353), new Vector2(54, 356), new Vector2(28, 356),
                new Vector2(20, 353), new Vector2(12, 346), new Vector2(12, 324),
                new Vector2(17, 317), new Vector2(17, 40), new Vector2(13, 30),
                new Vector2(7, 18), new Vector2(2, 10)
            }, BasePixels));
            var sprites = settings.FindProperty("damageStages");
            sprites.arraySize = stages.Length;
            for (int i = 0; i < stages.Length; i++)
                sprites.GetArrayElementAtIndex(i).objectReferenceValue = stages[i];
            Vector2 pivot = new Vector2(25f / Ppu, -2f / Ppu);
            settings.FindProperty("fallPivotOffset").vector2Value = pivot;
            settings.FindProperty("landingSeparation").floatValue = 0.85f * 21f / Ppu;
            settings.FindProperty("landingDrop").floatValue = BaseHeight - Width * 0.5f + pivot.x + pivot.y;
            settings.FindProperty("fallenColliderSize").vector2Value = new Vector2(Width, FallenLength);
            settings.FindProperty("fallenColliderOffset").vector2Value = new Vector2(0f, FallenLength * 0.5f + 0.05f);
            Vector2[] fallen = Profile(new[] {
                new Vector2(24, 6), new Vector2(59, 6), new Vector2(67, 24),
                new Vector2(67, 264), new Vector2(71, 270), new Vector2(71, 291),
                new Vector2(62, 297), new Vector2(54, 300), new Vector2(28, 300),
                new Vector2(20, 297), new Vector2(12, 291), new Vector2(12, 270),
                new Vector2(16, 264), new Vector2(16, 24)
            }, 0);
            var fallenPoints = settings.FindProperty("fallenColliderPoints");
            fallenPoints.arraySize = fallen.Length;
            for (int i = 0; i < fallen.Length; i++)
                fallenPoints.GetArrayElementAtIndex(i).vector2Value = fallen[i];
            settings.FindProperty("hitClip").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SFX/Imported/StoneSwitchPress.mp3");
            settings.FindProperty("collapseClip").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SFX/Crystal/砕ける音.mp3");
            settings.FindProperty("metalFragmentPrefab").objectReferenceValue = fragment;
            settings.FindProperty("landingClip").objectReferenceValue = landing;
            settings.FindProperty("landingDustPrefab").objectReferenceValue = dust;
            settings.ApplyModifiedPropertiesWithoutUndo();
            if (!PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath))
                throw new InvalidOperationException("Could not save the pillar prefab.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }

        Undo.RecordObject(instance.transform, "Align stone pillar footing to Stage2 shore");
        instance.transform.position = new Vector3(position.x, shore.point.y + BaseHeight, position.z);
        PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!wasDirty && !EditorSceneManager.SaveScene(scene))
            throw new InvalidOperationException("Could not save Stage2.");
        Debug.Log($"[PillarBridge] {(UpperPixels + BasePixels) / Ppu:F2}-unit pillar, {FallenLength:F2}-unit span, floor {shore.point.y:F3}, scene saved={!wasDirty}.");
    }

    private static Vector2[] Profile(Vector2[] pixels, float pivotY)
    {
        return pixels.Select(point => new Vector2(point.x - 42f, point.y - pivotY) / Ppu).ToArray();
    }

    private static Sprite[] ImportPillarArt(out Sprite stump)
    {
        var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!source.LoadImage(File.ReadAllBytes(Art + "/StoneColumnStages_Source.png"))
                || source.width != 1536 || source.height != 1024)
                throw new InvalidOperationException("Expected the 1536x1024 four-stage stone source sheet.");

            int[] centers = { 207, 559, 943, 1327 };
            var stages = new Sprite[4];
            for (int i = 0; i < 3; i++)
            {
                var frame = new Texture2D(84, UpperPixels + BasePixels, TextureFormat.RGBA32, false);
                // Keep the foot anchored. Use the same pixel grid and split height for all upright states.
                SampleRows(source, frame, centers[i], 28, 832, BasePixels, UpperPixels);
                SampleRows(source, frame, centers[i], 832, 999, 0, BasePixels);
                stages[i] = SaveSprite(frame, "Pillar_" + i, (float)BasePixels / frame.height);
            }
            var top = new Texture2D(84, UpperPixels, TextureFormat.RGBA32, false);
            SampleRows(source, top, centers[3], 28, 700, 0, UpperPixels);
            stages[3] = SaveSprite(top, "Pillar_3", 0f);
            var bottom = new Texture2D(84, BasePixels, TextureFormat.RGBA32, false);
            // Preserve the dimensions of the visible stepped foundation in the remaining stump.
            const int foundationPixels = 32;
            SampleRows(source, bottom, centers[3], 707, 897, foundationPixels, BasePixels - foundationPixels);
            SampleRows(source, bottom, centers[3], 897, 999, 0, foundationPixels);
            stump = SaveSprite(bottom, "BrokenFooting", 0f);
            return stages;
        }
        finally
        {
            Object.DestroyImmediate(source);
        }
    }

    private static void SampleRows(Texture2D source, Texture2D frame, int center, int top, int bottom, int offset, int rows)
    {
        const int sourceWidth = 224;
        for (int y = 0; y < rows; y++)
        {
            int sourceY = source.height - 1 - (bottom - 1 - (int)((y + 0.5f) * (bottom - top) / rows));
            for (int x = 0; x < frame.width; x++)
            {
                int sourceX = center - sourceWidth / 2 + (int)((x + 0.5f) * sourceWidth / frame.width);
                frame.SetPixel(x, offset + y, source.GetPixel(sourceX, sourceY));
            }
        }
    }

    private static Sprite SaveSprite(Texture2D texture, string name, float pivotY)
    {
        string path = Art + "/" + name + ".png";
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = Ppu;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)SpriteAlignment.Custom;
        settings.spritePivot = new Vector2(0.5f, pivotY);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static GameObject UpdateFragment()
    {
        const string path = Art + "/PillarMetalFragment.prefab";
        var rock = AssetDatabase.LoadAllAssetsAtPath("Assets/image_/RockDebris.png")
            .OfType<Sprite>().First(s => s.name == "RockDebris_0");
        GameObject contents = PrefabUtility.LoadPrefabContents(path);
        try
        {
            contents.GetComponent<SpriteRenderer>().sprite = rock;
            contents.transform.localScale = Vector3.one * (0.24f / Mathf.Max(rock.bounds.size.x, rock.bounds.size.y));
            return PrefabUtility.SaveAsPrefabAsset(contents, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    private static ParticleSystem BuildDust()
    {
        GameObject go = new GameObject("ColumnLandingDust");
        try
        {
            var dust = go.AddComponent<ParticleSystem>();
            var main = dust.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 0.3f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.38f, 0.78f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.4f, 1.15f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.72f, 0.59f, 0.49f, 0.8f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = dust.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 20) });
            var shape = dust.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(1.45f, 0.12f, 0.02f);
            var velocity = dust.velocityOverLifetime;
            velocity.enabled = true;
            velocity.x = new ParticleSystem.MinMaxCurve(-3.2f, 3.2f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.8f, 2.3f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            var fade = dust.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Generated/AtmosphereEffects/GroundDust.mat");
            renderer.sortingOrder = 8;
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, Art + "/ColumnLandingDust.prefab");
            return prefab.GetComponent<ParticleSystem>();
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    private static AudioClip BuildLandingAudio()
    {
        const string path = Art + "/ColumnLanding.wav";
        const int rate = 44100;
        int samples = (int)(rate * 0.85f);
        var noise = new System.Random(117);
        using (var writer = new BinaryWriter(File.Create(path)))
        {
            writer.Write(new[] { 'R', 'I', 'F', 'F' });
            writer.Write(36 + samples * 2);
            writer.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(rate);
            writer.Write(rate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(new[] { 'd', 'a', 't', 'a' });
            writer.Write(samples * 2);
            double phase = 0, grit = 0;
            for (int i = 0; i < samples; i++)
            {
                double time = (double)i / rate;
                phase += 2 * Math.PI * (42 + 48 * Math.Exp(-11 * time)) / rate;
                grit = grit * 0.93 + (noise.NextDouble() * 2 - 1) * 0.07;
                double thud = (Math.Sin(phase) + 0.35 * Math.Sin(phase * 2.08)) * 0.49 * Math.Exp(-8 * time);
                double impact = grit * 0.9 * Math.Exp(-12 * time);
                double envelope = Math.Min(1, time * 420) * Math.Min(1, (0.85 - time) * 22);
                writer.Write((short)(Math.Clamp((thud + impact) * envelope, -1, 1) * short.MaxValue));
            }
        }
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    }
}

