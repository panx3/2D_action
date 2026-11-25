using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>既存敵のHP/破片/影/物理素材を使って、新規アセットだけを生成する。</summary>
public static class BeamScorpionSetup
{
    public const string PrefabPath = "Assets/Prefabs/Enemies/BeamScorpion.prefab";
    public const string VfxFolder = "Assets/Prefabs/Enemies/BeamScorpionVFX";
    public const string AnimationFolder = "Assets/Animation/BeamScorpion";
    public const string WalkPath = "Assets/image_/ビームサソリ_wolk.png";
    public const string AttackPath = "Assets/image_/ビームサソリ_attack.png";
    public const string ClawPath = "Assets/Art/Enemies/BeamScorpionClaw.png";
    public const string FireSoundPath = "Assets/Audio/SFX/BeamScorpionFire.wav";
    public const int BaseHealth = 8;
    private const string EnergyMaterialPath = "Assets/Art/Enemies/BeamScorpionEnergy.mat";

    [MenuItem("Tools/Beam Scorpion/Create Prefab")]
    public static void CreatePrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
            throw new InvalidOperationException("BeamScorpion already exists. Tune its Inspector; creation does not overwrite it.");
        Sprite[] walk = Frames(WalkPath), attack = Frames(AttackPath), claw = Frames(ClawPath);
        if (walk.Length != 4 || attack.Length != 6 || claw.Length != 6)
            throw new InvalidOperationException("Expected four walk sprites and six sprites in each attack sheet.");
        Folder(VfxFolder); Folder(AnimationFolder); Folder("Assets/Generated/BeamScorpion");
        AudioClip fireSound = CreateFireSound();
        Sprite pixel = CreatePixel();
        var energy = AssetDatabase.LoadAssetAtPath<Material>(EnergyMaterialPath);
        if (energy == null) throw new InvalidOperationException("Beam Scorpion energy material is missing.");
        var beam = CreateBeam("Beam", pixel, energy);
        var afterglow = CreateBeam("Afterglow", pixel, energy);
        var charge = CreateBurst("Charge", pixel, energy, BeamScorpionBurstVfx.Style.Charge, 0.65f, 0.61f);
        var muzzle = CreateBurst("Muzzle", pixel, energy, BeamScorpionBurstVfx.Style.Muzzle, 0.30f, 0.88f);
        var playerHit = CreateBurst("PlayerHit", pixel, energy, BeamScorpionBurstVfx.Style.Impact, 0.27f, 0.66f);
        var environmentHit = CreateBurst("EnvironmentHit", pixel, energy, BeamScorpionBurstVfx.Style.Impact, 0.30f, 0.92f);
        AnimatorController controller = CreateAnimator(walk, attack, claw);
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/WrenchCrab.prefab");
        if (source == null) throw new InvalidOperationException("WrenchCrab prefab is missing.");
        GameObject root = new GameObject("BeamScorpion");
        try
        {
            root.layer = LayerMask.NameToLayer("Enemy");
            Rigidbody2D body = root.AddComponent<Rigidbody2D>();
            body.gravityScale = 1f;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            BoxCollider2D collider = root.AddComponent<BoxCollider2D>();
            collider.sharedMaterial = source.GetComponent<BoxCollider2D>().sharedMaterial;
            collider.size = new Vector2(2.375f, 1.5f);
            collider.offset = new Vector2(0f, 0.7f);
            collider.forceReceiveLayers = ~LayerMask.GetMask("morningstar", "chain");
            Transform visual = Child(root.transform, "Visual", Vector2.zero);
            visual.localScale = new Vector3(2f, 2f, 1f);
            SpriteRenderer renderer = visual.gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = walk[0]; renderer.sortingOrder = 5;
            renderer.sharedMaterial = source.GetComponentInChildren<SpriteRenderer>().sharedMaterial;
            Animator animator = visual.gameObject.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            EnemyHealth health = root.AddComponent<EnemyHealth>();
            EditorUtility.CopySerialized(source.GetComponent<EnemyHealth>(), health);
            AudioSource audio = root.AddComponent<AudioSource>();
            audio.playOnAwake = false; audio.loop = false; audio.spatialBlend = 0f;
            AudioSource worldAudio = OneShotAudioUtility.FindWorldImpactSource();
            if (worldAudio != null) audio.outputAudioMixerGroup = worldAudio.outputAudioMixerGroup;
            BeamScorpionController scorpion = root.AddComponent<BeamScorpionController>();
            var settings = new SerializedObject(scorpion);
            Ref(settings, "visualRenderer", renderer); Ref(settings, "visualAnimator", animator);
            Ref(settings, "groundCheck", Child(root.transform, "GroundCheck", new Vector2(0f, -0.05f)));
            Ref(settings, "edgeCheck", Child(root.transform, "EdgeCheck", new Vector2(-1.3075f, -0.05f)));
            Ref(settings, "wallCheck", Child(root.transform, "WallCheck", new Vector2(-1.1875f, 0.7f)));
            Ref(settings, "beamOrigin", Child(root.transform, "BeamOrigin", new Vector2(-0.8125f, 2.4375f)));
            Ref(settings, "audioSource", audio);
            Ref(settings, "beamFireSE", fireSound);
            Ref(settings, "beamHitSE", AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SFX/Imported/打撃6.mp3"));
            Ref(settings, "beamVfxPrefab", beam); Ref(settings, "afterglowVfxPrefab", afterglow);
            Ref(settings, "chargeVfxPrefab", charge); Ref(settings, "muzzleVfxPrefab", muzzle);
            Ref(settings, "playerHitVfxPrefab", playerHit); Ref(settings, "environmentHitVfxPrefab", environmentHit);
            SerializedProperty frames = settings.FindProperty("attackFrames"); frames.arraySize = attack.Length;
            SerializedProperty offsets = settings.FindProperty("muzzleOffsets"); offsets.arraySize = attack.Length;
            // しっぽ先の赤いレンズ中心（各スプライト内ピクセル位置）。
            Vector2[] lens = { new Vector2(31f, 74f), new Vector2(37f, 96f), new Vector2(26.5f, 79f),
                new Vector2(13f, 74f), new Vector2(19f, 80f), new Vector2(23f, 74f) };
            for (int i = 0; i < attack.Length; i++)
            {
                frames.GetArrayElementAtIndex(i).objectReferenceValue = attack[i];
                offsets.GetArrayElementAtIndex(i).vector2Value = (lens[i] - attack[i].pivot) / attack[i].pixelsPerUnit;
            }
            settings.ApplyModifiedPropertiesWithoutUndo();
            var hp = new SerializedObject(health);
            hp.FindProperty("maxHp").intValue = BaseHealth;
            hp.FindProperty("maxDamagePerHit").intValue = 1;
            hp.FindProperty("hitStunDuration").floatValue = 0f;
            hp.FindProperty("suppressMorningStarKnockback").boolValue = true;
            hp.FindProperty("hitInvincibleDuration").floatValue = 0.75f;
            hp.FindProperty("minimumMorningStarHitStop").floatValue = 0.045f;
            hp.FindProperty("destroyOnDeath").boolValue = true;
            Ref(hp, "hitFlashRenderer", renderer); Ref(hp, "hitAudioSource", null);
            var disable = hp.FindProperty("disableOnDeath"); disable.arraySize = 1;
            disable.GetArrayElementAtIndex(0).objectReferenceValue = scorpion;
            hp.ApplyModifiedPropertiesWithoutUndo();
            Transform sourceShadow = source.transform.Find("GroundShadow");
            if (sourceShadow != null)
            {
                var shadowVisual = UnityEngine.Object.Instantiate(sourceShadow.gameObject, root.transform);
                shadowVisual.name = "GroundShadow";
                var shadow = root.AddComponent<GroundProjectedShadow>();
                EditorUtility.CopySerialized(source.GetComponent<GroundProjectedShadow>(), shadow);
                var shadowData = new SerializedObject(shadow);
                Ref(shadowData, "body", collider); Ref(shadowData, "groundShadow", shadowVisual.GetComponent<SpriteRenderer>());
                shadowData.FindProperty("width").floatValue = 2.5f;
                shadowData.FindProperty("scaleWithOwner").boolValue = true;
                shadowData.ApplyModifiedPropertiesWithoutUndo();
            }
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        Validate();
    }

    [MenuItem("Tools/Beam Scorpion/Validate Prefab")]
    public static void Validate()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null || prefab.GetComponent<EnemyHealth>().MaxHp != BaseHealth
            || prefab.GetComponent<Enemy>() != null || prefab.GetComponent<WrenchCrab>() != null)
            throw new InvalidOperationException("Invalid BeamScorpion health/controller.");
        var settings = new SerializedObject(prefab.GetComponent<BeamScorpionController>());
        foreach (string field in new[] { "visualRenderer", "visualAnimator", "beamOrigin", "groundCheck", "edgeCheck", "wallCheck",
            "beamVfxPrefab", "chargeVfxPrefab", "muzzleVfxPrefab", "playerHitVfxPrefab", "environmentHitVfxPrefab", "afterglowVfxPrefab", "audioSource", "beamFireSE" })
            if (settings.FindProperty(field).objectReferenceValue == null) throw new InvalidOperationException("Missing reference: " + field);
        foreach (var path in new[] { WalkPath, AttackPath, ClawPath })
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer == null || importer.filterMode != FilterMode.Point || importer.mipmapEnabled
                || importer.textureCompression != TextureImporterCompression.Uncompressed)
                throw new InvalidOperationException("Sprite import settings: " + path);
        }
        var animator = (Animator)settings.FindProperty("visualAnimator").objectReferenceValue;
        var clips = animator.runtimeAnimatorController.animationClips;
        if (!clips.Any(clip => clip.name == "ClawWindup") || !clips.Any(clip => clip.name == "ClawSwing"))
            throw new InvalidOperationException("Claw animation clips are missing.");
        var health = new SerializedObject(prefab.GetComponent<EnemyHealth>());
        if (health.FindProperty("disableOnDeath").GetArrayElementAtIndex(0).objectReferenceValue != prefab.GetComponent<BeamScorpionController>())
            throw new InvalidOperationException("Death cleanup must disable BeamScorpionController.");
        if (prefab.GetComponentsInChildren<Component>(true).Any(item => item == null)) throw new InvalidOperationException("Missing script.");
        Debug.Log($"[BeamScorpion] Prefab validation PASS: HP{BaseHealth}, Animator, death cleanup, audio and six replaceable VFX references.");
    }

    private static Sprite[] Frames(string path) => AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().OrderBy(s => s.rect.x).ToArray();

    /// <summary>短い下降音・機械的な倍音・発射ノイズを重ねた専用の合成SE。</summary>
    public static AudioClip CreateFireSound()
    {
        if (!File.Exists(FireSoundPath))
        {
            const int sampleRate = 44100;
            int count = (int)(sampleRate * 0.42f);
            var random = new System.Random(731);
            double phase = 0d, lowNoise = 0d;
            using (var writer = new BinaryWriter(File.Create(FireSoundPath)))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + count * 2);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
                writer.Write((short)1); writer.Write((short)1); writer.Write(sampleRate); writer.Write(sampleRate * 2);
                writer.Write((short)2); writer.Write((short)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(count * 2);
                for (int i = 0; i < count; i++)
                {
                    double t = (double)i / sampleRate;
                    double frequency = 340d + 2100d * Math.Exp(-t * 17d);
                    phase += 2d * Math.PI * frequency / sampleRate;
                    double envelope = Math.Min(1d, t / 0.004d) * Math.Exp(-t * 7d) * Math.Pow(1d - (double)i / count, 0.5d);
                    double noise = random.NextDouble() * 2d - 1d;
                    lowNoise += (noise - lowNoise) * 0.18d;
                    double tone = Math.Sin(phase) * 0.58d + Math.Sin(phase * 1.99d) * 0.16d
                        + Math.Sin(phase * 0.5d) * 0.12d;
                    double transient = (noise - lowNoise) * 0.2d * Math.Exp(-t * 45d);
                    double value = (tone * envelope + transient) * 0.75d;
                    writer.Write((short)(Math.Max(-0.95d, Math.Min(0.95d, value)) * short.MaxValue));
                }
            }
            AssetDatabase.ImportAsset(FireSoundPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (AudioImporter)AssetImporter.GetAtPath(FireSoundPath);
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.PCM;
            importer.defaultSampleSettings = settings; importer.forceToMono = true; importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<AudioClip>(FireSoundPath);
    }
    private static void Ref(SerializedObject target, string name, UnityEngine.Object value) => target.FindProperty(name).objectReferenceValue = value;
    private static Transform Child(Transform parent, string name, Vector2 position)
    {
        var item = new GameObject(name).transform; item.SetParent(parent, false); item.localPosition = position; return item;
    }
    private static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = path.Substring(0, path.LastIndexOf('/')); Folder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
    }
    private static Sprite CreatePixel()
    {
        const string path = "Assets/Generated/BeamScorpion/BeamPixel.png";
        Texture2D texture = new Texture2D(1, 1); texture.SetPixel(0, 0, Color.white); texture.Apply();
        File.WriteAllBytes(path, texture.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 1f; importer.filterMode = FilterMode.Point; importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed; importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
    private static BeamScorpionBeamVfx CreateBeam(string name, Sprite pixel, Material energy)
    {
        var root = new GameObject(name);
        try
        {
            var effect = root.AddComponent<BeamScorpionBeamVfx>(); var data = new SerializedObject(effect);
            var renderer = Child(root.transform, "core", Vector2.zero).gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = pixel;
            renderer.sortingOrder = 14;
            renderer.sharedMaterial = energy;
            renderer.color = Color.clear;
            Ref(data, "core", renderer);
            data.ApplyModifiedPropertiesWithoutUndo();
            return PrefabUtility.SaveAsPrefabAsset(root, VfxFolder + "/" + name + ".prefab").GetComponent<BeamScorpionBeamVfx>();
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    private static BeamScorpionBurstVfx CreateBurst(string name, Sprite pixel, Material energy,
        BeamScorpionBurstVfx.Style style, float duration, float travel)
    {
        var root = new GameObject(name);
        try
        {
            var effect = root.AddComponent<BeamScorpionBurstVfx>(); var data = new SerializedObject(effect);
            data.FindProperty("style").intValue = (int)style;
            data.FindProperty("lifetime").floatValue = duration;
            data.FindProperty("sparkTravel").floatValue = travel;
            for (int i = 0; i < 13; i++)
            {
                var child = Child(root.transform, i == 0 ? "Core" : "Spark" + i, Vector2.zero);
                var renderer = child.gameObject.AddComponent<SpriteRenderer>(); renderer.sprite = pixel;
                renderer.sortingOrder = i == 0 ? 20 : 19;
                if (i != 0) continue;
                renderer.sharedMaterial = energy;
                Ref(data, "flash", renderer);
            }
            data.ApplyModifiedPropertiesWithoutUndo();
            return PrefabUtility.SaveAsPrefabAsset(root, VfxFolder + "/" + name + ".prefab").GetComponent<BeamScorpionBurstVfx>();
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    private static AnimatorController CreateAnimator(Sprite[] walk, Sprite[] attack, Sprite[] claw)
    {
        var controller = AnimatorController.CreateAnimatorControllerAtPath(AnimationFolder + "/BeamScorpion.controller");
        var machine = controller.layers[0].stateMachine;
        Clip(controller, machine, "Patrol", walk, new[] { 0f, 0.2f, 0.4f, 0.6f }, 0.8f, true);
        Clip(controller, machine, "Windup", new[] { attack[0], attack[1] }, new[] { 0f, 0.5f }, 1f, false);
        Clip(controller, machine, "Firing", new[] { attack[2], attack[3] }, new[] { 0f, 0.1f }, 0.2f, true);
        Clip(controller, machine, "Recovery", new[] { attack[4], attack[5] }, new[] { 0f, 0.5f }, 1f, false);
        AddClawClips(controller, claw, attack[0]);
        EditorUtility.SetDirty(controller); AssetDatabase.SaveAssetIfDirty(controller);
        return controller;
    }

    private static void AddClawClips(AnimatorController controller, Sprite[] claw, Sprite rest)
    {
        var machine = controller.layers[0].stateMachine;
        Clip(controller, machine, "ClawWindup", new[] { claw[0], claw[1], claw[2] },
            new[] { 0f, 0.10f, 0.24f }, 0.46f, false, 50f);
        Clip(controller, machine, "ClawSwing", new[] { claw[2], claw[3], claw[4], claw[5], claw[4], claw[0], rest },
            new[] { 0f, 0.02f, 0.06f, 0.12f, 0.30f, 0.40f, 0.50f }, 0.60f, false, 50f);
    }

    private static void Clip(AnimatorController controller, AnimatorStateMachine machine, string name, Sprite[] sprites,
        float[] times, float length, bool loop, float sampleRate = 10f)
    {
        var clip = new AnimationClip { name = name, frameRate = sampleRate };
        var keys = new ObjectReferenceKeyframe[sprites.Length + 1];
        for (int i = 0; i < sprites.Length; i++) keys[i] = new ObjectReferenceKeyframe { time = times[i], value = sprites[i] };
        keys[sprites.Length] = new ObjectReferenceKeyframe { time = length, value = loop ? sprites[0] : sprites[sprites.Length - 1] };
        AnimationUtility.SetObjectReferenceCurve(clip, EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite"), keys);
        var settings = AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        AssetDatabase.CreateAsset(clip, AnimationFolder + "/" + name + ".anim");
        var state = machine.AddState(name); state.motion = clip;
        if (name == "Patrol") machine.defaultState = state;
    }
}
