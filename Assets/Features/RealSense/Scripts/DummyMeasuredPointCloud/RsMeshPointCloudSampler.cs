using System;
using System.Collections.Generic;
using UnityEngine;

namespace RealSense.DummyPointCloud
{
    public enum PointDensityUnit
    {
        PointsPerMm2,   // 1 mm^2 あたりの点数
        PointsPerCm2,   // 1 cm^2 あたりの点数
        PointSpacingMm, // 点間隔 (mm)
        TotalPointCount // 指定の合計点数
    }

    public enum PointColorMode
    {
        SolidColor,    // 単色指定
        MaterialColor, // マテリアル/メインカラー
        VertexColor    // メッシュの頂点カラー
    }

    public struct SampledPointCloudData
    {
        public Vector3[] Positions; // ワールド座標
        public Vector3[] Normals;   // ワールド法線
        public Color[] Colors;      // 各点の色
        public int PointCount;
    }

    public class RsMeshPointCloudSampler
    {
        private Mesh _sharedBakedMesh;
        private List<Vector3> _positionsCache = new List<Vector3>(100000);
        private List<Vector3> _normalsCache = new List<Vector3>(100000);
        private List<Color> _colorsCache = new List<Color>(100000);

        // トランスフォーム変更検出用キャッシュ (静的メッシュの CPU サンプリング 0ms 化)
        private struct TransformState
        {
            public Renderer renderer;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
        }

        private List<TransformState> _lastTransformStates = new List<TransformState>();
        private SampledPointCloudData _lastSampledResult;
        private bool _hasCachedResult = false;
        private PointDensityUnit _lastDensityUnit;
        private float _lastDensityValue;
        private PointColorMode _lastColorMode;
        private Color _lastSolidColor;

        public RsMeshPointCloudSampler()
        {
            _sharedBakedMesh = new Mesh();
        }

        ~RsMeshPointCloudSampler()
        {
            if (_sharedBakedMesh != null)
            {
                UnityEngine.Object.DestroyImmediate(_sharedBakedMesh);
                _sharedBakedMesh = null;
            }
        }

        /// <summary>
        /// キャッシュを強制的に無効化し、次回の SamplePointCloud で確実に再サンプリングを実行させます。
        /// </summary>
        public void InvalidateCache()
        {
            _hasCachedResult = false;
        }

        /// <summary>
        /// 指定された Renderer が点群サンプリング対象として有効かどうかを判定します。
        /// 非アクティブなオブジェクトや無効化されたコンポーネントは確実に除外します。
        /// </summary>
        public static bool IsRendererActiveForSampling(Renderer r, GameObject rootObj = null)
        {
            if (r == null || !r.enabled) return false;
            if (r.gameObject == null || !r.gameObject.activeInHierarchy) return false;
            if (rootObj != null && !rootObj.activeInHierarchy) return false;
            return true;
        }

