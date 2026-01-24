using UnityEditor;
using UnityEngine;

/// <summary>
/// RsPointCloudRendererのカスタムエディター
/// 各機能は専用クラスに委譲される：
/// - RsPointCloudExportTool: エクスポート機能
/// - RsPointCloudSceneGizmo: SceneView描画
/// </summary>
[CustomEditor(typeof(RsPointCloudRenderer))]
public class RsPointCloudRendererEditor : Editor
{
    private bool _isVerticesSaved = false;
    private SerializedProperty _exportFileNameProp;

    void OnEnable()
    {
        _exportFileNameProp = serializedObject.FindProperty("exportFileName");
    }

    public override void OnInspectorGUI()
    {
        if (_exportFileNameProp == null)
        {
            OnEnable();
        }

        serializedObject.Update();
        base.OnInspectorGUI();

        var renderer = (RsPointCloudRenderer)target;

        _isVerticesSaved = RsPointCloudExportTool.DrawExportUI(renderer, _exportFileNameProp, _isVerticesSaved);

        DrawPerformanceLoggerUI(renderer);

        DrawRangeFilterUI(renderer);

        RsPointCloudSceneGizmo.DrawPCAModeInfo();

        serializedObject.ApplyModifiedProperties();
    }

    void OnSceneGUI()
    {
        RsPointCloudRenderer renderer = (RsPointCloudRenderer)target;
        RsPointCloudSceneGizmo.DrawPCAEstimationGizmo(renderer);
    }

    private void DrawPerformanceLoggerUI(RsPointCloudRenderer renderer)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Performance Logger", EditorStyles.boldLabel);

        EditorGUI.BeginDisabledGroup(!Application.isPlaying);

        GUIStyle buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            fixedHeight = 25
        };

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
    }

    private void DrawRangeFilterUI(RsPointCloudRenderer renderer)
    {
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
    }
}