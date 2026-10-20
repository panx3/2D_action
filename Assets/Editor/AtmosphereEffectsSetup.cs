using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>既存のScene所有者に演出だけを接続する。再実行しても影や設定を重複させない。</summary>
public static class AtmosphereEffectsSetup
{
    private const string Folder = "Assets/Generated/AtmosphereEffects";
    private const string TitlePath = "Assets/Scenes/TitleScene.unity";
    private const string GamePath = "Assets/Scenes/CompletScene.unity";

    [MenuItem("鉄球少女/演出/接地影・火花・空気遠近・ドット花びらを適用")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play Mode before installing presentation effects.");
        foreach (string path in new[] { TitlePath, GamePath })
        {
            Scene opened = SceneManager.GetSceneByPath(path);
            if (opened.isLoaded && opened.isDirty)
                throw new InvalidOperationException(path + " has unsaved edits; preserve them before installing.");
        }
        EnsureFolder();
        CreatePetalAtlas();
        CreateSparkMaterial();
        ModifyScene(GamePath, ConfigureGame);
        ModifyScene(TitlePath, scene => {
            var atmosphere = Components<TitleAtmosphere>(scene).FirstOrDefault();
            if (atmosphere == null) throw new InvalidOperationException("TitleAtmosphere is missing.");
            ConfigureTitle(atmosphere);
        });
        Debug.Log("[AtmosphereEffects] Installed ground shadows, impact sparks, opaque atmospheric backgrounds and four pixel petal variants.");
    }

    private static T[] Components<T>(Scene scene) where T : Component =>
        scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();

