using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(RsPointCloudGroupController))]
public class RsPointCloudGroupControllerEditor : Editor
{
    private bool isVerticesSaved = false;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Batch Control for RsPointCloudRenderer Children", EditorStyles.boldLabel);

        GUI.backgroundColor = Color.cyan;
        if (GUILayout.Button("Export All Current Vertices"))
        {
            ApplyToAllRenderers(renderer =>
            {
                var vertices = renderer.GetFilteredVertices();
                var exportFileName = GetExportFileName(renderer);
                if (vertices != null && vertices.Length > 0 && !string.IsNullOrWhiteSpace(exportFileName))
                {
                    SaveVerticesToFile(vertices, exportFileName);
                }
            });
            isVerticesSaved = true;
        }

        GUI.backgroundColor = Color.white;

        if (isVerticesSaved && GUILayout.Button("Reset Save Status"))
        {
            isVerticesSaved = false;
        }

        GUI.backgroundColor = Color.yellow;

        if (GUILayout.Button("Toggle Range Filter on All"))
        {
            ApplyToAllRenderers(renderer =>
            {
                renderer.IsGlobalRangeFilterEnabled = !renderer.IsGlobalRangeFilterEnabled;
            });
            SceneView.RepaintAll();
            UnityEngine.Debug.Log("Toggle Range Filter on All");
        }

        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space(20);

        EditorGUILayout.LabelField("Performance Logger (Batch Control)", EditorStyles.boldLabel);

        EditorGUI.BeginDisabledGroup(!UnityEngine.Application.isPlaying);

        var firstRenderer = GetFirstRenderer();
        bool isAnyLogging = firstRenderer != null && firstRenderer.IsPerformanceLogging;

        if (isAnyLogging)
        {
            GUI.backgroundColor = new Color(1f, 0.6f, 0.6f);
            if (GUILayout.Button("Stop Performance Logging on All"))
            {
                ApplyToAllRenderers(renderer => renderer.StopPerformanceLog());
            }
            GUI.backgroundColor = Color.white;
        }
        else
        {
            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.6f, 1f, 0.6f);

            if (GUILayout.Button("Start Logging on All (New Files)"))
            {
                ApplyToAllRenderers(renderer => {
                    renderer.appendLog = false;
                    renderer.StartPerformanceLog();
                });
            }

            if (GUILayout.Button("Start Logging on All (Append)"))
            {
                ApplyToAllRenderers(renderer => {
                    renderer.appendLog = true;
                    renderer.StartPerformanceLog();
                });
            }

            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
        }

        EditorGUI.EndDisabledGroup();
    }

    private void OnSceneGUI()
    {
        if (UnityEngine.Application.isPlaying)
        {
            return;
        }

        RsDeviceController deviceController = FindObjectOfType<RsDeviceController>();
        if (deviceController == null)
        {
            Handles.BeginGUI();
            GUILayout.Window(0, new Rect(10, 10, 320, 50), (id) =>
            {
                EditorGUILayout.HelpBox("RsDeviceController がシーンに見つかりません。スキャン範囲を描画できません。", MessageType.Warning);
            }, "スキャン範囲 警告");
            Handles.EndGUI();
            return;
        }

        RsPointCloudGroupController groupController = (RsPointCloudGroupController)target;
        Transform groupTransform = groupController.transform;

        Vector3 scanRange = deviceController.RealSenseScanRange;
        float frameWidth = deviceController.FrameWidth;

        Vector3 minPoint = new Vector3(frameWidth, frameWidth, frameWidth);
        Vector3 maxPoint = new Vector3(
            scanRange.x - frameWidth,
            scanRange.y - frameWidth,
            scanRange.z - frameWidth
        );

        Vector3 size = maxPoint - minPoint;
        Vector3 center = minPoint + (size * 0.5f);

        if (size.x < 0 || size.y < 0 || size.z < 0)
        {
            return;
        }

        Handles.matrix = groupTransform.localToWorldMatrix;

        Handles.color = Color.yellow;

        Handles.DrawWireCube(center, size);
    }

    private void ApplyToAllRenderers(System.Action<RsPointCloudRenderer> action)
    {
        RsPointCloudGroupController group = (RsPointCloudGroupController)target;
        foreach (Transform child in group.transform)
        {
            var renderer = child.GetComponent<RsPointCloudRenderer>();
            if (renderer != null)
            {
                action.Invoke(renderer);
            }
        }
    }

    private RsPointCloudRenderer GetFirstRenderer()
    {
        RsPointCloudGroupController group = (RsPointCloudGroupController)target;
        foreach (Transform child in group.transform)
        {
            var renderer = child.GetComponent<RsPointCloudRenderer>();
            if (renderer != null)
            {
                return renderer;
            }
        }
        return null;
    }

    private string GetExportFileName(RsPointCloudRenderer renderer)
    {
        var type = typeof(RsPointCloudRenderer);
        var field = type.GetField("exportFileName", BindingFlags.NonPublic | BindingFlags.Instance);
        return field?.GetValue(renderer) as string;
    }

    private void SaveVerticesToFile(Vector3[] vertices, string fileName)
    {
        string directoryPath = "Assets/HandTrakingData/PointCloudData";
        if (!System.IO.Directory.Exists(directoryPath))
        {
            System.IO.Directory.CreateDirectory(directoryPath);
        }

        string path = $"{directoryPath}/{fileName}";

        using (System.IO.StreamWriter writer = new System.IO.StreamWriter(path))
        {
            foreach (var v in vertices)
            {
                writer.WriteLine($"{v.x}, {v.y}, {v.z}");
            }
        }

        UnityEngine.Debug.Log($"Saved {vertices.Length} vertices to {path}");
        AssetDatabase.Refresh();
    }
}