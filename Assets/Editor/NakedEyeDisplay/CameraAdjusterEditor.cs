using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(CameraAdjuster))]
public class CameraAdjusterEditor : Editor
{
    public override void OnInspectorGUI()
    {
        if (target == null)
        {
            return;
        }

        base.OnInspectorGUI();
        CameraAdjuster adjuster = (CameraAdjuster)target;

        EditorGUILayout.Space();
        if (GUILayout.Button($"Move to default position : {adjuster.defaultPosition}"))
        {
            Undo.RecordObject(adjuster.transform, "Move Camera Adjuster Position");
            adjuster.MoveToDefaultPosition();
            EditorUtility.SetDirty(adjuster.transform);
        }
    }
}
