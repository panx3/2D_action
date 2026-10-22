using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>編集時だけ敵の足元と固定Colliderを揃える。実行中の物理・AIには介入しない。</summary>
public static class EnemyPlacementTools
{
    // PlayerのSprite底面(-2)とCapsule底面(-2.05)と同じ隙間。
    private const float FootClearance = 0.05f;
    private const float ContactGap = 0.01f;

    [MenuItem("Tools/Enemies/Align Selected To Ground")]
    private static void AlignSelected()
    {
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Align Enemies To Ground");
        foreach (GameObject root in SelectedEnemies())
        {
            FitCollider(root);
            if (!SnapToGround(root))
                Debug.LogWarning("[EnemyPlacement] 足元から下8m以内に地面がありません: " + root.name, root);
        }
        Undo.CollapseUndoOperations(group);
    }

    [MenuItem("Tools/Enemies/Fit Selected Colliders To Feet")]
    private static void FitSelected()
    {
        foreach (GameObject root in SelectedEnemies()) FitCollider(root);
    }

    [MenuItem("Tools/Enemies/Align Selected To Ground", true)]
    [MenuItem("Tools/Enemies/Fit Selected Colliders To Feet", true)]
    private static bool CanEditSelection() => !EditorApplication.isPlayingOrWillChangePlaymode
        && SelectedEnemies().Any();