        /// <summary>
        /// 指定された GameObject のリスト配下から Mesh を取得し、
        /// トランスフォーム（位置・回転・スケール）の変化がない場合は前回のサンプリング結果を再利用 (CPU 0ms) します。
        /// </summary>
        public SampledPointCloudData SamplePointCloud(
            List<GameObject> rootObjects,
            bool includeChildren,
            PointDensityUnit densityUnit,
            float densityValue,
            PointColorMode colorMode,
            Color solidColor,
            int maxPointLimit = 150000)
        {
            if (rootObjects == null || rootObjects.Count == 0)
            {
                return new SampledPointCloudData { Positions = Array.Empty<Vector3>(), Colors = Array.Empty<Color>(), PointCount = 0 };
            }

            // 1. レンダラー一覧の検出とトランスフォーム変更判定 (アクティブなもののみ収集)
            List<Renderer> allRenderers = new List<Renderer>();
            bool transformChanged = false;
            bool isSkinnedMeshPresent = false;

            foreach (var rootObj in rootObjects)
            {
                if (rootObj == null || !rootObj.activeInHierarchy) continue;

                var renderers = includeChildren
                    ? rootObj.GetComponentsInChildren<Renderer>()
                    : rootObj.GetComponents<Renderer>();

                foreach (var r in renderers)
                {
                    if (r != null && IsRendererActiveForSampling(r, rootObj) && !allRenderers.Contains(r))
                    {
                        allRenderers.Add(r);
                        if (r is SkinnedMeshRenderer) isSkinnedMeshPresent = true;
                    }
                }
            }

            if (_hasCachedResult && !isSkinnedMeshPresent &&
                densityUnit == _lastDensityUnit &&
                Mathf.Approximately(densityValue, _lastDensityValue) &&
                colorMode == _lastColorMode &&
                solidColor == _lastSolidColor &&
                allRenderers.Count == _lastTransformStates.Count)
            {
                for (int i = 0; i < allRenderers.Count; i++)
                {
                    Transform t = allRenderers[i].transform;
                    if (allRenderers[i] != _lastTransformStates[i].renderer ||
                        t.position != _lastTransformStates[i].position ||
                        t.rotation != _lastTransformStates[i].rotation ||
                        t.lossyScale != _lastTransformStates[i].scale)
                    {
                        transformChanged = true;
                        break;
                    }
                }

                // 変化がない場合は超高速キャッシュ（CPU 再計算 0ms）を返す
                if (!transformChanged)
                {
                    return _lastSampledResult;
                }
            }

            // 2. トランスフォームやパラメーター変更時のみ再計算
            _positionsCache.Clear();
            _normalsCache.Clear();
            _colorsCache.Clear();
            _lastTransformStates.Clear();

            // 各 Renderer のメッシュとトランスフォーム情報を取得し、表面積を計算
            List<(Renderer renderer, Mesh mesh, Matrix4x4 localToWorld, float area)> rendererInfoList = new List<(Renderer, Mesh, Matrix4x4, float)>();
            List<Mesh> tempBakedMeshes = new List<Mesh>();
            float totalSurfaceArea = 0f;

            foreach (var renderer in allRenderers)
            {
                _lastTransformStates.Add(new TransformState
                {
                    renderer = renderer,
                    position = renderer.transform.position,
                    rotation = renderer.transform.rotation,
                    scale = renderer.transform.lossyScale
                });

                Mesh mesh = null;
                Matrix4x4 localToWorld = renderer.transform.localToWorldMatrix;

                if (renderer is SkinnedMeshRenderer skinnedRenderer)
                {
                    if (skinnedRenderer.sharedMesh != null)
                    {
                        Mesh bakedMesh = new Mesh();
#if UNITY_2017_3_OR_NEWER
                        skinnedRenderer.BakeMesh(bakedMesh, false);
#else
                        skinnedRenderer.BakeMesh(bakedMesh);
#endif
                        mesh = bakedMesh;
                        tempBakedMeshes.Add(bakedMesh);
                    }
                }
                else if (renderer is MeshRenderer meshRenderer)
                {
                    var meshFilter = renderer.GetComponent<MeshFilter>();
                    if (meshFilter != null && meshFilter.sharedMesh != null)
                    {
                        mesh = meshFilter.sharedMesh;
                    }
                }

                if (mesh != null)
                {
                    float area = CalculateMeshArea(mesh, localToWorld);
                    rendererInfoList.Add((renderer, mesh, localToWorld, area));
                    totalSurfaceArea += area;
                }
            }

            foreach (var info in rendererInfoList)
            {
                float areaRatio = totalSurfaceArea > 0f ? (info.area / totalSurfaceArea) : (1f / rendererInfoList.Count);
                int rendererMaxLimit = Mathf.Max(1, Mathf.RoundToInt(maxPointLimit * areaRatio));
                float rendererDensityValue = (densityUnit == PointDensityUnit.TotalPointCount) ? (densityValue * areaRatio) : densityValue;

                SampleMeshInternal(info.mesh, info.renderer, info.localToWorld, densityUnit, rendererDensityValue, colorMode, solidColor, _positionsCache, _normalsCache, _colorsCache, rendererMaxLimit);

                if (_positionsCache.Count >= maxPointLimit) break;
            }

            foreach (var bakedMesh in tempBakedMeshes)
            {
                if (bakedMesh != null) UnityEngine.Object.Destroy(bakedMesh);
            }

            _lastDensityUnit = densityUnit;
            _lastDensityValue = densityValue;
            _lastColorMode = colorMode;
            _lastSolidColor = solidColor;

            _lastSampledResult = new SampledPointCloudData
            {
                Positions = _positionsCache.ToArray(),
                Normals = _normalsCache.ToArray(),
                Colors = _colorsCache.ToArray(),
                PointCount = _positionsCache.Count
            };
            _hasCachedResult = true;

            return _lastSampledResult;
        }

