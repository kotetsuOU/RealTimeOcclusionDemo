using UnityEditor;
using UnityEngine;
using System.Linq;

[CustomEditor(typeof(MaterialController))]
public class MaterialControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        MaterialController controller = (MaterialController)target;

        if (controller.materials == null || controller.materials.Count == 0)
        {
            return;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Material Control", EditorStyles.boldLabel);

        string[] materialNames = controller.materials.Select(m => m != null ? m.name : "None").ToArray();

        int currentIndex = controller.GetCurrentMaterialIndex();

        int selectedIndex = EditorGUILayout.Popup("Select Material", currentIndex, materialNames);

        if (selectedIndex != currentIndex)
        {
            controller.ChangeMaterial(selectedIndex);
            EditorUtility.SetDirty(controller);
        }
    }
}
