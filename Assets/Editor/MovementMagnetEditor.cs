using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MovementMagnet))]
public sealed class MovementMagnetEditor : Editor
{
    public override void OnInspectorGUI()
    {
        using (new EditorGUI.DisabledScope(Application.isPlaying))
        {
            if (DrawDefaultInspector())
                RefreshRail();

            EditorGUILayout.HelpBox("本体の位置が始点の吸着点です。Sceneビューのレール上にある番号付きハンドルをドラッグしてルートを編集できます。",
                MessageType.Info);

            if (GUILayout.Button("経由点を追加"))
            {
                serializedObject.Update();
                var points = serializedObject.FindProperty("waypoints");
                int index = points.arraySize;
                Vector2 previous = index > 0 ? points.GetArrayElementAtIndex(index - 1).vector2Value : Vector2.zero;
                points.arraySize++;
                points.GetArrayElementAtIndex(index).vector2Value = previous + Vector2.right * 2f;
                serializedObject.ApplyModifiedProperties();
                RefreshRail();
            }
        }
    }

    private void OnSceneGUI()
    {
        if (Application.isPlaying)
            return;

        var root = ((MovementMagnet)target).transform;
        serializedObject.Update();
        var points = serializedObject.FindProperty("waypoints");
        var rail = serializedObject.FindProperty("rail").objectReferenceValue as LineRenderer;
        var railTransform = rail != null ? rail.transform : root;

        using (new Handles.DrawingScope(new Color(0.75f, 0.9f, 1f)))
        {
            Handles.Label(railTransform.position, "0 始点");
            if (railTransform != root)
            {
                Handles.DrawDottedLine(railTransform.position, root.position, 4f);
                Handles.Label(root.position, "吸着点");
            }

            for (int i = 0; i < points.arraySize; i++)
            {
                var point = points.GetArrayElementAtIndex(i);
                Vector3 world = railTransform.TransformPoint(point.vector2Value);
                float size = HandleUtility.GetHandleSize(world) * 0.08f;
                Handles.Label(world + root.up * size, (i + 1).ToString());
                EditorGUI.BeginChangeCheck();
                Vector3 moved = Handles.FreeMoveHandle(world, size, Vector3.zero, Handles.DotHandleCap);
                if (EditorGUI.EndChangeCheck())
                    point.vector2Value = railTransform.InverseTransformPoint(moved);
            }
        }

        if (serializedObject.ApplyModifiedProperties())
            RefreshRail();
    }

    private void RefreshRail()
    {
        var rail = serializedObject.FindProperty("rail").objectReferenceValue;
        if (rail != null) Undo.RecordObject(rail, "Edit magnet rail");

        ((MovementMagnet)target).RefreshRail();

        if (rail != null) PrefabUtility.RecordPrefabInstancePropertyModifications(rail);
        SceneView.RepaintAll();
    }
}
