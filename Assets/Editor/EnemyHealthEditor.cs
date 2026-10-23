using UnityEditor;
using UnityEngine;

/// <summary>既存の調整項目を保ったまま、現在HPを読み取り専用で表示する。</summary>
[CustomEditor(typeof(EnemyHealth)), CanEditMultipleObjects]
public sealed class EnemyHealthEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        if (targets.Length != 1) return;
        var health = (EnemyHealth)target;
        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.IntField("Current HP (read only)", Application.isPlaying ? health.CurrentHp : health.MaxHp);
        if (Application.isPlaying) Repaint();
    }
}