        private void SampleFromSkinnedMesh(
            SkinnedMeshRenderer skinnedRenderer,
            PointDensityUnit densityUnit,
            float densityValue,
            PointColorMode colorMode,
            Color solidColor,
            List<Vector3> outPositions,
            List<Vector3> outNormals,
            List<Color> outColors,
            int maxPointLimit)
        {
            if (skinnedRenderer.sharedMesh == null) return;

            if (_sharedBakedMesh == null) _sharedBakedMesh = new Mesh();
            _sharedBakedMesh.Clear();

#if UNITY_2017_3_OR_NEWER
            skinnedRenderer.BakeMesh(_sharedBakedMesh, false);
#else
            skinnedRenderer.BakeMesh(_sharedBakedMesh);
#endif

            Matrix4x4 localToWorld = skinnedRenderer.transform.localToWorldMatrix;
            SampleMeshInternal(_sharedBakedMesh, skinnedRenderer, localToWorld, densityUnit, densityValue, colorMode, solidColor, outPositions, outNormals, outColors, maxPointLimit);
        }

        private void SampleFromStaticMesh(
            Mesh mesh,
            MeshRenderer meshRenderer,
            PointDensityUnit densityUnit,
            float densityValue,
            PointColorMode colorMode,
            Color solidColor,
            List<Vector3> outPositions,
            List<Vector3> outNormals,
            List<Color> outColors,
            int maxPointLimit)
        {
            Matrix4x4 localToWorld = meshRenderer.transform.localToWorldMatrix;
            SampleMeshInternal(mesh, meshRenderer, localToWorld, densityUnit, densityValue, colorMode, solidColor, outPositions, outNormals, outColors, maxPointLimit);
        }

