using UnityEngine;
using System.Linq;
using System.Collections.Generic;

#nullable enable

public static partial class HAP_GizmoVisualizer
{
    private static void DrawVirtualObjectSurfaceMapping(GameObject obj, List<List<AUTD3Device>> deviceGroups, float directionalAngleThreshold, HAP_AUTDDebugDisabler? debugDisabler, HAP_HapticsIllusionCustomController? illusionController = null)
    {
        // 余計なオブジェクトを付けず、全ての子メッシュから「仮想的なバウンディングボックス」を計算する
        var renderers = obj.GetComponentsInChildren<Renderer>();
        if (renderers == null || renderers.Length == 0) return;

        Bounds localBounds = new Bounds();
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
                    meshBounds = r.bounds; // Fallback
                    rendererTransform = null; // Already in world space
                }
            }
            else
            {
                meshBounds = r.bounds;
                rendererTransform = null; // Already in world space
            }

            if (meshBounds.size.sqrMagnitude < 1e-10f) continue;

            Vector3 rc = meshBounds.center;
            Vector3 re = meshBounds.extents;
            
            Vector3[] corners = {
                rc + new Vector3(re.x, re.y, re.z),
                rc + new Vector3(re.x, re.y, -re.z),
                rc + new Vector3(re.x, -re.y, re.z),
                rc + new Vector3(re.x, -re.y, -re.z),
                rc + new Vector3(-re.x, re.y, re.z),
                rc + new Vector3(-re.x, re.y, -re.z),
                rc + new Vector3(-re.x, -re.y, re.z),
                rc + new Vector3(-re.x, -re.y, -re.z)
            };

            foreach (var corner in corners)
            {
                // meshBounds がローカル空間の場合、まずワールド空間に変換してから obj のローカル空間に変換する
                Vector3 worldCorner = rendererTransform != null ? rendererTransform.TransformPoint(corner) : corner;
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

        if (!hasBounds) return;

        // オブジェクトの回転・スケールを反映したローカル空間(OBB)として扱う
        Gizmos.matrix = obj.transform.localToWorldMatrix;
        Vector3 center = localBounds.center;
        Vector3 size = localBounds.size;
        Vector3 extents = localBounds.extents;

        Vector3[] normals = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        Vector3[] centers = {
            center + Vector3.right * extents.x,
            center + Vector3.left * extents.x,
            center + Vector3.up * extents.y,
            center + Vector3.down * extents.y,
            center + Vector3.forward * extents.z,
            center + Vector3.back * extents.z
        };
        Vector3[] sizes = {
            new Vector3(0.005f, size.y, size.z),
            new Vector3(0.005f, size.y, size.z),
            new Vector3(size.x, 0.005f, size.z),
            new Vector3(size.x, 0.005f, size.z),
            new Vector3(size.x, size.y, 0.005f),
            new Vector3(size.x, size.y, 0.005f)
        };

        for (int i = 0; i < 6; i++)
        {
            // OBBのローカル法線をワールド法線に変換
            Vector3 worldNormal = obj.transform.TransformDirection(normals[i]).normalized;
            
            List<Color> qualifyingColors = new List<Color>();

            // 全デバイスグループについて、面の法線と向きが閾値以内か判定する
            for (int g = 0; g < deviceGroups.Count; g++)
            {
                // もしこのグループの全デバイスが無効化されているなら、このグループは色を塗らない
                if (debugDisabler != null && deviceGroups[g].All(d => debugDisabler.IsDisabled(d.ID)))
                {
                    continue;
                }

                var groupForward = deviceGroups[g][0].transform.forward;
                // デバイスが面の「外側」から「内側」に向かっている場合、groupForward と -worldNormal のなす角が小さくなる
                float angle = Vector3.Angle(groupForward, -worldNormal);
                if (angle <= directionalAngleThreshold)
                {
                    Color c = Color.HSVToRGB((float)g / deviceGroups.Count, 0.8f, 1f);
                    c.a = 0.6f; // よりハッキリと面を塗りつぶすために不透明度を上げる
                    qualifyingColors.Add(c);
                }
            }

            // 担当デバイスグループがあれば色を塗る（複数ならストライプ）
            if (qualifyingColors.Count > 0)
            {
                DrawStripedFace(centers[i], sizes[i], i, qualifyingColors);
            }
        }
    }

    private static void DrawStripedFace(Vector3 center, Vector3 size, int faceIndex, List<Color> colors)
    {
        if (colors.Count == 1)
        {
            Gizmos.color = colors[0];
            Gizmos.DrawCube(center, size);
            return;
        }

        // 複数ある場合はストライプ模様（10分割）にする
        int numStripes = 10;
        Vector3 step = Vector3.zero;
        Vector3 startOffset = Vector3.zero;
        Vector3 stripeSize = size;

        // faceIndex: 0=Right, 1=Left, 2=Up, 3=Down, 4=Forward, 5=Back
        if (faceIndex == 0 || faceIndex == 1)
        {
            // 右・左面 (Xが薄い) -> Y軸方向に分割
            stripeSize.y = size.y / numStripes;
            step = new Vector3(0, stripeSize.y, 0);
            startOffset = new Vector3(0, -size.y / 2f + stripeSize.y / 2f, 0);
        }
        else if (faceIndex == 2 || faceIndex == 3)
        {
            // 上・下面 (Yが薄い) -> X軸方向に分割
            stripeSize.x = size.x / numStripes;
            step = new Vector3(stripeSize.x, 0, 0);
            startOffset = new Vector3(-size.x / 2f + stripeSize.x / 2f, 0, 0);
        }
        else
        {
            // 前・後面 (Zが薄い) -> X軸方向に分割
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
