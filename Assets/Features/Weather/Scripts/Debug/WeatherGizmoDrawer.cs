using UnityEngine;

namespace Features.Weather
{
    /// <summary>
    /// 天候システム（雲の発生面・落雷着地面・境界ボックス）の Scene ビュー Gizmo 描画を担当するヘルパークラス。
    /// WeatherManager からデバッグ描画ロジックを分離します。
    /// </summary>
    public static class WeatherGizmoDrawer
    {
        /// <summary>
        /// 雲の面 (水色) と着地面 (黄色)、および空間境界ボックスをワイヤー描画します。
        /// </summary>
        public static void DrawSpaceGizmos(
            Transform viewer,
            float cloudY,
            float groundY,
            bool followViewer,
            Vector2 boundsCenter,
            Vector2 boundsSize)
        {
            Vector3 center = followViewer && viewer != null
                ? new Vector3(viewer.position.x + boundsCenter.x, 0f, viewer.position.z + boundsCenter.y)
                : new Vector3(boundsCenter.x, 0f, boundsCenter.y);

            // 1. 雲の発生面 (水色)
            Gizmos.color = new Color(0.2f, 0.7f, 1.0f, 0.75f);
            Vector3 cloudCenter = new Vector3(center.x, cloudY, center.z);
            Vector3 halfSize = new Vector3(boundsSize.x * 0.5f, 0f, boundsSize.y * 0.5f);
            Vector3 t0 = cloudCenter + new Vector3(-halfSize.x, 0f, -halfSize.z);
            Vector3 t1 = cloudCenter + new Vector3( halfSize.x, 0f, -halfSize.z);
            Vector3 t2 = cloudCenter + new Vector3( halfSize.x, 0f,  halfSize.z);
            Vector3 t3 = cloudCenter + new Vector3(-halfSize.x, 0f,  halfSize.z);
            Gizmos.DrawLine(t0, t1);
            Gizmos.DrawLine(t1, t2);
            Gizmos.DrawLine(t2, t3);
            Gizmos.DrawLine(t3, t0);

            // 2. 地面の着地・落雷面 (黄色) - Z-fighting (床メッシュとの重なりチラつき) 防止でわずかにオフセット
            Gizmos.color = new Color(1.0f, 0.85f, 0.2f, 0.8f);
            Vector3 groundCenter = new Vector3(center.x, groundY + 0.002f, center.z);
            Vector3 b0 = groundCenter + new Vector3(-halfSize.x, 0f, -halfSize.z);
            Vector3 b1 = groundCenter + new Vector3( halfSize.x, 0f, -halfSize.z);
            Vector3 b2 = groundCenter + new Vector3( halfSize.x, 0f,  halfSize.z);
            Vector3 b3 = groundCenter + new Vector3(-halfSize.x, 0f,  halfSize.z);
            Gizmos.DrawLine(b0, b1);
            Gizmos.DrawLine(b1, b2);
            Gizmos.DrawLine(b2, b3);
            Gizmos.DrawLine(b3, b0);

            // 3. 領域枠の縦線 (薄青)
            Gizmos.color = new Color(0.5f, 0.8f, 1.0f, 0.35f);
            Gizmos.DrawLine(b0, t0);
            Gizmos.DrawLine(b1, t1);
            Gizmos.DrawLine(b2, t2);
            Gizmos.DrawLine(b3, t3);
        }
    }
}
