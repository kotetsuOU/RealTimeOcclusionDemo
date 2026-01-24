using System.IO;
using UnityEditor;
using UnityEngine;

public static class RsPointCloudExportTool
{
    public static void SaveToFile(Vector3[] vertices, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            Debug.LogWarning("Export file name is empty.");
            return;
        }

        string directory = "Assets/HandTrackingData/PointCloudData";
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string path = Path.Combine(directory, fileName);
        using (var writer = new StreamWriter(path))
        {
            foreach (var v in vertices)
            {
                writer.WriteLine($"{v.x}, {v.y}, {v.z}");
            }
        }

        Debug.Log($"Saved {vertices.Length} vertices to {path}");
        AssetDatabase.Refresh();
    }

    public static bool DrawExportUI(RsPointCloudRenderer renderer, SerializedProperty exportFileNameProp, bool isVerticesSaved)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Debug Tools", EditorStyles.boldLabel);

        if (exportFileNameProp != null)
        {
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(exportFileNameProp, new GUIContent("Export File Name"));
            if (EditorGUI.EndChangeCheck())
            {
                isVerticesSaved = false;
            }
        }
        else
        {
            EditorGUILayout.HelpBox("SerializedProperty 'exportFileName' not found.", MessageType.Error);
        }

        EditorGUILayout.Space();

        GUI.backgroundColor = Color.cyan;
        if (GUILayout.Button("Export Current Frame Vertices"))
        {
            Vector3[] vertices = renderer.GetFilteredVertices();
            if (vertices != null && vertices.Length > 0)
            {
                if (!isVerticesSaved)
                {
                    SaveToFile(vertices, exportFileNameProp?.stringValue ?? "export.txt");
                    isVerticesSaved = true;
                }
            }
            else
            {
                Debug.LogWarning("Filtered vertices not available.");
            }
        }

        if (isVerticesSaved && GUILayout.Button("Reset Save Status"))
        {
            isVerticesSaved = false;
        }

        GUI.backgroundColor = Color.white;
        return isVerticesSaved;
    }
}
