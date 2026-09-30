using System.Collections.Generic;
using UnityEngine;

namespace Features.PhysicalResponse
{
    /// <summary>
    /// 足4点の座標から基準平面および足領域バウンディングボックスの幾何計算を行うプロセッサクラス。
    /// </summary>
    public static class PR_LiftPlaneCalculator
    {
        /// <summary>
        /// 計算された足平面および領域バウンディングの幾何データ。
        /// </summary>
        public struct FootPlaneData
        {
            public Vector3 Origin;
            public Vector3 Normal;
            public float MinX;
            public float MaxX;
            public float MinZ;
            public float MaxZ;
            public float RefY;
            public bool IsValid;
        }

        /// <summary>
        /// 足4点のワールド座標と有効トグルから基準平面とローカルバウンディングを算出します。
        /// </summary>
        public static FootPlaneData CalculatePlane(
            Transform targetTransform,
            Vector3 fl, Vector3 fr, Vector3 bl, Vector3 br,
            bool enableFL, bool enableFR, bool enableBL, bool enableBR,
            float planeMargin,
            bool hasRestPose,
            Vector3 localFL, Vector3 localFR, Vector3 localBL, Vector3 localBR)
        {
            FootPlaneData data = default;
            if (targetTransform == null) return data;

            List<Vector3> activeFeet = new List<Vector3>(4);
            if (enableFL) activeFeet.Add(fl);
            if (enableFR) activeFeet.Add(fr);
            if (enableBL) activeFeet.Add(bl);
            if (enableBR) activeFeet.Add(br);

            if (activeFeet.Count == 0) return data;

            // 1. 平面の重心（Origin）
            Vector3 origin = Vector3.zero;
            for (int i = 0; i < activeFeet.Count; i++)
            {
                origin += activeFeet[i];
            }
            origin /= activeFeet.Count;
            data.Origin = origin;

            // 2. 平面の法線（Normal）
            Vector3 normal = targetTransform.up;
            if (activeFeet.Count == 4)
            {
                Vector3 diag1 = fl - br;
                Vector3 diag2 = fr - bl;
                Vector3 cross = Vector3.Cross(diag1, diag2);
                if (cross.sqrMagnitude > 1e-6f)
                {
                    normal = cross.normalized;
                }
            }
            else if (activeFeet.Count >= 3)
            {
                Vector3 edge1 = activeFeet[1] - activeFeet[0];
                Vector3 edge2 = activeFeet[2] - activeFeet[0];
                Vector3 cross = Vector3.Cross(edge1, edge2);
                if (cross.sqrMagnitude > 1e-6f)
                {
                    normal = cross.normalized;
                }
            }

            // キャラクターの上方向と逆向きなら上に向ける
            if (Vector3.Dot(normal, targetTransform.up) < 0f)
            {
                normal = -normal;
            }
            data.Normal = normal;

            // 3. ローカル空間での足四角形バウンディング (XZ) と基準高さ (Y)
            Vector3 locFL = hasRestPose ? localFL : targetTransform.InverseTransformPoint(fl);
            Vector3 locFR = hasRestPose ? localFR : targetTransform.InverseTransformPoint(fr);
            Vector3 locBL = hasRestPose ? localBL : targetTransform.InverseTransformPoint(bl);
            Vector3 locBR = hasRestPose ? localBR : targetTransform.InverseTransformPoint(br);

            data.MinX = Mathf.Min(locFL.x, locFR.x, locBL.x, locBR.x) - planeMargin;
            data.MaxX = Mathf.Max(locFL.x, locFR.x, locBL.x, locBR.x) + planeMargin;
            data.MinZ = Mathf.Min(locFL.z, locFR.z, locBL.z, locBR.z) - planeMargin;
            data.MaxZ = Mathf.Max(locFL.z, locFR.z, locBL.z, locBR.z) + planeMargin;
            data.RefY = (locFL.y + locFR.y + locBL.y + locBR.y) * 0.25f;
            data.IsValid = true;

            return data;
        }
    }
}
