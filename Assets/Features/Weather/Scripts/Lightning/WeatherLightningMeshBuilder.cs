using UnityEngine;
using UnityEngine.Rendering;

namespace Features.Weather
{
    /// <summary>
    /// 十字リボンメッシュを構成する Core (核) および Glow (外側発光) のレンダラー・メッシュペアデータ。
    /// </summary>
    public class WeatherBoltRibbon
    {
        public GameObject Root;
        public MeshFilter CoreFilter;
        public MeshRenderer CoreRenderer;
        public Mesh CoreMesh;
        public MaterialPropertyBlock CoreMpb;

        public MeshFilter GlowFilter;
        public MeshRenderer GlowRenderer;
        public Mesh GlowMesh;
        public MaterialPropertyBlock GlowMpb;

        public Vector3[] Points;
        public int MaxPoints;
        public float Weight;

        // 事前確保バッファ (Zero-GC 十字メッシュ: 1ポイントあたり4頂点)
        public Vector3[] CoreVertices;
        public Vector3[] GlowVertices;
        public Vector2[] UVs;
        public int[] Indices;

        public void Dispose()
        {
            if (CoreMesh != null) Object.Destroy(CoreMesh);
            if (GlowMesh != null) Object.Destroy(GlowMesh);
        }
    }

    /// <summary>
    /// 稲妻の 3D 十字帯メッシュ (Cross Ribbon Mesh) および着地点放電スパークの生成・頂点計算を担う純粋ビルダー。
    /// メモリ割り当て (GC Allocation) を一切行わず、事前確保バッファに対するインプレース更新で完全な Zero-GC を保証します。
    /// </summary>
    public static class WeatherLightningMeshBuilder
    {
        public const int ImpactRayCount = 4;
        public const int ImpactPointsPerRay = 5;

        /// <summary>
        /// 直交する2枚の帯ポリゴンからなる 3D 十字リボン GameObject とメッシュを生成します。
        /// </summary>
        public static WeatherBoltRibbon CreateRibbon(string name, Transform parent, Material mat, float weight, int maxPts, int layer)
        {
            var ribbonGo = new GameObject(name);
            ribbonGo.transform.SetParent(parent, false);
            ribbonGo.transform.localPosition = Vector3.zero;
            ribbonGo.transform.localRotation = Quaternion.identity;
            ribbonGo.transform.localScale = Vector3.one;
            ribbonGo.layer = layer;

            int maxVerts = maxPts * 4;
            int maxIndices = (maxPts - 1) * 12;

            var coreVerts = new Vector3[maxVerts];
            var glowVerts = new Vector3[maxVerts];
            var uvs = new Vector2[maxVerts];
            var indices = new int[maxIndices];

            // インデックスの事前初期化 (板A: 0,1,4,5 / 板B: 2,3,6,7)
            for (int i = 0; i < maxPts - 1; i++)
            {
                int vi = i * 4;
                int ii = i * 12;

                // 板 A (第1直交板)
                indices[ii + 0] = vi + 0;
                indices[ii + 1] = vi + 1;
                indices[ii + 2] = vi + 4;

                indices[ii + 3] = vi + 1;
                indices[ii + 4] = vi + 5;
                indices[ii + 5] = vi + 4;

                // 板 B (第2直交板)
                indices[ii + 6] = vi + 2;
                indices[ii + 7] = vi + 3;
                indices[ii + 8] = vi + 6;

                indices[ii + 9] = vi + 3;
                indices[ii + 10] = vi + 7;
                indices[ii + 11] = vi + 6;
            }

            // UV の事前初期化
            for (int i = 0; i < maxPts; i++)
            {
                float v = (float)i / Mathf.Max(1, maxPts - 1);
                uvs[i * 4 + 0] = new Vector2(0f, v);
                uvs[i * 4 + 1] = new Vector2(1f, v);
                uvs[i * 4 + 2] = new Vector2(0f, v);
                uvs[i * 4 + 3] = new Vector2(1f, v);
            }

            // Glow ジオメトリ (外側発光十字リボン)
            var glowGo = new GameObject("Glow");
            glowGo.transform.SetParent(ribbonGo.transform, false);
            glowGo.layer = layer;
            var glowMf = glowGo.AddComponent<MeshFilter>();
            var glowMr = glowGo.AddComponent<MeshRenderer>();
            glowMr.shadowCastingMode = ShadowCastingMode.Off;
            glowMr.receiveShadows = false;
            glowMr.sharedMaterial = mat;
            var glowMesh = new Mesh { name = $"{name}_GlowMesh" };
            glowMesh.MarkDynamic();
            glowMf.sharedMesh = glowMesh;
            var glowMpb = new MaterialPropertyBlock();

            // Core ジオメトリ (芯の眩しい核十字リボン)
            var coreGo = new GameObject("Core");
            coreGo.transform.SetParent(ribbonGo.transform, false);
            coreGo.layer = layer;
            var coreMf = coreGo.AddComponent<MeshFilter>();
            var coreMr = coreGo.AddComponent<MeshRenderer>();
            coreMr.shadowCastingMode = ShadowCastingMode.Off;
            coreMr.receiveShadows = false;
            coreMr.sharedMaterial = mat;
            var coreMesh = new Mesh { name = $"{name}_CoreMesh" };
            coreMesh.MarkDynamic();
            coreMf.sharedMesh = coreMesh;
            var coreMpb = new MaterialPropertyBlock();

            return new WeatherBoltRibbon
            {
                Root = ribbonGo,
                CoreFilter = coreMf,
                CoreRenderer = coreMr,
                CoreMesh = coreMesh,
                CoreMpb = coreMpb,
                GlowFilter = glowMf,
                GlowRenderer = glowMr,
                GlowMesh = glowMesh,
                GlowMpb = glowMpb,
                Points = new Vector3[maxPts],
                MaxPoints = maxPts,
                Weight = weight,
                CoreVertices = coreVerts,
                GlowVertices = glowVerts,
                UVs = uvs,
                Indices = indices
            };
        }

