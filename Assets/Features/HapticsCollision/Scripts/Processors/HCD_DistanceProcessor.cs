using UnityEngine;
using System;
using Features.HapticsCollision.Processors;

[Serializable]
public class HCD_DistanceProcessor : IHCD_Processor
    {
        public string ProcessorName => "DistanceCalculator";

        public enum DetectionMode { TransformOnly, SkinnedMeshRenderer, MeshFilter, FootPlane }
        public enum DistanceMode { MeshSurface, ViewDirection }

        [Header("Mode Settings")]
        public DetectionMode detectionMode;

        [Header("FootPlane Mode Settings")]
        public Transform footPlaneTarget;
        public Vector3 footPlaneBoundsMin;
        public Vector3 footPlaneBoundsMax;
        public float footPlaneRefY;
        public float footPlaneDepthThreshold = 0.05f;
        public float footPlaneUpperMargin = 0.005f;
        public Vector3 footPlaneNormal = Vector3.up;

        [Header("Distance Mode Settings")]
        [Tooltip("距離判定モード (MeshSurface: メッシュ表面基準の手前奥 / ViewDirection: 視線方向基準の手前奥)")]
        public DistanceMode distanceMode = DistanceMode.MeshSurface;

        [Tooltip("視線方向モードで基準とするカメラ。null の場合は Camera.main を使用します。")]
        public Camera viewCamera;

        [Header("Transform Mode Settings")]
        public Transform targetObject;
        public Transform[] targetTransforms;

        [Header("SkinnedMesh Mode Settings")]
        public SkinnedMeshRenderer[] targetSkinnedMeshes;

        [Header("MeshFilter Mode Settings")]
        public MeshFilter[] targetMeshFilters;

        [Header("Mesh Surface Parameters")]
        [Tooltip("【メッシュ表面モード】これより近いと接触と判定する距離(m)")]
        public float meshSurfaceDistanceThreshold = 0.02f;
        [Tooltip("【メッシュ表面モード】これより深くめり込むと貫通として無視する距離(m)")]
        public float meshBackfaceDistanceThreshold = 0.05f;

        [Header("View Direction Parameters")]
        [Tooltip("【視線方向モード】視線可視（表向き）の手前判定距離(m)")]
        public float visibleSurfaceDistanceThreshold = 0.02f;
        [Tooltip("【視線方向モード】視線可視（表向き）の奥判定距離(m)")]
        public float visibleBackfaceDistanceThreshold = 0.05f;
        [Tooltip("【視線方向モード】視線非可視（裏向き）の手前判定距離(m)")]
        public float occludedSurfaceDistanceThreshold = 0.02f;
        [Tooltip("【視線方向モード】視線非可視（裏向き）の奥判定距離(m)")]
        public float occludedBackfaceDistanceThreshold = 0.05f;

        public float surfaceDistanceThreshold => distanceMode == DistanceMode.ViewDirection ? visibleSurfaceDistanceThreshold : meshSurfaceDistanceThreshold;
        public float backfaceDistanceThreshold => distanceMode == DistanceMode.ViewDirection ? visibleBackfaceDistanceThreshold : meshBackfaceDistanceThreshold;

        public ComputeShader collisionComputeShader;
        public const string ResultBufferName = "CollisionResultBuffer";

        private HCD_Pipeline _pipeline;
        private ComputeBuffer _resultBuffer;
        private ComputeBuffer _targetPositionsBuffer;
        private int _kernelTransform;
        private int _kernelMesh;
        private int _kernelClearGrid;
        private int _kernelBuildGrid;
        private int _kernelFootPlane;

        private readonly HCD_MeshBaker _meshBaker = new HCD_MeshBaker();
        private readonly HCD_SpatialGridBuilder _gridBuilder = new HCD_SpatialGridBuilder();

        private const int STRIDE = 44;

        public void Setup(HCD_Pipeline pipeline)
        {
            _pipeline = pipeline;
            if (collisionComputeShader != null)
            {
                _kernelTransform = collisionComputeShader.FindKernel("CheckCollisionTransform");
                _kernelMesh = collisionComputeShader.FindKernel("CheckCollisionMesh");
                _kernelClearGrid = collisionComputeShader.FindKernel("ClearMeshGrid");
                _kernelBuildGrid = collisionComputeShader.FindKernel("BuildMeshGrid");
                _kernelFootPlane = collisionComputeShader.FindKernel("CheckCollisionFootPlane");
            }
            
            _gridBuilder.Setup();
        }

        public void Dispatch(ComputeBuffer pointCloudBuffer, int pointCount)
        {
            if (collisionComputeShader == null || pointCount == 0) return;

            if (_resultBuffer == null || _resultBuffer.count != pointCount)
            {
                _resultBuffer?.Release();
                _resultBuffer = new ComputeBuffer(pointCount, STRIDE);
                _pipeline.SetSharedBuffer(ResultBufferName, _resultBuffer);
            }

            Vector3 cameraPos = Vector3.zero;
            if (distanceMode == DistanceMode.ViewDirection)
            {
                var cam = viewCamera != null ? viewCamera : Camera.main;
                if (cam != null) cameraPos = cam.transform.position;
            }

            if (detectionMode == DetectionMode.TransformOnly)
            {
                DispatchTransformMode(pointCloudBuffer, pointCount, cameraPos);
            }
            else if (detectionMode == DetectionMode.SkinnedMeshRenderer || detectionMode == DetectionMode.MeshFilter)
            {
                DispatchMeshMode(pointCloudBuffer, pointCount, cameraPos);
            }
            else if (detectionMode == DetectionMode.FootPlane)
            {
                DispatchFootPlaneMode(pointCloudBuffer, pointCount);
            }
        }

        private void DispatchTransformMode(ComputeBuffer pointCloudBuffer, int pointCount, Vector3 cameraPos)
        {
            var targetPosList = new System.Collections.Generic.List<Vector3>();
            if (targetTransforms != null && targetTransforms.Length > 0)
            {
                foreach (var t in targetTransforms)
                {
                    if (t != null) targetPosList.Add(t.position);
                }
            }

            if (targetPosList.Count == 0 && targetObject != null)
            {
                targetPosList.Add(targetObject.position);
            }

            if (targetPosList.Count == 0) return;

            if (_targetPositionsBuffer == null || _targetPositionsBuffer.count != targetPosList.Count)
            {
                _targetPositionsBuffer?.Release();
                _targetPositionsBuffer = new ComputeBuffer(targetPosList.Count, sizeof(float) * 3);
            }

            _targetPositionsBuffer.SetData(targetPosList.ToArray());

            collisionComputeShader.SetBuffer(_kernelTransform, "PointCloudBuffer", pointCloudBuffer);
            collisionComputeShader.SetBuffer(_kernelTransform, "ResultBuffer", _resultBuffer);
            collisionComputeShader.SetBuffer(_kernelTransform, "TargetPositionsBuffer", _targetPositionsBuffer);
            collisionComputeShader.SetInt("TargetPositionsCount", targetPosList.Count);
            collisionComputeShader.SetInt("PointsCount", pointCount);
            collisionComputeShader.SetVector("TargetPosition", targetPosList[0]);
            collisionComputeShader.SetFloat("SurfaceDistanceThreshold", surfaceDistanceThreshold);
            collisionComputeShader.SetFloat("BackfaceDistanceThreshold", backfaceDistanceThreshold);
            collisionComputeShader.SetFloat("VisibleSurfaceDistanceThreshold", visibleSurfaceDistanceThreshold);
            collisionComputeShader.SetFloat("VisibleBackfaceDistanceThreshold", visibleBackfaceDistanceThreshold);
            collisionComputeShader.SetFloat("OccludedSurfaceDistanceThreshold", occludedSurfaceDistanceThreshold);
            collisionComputeShader.SetFloat("OccludedBackfaceDistanceThreshold", occludedBackfaceDistanceThreshold);
            collisionComputeShader.SetInt("DistanceMode", (int)distanceMode);
            collisionComputeShader.SetVector("CameraPosition", cameraPos);

            int threadGroups = Mathf.CeilToInt(pointCount / 256.0f);
            collisionComputeShader.Dispatch(_kernelTransform, threadGroups, 1, 1);
        }

        private void DispatchFootPlaneMode(ComputeBuffer pointCloudBuffer, int pointCount)
        {
            if (footPlaneTarget == null) return;

            collisionComputeShader.SetBuffer(_kernelFootPlane, "PointCloudBuffer", pointCloudBuffer);
            collisionComputeShader.SetBuffer(_kernelFootPlane, "ResultBuffer", _resultBuffer);

            collisionComputeShader.SetInt("PointsCount", pointCount);
            collisionComputeShader.SetMatrix("WorldToTargetMatrix", footPlaneTarget.worldToLocalMatrix);
            collisionComputeShader.SetVector("FootPlaneBoundsMin", footPlaneBoundsMin);
            collisionComputeShader.SetVector("FootPlaneBoundsMax", footPlaneBoundsMax);
            collisionComputeShader.SetFloat("FootPlaneRefY", footPlaneRefY);
            collisionComputeShader.SetFloat("FootPlaneDepthThreshold", footPlaneDepthThreshold);
            collisionComputeShader.SetFloat("FootPlaneUpperMargin", footPlaneUpperMargin);
            collisionComputeShader.SetVector("FootPlaneNormal", footPlaneNormal);

            int threadGroups = Mathf.CeilToInt(pointCount / 256.0f);
            collisionComputeShader.Dispatch(_kernelFootPlane, threadGroups, 1, 1);
        }

        private void DispatchMeshMode(ComputeBuffer pointCloudBuffer, int pointCount, Vector3 cameraPos)
        {
            if (!_meshBaker.BakeAndCombine(targetSkinnedMeshes, targetMeshFilters, targetObject))
            {
                return;
            }

            float maxThresh = Mathf.Max(surfaceDistanceThreshold, backfaceDistanceThreshold);

            _gridBuilder.BuildGrid(
                collisionComputeShader,
                _kernelClearGrid,
                _kernelBuildGrid,
                _meshBaker.Vertices,
                _meshBaker.Normals,
                _meshBaker.Triangles,
                _meshBaker.MeshBounds,
                _meshBaker.TargetTransform,
                maxThresh);

            collisionComputeShader.SetBuffer(_kernelMesh, "PointCloudBuffer", pointCloudBuffer);
            collisionComputeShader.SetBuffer(_kernelMesh, "ResultBuffer", _resultBuffer);
            collisionComputeShader.SetBuffer(_kernelMesh, "MeshVerticesBuffer", _gridBuilder.MeshVerticesBuffer);
            collisionComputeShader.SetBuffer(_kernelMesh, "MeshNormalsBuffer", _gridBuilder.MeshNormalsBuffer);
            collisionComputeShader.SetBuffer(_kernelMesh, "MeshIndicesBuffer", _gridBuilder.MeshIndicesBuffer);
            collisionComputeShader.SetBuffer(_kernelMesh, "GridBuffer", _gridBuilder.GridBuffer);

            collisionComputeShader.SetInt("PointsCount", pointCount);
            collisionComputeShader.SetInt("MeshTrianglesCount", _meshBaker.TrianglesCount);
            collisionComputeShader.SetMatrix("LocalToWorldMatrix", _meshBaker.TargetTransform.localToWorldMatrix);

            float totalPadding = _gridBuilder.TotalPadding;
            Bounds bounds = _meshBaker.MeshBounds;

            collisionComputeShader.SetVector("MeshBoundsMin", bounds.min - new Vector3(totalPadding, totalPadding, totalPadding));
            collisionComputeShader.SetVector("MeshBoundsMax", bounds.max + new Vector3(totalPadding, totalPadding, totalPadding));
            
            collisionComputeShader.SetVector("GridBoundsMin", _gridBuilder.GridMin);
            collisionComputeShader.SetVector("GridCellSize", _gridBuilder.CellSize);
            collisionComputeShader.SetInts("GridResolution", _gridBuilder.GridResolution);
            
            collisionComputeShader.SetFloat("SurfaceDistanceThreshold", surfaceDistanceThreshold);
            collisionComputeShader.SetFloat("BackfaceDistanceThreshold", backfaceDistanceThreshold);
            collisionComputeShader.SetFloat("VisibleSurfaceDistanceThreshold", visibleSurfaceDistanceThreshold);
            collisionComputeShader.SetFloat("VisibleBackfaceDistanceThreshold", visibleBackfaceDistanceThreshold);
            collisionComputeShader.SetFloat("OccludedSurfaceDistanceThreshold", occludedSurfaceDistanceThreshold);
            collisionComputeShader.SetFloat("OccludedBackfaceDistanceThreshold", occludedBackfaceDistanceThreshold);
            collisionComputeShader.SetInt("DistanceMode", (int)distanceMode);
            collisionComputeShader.SetVector("CameraPosition", cameraPos);

            int threadGroups = Mathf.CeilToInt(pointCount / 64.0f);
            collisionComputeShader.Dispatch(_kernelMesh, threadGroups, 1, 1);

#if UNITY_EDITOR
            if (Core.Logging.AppLogger.IsEnabled(_pipeline, HCD_Pipeline.TagDistanceProcessor) && Time.frameCount % 120 == 0)
            {
                Core.Logging.AppLogger.Log(_pipeline, HCD_Pipeline.TagDistanceProcessor,
                    $"MeshFilter Mode Debug:\n" +
                    $"  TargetTransform    : {_meshBaker.TargetTransform?.name} (pos={_meshBaker.TargetTransform?.position})\n" +
                    $"  Registered Filters : {targetMeshFilters?.Length ?? 0}\n" +
                    $"  Combined Instances  : {_meshBaker.ValidInstanceCount}\n" +
                    $"  Vertices / Triangles: {_meshBaker.Vertices?.Length ?? 0} / {_meshBaker.TrianglesCount}\n" +
                    $"  World Bounds        : min={bounds.min:F3} max={bounds.max:F3} size={bounds.size:F3}\n" +
                    $"  Grid Cell Size      : {_gridBuilder.CellSize:F4}  (8x8x8 grid, 31 tris/cell max)\n" +
                    $"  Total Padding       : {totalPadding:F4}m  (thresh={maxThresh:F4})");
            }
#endif
        }

        public void Release()
        {
            _resultBuffer?.Release();
            _targetPositionsBuffer?.Release();
            _meshBaker.Release();
            _gridBuilder.Release();
        }
    }
