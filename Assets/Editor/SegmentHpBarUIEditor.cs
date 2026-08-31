using UnityEditor;

[CustomEditor(typeof(SegmentHpBarUI))]
public class SegmentHpBarUIEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawDefaultInspector();
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(6f);
        EditorGUILayout.HelpBox(
            "HpFill / DamageFill画像は拡縮せず、HpMask / DamageMaskの幅だけを更新します。",
            MessageType.Info);
    }
}
