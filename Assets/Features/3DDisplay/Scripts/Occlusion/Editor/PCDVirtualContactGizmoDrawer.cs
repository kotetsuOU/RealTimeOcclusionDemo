using UnityEditor;
using UnityEngine;

/// <summary>
/// <see cref="PCDOcclusionPipelineController"/> の仮想接触オクルージョン領域を
/// Scene ビュー上に Gizmo 描画する Editor 専用クラス。
/// ランタイムコントローラーからデバッグ可視化および HCD への直接依存を分離します。
/// </summary>
public static class PCDVirtualContactGizmoDrawer
{
    [DrawGizmo(GizmoType.Active | GizmoType.Selected | GizmoType.NonSelected)]
    private static void DrawVirtualContactGizmos(PCDOcclusionPipelineController controller, GizmoType gizmoType)
    {
        if (controller == null || !controller.enableVirtualContactOcclusion || !Application.isPlaying)
        {
            return;
        }

        if (HCD_Pipeline.Instance == null || HCD_Pipeline.Instance.distanceProcessor == null)
        {
            return;
        }

        var trackedClusters = HCD_Pipeline.Instance.GetTrackedClusters();
        if (trackedClusters == null)
        {
            return;
        }

        float radius = controller.virtualContactRadius;
        float offset = HCD_Pipeline.Instance.distanceProcessor.surfaceDistanceThreshold;
        Color color = controller.virtualContactColor;

        Gizmos.color = color;
        Handles.color = color;

        foreach (var c in trackedClusters)
        {
            if (!c.IsAlive) continue;

            Vector3 centroid = c.Centroid;
            Vector3 normal = c.Normal.normalized;
            if (normal.sqrMagnitude < 0.1f) normal = Vector3.up;

            centroid += normal * offset;

            Handles.DrawWireDisc(centroid, normal, radius);
            Gizmos.DrawLine(centroid, centroid + normal * 0.05f);
        }
    }
}

