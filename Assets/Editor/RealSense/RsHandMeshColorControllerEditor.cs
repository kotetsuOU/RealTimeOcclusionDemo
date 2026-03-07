using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(RsHandMeshColorController))]
public class RsHandMeshColorControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        RsHandMeshColorController controller = (RsHandMeshColorController)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Color Selection", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("RealSense uses source vertex color. Skin/Black use a solid color and avoid RealSense color usage.", MessageType.Info);

        EditorGUI.BeginChangeCheck();
        RsHandMeshDisplayColorMode selectedMode =
            (RsHandMeshDisplayColorMode)EditorGUILayout.EnumPopup("Color Mode", controller.colorMode);

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(controller, "Change Hand Mesh Color");
            controller.ChangeColorMode(selectedMode);
            EditorUtility.SetDirty(controller);
        }
    }
}
