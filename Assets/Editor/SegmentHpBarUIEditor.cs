using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SegmentHpBarUI))]
public class SegmentHpBarUIEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawDefaultInspector();
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(8f);

        var ui = (SegmentHpBarUI)target;
        if (ui == null)
            return;

        if (GUILayout.Button("Recalculate Mask Rects From Bar"))
        {
            Schedule(ui, static u =>
            {
                Undo.RecordObject(u, "Recalculate HP Mask Rects");
                u.RecalculateSegmentMaskRectsFromBar();
                EditorUtility.SetDirty(u);
            });
        }

        if (GUILayout.Button("Apply Display Layout From Script"))
        {
            Schedule(ui, static u =>
            {
                Undo.RecordObject(u, "Apply HP Bar Display Layout");
                u.ApplyDisplayLayoutFromScript();
                EditorUtility.SetDirty(u);
            });
        }

        if (GUILayout.Button("Apply Masks To Scene"))
        {
            Schedule(ui, static u =>
            {
                Undo.RecordObject(u, "Apply HP Masks To Scene");
                u.RebuildSegmentMaskLayer();
                EditorUtility.SetDirty(u);
            });
        }
    }

    static void Schedule(SegmentHpBarUI ui, System.Action<SegmentHpBarUI> action)
    {
        EditorApplication.delayCall += () =>
        {
            if (ui == null)
                return;

            action(ui);
        };
    }
}
