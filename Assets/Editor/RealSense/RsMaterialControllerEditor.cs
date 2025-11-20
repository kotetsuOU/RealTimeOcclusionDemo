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

        if (controller.materials == null || controller.materials.Count == 0)
        {
            return;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Material Control", EditorStyles.boldLabel);

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
}