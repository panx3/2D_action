using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 添付済みの tekkyu_enemy.png と既存 Enemy Prefab を使い、
/// TekkyuEnemy Prefab と CompletScene の配置を再現可能に構築する。
/// </summary>
public static class TekkyuEnemySetup
{
    private const string SpritePath = "Assets/image_/tekkyu_enemy.png";
    private const string BasePrefabPath = "Assets/Prefabs/Enemy.prefab";
    private const string EnemyFolderPath = "Assets/Prefabs/Enemies";
    private const string PrefabPath = EnemyFolderPath + "/TekkyuEnemy.prefab";
    private const string ScenePath = "Assets/Scenes/CompletScene.unity";
    private const string SceneObjectName = "TekkyuEnemy_MidStage";

    private static readonly Vector2 BodyColliderSize = new Vector2(0.76f, 0.58f);
    private static readonly Vector2 BodyColliderOffset = new Vector2(0.43f, 0.32f);

    [MenuItem("Tools/Tekkyu Enemy/Apply Setup")]
    public static void Apply()
    {
        // 旧1枚絵をRootへ戻す処理は、Visual分離と固定Colliderを壊すため使用しない。
        RatEnemyVisualSetup.Apply();
    }

    public static void ApplyFromCommandLine()
    {
        try
        {
            Apply();
            EditorApplication.Exit(0);
        }
        catch
        {
            EditorApplication.Exit(1);
        }
    }

    [MenuItem("Tools/Tekkyu Enemy/Validate Setup")]
    public static void Validate()
    {
        RatEnemyVisualSetup.Validate();
        Debug.Log("[TekkyuEnemySetup] VALIDATION PASSED");
    }

    public static void ValidateFromCommandLine()
    {
        try
        {
            Validate();
            EditorApplication.Exit(0);
        }
        catch
        {
            EditorApplication.Exit(1);
        }
    }

    private static void ConfigureSpriteImporter()
    {
        TextureImporter importer = AssetImporter.GetAtPath(SpritePath) as TextureImporter;
        if (importer == null)
            throw new InvalidOperationException($"TextureImporter not found: {SpritePath}");

        TextureImporterSettings spriteSettings = new TextureImporterSettings();
        importer.ReadTextureSettings(spriteSettings);

        // 既存のMultiple Spriteが使っていた切り出し原点を保存する。
        // これにより、同じ画像を参照している既存Enemyの見た目位置をSingle化後も維持する。
        Vector2 preservedPivot = new Vector2(0.5f, 0.5f);
#pragma warning disable 0618
        SpriteMetaData[] sheet = importer.spritesheet;
#pragma warning restore 0618
        if (importer.spriteImportMode == SpriteImportMode.Multiple && sheet != null && sheet.Length > 0)
        {
            Rect rect = sheet[0].rect;
            Vector2 pixelPivot = rect.position + Vector2.Scale(rect.size, sheet[0].pivot);
            importer.GetSourceTextureWidthAndHeight(out int sourceWidth, out int sourceHeight);
            float width = Mathf.Max(1f, sourceWidth);
            float height = Mathf.Max(1f, sourceHeight);
            preservedPivot = new Vector2(pixelPivot.x / width, pixelPivot.y / height);
        }
        else if (spriteSettings.spriteAlignment == (int)SpriteAlignment.Custom)
        {
            preservedPivot = spriteSettings.spritePivot;
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spritePixelsPerUnit = 100f;
        spriteSettings.spriteMeshType = SpriteMeshType.FullRect;
        spriteSettings.spriteAlignment = (int)SpriteAlignment.Custom;
        spriteSettings.spritePivot = preservedPivot;
        importer.SetTextureSettings(spriteSettings);
        // SetTextureSettingsには旧spriteModeも含まれるため、Single指定はその後に行う。
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.crunchedCompression = false;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.SaveAndReimport();

        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(SpritePath);
        if (texture == null || texture.width != 133 || texture.height != 136)
            throw new InvalidOperationException("tekkyu_enemy.png must be the attached 133 x 136 texture.");

        Debug.Log($"[TekkyuEnemySetup] Sprite imported Single/Point/Uncompressed, " +
                  $"size={texture.width}x{texture.height}, pivot={preservedPivot}");
    }

    private static GameObject CreateOrUpdatePrefab()
    {
        GameObject basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BasePrefabPath);
        if (basePrefab == null)
            throw new InvalidOperationException($"Base Enemy prefab not found: {BasePrefabPath}");

        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
        {
            if (!AssetDatabase.CopyAsset(BasePrefabPath, PrefabPath))
                throw new InvalidOperationException($"Could not duplicate {BasePrefabPath} to {PrefabPath}");
            AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceSynchronousImport);
        }

        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
        if (sprite == null)
            throw new InvalidOperationException($"Sprite could not be loaded: {SpritePath}");

        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            root.name = "TekkyuEnemy";
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            int enemyLayer = LayerMask.NameToLayer("Enemy");
            if (enemyLayer >= 0)
                root.layer = enemyLayer;

