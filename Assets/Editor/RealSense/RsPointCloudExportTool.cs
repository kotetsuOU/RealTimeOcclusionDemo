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

        bool isPlaying = Application.isPlaying;
        bool isReadbackPending = renderer != null && renderer.IsFilteredCountReadbackPending;
        int availableVertexCount = renderer != null ? renderer.GetLastFilteredCount() : 0;

        if (!isPlaying)
        {
            EditorGUILayout.HelpBox("Export is available only during Play Mode.", MessageType.Info);
        }
        else if (isReadbackPending)
        {
            EditorGUILayout.HelpBox("Waiting for GPU readback...", MessageType.Info);
        }
        else if (availableVertexCount <= 0)
        {
            EditorGUILayout.HelpBox("No filtered vertices available for export yet.", MessageType.Warning);
        }

        GUI.backgroundColor = Color.cyan;
        EditorGUI.BeginDisabledGroup(!isPlaying || isReadbackPending || availableVertexCount <= 0);
        if (GUILayout.Button($"Export Current Frame Vertices ({availableVertexCount})"))
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
        EditorGUI.EndDisabledGroup();

        if (isVerticesSaved && GUILayout.Button("Reset Save Status"))
        {
            isVerticesSaved = false;
        }

        GUI.backgroundColor = Color.white;
        return isVerticesSaved;
    }
}
