using UnityEditor;
using UnityEngine;

public static class RsPointCloudSceneGizmo
{
    public static void DrawPCAEstimationGizmo(RsPointCloudRenderer renderer)
    {
        bool isIntegratedMode = RsGlobalPointCloudManager.Instance != null &&
                                RsGlobalPointCloudManager.Instance.IsIntegratedPCAMode;

        Vector3 point;
        Vector3 dir;
        Color cylinderColor;
        string modeLabel;

        if (isIntegratedMode)
        {
            point = RsGlobalPointCloudManager.Instance.IntegratedLinePoint;
            dir = RsGlobalPointCloudManager.Instance.IntegratedLineDir;
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

        DrawCylinder(point, dir, halfLength, radius, cylinderColor, isIntegratedMode);
        DrawLabel(point, dir, radius, modeLabel, isIntegratedMode);
    }

    private static void DrawCylinder(Vector3 point, Vector3 dir, float halfLength, float radius, Color color, bool isIntegratedMode)
    {
        int segments = 32;

        Vector3 p1 = point - dir * halfLength;
        Vector3 p2 = point + dir * halfLength;
        Quaternion rot = Quaternion.LookRotation(dir);

        Handles.color = color;
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
    }

    private static void DrawLabel(Vector3 point, Vector3 dir, float radius, string modeLabel, bool isIntegratedMode)
    {
        GUIStyle style = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 16
        };
        style.normal.textColor = isIntegratedMode ? Color.yellow : Color.white;

        string labelText = $"{modeLabel}\nRadius: {radius:F3}\nDirection: ({dir.x:F3}, {dir.y:F3}, {dir.z:F3})\nPoint: ({point.x:F3}, {point.y:F3}, {point.z:F3})";
        Handles.Label(point, labelText, style);
    }

    public static void DrawPCAModeInfo()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("PCA Mode Info", EditorStyles.boldLabel);

        bool isIntegratedMode = RsGlobalPointCloudManager.Instance != null &&
                                RsGlobalPointCloudManager.Instance.IsIntegratedPCAMode;

        EditorGUILayout.HelpBox(
            isIntegratedMode
                ? "Integrated PCA Mode: Using RsGlobalPointCloudManager estimation"
                : "Individual PCA Mode: Using per-renderer estimation",
            isIntegratedMode ? MessageType.Info : MessageType.None
        );
    }
}
