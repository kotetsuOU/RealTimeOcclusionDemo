using UnityEditor;
using UnityEngine;

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

        DrawRangeFilterUI(renderer);

        RsPointCloudSceneGizmo.DrawPCAModeInfo();

        serializedObject.ApplyModifiedProperties();
    }

    void OnSceneGUI()
    {
        RsPointCloudRenderer renderer = (RsPointCloudRenderer)target;
        RsPointCloudSceneGizmo.DrawPCAEstimationGizmo(renderer);
    }

    internal static void DrawRangeFilterUI(RsPointCloudRenderer renderer)
    {
        EditorGUILayout.Space();
        EditorGUILayout.Space();

        if (renderer.IsGlobalRangeFilterEnabled)
        {
            GUI.backgroundColor = new Color(1f, 0.6f, 0.6f);
            if (GUILayout.Button("Disable Range Filter"))
            {
                Undo.RecordObject(renderer, "Disable Range Filter");
                renderer.IsGlobalRangeFilterEnabled = false;
                EditorUtility.SetDirty(renderer);
                SceneView.RepaintAll();
            }
        }
        else
        {
            GUI.backgroundColor = new Color(0.6f, 1f, 0.6f);
            if (GUILayout.Button("Enable Range Filter"))
            {
                Undo.RecordObject(renderer, "Enable Range Filter");
                renderer.IsGlobalRangeFilterEnabled = true;
                EditorUtility.SetDirty(renderer);
                SceneView.RepaintAll();
            }
        }

        GUI.backgroundColor = Color.white;
    }
}