    private static void ModifyScene(string path, Action<Scene> configure)
    {
        Scene previous = SceneManager.GetActiveScene();
        Scene scene = SceneManager.GetSceneByPath(path);
        bool wasOpen = scene.isLoaded;
        if (!wasOpen) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            configure(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save " + path);
        }
        finally
        {
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            if (!wasOpen) EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static void ConfigureGame(Scene scene)
    {
        var launcher = Components<MorningStarLauncher>(scene).FirstOrDefault();
        if (launcher == null) throw new InvalidOperationException("MorningStarLauncher is missing.");
        var settings = new SerializedObject(launcher);
        var player = settings.FindProperty("player").objectReferenceValue as Player;
        var ball = settings.FindProperty("morningStarRb").objectReferenceValue as Rigidbody2D;
        if (player == null || ball == null) throw new InvalidOperationException("Player / ball reference is missing.");
        Sprite shadow = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Generated/Tapioca/GroundShadow.png");
        if (shadow == null) throw new InvalidOperationException("Tapioca ground shadow sprite is missing.");
        ConfigureShadow(player.gameObject, shadow, 1.55f, 0.52f);
        var ballCollider = ball.GetComponent<Collider2D>();
        ConfigureShadow(ball.gameObject, shadow, Mathf.Clamp(ballCollider.bounds.size.x * 0.95f, 0.65f, 2f), 0.48f);
        var sparks = ball.GetComponent<MorningStarImpactSparks>();
        if (sparks == null) sparks = ball.gameObject.AddComponent<MorningStarImpactSparks>();
        ConfigureImpactEffects(sparks);
        ConfigurePlayerMotionFeedback(player, launcher, ball);
        Transform background = scene.GetRootGameObjects().First(root => root.name == "BackgroundRoot").transform;
        ConfigureBackground(background.Find("BG_Far/SkySea").GetComponent<SpriteRenderer>(), 0);
        ConfigureBackground(background.Find("BG_Mid/FloatingIslands").GetComponent<SpriteRenderer>(), 1);
        ConfigureBackground(background.Find("BG_Front/ForeRuins").GetComponent<SpriteRenderer>(), 2);
    }

    public static void ConfigureImpactEffects(MorningStarImpactSparks effect)
    {
        var settings = new SerializedObject(effect);
        settings.FindProperty("sparkMaterial").objectReferenceValue = CreateSparkMaterial();
        settings.FindProperty("emberMaterial").objectReferenceValue = CreateEmberMaterial();
        settings.FindProperty("sparkTrailMaterial").objectReferenceValue = UpdateMetalMaterial("MetalSparkTrail", null, 1.15f);
        settings.FindProperty("dustMaterial").objectReferenceValue = CreateDustMaterial();
        settings.FindProperty("ballRenderer").objectReferenceValue = effect.GetComponentInChildren<SpriteRenderer>();
        settings.FindProperty("impactFlashMaterial").objectReferenceValue = CreateImpactFlashMaterial();
        // 前回の既定値だけを更新し、それ以外の個別調整値は保持する。
        if (settings.FindProperty("maximumSparks").intValue == 7)
            settings.FindProperty("maximumSparks").intValue = 32;
        settings.ApplyModifiedPropertiesWithoutUndo();
        Record(effect);
    }

    public static void ConfigurePlayerMotionFeedback(Player player, MorningStarLauncher launcher, Rigidbody2D ball)
    {
        var effect = player.GetComponent<PlayerMotionFeedback>();
        if (effect == null) effect = player.gameObject.AddComponent<PlayerMotionFeedback>();
        var playerSettings = new SerializedObject(player);
        var launcherSettings = new SerializedObject(launcher);
        var settings = new SerializedObject(effect);
        settings.FindProperty("player").objectReferenceValue = player;
        settings.FindProperty("launcher").objectReferenceValue = launcher;
        settings.FindProperty("chain").objectReferenceValue = launcherSettings.FindProperty("chainConstraint").objectReferenceValue;
        settings.FindProperty("ball").objectReferenceValue = ball;
        settings.FindProperty("bodyCollider").objectReferenceValue = playerSettings.FindProperty("_groundCheckCollider").objectReferenceValue
            ?? player.GetComponent<Collider2D>();
        settings.FindProperty("bodySprite").objectReferenceValue = playerSettings.FindProperty("_bodySprite").objectReferenceValue
            ?? player.GetComponent<SpriteRenderer>();
        settings.FindProperty("dustMaterial").objectReferenceValue = CreateDustMaterial();
        settings.ApplyModifiedPropertiesWithoutUndo();
        Record(effect);
    }

    private static Material CreateImpactFlashMaterial()
    {
        EnsureFolder();
        string texturePath = Folder + "/ImpactFlash.png";
        var texture = new Texture2D(32, 16, TextureFormat.RGBA32, false);
        var pixels = new Color32[32 * 16];
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 32; x++)
        {
            float dx = x - 15.5f;
            float dy = y - 7.5f;
            bool core = Mathf.Abs(dx) + Mathf.Abs(dy) * 1.6f <= 3f;
            bool slash = Mathf.Abs(dy) <= 0.5f && dx > -13f && dx < 12f;
            bool fork = (Mathf.Abs(dy - dx * 0.45f) <= 0.5f && dx > -5f && dx < 9f)
                || (Mathf.Abs(dy + dx * 0.55f) <= 0.5f && dx > -8f && dx < 6f);
            if (core || slash || fork)
                pixels[y * 32 + x] = core ? new Color32(255, 255, 235, 255) : new Color32(255, 215, 140, 230);
        }
        texture.SetPixels32(pixels);
        WriteTexture(texturePath, texture);
        return UpdateMetalMaterial("ImpactFlash", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath), 1.6f);
    }

    private static void ConfigureShadow(GameObject owner, Sprite sprite, float width, float opacity)
    {
        var projection = owner.GetComponent<GroundProjectedShadow>();
        bool added = projection == null;
        if (added) projection = owner.AddComponent<GroundProjectedShadow>();
        Transform existing = owner.transform.Find("GroundShadow");
        var shadow = existing != null ? existing.GetComponent<SpriteRenderer>() : null;
        if (shadow == null)
        {
            var child = new GameObject("GroundShadow");
            child.transform.SetParent(owner.transform, false);
            shadow = child.AddComponent<SpriteRenderer>();
        }
        shadow.sprite = sprite;
        shadow.color = new Color(0.055f, 0.04f, 0.09f, opacity);
        shadow.sortingOrder = 1;
        shadow.enabled = false; // 正しい床の位置は実行時に投影する。
        var settings = new SerializedObject(projection);
        settings.FindProperty("body").objectReferenceValue = owner.GetComponent<Collider2D>();
        settings.FindProperty("groundShadow").objectReferenceValue = shadow;
        if (added)
        {
            settings.FindProperty("width").floatValue = width;
            settings.FindProperty("opacity").floatValue = opacity;
            settings.FindProperty("groundLayers").intValue = LayerMask.GetMask("Default", "Walls", "WallFragment");
        }
        // このプロジェクトの既存床はDefaultレイヤー。Wallsだけでは接地面を拾えない。
        settings.FindProperty("groundLayers").intValue |= LayerMask.GetMask("Default");
        settings.ApplyModifiedPropertiesWithoutUndo();
        Record(projection);
        Record(shadow);
    }

