using System;
using System.Collections.Generic;
using UnityEngine;
using Core.Logging;

namespace RealSense.DummyPointCloud
{
    public class RsMeshPointCloudSampler
    {
        private readonly List<Vector3> _positionsCache = new List<Vector3>(100000);
        private readonly List<Vector3> _normalsCache = new List<Vector3>(100000);
        private readonly List<Color> _colorsCache = new List<Color>(100000);

        private struct TransformState
        {
            public Renderer renderer;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
        }

        private readonly List<TransformState> _lastTransformStates = new List<TransformState>();
        private SampledPointCloudData _lastSampledResult;
        private bool _hasCachedResult = false;
        private PointDensityUnit _lastDensityUnit;
        private float _lastDensityValue;
        private PointColorMode _lastColorMode;
        private Color _lastSolidColor;

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
                    if (!mesh.isReadable)
                    {
                        AppLogger.LogWarning(renderer, "DummyPointCloudProvider",
                            $"[RsMeshPointCloudSampler] Mesh '{mesh.name}' on '{renderer.gameObject.name}' is not readable (Read/Write disabled). Skipping point sampling.");
                        continue;
                    }
                    float area = RsMeshTriangleSampler.CalculateMeshArea(mesh, localToWorld);
                    rendererInfoList.Add((renderer, mesh, localToWorld, area));
                    totalSurfaceArea += area;
                }
            }

            foreach (var info in rendererInfoList)
            {
                float areaRatio = totalSurfaceArea > 0f ? (info.area / totalSurfaceArea) : (1f / rendererInfoList.Count);
                int rendererMaxLimit = Mathf.Max(1, Mathf.RoundToInt(maxPointLimit * areaRatio));
                float rendererDensityValue = (densityUnit == PointDensityUnit.TotalPointCount) ? (densityValue * areaRatio) : densityValue;

                RsMeshTriangleSampler.SampleMesh(
                    info.mesh, info.renderer, info.localToWorld,
                    densityUnit, rendererDensityValue, colorMode, solidColor,
                    _positionsCache, _normalsCache, _colorsCache, rendererMaxLimit);

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
    }
}