    [MenuItem("CONTEXT/Enemy/Align To Ground")]
    [MenuItem("CONTEXT/WrenchCrab/Align To Ground")]
    private static void AlignContext(MenuCommand command)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        GameObject root = ((Component)command.context).gameObject;
        FitCollider(root);
        SnapToGround(root);
    }

    private static IEnumerable<GameObject> SelectedEnemies() => Selection.transforms
        .Select(t => t.GetComponentInParent<Enemy>() as Component
            ?? t.GetComponentInParent<WrenchCrab>())
        .Where(c => c != null && !EditorUtility.IsPersistent(c))
        .Select(c => c.gameObject).Distinct();

    public static void CalculateCollider(GameObject root, out Vector2 size, out Vector2 offset)
    {
        bool rat = root.GetComponent<Enemy>() != null;
        if (!rat && root.GetComponent<WrenchCrab>() == null)
            throw new ArgumentException("Rat / WrenchCrab only", nameof(root));
        Transform visual = root.transform.Find("Visual");
        SpriteRenderer renderer = visual != null ? visual.GetComponent<SpriteRenderer>() : null;
        if (renderer == null || renderer.sprite == null)
            throw new InvalidOperationException(root.name + ": Visual Sprite is missing");
        if (Quaternion.Angle(visual.localRotation, Quaternion.identity) > 0.01f
            || Vector3.Dot(root.transform.up, Vector3.up) < 0.999f
            || visual.localScale.y <= 0f)
            throw new InvalidOperationException(root.name + ": upright enemies only");

        Vector3 scale = visual.localScale;
        // 既存の胴体判定の比率を維持。尻尾・レンチまで判定を広げない。
        size = Vector2.Scale(rat ? new Vector2(0.76f, 0.62f) : new Vector2(0.98f, 0.82f),
            new Vector2(Mathf.Abs(scale.x), scale.y));
        float feet = visual.localPosition.y + LowestFoot(renderer) * scale.y;
        float clearance = FootClearance / Mathf.Max(0.0001f, Mathf.Abs(root.transform.lossyScale.y));
        offset = new Vector2(visual.localPosition.x + (rat ? -0.05f * scale.x : 0f),
            feet - clearance + size.y * 0.5f);
    }

    public static void FitCollider(GameObject root, bool recordUndo = true)
    {
        CalculateCollider(root, out Vector2 size, out Vector2 offset);
        BoxCollider2D body = root.GetComponent<BoxCollider2D>();
        if (body == null) throw new InvalidOperationException(root.name + ": BoxCollider2D is missing");
        if ((body.size - size).sqrMagnitude < 0.00000001f
            && (body.offset - offset).sqrMagnitude < 0.00000001f) return;
        if (recordUndo) Undo.RecordObject(body, "Fit Enemy Collider To Feet");
        body.size = size;
        body.offset = offset;
        EditorUtility.SetDirty(body);
        if (PrefabUtility.IsPartOfPrefabInstance(body))
            PrefabUtility.RecordPrefabInstancePropertyModifications(body);
    }

    public static bool SnapToGround(GameObject root)
    {
        BoxCollider2D body = root.GetComponent<BoxCollider2D>();
        if (body == null || !root.scene.IsValid() || !root.activeInHierarchy) return false;
        Physics2D.SyncTransforms();
        Bounds bounds = body.bounds;
        var filter = new ContactFilter2D { useTriggers = false };
        Component owner = root.GetComponent<Enemy>() as Component ?? root.GetComponent<WrenchCrab>();
        // AIが実際に歩けるTerrainだけを配置先にする。
        filter.SetLayerMask(new SerializedObject(owner).FindProperty("_terrainLayers").intValue);
        var hits = new RaycastHit2D[64];
        RaycastHit2D floor = default;
        float nearest = float.PositiveInfinity;
        // 軽いめり込みも直せるよう足元の0.5m上から探す。壁側面・Triggerは除外。
        int count = root.scene.GetPhysicsScene2D().Raycast(
            new Vector2(bounds.center.x, bounds.min.y + 0.5f), Vector2.down, 8.5f, filter, hits);
        for (int i = 0; i < count; i++)
        {
            RaycastHit2D hit = hits[i];
            if (hit.collider == null || hit.collider.transform.IsChildOf(root.transform)
                || hit.normal.y < 0.9f || hit.distance >= nearest) continue;
            nearest = hit.distance;
            floor = hit;
        }
        if (floor.collider == null) return false;
        float delta = floor.point.y + ContactGap - bounds.min.y;
        if (Mathf.Abs(delta) < 0.0001f) return true;
        Undo.RecordObject(root.transform, "Align Enemy To Ground");
        root.transform.position += Vector3.up * delta;
        if (PrefabUtility.IsPartOfPrefabInstance(root))
            PrefabUtility.RecordPrefabInstancePropertyModifications(root.transform);
        Physics2D.SyncTransforms();
        return true;
    }

    private static float LowestFoot(SpriteRenderer renderer)
    {
        var sprites = new HashSet<Sprite> { renderer.sprite };
        Animator animator = renderer.GetComponent<Animator>();
        if (animator != null && animator.runtimeAnimatorController != null)
            foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
                foreach (EditorCurveBinding binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                    if (binding.path == "" && binding.type == typeof(SpriteRenderer)
                        && binding.propertyName == "m_Sprite")
                        foreach (ObjectReferenceKeyframe key in AnimationUtility.GetObjectReferenceCurve(clip, binding))
                            if (key.value is Sprite sprite) sprites.Add(sprite);

        var textures = new Dictionary<string, Texture2D>();
        float lowest = float.PositiveInfinity;
        try
        {
            foreach (Sprite sprite in sprites)
            {
                string path = AssetDatabase.GetAssetPath(sprite);
                if (!textures.TryGetValue(path, out Texture2D texture))
                {
                    texture = new Texture2D(2, 2);
                    textures.Add(path, texture);
                    if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path)))
                        throw new InvalidOperationException("Cannot read sprite: " + path);
                }
                Rect rect = sprite.rect;
                bool found = false;
                for (int y = (int)rect.yMin; y < (int)rect.yMax && !found; y++)
                    for (int x = (int)rect.xMin; x < (int)rect.xMax; x++)
                        if (texture.GetPixel(x, y).a > 0.1f)
                        {
                            lowest = Mathf.Min(lowest, (y - rect.yMin - sprite.pivot.y) / sprite.pixelsPerUnit);
                            found = true;
                            break;
                        }
            }
        }
        finally
        {
            foreach (Texture2D texture in textures.Values) UnityEngine.Object.DestroyImmediate(texture);
        }
        if (float.IsPositiveInfinity(lowest)) throw new InvalidOperationException("Empty enemy sprites");
        return lowest;
    }
}