    public static void ConfigureBackground(SpriteRenderer renderer, int depth)
    {
        EnsureFolder();
        string[] names = { "FarAtmosphere", "MidAtmosphere", "FrontAtmosphere" };
        float[] strength = { 0.12f, 0.38f, 0.18f };
        float[] saturation = { 0.96f, 0.80f, 0.92f };
        string path = Folder + "/" + names[depth] + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        bool created = material == null;
        if (created)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/BackgroundAerialPerspective.shader");
            if (shader == null) throw new InvalidOperationException("Aerial perspective shader is missing.");
            material = new Material(shader);
            material.SetColor("_AtmosphereColor", new Color(0.70f, 0.83f, 0.96f, 1f));
            material.SetFloat("_AtmosphereStrength", strength[depth]);
            material.SetFloat("_Saturation", saturation[depth]);
            AssetDatabase.CreateAsset(material, path);
        }
        renderer.sharedMaterial = material;
        Color tint = renderer.color;
        tint.a = 1f;
        renderer.color = tint;
        Record(renderer);
    }

    public static void ConfigureTitle(TitleAtmosphere atmosphere)
    {
        var settings = new SerializedObject(atmosphere);
        if (settings.FindProperty("petalAtlas").objectReferenceValue == null)
            settings.FindProperty("petalAtlas").objectReferenceValue = CreatePetalAtlas();
        settings.ApplyModifiedPropertiesWithoutUndo();
        atmosphere.SetAllDirty();
        Record(atmosphere);
    }

    private static Texture2D CreatePetalAtlas()
    {
        EnsureFolder();
        string path = Folder + "/TitlePetals.png";
        // 4種類を1px単位で描画。D=くすんだ輪郭、P=桜色、L=光、H=淡いクリーム。
        string[][] petals = {
            new[] {
                "..DD....DD..", ".DPPL..LPPD.", ".PLHL..LHLP.", "DPLHLLLLLPPD",
                "DPLHHLLLPPPD", ".DPLHLLPPPD.", "..DPLLLPPD..", "...DPLPPD...",
                "....DPPD....", ".....DD.....", "............", "............"
            },
            new[] {
                ".........DD.", "......DDPLD.", "....DPLLHPD.", "..DPLHHLLPD.", ".DPLHLLPPD..",
                "DPLHLLPPD...", "DPLLLPPD....", ".DPLPPD.....", "..DPPD......", "...DD.......",
                "............", "............"
            },
            new[] {
                "............", ".......DD...", ".....DPLD...", "....DPLHD...", "...DPLHLD...",
                "..DPPLLPD...", "..DPLLPPD...", "...DPPPD....", "....DPD.....", ".....D......",
                "............", "............"
            },
            new[] {
                "............", "...DDDD.....", "..DPLLLDD...", ".DPLHHLLPD..", "DPPLHLLLPPD.",
                "DPPLLLPPPPD.", ".DPPPPDDPD..", "..DDPD..D...", "....D.......", "............",
                "............", "............"
            }
        };
        var texture = new Texture2D(64, 32, TextureFormat.RGBA32, false);
        var pixels = new Color32[64 * 32];
        for (int variant = 0; variant < petals.Length; variant++)
        for (int y = 0; y < petals[variant].Length; y++)
        for (int x = 0; x < petals[variant][y].Length; x++)
        {
            char p = petals[variant][y][x];
            Color32 c = p == 'D' ? new Color32(168, 95, 139, 255) :
                p == 'P' ? new Color32(233, 154, 191, 255) :
                p == 'L' ? new Color32(255, 211, 219, 255) :
                p == 'H' ? new Color32(255, 242, 223, 255) : new Color32(0, 0, 0, 0);
            pixels[(13 - y) * 64 + variant * 16 + x + 2] = c;
        }
        for (int y = 24; y < 28; y++)
        for (int x = 0; x < 4; x++) pixels[y * 64 + x] = new Color32(255, 255, 255, 255);
        texture.SetPixels32(pixels);
        WriteTexture(path, texture);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static Material CreateSparkMaterial()
    {
        EnsureFolder();
        string texturePath = Folder + "/SparkStreak.png";
        var texture = new Texture2D(8, 32, TextureFormat.RGBA32, false);
        var pixels = new Color32[8 * 32];
        for (int y = 2; y < 30; y++)
        for (int x = 0; x < 8; x++)
        {
            float halfWidth = y > 10 && y < 27 ? 1.5f : 0.5f;
            if (Mathf.Abs(x - 3.5f) > halfWidth) continue;
            float heat = Mathf.InverseLerp(4f, 26f, y);
            Color c = Color.Lerp(new Color(1f, 0.48f, 0.08f), new Color(1f, 1f, 0.94f), heat);
            c.a = y < 9 ? 0.28f : y < 17 ? 0.65f : 1f;
            if (Mathf.Abs(x - 3.5f) > 0.5f) c.a *= 0.45f;
            pixels[y * 8 + x] = c;
        }
        texture.SetPixels32(pixels);
        WriteTexture(texturePath, texture);
        return UpdateMetalMaterial("ImpactSparks", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath), 1.45f);
    }

    private static Material CreateEmberMaterial()
    {
        string texturePath = Folder + "/SparkEmber.png";
        var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
        var pixels = new Color32[64];
        string[] rows = { "........", "...XX...", "..XXXX..", "..XXHX..", "...XXX..", "....X...", "........", "........" };
        for (int y = 0; y < 8; y++)
        for (int x = 0; x < 8; x++)
            if (rows[y][x] != '.') pixels[(7 - y) * 8 + x] = rows[y][x] == 'H'
                ? new Color32(255, 255, 240, 255) : new Color32(255, 205, 115, 255);
        texture.SetPixels32(pixels);
        WriteTexture(texturePath, texture);
        return UpdateMetalMaterial("MetalEmbers", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath), 1.3f);
    }

    private static Material UpdateMetalMaterial(string name, Texture texture, float brightness)
    {
        string path = Folder + "/" + name + ".mat";
        var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/MetalSpark.shader");
        if (shader == null) throw new InvalidOperationException("MetalSpark shader is missing.");
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = shader;
        material.mainTexture = texture;
        material.SetColor("_Color", Color.white);
        material.SetFloat("_Brightness", brightness);
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssetIfDirty(material);
        return material;
    }

    private static Material CreateDustMaterial()
    {
        string path = Folder + "/GroundDust.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        string texturePath = Folder + "/SparkPixel.png";
        var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        var pixels = new Color32[16];
        pixels[5] = pixels[6] = pixels[9] = pixels[10] = new Color32(255, 255, 255, 255);
        texture.SetPixels32(pixels);
        WriteTexture(texturePath, texture);
        material = new Material(Shader.Find("Sprites/Default"));
        material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static void WriteTexture(string path, Texture2D texture)
    {
        texture.Apply();
        byte[] png = texture.EncodeToPNG();
        UnityEngine.Object.DestroyImmediate(texture);
        if (!File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(png)) File.WriteAllBytes(path, png);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Default;
        importer.filterMode = FilterMode.Point;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.SaveAndReimport();
    }

    private static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Generated", "AtmosphereEffects");
    }

    private static void Record(UnityEngine.Object target)
    {
        EditorUtility.SetDirty(target);
        PrefabUtility.RecordPrefabInstancePropertyModifications(target);
    }
}
