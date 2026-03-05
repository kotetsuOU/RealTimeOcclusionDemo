using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(SMV_Controller))]
public class SMV_ControllerEditor : Editor
{
    private SMV_Controller controller;
    private SerializedObject settingsObject;
    private SMV_Settings settingsComponent;

    private SerializedProperty fileEntriesProp;
    private SerializedProperty edgeThresholdProp;
    private SerializedProperty meshMaterialProp;

    private void OnEnable()
    {
        controller = (SMV_Controller)target;
        settingsComponent = controller.GetComponent<SMV_Settings>();

        if (settingsComponent != null)
        {
            settingsObject = new SerializedObject(settingsComponent);
            fileEntriesProp = settingsObject.FindProperty("fileEntries");
            edgeThresholdProp = settingsObject.FindProperty("edgeThreshold");
            meshMaterialProp = settingsObject.FindProperty("meshMaterial");
        }
    }

    public override void OnInspectorGUI()
    {
        if (settingsObject == null)
        {
            EditorGUILayout.HelpBox("SMV_Settings component is missing.", MessageType.Error);
            return;
        }

        settingsObject.Update();

        EditorGUILayout.LabelField("Data Files", EditorStyles.boldLabel);
        
        int indexToRemove = -1;

        for (int i = 0; i < fileEntriesProp.arraySize; i++)
        {
            SerializedProperty entryProp = fileEntriesProp.GetArrayElementAtIndex(i);
            SerializedProperty useFileProp = entryProp.FindPropertyRelative("useFile");
            SerializedProperty binProp = entryProp.FindPropertyRelative("binFilePath");
            SerializedProperty jsonProp = entryProp.FindPropertyRelative("jsonFilePath");
            SerializedProperty targetObjProp = entryProp.FindPropertyRelative("targetPointCloudObject");

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(useFileProp, new GUIContent($"File {i}"));
            if (GUILayout.Button("Remove", GUILayout.Width(60)))
            {
                indexToRemove = i;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.BeginDisabledGroup(!useFileProp.boolValue);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(binProp, new GUIContent("BIN File"));
            if (GUILayout.Button("Select", GUILayout.Width(60)))
            {
                string path = EditorUtility.OpenFilePanel("Select BIN File", "Assets", "bin");
                if (!string.IsNullOrEmpty(path))
                {
                    // Optionally make path relative to project
                    if (path.StartsWith(Application.dataPath))
                        path = "Assets" + path.Substring(Application.dataPath.Length);
                    binProp.stringValue = path;
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(jsonProp, new GUIContent("JSON File"));
            if (GUILayout.Button("Select", GUILayout.Width(60)))
            {
                string path = EditorUtility.OpenFilePanel("Select JSON File", "Assets", "json");
                if (!string.IsNullOrEmpty(path))
                {
                    if (path.StartsWith(Application.dataPath))
                        path = "Assets" + path.Substring(Application.dataPath.Length);
                    jsonProp.stringValue = path;
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.PropertyField(targetObjProp, new GUIContent("Target GameObject (Transform)"));

            EditorGUI.EndDisabledGroup();
            
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();
        }

        if (indexToRemove >= 0)
        {
            fileEntriesProp.DeleteArrayElementAtIndex(indexToRemove);
        }

        if (GUILayout.Button("Add File Entry"))
        {
            fileEntriesProp.InsertArrayElementAtIndex(fileEntriesProp.arraySize);
            // Reset the newly added entry
            SerializedProperty newEntryProp = fileEntriesProp.GetArrayElementAtIndex(fileEntriesProp.arraySize - 1);
            newEntryProp.FindPropertyRelative("useFile").boolValue = true;
            newEntryProp.FindPropertyRelative("binFilePath").stringValue = "";
            newEntryProp.FindPropertyRelative("jsonFilePath").stringValue = "";
            newEntryProp.FindPropertyRelative("targetPointCloudObject").objectReferenceValue = null;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Mesh Settings", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(edgeThresholdProp);
        EditorGUILayout.PropertyField(meshMaterialProp);

        EditorGUILayout.Space();
        GUI.backgroundColor = new Color(0.8f, 1f, 0.8f);
        if (GUILayout.Button("Generate/Rebuild Mesh", GUILayout.Height(30)))
        {
            controller.RebuildMesh();
        }
        GUI.backgroundColor = Color.white;

        if (settingsObject.ApplyModifiedProperties())
        {
            settingsComponent.MarkDirty();
            if (!Application.isPlaying)
            {
                EditorUtility.SetDirty(settingsComponent);
            }
        }
    }
}
