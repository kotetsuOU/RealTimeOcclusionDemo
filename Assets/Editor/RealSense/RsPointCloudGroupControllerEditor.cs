using System.Reflection;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(RsPointCloudGroupController))]
public class RsPointCloudGroupControllerEditor : Editor
{
    private bool _isVerticesSaved = false;
    private RsPointCloudGroupController _controller;

    private void OnEnable()
    {
        _controller = (RsPointCloudGroupController)target;
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();

        DrawBatchControlSection();
        EditorGUILayout.Space(20);
        DrawPerformanceLoggerSection();
    }

    private void OnSceneGUI()
    {
        if (Application.isPlaying) return;

        DrawScanRangeGizmo();
    }

    #region Inspector Sections

    private void DrawBatchControlSection()
    {
        EditorGUILayout.LabelField("Batch Control for RsPointCloudRenderer Children", EditorStyles.boldLabel);

        // Export Button
        GUI.backgroundColor = Color.cyan;
        if (GUILayout.Button("Export All Current Vertices"))
        {
            ExportAllVertices();
            _isVerticesSaved = true;
        }
        GUI.backgroundColor = Color.white;

        if (_isVerticesSaved && GUILayout.Button("Reset Save Status"))
        {
            _isVerticesSaved = false;
        }

        // Toggle Range Filter Button
        GUI.backgroundColor = Color.yellow;
        if (GUILayout.Button("Toggle Range Filter on All"))
        {
            _controller.ToggleAllRangeFilters();
            SceneView.RepaintAll();
            Debug.Log("[RsPointCloudGroupController] Toggled Range Filter on All");
        }
        GUI.backgroundColor = Color.white;
    }

    private void DrawPerformanceLoggerSection()
    {
        EditorGUILayout.LabelField("Performance Logger (Batch Control)", EditorStyles.boldLabel);

        EditorGUI.BeginDisabledGroup(!Application.isPlaying);

        if (_controller.IsAnyPerformanceLogging())
        {
            GUI.backgroundColor = new Color(1f, 0.6f, 0.6f);
            if (GUILayout.Button("Stop Performance Logging on All"))
            {
                _controller.StopAllPerformanceLogs();
            }
            GUI.backgroundColor = Color.white;
        }
        else
        {
            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.6f, 1f, 0.6f);

            if (GUILayout.Button("Start Logging on All (New Files)"))
            {
                _controller.StartAllPerformanceLogs(append: false);
            }

            if (GUILayout.Button("Start Logging on All (Append)"))
            {
                _controller.StartAllPerformanceLogs(append: true);
            }

            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
        }

        EditorGUI.EndDisabledGroup();
    }

    #endregion

    #region Scene GUI

    private void DrawScanRangeGizmo()
    {
        var deviceController = Object.FindFirstObjectByType<RsDeviceController>();
        if (deviceController == null)
        {
            DrawWarningWindow("RsDeviceController がシーンに見つかりません。スキャン範囲を描画できません。");
            return;
        }

        Vector3 scanRange = deviceController.RealSenseScanRange;
        float frameWidth = deviceController.FrameWidth;

        Vector3 minPoint = new Vector3(frameWidth, frameWidth, frameWidth);
        Vector3 maxPoint = scanRange - minPoint;
        Vector3 size = maxPoint - minPoint;

        if (size.x < 0 || size.y < 0 || size.z < 0) return;

        Vector3 center = minPoint + size * 0.5f;

        Handles.matrix = _controller.transform.localToWorldMatrix;
        Handles.color = Color.yellow;
        Handles.DrawWireCube(center, size);
    }

    private void DrawWarningWindow(string message)
    {
        Handles.BeginGUI();
        GUILayout.Window(0, new Rect(10, 10, 320, 50), (id) =>
        {
            EditorGUILayout.HelpBox(message, MessageType.Warning);
        }, "スキャン範囲 警告");
        Handles.EndGUI();
    }

    #endregion

    #region Export

    private void ExportAllVertices()
    {
        _controller.ApplyToAllRenderers(renderer =>
        {
            var vertices = renderer.GetFilteredVertices();
            var exportFileName = GetExportFileName(renderer);

            if (vertices != null && vertices.Length > 0 && !string.IsNullOrWhiteSpace(exportFileName))
            {
                RsPointCloudExportTool.SaveToFile(vertices, exportFileName);
            }
        });
    }

    private string GetExportFileName(RsPointCloudRenderer renderer)
    {
        var field = typeof(RsPointCloudRenderer).GetField("exportFileName", BindingFlags.NonPublic | BindingFlags.Instance);
        return field?.GetValue(renderer) as string;
    }

    #endregion
}