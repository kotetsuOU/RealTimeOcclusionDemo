using UnityEditor;
using UnityEngine;
using System.Linq;

[CustomEditor(typeof(RsMaterialController))]
public class RsMaterialControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        RsMaterialController controller = (RsMaterialController)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Control Panel", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        if (controller.materials != null && controller.materials.Count > 0)
        {
            EditorGUILayout.LabelField("Material Selection", EditorStyles.boldLabel);
            string[] materialNames = controller.materials.Select(m => m != null ? m.name : "None").ToArray();
            int currentIndex = controller.GetCurrentMaterialIndex();

            EditorGUI.BeginChangeCheck();
            int selectedIndex = EditorGUILayout.Popup("Select Material", currentIndex, materialNames);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(controller, "Change Material");
                controller.ChangeMaterial(selectedIndex);
                EditorUtility.SetDirty(controller);
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Color Selection", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        PointCloudColorMode selectedMode =
            (PointCloudColorMode)EditorGUILayout.EnumPopup("Color Mode", controller.colorMode);

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(controller, "Change PointCloud Color");
            controller.ChangeColorMode(selectedMode);
            EditorUtility.SetDirty(controller);
        }
    }
}