            SpriteRenderer renderer = RequireComponent<SpriteRenderer>(root);
            renderer.sprite = sprite;
            renderer.color = Color.white;
            renderer.flipX = false;

            BoxCollider2D collider = RequireComponent<BoxCollider2D>(root);
            collider.isTrigger = false;
            collider.enabled = true;
            collider.size = BodyColliderSize;
            collider.offset = BodyColliderOffset;

            // Rigidbody2D と Enemy Componentは複製元をそのまま利用する。
            RequireComponent<Rigidbody2D>(root);
            Enemy enemy = RequireComponent<Enemy>(root);
            SerializedObject enemyObject = new SerializedObject(enemy);
            // 既存Enemy.csは常に左へ進み折り返しを持たないため、
            // 中間足場から落下しない戦闘確認用Enemyとして、このPrefabだけ静止させる。
            SetFloat(enemyObject, "_moveSpeed", 0f);
            enemyObject.ApplyModifiedPropertiesWithoutUndo();

            EnemyHealth health = RequireComponent<EnemyHealth>(root);
            SerializedObject healthObject = new SerializedObject(health);
            SetInt(healthObject, "maxHp", 3);
            SetFloat(healthObject, "knockbackResistance", 1f);
            SetBool(healthObject, "destroyOnDeath", true);
            SetFloat(healthObject, "hitStunDuration", 0.18f);
            SetFloat(healthObject, "maxKnockbackSpeed", 8f);
            SetBool(healthObject, "freezeRotationOnHit", true);
            SetBool(healthObject, "stopAngularVelocityOnHit", true);
            SetBool(healthObject, "debugLog", false);
            healthObject.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
            throw new InvalidOperationException($"Prefab was not saved: {PrefabPath}");

