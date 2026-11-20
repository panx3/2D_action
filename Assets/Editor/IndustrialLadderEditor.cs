using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(IndustrialLadder))]
public sealed class IndustrialLadderEditor : Editor
{
    public override void OnInspectorGUI()
    {
        if (!DrawDefaultInspector()) return;
        var ladder = (IndustrialLadder)target;
        var trigger = ladder.GetComponent<BoxCollider2D>();
        var renderers = ladder.GetComponentsInChildren<SpriteRenderer>(true);
        Undo.RecordObject(trigger, "Resize ladder");
        foreach (var renderer in renderers)
        {
            Undo.RecordObject(renderer, "Resize ladder");
            Undo.RecordObject(renderer.transform, "Resize ladder");
        }
        ladder.RefreshGeometry();
        PrefabUtility.RecordPrefabInstancePropertyModifications(trigger);
        foreach (var renderer in renderers)
        {
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer.transform);
        }
    }
}
