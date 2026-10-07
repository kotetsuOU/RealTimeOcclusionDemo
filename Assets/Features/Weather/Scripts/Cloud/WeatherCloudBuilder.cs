using System.Collections.Generic;
using UnityEngine;

namespace Features.Weather
{
    /// <summary>
    /// 雲（Cloud）の低ポリゴン 3D 結合メッシュ生成および不透明マテリアル生成を担当する Pure C# ビルダー。
    /// DrawCall 1 回で描画可能な結合メッシュを生成し、点群オクルージョン（PCD レイヤー）に適合する深度を提供します。
    /// </summary>
    public static class WeatherCloudBuilder
    {
        private const int SphereSegments = 10;
        private const int SphereRings = 7;

        /// <summary>
        /// 指定された範囲サイズと厚みに応じたプロシージャル雲メッシュを生成します。
        /// </summary>
        public static Mesh BuildCloudMesh(Vector2 areaSize, float thickness, int clusterCount, int puffsPerCluster, int seed = 12345)
        {
            var oldState = Random.state;
            Random.InitState(seed);

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            float halfX = areaSize.x * 0.45f;
            float halfZ = areaSize.y * 0.45f;

            // 基本パフ半径 (エリアサイズに追従。卓上SRDisplay等の0.1〜0.2m極小スケールにも完全適応)
            float minDim = Mathf.Max(0.01f, Mathf.Min(areaSize.x, areaSize.y));
            float basePuffRadius = Mathf.Clamp(minDim * 0.18f, 0.005f, 0.8f);

            for (int c = 0; c < clusterCount; c++)
            {
                // クラスタの中心 (水平面)
                float clusterCenterX = Random.Range(-halfX, halfX);
                float clusterCenterZ = Random.Range(-halfZ, halfZ);
                float clusterBaseY = Random.Range(-thickness * 0.2f, thickness * 0.2f);

                for (int p = 0; p < puffsPerCluster; p++)
                {
                    // クラスタ内のパフオフセット
                    float angle = Random.Range(0f, Mathf.PI * 2f);
                    float dist = Random.Range(0f, basePuffRadius * 1.5f);
                    float px = clusterCenterX + Mathf.Cos(angle) * dist;
                    float pz = clusterCenterZ + Mathf.Sin(angle) * dist;
                    float py = clusterBaseY + Random.Range(-thickness * 0.3f, thickness * 0.5f);

                    // パフのスケール (扁平な楕円体にして雲らしいボリューム感を出す)
                    float r = basePuffRadius * Random.Range(0.7f, 1.35f);
                    Vector3 center = new Vector3(px, py, pz);
                    Vector3 radii = new Vector3(r, r * 0.65f, r);

                    AppendEllipsoid(vertices, normals, uvs, triangles, center, radii);
                }
            }

            Random.state = oldState;

            var mesh = new Mesh
            {
                name = "Procedural_Cloud_Mesh"
            };

            if (vertices.Count > 65535)
            {
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();

            return mesh;
        }

        private static void AppendEllipsoid(
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector2> uvs,
            List<int> triangles,
            Vector3 center,
            Vector3 radii)
        {
            int baseIndex = vertices.Count;

            // 頂点と法線の生成
            for (int ring = 0; ring <= SphereRings; ring++)
            {
                float v = (float)ring / SphereRings;
                float phi = v * Mathf.PI; // 0 to PI

                for (int seg = 0; seg <= SphereSegments; seg++)
                {
                    float u = (float)seg / SphereSegments;
                    float theta = u * Mathf.PI * 2f; // 0 to 2PI

                    float nx = Mathf.Sin(phi) * Mathf.Cos(theta);
                    float ny = Mathf.Cos(phi);
                    float nz = Mathf.Sin(phi) * Mathf.Sin(theta);

                    Vector3 normal = new Vector3(nx, ny, nz);
                    Vector3 pos = center + new Vector3(nx * radii.x, ny * radii.y, nz * radii.z);

                    vertices.Add(pos);
                    normals.Add(normal);
                    uvs.Add(new Vector2(u, v));
                }
            }

            // インデックスの生成
            int stride = SphereSegments + 1;
            for (int ring = 0; ring < SphereRings; ring++)
            {
                for (int seg = 0; seg < SphereSegments; seg++)
                {
                    int current = baseIndex + ring * stride + seg;
                    int next = current + stride;

                    triangles.Add(current);
                    triangles.Add(next);
                    triangles.Add(current + 1);

                    triangles.Add(current + 1);
                    triangles.Add(next);
                    triangles.Add(next + 1);
                }
            }
        }

        /// <summary>
        /// 不透明描画 (URP Lit, Opaque, ZWrite On) のフォールバックマテリアルを生成します。
        /// </summary>
        public static Material CreateFallbackMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Standard");

            var mat = new Material(shader)
            {
                name = "M_Weather_Cloud_Fallback"
            };

            // URP 不透明設定の保証
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 0f); // 0 = Opaque
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 1f);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(0.9f, 0.92f, 0.96f, 1.0f));
            mat.SetOverrideTag("RenderType", "Opaque");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;

            return mat;
        }
    }
}