        Debug.Log("[TekkyuEnemySetup] Prefab created from the existing Enemy prefab; " +
                  "Rigidbody2D/contact damage/hit SFX were preserved, Move Speed=0 keeps it on the platform.");
        return prefab;
    }

    private static Vector3 PlaceInCompletScene(GameObject prefab)
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Transform[] sceneTransforms = GetSceneTransforms(scene);

        foreach (Transform duplicate in sceneTransforms.Where(t => t.name == SceneObjectName).ToArray())
            UnityEngine.Object.DestroyImmediate(duplicate.gameObject);

        sceneTransforms = GetSceneTransforms(scene);
        Transform floor = FindPlacementFloor(sceneTransforms);
        Collider2D floorCollider = floor != null ? floor.GetComponent<Collider2D>() : null;
        if (floorCollider == null)
            throw new InvalidOperationException("A middle-stage Floor collider was not found in CompletScene.");

        Bounds floorBounds = floorCollider.bounds;
        float x = FindClearPlacementX(sceneTransforms, floorBounds);
        float y = floorBounds.max.y + BodyColliderSize.y * 0.5f - BodyColliderOffset.y + 0.03f;
        Vector3 position = new Vector3(x, y, 0f);

        Transform enemyParent = sceneTransforms.FirstOrDefault(t => t.name == "04_Enemies");
        if (enemyParent == null)
            enemyParent = sceneTransforms.FirstOrDefault(t => t.name == "Test_Stage");

        GameObject instance = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
        if (instance == null)
            throw new InvalidOperationException("Could not instantiate TekkyuEnemy prefab.");

        instance.name = SceneObjectName;
        if (enemyParent != null)
            instance.transform.SetParent(enemyParent, true);
        instance.transform.position = position;
        instance.transform.rotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[TekkyuEnemySetup] Placed {SceneObjectName} on {GetHierarchyPath(floor)} at {position}");
        return position;
    }

    private static void ValidateAll(Vector3? expectedPosition)
    {
        TextureImporter importer = AssetImporter.GetAtPath(SpritePath) as TextureImporter;
        Check(importer != null, "Sprite importer exists");
        Check(importer.spriteImportMode == SpriteImportMode.Single, "Sprite Mode is Single");
        Check(importer.filterMode == FilterMode.Point, "Filter Mode is Point");
        Check(importer.textureCompression == TextureImporterCompression.Uncompressed, "Compression is Uncompressed");
        Check(!importer.mipmapEnabled, "Mip maps are disabled");

        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(SpritePath);
        Check(texture != null && texture.width == 133 && texture.height == 136, "Texture is 133 x 136");

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Check(prefab != null, "TekkyuEnemy prefab exists");
        ValidatePrefab(prefab);

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Transform[] sceneTransforms = GetSceneTransforms(scene);
        Transform[] instances = sceneTransforms.Where(t => t.name == SceneObjectName).ToArray();
        Check(instances.Length == 1, "CompletScene contains exactly one TekkyuEnemy_MidStage");

        Transform instance = instances[0];
        Check(PrefabUtility.GetCorrespondingObjectFromSource(instance.gameObject) == prefab,
            "Scene instance is connected to TekkyuEnemy prefab");
        if (expectedPosition.HasValue)
            Check(Vector3.Distance(instance.position, expectedPosition.Value) < 0.001f, "Scene position was saved");

        Collider2D body = instance.GetComponent<Collider2D>();
        Transform floor = FindPlacementFloor(sceneTransforms);
        Collider2D floorCollider = floor != null ? floor.GetComponent<Collider2D>() : null;
        Check(body != null && floorCollider != null, "Enemy body and placement floor colliders exist");
        Check(body.bounds.max.x > floorCollider.bounds.min.x && body.bounds.min.x < floorCollider.bounds.max.x,
            "Enemy is horizontally above the placement floor");
        Check(Mathf.Abs(body.bounds.min.y - floorCollider.bounds.max.y) <= 0.08f,
            "Enemy body is positioned on the floor surface");

        Player player = sceneTransforms.Select(t => t.GetComponent<Player>()).FirstOrDefault(p => p != null);
        if (player != null)
            Check(Vector2.Distance(player.transform.position, instance.position) > 10f,
                "Enemy is not beside the Player start");

        string[] forbiddenOverlapNames = { "Spike", "Door", "Switch", "Goal", "MagnetPoint" };
        foreach (Collider2D other in sceneTransforms.SelectMany(t => t.GetComponents<Collider2D>()))
        {
            if (other == body || !other.enabled)
                continue;
            if (!forbiddenOverlapNames.Any(token => other.name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0))
                continue;
            Check(!body.bounds.Intersects(other.bounds), $"Enemy does not overlap {other.name}");
        }

        Debug.Log($"[TekkyuEnemySetup] VALIDATION prefabHP={prefab.GetComponent<EnemyHealth>().MaxHp}, " +
                  $"collider=Box size={BodyColliderSize} offset={BodyColliderOffset}, " +
                  $"position={instance.position}, floor={GetHierarchyPath(floor)}");
    }

    private static void ValidatePrefab(GameObject prefab)
    {
        SpriteRenderer renderer = prefab.GetComponent<SpriteRenderer>();
        Rigidbody2D rigidbody2D = prefab.GetComponent<Rigidbody2D>();
        BoxCollider2D collider = prefab.GetComponent<BoxCollider2D>();
        Enemy enemy = prefab.GetComponent<Enemy>();
        EnemyHealth health = prefab.GetComponent<EnemyHealth>();

        Check(renderer != null && renderer.sprite != null &&
              AssetDatabase.GetAssetPath(renderer.sprite) == SpritePath, "Prefab uses tekkyu_enemy.png");
        Check(rigidbody2D != null, "Prefab reuses Rigidbody2D");
        Check(collider != null, "Prefab uses BoxCollider2D");
        Check(Vector2.Distance(collider.size, BodyColliderSize) < 0.001f, "Collider matches body size");
        Check(Vector2.Distance(collider.offset, BodyColliderOffset) < 0.001f, "Collider excludes the tail");
        Check(enemy != null, "Prefab reuses Enemy.cs");
        Check(health != null && health.MaxHp == 3, "EnemyHealth Max HP is 3");

        GameObject basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BasePrefabPath);
        Rigidbody2D baseBody = basePrefab != null ? basePrefab.GetComponent<Rigidbody2D>() : null;
        Check(baseBody != null, "Base Enemy Rigidbody2D exists");
        Check(rigidbody2D.bodyType == baseBody.bodyType &&
              Mathf.Approximately(rigidbody2D.mass, baseBody.mass) &&
              Mathf.Approximately(rigidbody2D.gravityScale, baseBody.gravityScale) &&
              rigidbody2D.collisionDetectionMode == baseBody.collisionDetectionMode &&
              rigidbody2D.constraints == baseBody.constraints,
            "Rigidbody2D settings match the existing Enemy prefab");

        SerializedObject enemyObject = new SerializedObject(enemy);
        Check(Mathf.Approximately(enemyObject.FindProperty("_moveSpeed").floatValue, 0f),
            "TekkyuEnemy remains stationary on the middle platform");
        Check(enemyObject.FindProperty("_contactDamage").intValue == 1, "Contact damage remains 1");
        Check(Mathf.Approximately(enemyObject.FindProperty("_contactKnockback").floatValue, 5f),
            "Contact knockback remains 5");

        SerializedObject healthObject = new SerializedObject(health);
        Check(Mathf.Approximately(healthObject.FindProperty("knockbackResistance").floatValue, 1f),
            "Knockback resistance is 1");
        Check(Mathf.Approximately(healthObject.FindProperty("hitStunDuration").floatValue, 0.18f),
            "Hit stun is 0.18 seconds");
        Check(Mathf.Approximately(healthObject.FindProperty("maxKnockbackSpeed").floatValue, 8f),
            "Max knockback speed is 8");
        Check(healthObject.FindProperty("destroyOnDeath").boolValue, "Destroy On Death is enabled");
    }

    private static Transform FindPlacementFloor(Transform[] sceneTransforms)
    {
        Collider2D[] floors = sceneTransforms
            .Select(t => t.GetComponent<Collider2D>())
            .Where(c => c != null && c.enabled && c.gameObject.CompareTag("Floor"))
            .OrderBy(c => c.bounds.center.x)
            .ToArray();
        if (floors.Length == 0)
            return null;

        // CompletSceneの左右端ではなく、X順で中央にある床をステージ中盤として使用する。
        return floors[floors.Length / 2].transform;
    }

    private static float FindClearPlacementX(Transform[] sceneTransforms, Bounds floorBounds)
    {
        Collider2D[] protectedColliders = sceneTransforms
            .SelectMany(t => t.GetComponents<Collider2D>())
            .Where(c => c != null && c.enabled && IsProtectedGimmick(c))
            .ToArray();

        float bodyHalfWidth = BodyColliderSize.x * 0.5f;
        float bodyCenterY = floorBounds.max.y + 0.03f + BodyColliderSize.y * 0.5f;
        float rightmostBodyCenter = floorBounds.max.x - bodyHalfWidth - 0.35f;
        float leftmostBodyCenter = floorBounds.min.x + bodyHalfWidth + 0.35f;

        // Enemy.csは既存どおり左へ移動するため、右寄りから探索して床上の走行距離を確保する。
        for (float bodyCenterX = rightmostBodyCenter;
             bodyCenterX >= leftmostBodyCenter;
             bodyCenterX -= 0.35f)
        {
            Bounds candidate = new Bounds(
                new Vector3(bodyCenterX, bodyCenterY, 0f),
                new Vector3(BodyColliderSize.x, BodyColliderSize.y, 0.1f));
            if (protectedColliders.All(other => !ExpandedBounds(other.bounds, 0.25f).Intersects(candidate)))
                return bodyCenterX - BodyColliderOffset.x;
        }

        throw new InvalidOperationException("No clear position was found on the middle Floor.");
    }

    private static bool IsProtectedGimmick(Collider2D collider)
    {
        return collider.GetComponentInParent<SpikeTrap>() != null ||
               collider.GetComponentInParent<GimmickDoor>() != null ||
               collider.GetComponentInParent<HitSwitch>() != null ||
               collider.GetComponentInParent<WeightSwitch>() != null ||
               collider.GetComponentInParent<GoalPoint>() != null ||
               collider.GetComponentInParent<MagnetPoint>() != null;
    }

    private static Bounds ExpandedBounds(Bounds bounds, float amount)
    {
        bounds.Expand(new Vector3(amount * 2f, amount * 2f, 0f));
        return bounds;
    }

    private static T RequireComponent<T>(GameObject gameObject) where T : Component
    {
        T component = gameObject.GetComponent<T>();
        if (component == null)
            throw new InvalidOperationException($"Existing Enemy prefab is missing {typeof(T).Name}");
        return component;
    }

    private static Transform[] GetSceneTransforms(Scene scene)
    {
        return scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .ToArray();
    }

    private static string GetHierarchyPath(Transform transform)
    {
        if (transform == null)
            return "<missing>";
        string path = transform.name;
        while (transform.parent != null)
        {
            transform = transform.parent;
            path = transform.name + "/" + path;
        }
        return path;
    }

    private static void EnsureFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
            return;

        string parent = folderPath.Substring(0, folderPath.LastIndexOf('/'));
        string name = folderPath.Substring(folderPath.LastIndexOf('/') + 1);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }

    private static void SetInt(SerializedObject serializedObject, string name, int value)
    {
        SerializedProperty property = serializedObject.FindProperty(name);
        if (property == null)
            throw new InvalidOperationException($"Serialized property not found: {name}");
        property.intValue = value;
    }

    private static void SetFloat(SerializedObject serializedObject, string name, float value)
    {
        SerializedProperty property = serializedObject.FindProperty(name);
        if (property == null)
            throw new InvalidOperationException($"Serialized property not found: {name}");
        property.floatValue = value;
    }

    private static void SetBool(SerializedObject serializedObject, string name, bool value)
    {
        SerializedProperty property = serializedObject.FindProperty(name);
        if (property == null)
            throw new InvalidOperationException($"Serialized property not found: {name}");
        property.boolValue = value;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("[TekkyuEnemySetup] Validation failed: " + message);
    }
}
