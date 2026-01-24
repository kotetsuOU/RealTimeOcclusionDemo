using System.IO;
using UnityEditor;
using UnityEngine;
using static System.Net.Mime.MediaTypeNames;

[CustomEditor(typeof(RsPointCloudRenderer))]
public class RsPointCloudRendererEditor : Editor
{
    private bool isVerticesSaved = false;
    private SerializedProperty exportFileNameProp;

    void OnEnable()
    {
        exportFileNameProp = serializedObject.FindProperty("exportFileName");
    }

    public override void OnInspectorGUI()
    {
        if (exportFileNameProp == null)
        {
            OnEnable();
        }

        serializedObject.Update();
        base.OnInspectorGUI();

        var renderer = (RsPointCloudRenderer)target;

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
                    SaveToFile(vertices, exportFileNameProp.stringValue);
                    isVerticesSaved = true;
                }
            }
            else
            {
                UnityEngine.Debug.LogWarning("Filtered vertices not available.");
            }
        }

        if (isVerticesSaved && GUILayout.Button("Reset Save Status"))
        {
            isVerticesSaved = false;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Performance Logger", EditorStyles.boldLabel);

        EditorGUI.BeginDisabledGroup(!UnityEngine.Application.isPlaying);

        GUIStyle buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 12, fontStyle = FontStyle.Bold, fixedHeight = 25 };

        if (renderer.IsPerformanceLogging)
        {
            GUI.backgroundColor = new Color(1f, 0.6f, 0.6f);
            if (GUILayout.Button("Stop Performance Logging", buttonStyle))
            {
                renderer.StopPerformanceLog();
            }
        }
        else
        {
            GUI.backgroundColor = new Color(0.6f, 1f, 0.6f);
            if (GUILayout.Button("Start Performance Logging", buttonStyle))
            {
                renderer.StartPerformanceLog();
            }
        }
        GUI.backgroundColor = Color.white;

        EditorGUI.EndDisabledGroup();

        EditorGUILayout.Space();
        EditorGUILayout.Space();

        if (renderer.IsGlobalRangeFilterEnabled)
        {
            GUI.backgroundColor = new Color(1f, 0.6f, 0.6f);
            if (GUILayout.Button("Disable Range Filter"))
            {
                renderer.IsGlobalRangeFilterEnabled = false;
                SceneView.RepaintAll();
            }
        }
        else
        {
            GUI.backgroundColor = new Color(0.6f, 1f, 0.6f);
            if (GUILayout.Button("Enable Range Filter"))
            {
                renderer.IsGlobalRangeFilterEnabled = true;
                SceneView.RepaintAll();
            }
        }
 
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("PCA Mode Info", EditorStyles.boldLabel);
        
        bool isIntegratedMode = GlobalPointCloudManager.Instance != null && 
                                GlobalPointCloudManager.Instance.IsIntegratedPCAMode;
        
        EditorGUILayout.HelpBox(
            isIntegratedMode 
                ? "Integrated PCA Mode: Using GlobalPointCloudManager estimation" 
                : "Individual PCA Mode: Using per-renderer estimation",
            isIntegratedMode ? MessageType.Info : MessageType.None
        );

        serializedObject.ApplyModifiedProperties();
    }

    void OnSceneGUI()
    {
        RsPointCloudRenderer renderer = (RsPointCloudRenderer)target;
        
        bool isIntegratedMode = GlobalPointCloudManager.Instance != null && 
                                GlobalPointCloudManager.Instance.IsIntegratedPCAMode;
        
        Vector3 point;
        Vector3 dir;
        Color cylinderColor;
        string modeLabel;
        
        if (isIntegratedMode)
        {
            point = GlobalPointCloudManager.Instance.IntegratedLinePoint;
            dir = GlobalPointCloudManager.Instance.IntegratedLineDir;
            cylinderColor = new Color(1f, 0.5f, 0f, 0.1f);
            modeLabel = "[Integrated]";
        }
        else
        {
            point = renderer.EstimatedPoint;
            dir = renderer.EstimatedDir;
            cylinderColor = new Color(0f, 0.5f, 1f, 0.1f);
            modeLabel = "[Individual]";
        }
        
        if (dir == Vector3.zero) return;
        dir.Normalize();
        
        float halfLength = 0.3f;
        float radius = renderer.maxPlaneDistance;
        int segments = 32;
        
        Vector3 p1 = point - dir * halfLength;
        Vector3 p2 = point + dir * halfLength;
        Quaternion rot = Quaternion.LookRotation(dir);
        
        Handles.color = cylinderColor;
        Vector3[] topCircle = new Vector3[segments];
        Vector3[] bottomCircle = new Vector3[segments];
        for (int i = 0; i < segments; i++)
        {
            float angle = (i / (float)segments) * Mathf.PI * 2f;
            Vector3 r = rot * new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0);
            topCircle[i] = p2 + r;
            bottomCircle[i] = p1 + r;
        }
        for (int i = 0; i < segments; i++)
        {
            int next = (i + 1) % segments;
            Handles.DrawAAConvexPolygon(bottomCircle[i], bottomCircle[next], topCircle[next], topCircle[i]);
        }
        Handles.DrawAAConvexPolygon(topCircle);
        Handles.DrawAAConvexPolygon(bottomCircle);
        
        Handles.color = isIntegratedMode ? Color.yellow : Color.green;
        Handles.DrawLine(p1, p2);
        
        GUIStyle style = new GUIStyle(EditorStyles.boldLabel);
        style.fontSize = 16;
        style.normal.textColor = isIntegratedMode ? Color.yellow : Color.white;
        
        string labelText = $"{modeLabel}\nRadius: {radius:F3}\nDirection: ({dir.x:F3}, {dir.y:F3}, {dir.z:F3})\nPoint: ({point.x:F3}, {point.y:F3}, {point.z:F3})";
        Handles.Label(point, labelText, style);
    }

    private void SaveToFile(Vector3[] vertices, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            UnityEngine.Debug.LogWarning("Export file name is empty.");
            return;
        }

        string path = Path.Combine("Assets/HandTrackingData/PointCloudData", fileName);
        using (var writer = new StreamWriter(path))
        {
            foreach (var v in vertices)
                writer.WriteLine($"{v.x}, {v.y}, {v.z}");
        }

        UnityEngine.Debug.Log($"Saved {vertices.Length} vertices to {path}");
        AssetDatabase.Refresh();
    }
}