        private void SampleMeshInternal(
            Mesh mesh,
            Renderer renderer,
            Matrix4x4 localToWorld,
            PointDensityUnit densityUnit,
            float densityValue,
            PointColorMode colorMode,
            Color solidColor,
            List<Vector3> outPositions,
            List<Vector3> outNormals,
            List<Color> outColors,
            int maxPointLimit)
        {
            Vector3[] vertices = mesh.vertices;
            Vector3[] meshNormals = mesh.normals;
            int[] triangles = mesh.triangles;
            Color[] vertexColors = mesh.colors;

            if (vertices == null || vertices.Length == 0 || triangles == null || triangles.Length == 0)
                return;

            bool hasMeshNormals = meshNormals != null && meshNormals.Length == vertices.Length;
            bool hasVertexColors = vertexColors != null && vertexColors.Length == vertices.Length;
            Color baseMaterialColor = solidColor;

            if (colorMode == PointColorMode.MaterialColor && renderer.sharedMaterial != null)
            {
                if (renderer.sharedMaterial.HasProperty("_Color"))
                {
                    baseMaterialColor = renderer.sharedMaterial.color;
                }
                else if (renderer.sharedMaterial.HasProperty("_BaseColor"))
                {
                    baseMaterialColor = renderer.sharedMaterial.GetColor("_BaseColor");
                }
            }

            int triCount = triangles.Length / 3;
            float[] triAreas = new float[triCount];
            float totalWorldAreaSquareMeters = 0f;

            for (int i = 0; i < triCount; i++)
            {
                Vector3 p0 = localToWorld.MultiplyPoint3x4(vertices[triangles[i * 3]]);
                Vector3 p1 = localToWorld.MultiplyPoint3x4(vertices[triangles[i * 3 + 1]]);
                Vector3 p2 = localToWorld.MultiplyPoint3x4(vertices[triangles[i * 3 + 2]]);

                float area = Vector3.Cross(p1 - p0, p2 - p0).magnitude * 0.5f;
                triAreas[i] = area;
                totalWorldAreaSquareMeters += area;
            }

            if (totalWorldAreaSquareMeters <= 1e-8f) return;

            float areaMm2 = totalWorldAreaSquareMeters * 1000000f;
            float areaCm2 = totalWorldAreaSquareMeters * 10000f;

            int targetTotalPoints = 0;
            switch (densityUnit)
            {
                case PointDensityUnit.PointsPerMm2:
                    targetTotalPoints = Mathf.Max(1, Mathf.RoundToInt(areaMm2 * Mathf.Max(0.0001f, densityValue)));
                    break;
                case PointDensityUnit.PointsPerCm2:
                    targetTotalPoints = Mathf.Max(1, Mathf.RoundToInt(areaCm2 * Mathf.Max(0.0001f, densityValue)));
                    break;
                case PointDensityUnit.PointSpacingMm:
                    float spacingMm = Mathf.Max(0.1f, densityValue);
                    float pointsPerMm2 = 1f / (spacingMm * spacingMm);
                    targetTotalPoints = Mathf.Max(1, Mathf.RoundToInt(areaMm2 * pointsPerMm2));
                    break;
                case PointDensityUnit.TotalPointCount:
                    targetTotalPoints = Mathf.Max(1, Mathf.RoundToInt(densityValue));
                    break;
            }

            targetTotalPoints = Mathf.Min(targetTotalPoints, maxPointLimit);
            System.Random rand = new System.Random(42);
            int startCount = outPositions.Count;

            for (int i = 0; i < triCount; i++)
            {
                if (outPositions.Count - startCount >= maxPointLimit) break;

                float areaFrac = triAreas[i] / totalWorldAreaSquareMeters;
                int pointsForThisTri = Mathf.RoundToInt(targetTotalPoints * areaFrac);

                if (pointsForThisTri <= 0 && targetTotalPoints > 0 && rand.NextDouble() < areaFrac * targetTotalPoints)
                {
                    pointsForThisTri = 1;
                }

                if (pointsForThisTri <= 0) continue;

                int i0 = triangles[i * 3];
                int i1 = triangles[i * 3 + 1];
                int i2 = triangles[i * 3 + 2];

                Vector3 v0 = localToWorld.MultiplyPoint3x4(vertices[i0]);
                Vector3 v1 = localToWorld.MultiplyPoint3x4(vertices[i1]);
                Vector3 v2 = localToWorld.MultiplyPoint3x4(vertices[i2]);

                Vector3 faceNormal = Vector3.Cross(v1 - v0, v2 - v0).normalized;

                Vector3 n0 = hasMeshNormals ? localToWorld.MultiplyVector(meshNormals[i0]).normalized : faceNormal;
                Vector3 n1 = hasMeshNormals ? localToWorld.MultiplyVector(meshNormals[i1]).normalized : faceNormal;
                Vector3 n2 = hasMeshNormals ? localToWorld.MultiplyVector(meshNormals[i2]).normalized : faceNormal;

                Color c0 = (colorMode == PointColorMode.SolidColor) ? solidColor : ((colorMode == PointColorMode.VertexColor && hasVertexColors) ? vertexColors[i0] : baseMaterialColor);
                Color c1 = (colorMode == PointColorMode.SolidColor) ? solidColor : ((colorMode == PointColorMode.VertexColor && hasVertexColors) ? vertexColors[i1] : baseMaterialColor);
                Color c2 = (colorMode == PointColorMode.SolidColor) ? solidColor : ((colorMode == PointColorMode.VertexColor && hasVertexColors) ? vertexColors[i2] : baseMaterialColor);

                for (int p = 0; p < pointsForThisTri; p++)
                {
                    if (outPositions.Count - startCount >= maxPointLimit) break;

                    float r1 = (float)rand.NextDouble();
                    float r2 = (float)rand.NextDouble();

                    float sqrtR1 = Mathf.Sqrt(r1);
                    float u = 1f - sqrtR1;
                    float v = r2 * sqrtR1;
                    float w = 1f - u - v;

                    Vector3 pos = u * v0 + v * v1 + w * v2;
                    Vector3 norm = (u * n0 + v * n1 + w * n2).normalized;
                    if (norm.sqrMagnitude < 0.001f) norm = faceNormal;
                    Color col = u * c0 + v * c1 + w * c2;

                    outPositions.Add(pos);
                    outNormals?.Add(norm);
                    outColors.Add(col);
                }
            }
        }

        private float CalculateMeshArea(Mesh mesh, Matrix4x4 localToWorld)
        {
            if (mesh == null) return 0f;
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            if (vertices == null || vertices.Length == 0 || triangles == null || triangles.Length == 0) return 0f;

            float totalArea = 0f;
            int triCount = triangles.Length / 3;
            for (int i = 0; i < triCount; i++)
            {
                Vector3 p0 = localToWorld.MultiplyPoint3x4(vertices[triangles[i * 3]]);
                Vector3 p1 = localToWorld.MultiplyPoint3x4(vertices[triangles[i * 3 + 1]]);
                Vector3 p2 = localToWorld.MultiplyPoint3x4(vertices[triangles[i * 3 + 2]]);
                totalArea += Vector3.Cross(p1 - p0, p2 - p0).magnitude * 0.5f;
            }
            return totalArea;
        }
    }
}