        /// <summary>
        /// 折れ線点列から 3D 十字帯メッシュの頂点を計算し、Core および Glow メッシュを高速更新します。
        /// </summary>
        public static void UpdateRibbonMesh(WeatherBoltRibbon ribbon, Vector3[] points, int count, float baseWidth)
        {
            if (ribbon == null || points == null || count < 2) return;

            int vertCount = count * 4;
            int indexCount = (count - 1) * 12;

            for (int i = 0; i < count; i++)
            {
                Vector3 p = points[i];
                Vector3 dir;
                if (i == 0)
                {
                    dir = points[1] - p;
                }
                else if (i == count - 1)
                {
                    dir = p - points[count - 2];
                }
                else
                {
                    dir = points[i + 1] - points[i - 1];
                }

                if (dir.sqrMagnitude < 1e-8f) dir = Vector3.down;
                dir.Normalize();

                // 進行方向に直交する2軸 (n1, n2) を生成
                Vector3 axis = Mathf.Abs(dir.y) > 0.85f ? Vector3.forward : Vector3.up;
                Vector3 n1 = Vector3.Cross(dir, axis).normalized;
                Vector3 n2 = Vector3.Cross(dir, n1).normalized;

                float t = (float)i / Mathf.Max(1, count - 1);
                // 根元は太く、先端は細くテーパー (1.0 -> 0.4)
                float taper = Mathf.Lerp(1.0f, 0.4f, t);
                float coreHalfW = baseWidth * taper * 0.5f;
                float glowHalfW = baseWidth * 3.8f * taper * 0.5f;

                int vi = i * 4;

                // Core 頂点 (十字)
                ribbon.CoreVertices[vi + 0] = p - n1 * coreHalfW;
                ribbon.CoreVertices[vi + 1] = p + n1 * coreHalfW;
                ribbon.CoreVertices[vi + 2] = p - n2 * coreHalfW;
                ribbon.CoreVertices[vi + 3] = p + n2 * coreHalfW;

                // Glow 頂点 (十字幅広)
                ribbon.GlowVertices[vi + 0] = p - n1 * glowHalfW;
                ribbon.GlowVertices[vi + 1] = p + n1 * glowHalfW;
                ribbon.GlowVertices[vi + 2] = p - n2 * glowHalfW;
                ribbon.GlowVertices[vi + 3] = p + n2 * glowHalfW;
            }

            // Core メッシュ更新
            ribbon.CoreMesh.SetVertices(ribbon.CoreVertices, 0, vertCount);
            ribbon.CoreMesh.SetUVs(0, ribbon.UVs, 0, vertCount);
            ribbon.CoreMesh.SetTriangles(ribbon.Indices, 0, indexCount, 0, true);
            ribbon.CoreMesh.RecalculateBounds();

            // Glow メッシュ更新
            ribbon.GlowMesh.SetVertices(ribbon.GlowVertices, 0, vertCount);
            ribbon.GlowMesh.SetUVs(0, ribbon.UVs, 0, vertCount);
            ribbon.GlowMesh.SetTriangles(ribbon.Indices, 0, indexCount, 0, true);
            ribbon.GlowMesh.RecalculateBounds();
        }

        /// <summary>
        /// 着地点周囲に放射状に弾けるバチバチッとした放電スパークの点列を生成します。
        /// </summary>
        public static int BuildImpactSparkPoints(Vector3[] outPoints, Vector3 ground, float trunkWidth)
        {
            if (outPoints == null || outPoints.Length < ImpactRayCount * ImpactPointsPerRay) return 0;

            float sparkRadius = Mathf.Clamp(trunkWidth * 8.0f, 0.08f, 0.25f);
            int ptIndex = 0;

            for (int r = 0; r < ImpactRayCount; r++)
            {
                float baseAngle = (float)r / ImpactRayCount * Mathf.PI * 2.0f + Random.Range(-0.25f, 0.25f);
                Vector3 rayDir = new Vector3(Mathf.Cos(baseAngle), 0f, Mathf.Sin(baseAngle));

                Vector3 cur = ground + Vector3.up * 0.01f;
                outPoints[ptIndex++] = cur;

                for (int p = 1; p < ImpactPointsPerRay; p++)
                {
                    float t = (float)p / (ImpactPointsPerRay - 1);
                    float dist = t * sparkRadius;
                    Vector3 jitter = new Vector3(
                        Random.Range(-0.02f, 0.02f),
                        Random.Range(0.005f, 0.04f),
                        Random.Range(-0.02f, 0.02f)
                    );
                    cur = ground + rayDir * dist + jitter;
                    outPoints[ptIndex++] = cur;
                }
            }

            return ptIndex;
        }

        /// <summary>
        /// URP DepthOnlyPass (仮想深度マップ) で確実に書き込まれる不透明 Opaque マテリアルを生成します。
        /// </summary>
        public static Material CreateFallbackMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit")
                         ?? Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Standard");

            if (shader == null) return null;
            var mat = new Material(shader)
            {
                name = "Weather_Lightning_Material",
                renderQueue = (int)RenderQueue.Geometry
            };
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 0f);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 1f);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f); // 両面
            mat.SetOverrideTag("RenderType", "Opaque");
            mat.EnableKeyword("_EMISSION");
            return mat;
        }
    }
}
