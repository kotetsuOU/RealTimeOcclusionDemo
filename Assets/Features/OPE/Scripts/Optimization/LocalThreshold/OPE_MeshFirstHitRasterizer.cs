using UnityEngine;

namespace OPE
{
    /// <summary>
    /// MeshFilter / SkinnedMeshRenderer を走査し、各ピクセル中心レイとの最初の交差点（最小深度）を
    /// 透視補正付き重心座標補間で L0 マップへラスタライズする Pure C# ヘルパー。
    /// </summary>
    public static class OPE_MeshFirstHitRasterizer
    {
        /// <summary>
        /// 指定した GameObject 配下の全アクティブメッシュを L0 マップへラスタライズします。
        /// </summary>
        public static void RasterizeGameObjectFirstHitToL0(
            GameObject root,
            Matrix4x4 viewMatrix,
            Matrix4x4 projMatrix,
            int width,
            int height,
            Vector4[] targetL0Map)
        {
            if (root == null) return;

            Mesh sharedBakedMesh = new Mesh();
            try
            {
                var meshFilters = root.GetComponentsInChildren<MeshFilter>(true);
                foreach (var mf in meshFilters)
                {
                    if (mf == null || mf.sharedMesh == null) continue;
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (mr != null && !mr.enabled) continue;
                    if (!IsSelfAndAncestorsActiveUpToRoot(mf.transform, root.transform)) continue;

                    RasterizeMeshFirstHit(
                        mf.sharedMesh, mf.transform.localToWorldMatrix, viewMatrix, projMatrix, width, height, targetL0Map);
                }

                var skinnedRenderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                foreach (var smr in skinnedRenderers)
                {
                    if (smr == null || !smr.enabled || smr.sharedMesh == null) continue;
                    if (!IsSelfAndAncestorsActiveUpToRoot(smr.transform, root.transform)) continue;

                    smr.BakeMesh(sharedBakedMesh);
                    Matrix4x4 localToWorld = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                    RasterizeMeshFirstHit(
                        sharedBakedMesh, localToWorld, viewMatrix, projMatrix, width, height, targetL0Map);
                }
            }
            finally
            {
                Object.DestroyImmediate(sharedBakedMesh);
            }
        }

        private static bool IsSelfAndAncestorsActiveUpToRoot(Transform current, Transform root)
        {
            Transform t = current;
            while (t != null && t != root)
            {
                if (!t.gameObject.activeSelf) return false;
                t = t.parent;
            }
            return true;
        }

        private static void RasterizeMeshFirstHit(
            Mesh mesh,
            Matrix4x4 localToWorld,
            Matrix4x4 viewMatrix,
            Matrix4x4 projMatrix,
            int width,
            int height,
            Vector4[] targetL0Map)
        {
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            if (vertices == null || triangles == null || triangles.Length < 3) return;

            int vCount = vertices.Length;
            Matrix4x4 modelView = viewMatrix * localToWorld;
            Vector3[] viewVerts = new Vector3[vCount];
            Vector4[] clipVerts = new Vector4[vCount];

            for (int i = 0; i < vCount; i++)
            {
                Vector3 vp = modelView.MultiplyPoint3x4(vertices[i]);
                viewVerts[i] = vp;
                clipVerts[i] = projMatrix * new Vector4(vp.x, vp.y, vp.z, 1f);
            }

            float halfW = width * 0.5f;
            float halfH = height * 0.5f;
            int triCount = triangles.Length;

            for (int i = 0; i < triCount; i += 3)
            {
                int i0 = triangles[i];
                int i1 = triangles[i + 1];
                int i2 = triangles[i + 2];

                Vector4 c0 = clipVerts[i0];
                Vector4 c1 = clipVerts[i1];
                Vector4 c2 = clipVerts[i2];
                if (c0.w <= 1e-4f || c1.w <= 1e-4f || c2.w <= 1e-4f) continue;

                float invW0 = 1.0f / c0.w;
                float invW1 = 1.0f / c1.w;
                float invW2 = 1.0f / c2.w;

                float sx0 = (c0.x * invW0 + 1.0f) * halfW;
                float sy0 = (c0.y * invW0 + 1.0f) * halfH;
                float sx1 = (c1.x * invW1 + 1.0f) * halfW;
                float sy1 = (c1.y * invW1 + 1.0f) * halfH;
                float sx2 = (c2.x * invW2 + 1.0f) * halfW;
                float sy2 = (c2.y * invW2 + 1.0f) * halfH;

                float minX = Mathf.Min(sx0, Mathf.Min(sx1, sx2));
                float maxX = Mathf.Max(sx0, Mathf.Max(sx1, sx2));
                float minY = Mathf.Min(sy0, Mathf.Min(sy1, sy2));
                float maxY = Mathf.Max(sy0, Mathf.Max(sy1, sy2));
                if (maxX < 0f || minX >= width || maxY < 0f || minY >= height) continue;

                int xStart = Mathf.Clamp(Mathf.FloorToInt(minX), 0, width - 1);
                int xEnd = Mathf.Clamp(Mathf.FloorToInt(maxX), 0, width - 1);
                int yStart = Mathf.Clamp(Mathf.FloorToInt(minY), 0, height - 1);
                int yEnd = Mathf.Clamp(Mathf.FloorToInt(maxY), 0, height - 1);

                float denom = (sy1 - sy2) * (sx0 - sx2) + (sx2 - sx1) * (sy0 - sy2);
                if (Mathf.Abs(denom) < 1e-8f) continue;

                float invDenom = 1.0f / denom;
                float d0 = (c0.z * invW0) * 0.5f + 0.5f;
                float d1 = (c1.z * invW1) * 0.5f + 0.5f;
                float d2 = (c2.z * invW2) * 0.5f + 0.5f;

                Vector3 v0OverW = viewVerts[i0] * invW0;
                Vector3 v1OverW = viewVerts[i1] * invW1;
                Vector3 v2OverW = viewVerts[i2] * invW2;

                for (int py = yStart; py <= yEnd; py++)
                {
                    float sampleY = py + 0.5f;
                    int rowIdx = py * width;

                    for (int px = xStart; px <= xEnd; px++)
                    {
                        float sampleX = px + 0.5f;
                        float b0 = ((sy1 - sy2) * (sampleX - sx2) + (sx2 - sx1) * (sampleY - sy2)) * invDenom;
                        if (b0 < -1e-5f) continue;

                        float b1 = ((sy2 - sy0) * (sampleX - sx2) + (sx0 - sx2) * (sampleY - sy2)) * invDenom;
                        if (b1 < -1e-5f) continue;

                        float b2 = 1.0f - b0 - b1;
                        if (b2 < -1e-5f) continue;

                        float depth = b0 * d0 + b1 * d1 + b2 * d2;
                        if (depth < 0f || depth > 1f) continue;

                        int pixelIdx = rowIdx + px;
                        if (depth < targetL0Map[pixelIdx].w)
                        {
                            float interpInvW = b0 * invW0 + b1 * invW1 + b2 * invW2;
                            if (interpInvW > 1e-7f)
                            {
                                Vector3 hitViewPos = (b0 * v0OverW + b1 * v1OverW + b2 * v2OverW) / interpInvW;
                                targetL0Map[pixelIdx] = new Vector4(hitViewPos.x, hitViewPos.y, hitViewPos.z, depth);
                            }
                        }
                    }
                }
            }
        }
    }
}

