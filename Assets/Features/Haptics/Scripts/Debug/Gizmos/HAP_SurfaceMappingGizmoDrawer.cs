#nullable enable
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Features.Haptics.Hardware;

namespace Features.Haptics.Debug
{
    /// <summary>
    /// 仮想オブジェクトの統合ローカルバウンディングボックス (OBB) を算出し、
    /// 各面に対して指向性グルーピングで割り当てられるデバイスグループ色を Gizmo 描画する専任クラス。
    /// </summary>
    public static class HAP_SurfaceMappingGizmoDrawer
    {
        private static readonly Vector3[] ReusableCorners = new Vector3[8];
        private static readonly Vector3[] FaceNormals =
        {
            Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back
        };
        private static readonly List<Color> QualifyingColors = new List<Color>(8);

        /// <summary>
        /// 指定オブジェクトの全メッシュからローカル OBB を構築し、面ごとの担当グループを塗り分け描画します。
        /// </summary>
        public static void DrawVirtualObjectSurfaceMapping(
            GameObject obj,
            List<List<AUTD3Device>> deviceGroups,
            float directionalAngleThreshold,
            HAP_AUTDDebugDisabler? debugDisabler)
        {
            if (!TryComputeCombinedLocalBounds(obj, out Bounds localBounds)) return;

            Gizmos.matrix = obj.transform.localToWorldMatrix;
            Vector3 center = localBounds.center;
            Vector3 size = localBounds.size;
            Vector3 extents = localBounds.extents;

            for (int i = 0; i < 6; i++)
            {
                Vector3 faceNormal = FaceNormals[i];
                Vector3 faceCenter = center + Vector3.Scale(faceNormal, extents);
                Vector3 faceSize = i <= 1
                    ? new Vector3(0.005f, size.y, size.z)
                    : i <= 3
                        ? new Vector3(size.x, 0.005f, size.z)
                        : new Vector3(size.x, size.y, 0.005f);

                Vector3 worldNormal = obj.transform.TransformDirection(faceNormal).normalized;
                QualifyingColors.Clear();

                for (int g = 0; g < deviceGroups.Count; g++)
                {
                    if (debugDisabler != null && deviceGroups[g].All(d => debugDisabler.IsDisabled(d.ID)))
                    {
                        continue;
                    }

                    Vector3 groupForward = HAP_DeviceOrientationHelper.GetAdjustedForward(deviceGroups[g][0]);
                    float angle = Vector3.Angle(groupForward, -worldNormal);
                    if (angle <= directionalAngleThreshold)
                    {
                        Color c = Color.HSVToRGB((float)g / deviceGroups.Count, 0.8f, 1f);
                        c.a = 0.6f;
                        QualifyingColors.Add(c);
                    }
                }

                if (QualifyingColors.Count > 0)
                {
                    DrawStripedFace(faceCenter, faceSize, i, QualifyingColors);
                }
            }
        }

        private static bool TryComputeCombinedLocalBounds(GameObject obj, out Bounds localBounds)
        {
            localBounds = new Bounds();
            var renderers = obj.GetComponentsInChildren<Renderer>();
            if (renderers == null || renderers.Length == 0) return false;

            bool hasBounds = false;
            foreach (var r in renderers)
            {
                if (r is ParticleSystemRenderer) continue;

                Bounds meshBounds;
                Transform? rendererTransform = r.transform;

                if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null)
                {
                    meshBounds = smr.sharedMesh.bounds;
                }
                else if (r is MeshRenderer)
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null)
                    {
                        meshBounds = mf.sharedMesh.bounds;
                    }
                    else
                    {
                        meshBounds = r.bounds;
                        rendererTransform = null;
                    }
                }
                else
                {
                    meshBounds = r.bounds;
                    rendererTransform = null;
                }

                if (meshBounds.size.sqrMagnitude < 1e-10f) continue;

                Vector3 rc = meshBounds.center;
                Vector3 re = meshBounds.extents;
                ReusableCorners[0] = rc + new Vector3(re.x, re.y, re.z);
                ReusableCorners[1] = rc + new Vector3(re.x, re.y, -re.z);
                ReusableCorners[2] = rc + new Vector3(re.x, -re.y, re.z);
                ReusableCorners[3] = rc + new Vector3(re.x, -re.y, -re.z);
                ReusableCorners[4] = rc + new Vector3(-re.x, re.y, re.z);
                ReusableCorners[5] = rc + new Vector3(-re.x, re.y, -re.z);
                ReusableCorners[6] = rc + new Vector3(-re.x, -re.y, re.z);
                ReusableCorners[7] = rc + new Vector3(-re.x, -re.y, -re.z);

                for (int c = 0; c < 8; c++)
                {
                    Vector3 worldCorner = rendererTransform != null
                        ? rendererTransform.TransformPoint(ReusableCorners[c])
                        : ReusableCorners[c];
                    Vector3 localCorner = obj.transform.InverseTransformPoint(worldCorner);

                    if (!hasBounds)
                    {
                        localBounds = new Bounds(localCorner, Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        localBounds.Encapsulate(localCorner);
                    }
                }
            }

            return hasBounds;
        }

        private static void DrawStripedFace(Vector3 center, Vector3 size, int faceIndex, List<Color> colors)
        {
            if (colors.Count == 1)
            {
                Gizmos.color = colors[0];
                Gizmos.DrawCube(center, size);
                return;
            }

            const int numStripes = 10;
            Vector3 step;
            Vector3 startOffset;
            Vector3 stripeSize = size;

            if (faceIndex == 0 || faceIndex == 1)
            {
                stripeSize.y = size.y / numStripes;
                step = new Vector3(0, stripeSize.y, 0);
                startOffset = new Vector3(0, -size.y / 2f + stripeSize.y / 2f, 0);
            }
            else
            {
                stripeSize.x = size.x / numStripes;
                step = new Vector3(stripeSize.x, 0, 0);
                startOffset = new Vector3(-size.x / 2f + stripeSize.x / 2f, 0, 0);
            }

            Vector3 startPos = center + startOffset;
            for (int i = 0; i < numStripes; i++)
            {
                Gizmos.color = colors[i % colors.Count];
                Gizmos.DrawCube(startPos + step * i, stripeSize);
            }
        }
    }
}

