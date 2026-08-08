using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(RsMaterialController))]
public class RsMaterialControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUI.BeginChangeCheck();
        base.OnInspectorGUI();

        RsMaterialController controller = (RsMaterialController)target;

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(controller, "Change Material Settings");
            controller.ApplyMaterial();
            controller.ApplyColorMode();
            EditorUtility.SetDirty(controller);
        }
    }
}
