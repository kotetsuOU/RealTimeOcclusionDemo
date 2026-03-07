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
    private SerializedProperty useBoundsFilterProp;
    private SerializedProperty generationBoundsProp;
    private SerializedProperty useRsDeviceControllerBoundsProp;
    private SerializedProperty rsDeviceControllerProp;
    private SerializedProperty showBoundsPreviewProp;
    private SerializedProperty showPointsPreviewProp;
    private SerializedProperty boundsPreviewColorProp;
    private SerializedProperty pointsPreviewColorProp;
    private SerializedProperty previewPointSizeProp;
    private SerializedProperty maxPreviewPointCountProp;
    private SerializedProperty meshMaterialProp;

    private bool showDataFiles = true;
    private bool showAddFileEntry;
    private bool showMeshSettings = true;
    private bool showPreviewSettings = true;

    private void OnEnable()
    {
        controller = (SMV_Controller)target;
        settingsComponent = controller.GetComponent<SMV_Settings>();

        if (settingsComponent != null)
        {
            settingsObject = new SerializedObject(settingsComponent);
            fileEntriesProp = settingsObject.FindProperty("fileEntries");
            edgeThresholdProp = settingsObject.FindProperty("edgeThreshold");
            useBoundsFilterProp = settingsObject.FindProperty("useBoundsFilter");
            generationBoundsProp = settingsObject.FindProperty("generationBounds");
            useRsDeviceControllerBoundsProp = settingsObject.FindProperty("useRsDeviceControllerBounds");
            rsDeviceControllerProp = settingsObject.FindProperty("rsDeviceController");
            showBoundsPreviewProp = settingsObject.FindProperty("showBoundsPreview");
            showPointsPreviewProp = settingsObject.FindProperty("showPointsPreview");
            boundsPreviewColorProp = settingsObject.FindProperty("boundsPreviewColor");
            pointsPreviewColorProp = settingsObject.FindProperty("pointsPreviewColor");
            previewPointSizeProp = settingsObject.FindProperty("previewPointSize");
            maxPreviewPointCountProp = settingsObject.FindProperty("maxPreviewPointCount");
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

        showDataFiles = EditorGUILayout.Foldout(showDataFiles, "Data Files", true, EditorStyles.foldoutHeader);

        int indexToRemove = -1;

        if (showDataFiles)
        {
            for (int i = 0; i < fileEntriesProp.arraySize; i++)
            {
                SerializedProperty entryProp = fileEntriesProp.GetArrayElementAtIndex(i);
                SerializedProperty useFileProp = entryProp.FindPropertyRelative("useFile");
                SerializedProperty binProp = entryProp.FindPropertyRelative("binFilePath");
                SerializedProperty jsonProp = entryProp.FindPropertyRelative("jsonFilePath");
                SerializedProperty targetObjProp = entryProp.FindPropertyRelative("targetPointCloudObject");

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUILayout.BeginHorizontal();
                entryProp.isExpanded = EditorGUILayout.Foldout(entryProp.isExpanded, $"File {i}", true);
                EditorGUILayout.PropertyField(useFileProp, GUIContent.none, GUILayout.Width(18f));
                if (GUILayout.Button("Remove", GUILayout.Width(60)))
                {
                    indexToRemove = i;
                }
                EditorGUILayout.EndHorizontal();

                if (entryProp.isExpanded)
                {
                    EditorGUI.BeginDisabledGroup(!useFileProp.boolValue);

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.PropertyField(binProp, new GUIContent("BIN File"));
                    if (GUILayout.Button("Select", GUILayout.Width(60)))
                    {
                        string path = EditorUtility.OpenFilePanel("Select BIN File", "Assets", "bin");
                        if (!string.IsNullOrEmpty(path))
                        {
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
                }

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space();
            }
        }

        if (indexToRemove >= 0)
        {
            fileEntriesProp.DeleteArrayElementAtIndex(indexToRemove);
        }

        showAddFileEntry = EditorGUILayout.Foldout(showAddFileEntry, "Add File Entry", true, EditorStyles.foldoutHeader);
        if (showAddFileEntry && GUILayout.Button("Add File Entry"))
        {
            fileEntriesProp.InsertArrayElementAtIndex(fileEntriesProp.arraySize);
            SerializedProperty newEntryProp = fileEntriesProp.GetArrayElementAtIndex(fileEntriesProp.arraySize - 1);
            newEntryProp.FindPropertyRelative("useFile").boolValue = true;
            newEntryProp.FindPropertyRelative("binFilePath").stringValue = "";
            newEntryProp.FindPropertyRelative("jsonFilePath").stringValue = "";
            newEntryProp.FindPropertyRelative("targetPointCloudObject").objectReferenceValue = null;
            newEntryProp.isExpanded = true;
        }

        EditorGUILayout.Space();
        showMeshSettings = EditorGUILayout.Foldout(showMeshSettings, "Mesh Settings", true, EditorStyles.foldoutHeader);
        if (showMeshSettings)
        {
            EditorGUILayout.PropertyField(edgeThresholdProp);
            EditorGUILayout.PropertyField(useBoundsFilterProp, new GUIContent("Use Bounds Filter"));
            if (useBoundsFilterProp.boolValue)
            {
                EditorGUILayout.PropertyField(useRsDeviceControllerBoundsProp, new GUIContent("Use RsDeviceController Bounds"));
                if (useRsDeviceControllerBoundsProp.boolValue)
                {
                    EditorGUILayout.PropertyField(rsDeviceControllerProp, new GUIContent("RsDeviceController"));
                }
                else
                {
                    EditorGUILayout.PropertyField(generationBoundsProp, new GUIContent("Generation Bounds"), true);
                }
            }
            EditorGUILayout.PropertyField(meshMaterialProp);
        }

        EditorGUILayout.Space();
        showPreviewSettings = EditorGUILayout.Foldout(showPreviewSettings, "Preview Settings", true, EditorStyles.foldoutHeader);
        if (showPreviewSettings)
        {
            EditorGUILayout.PropertyField(showBoundsPreviewProp, new GUIContent("Show Bounds Preview"));
            EditorGUILayout.PropertyField(boundsPreviewColorProp, new GUIContent("Bounds Preview Color"));
            EditorGUILayout.PropertyField(showPointsPreviewProp, new GUIContent("Show Points Preview"));
            EditorGUILayout.PropertyField(pointsPreviewColorProp, new GUIContent("Points Preview Color"));
            EditorGUILayout.PropertyField(previewPointSizeProp, new GUIContent("Preview Point Size"));
            EditorGUILayout.PropertyField(maxPreviewPointCountProp, new GUIContent("Max Preview Point Count"));
        }

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
