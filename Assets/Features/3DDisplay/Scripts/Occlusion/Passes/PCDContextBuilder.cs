using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using Core.Logging;

/// <summary>
/// RenderGraph のパス登録前に、毎フレーム必要なカメラ行列の計算、
/// 点群バッファの調停、描画スキップ判定などを事前に行うビルダークラスです。
/// </summary>
internal class PCDContextBuilder
{
    private PCDPointBufferManager.Point[] _virtualContactPointsArray;

    // デバッグログ用状態キャプチャ
    private bool _lastShouldSkip = false;
    private bool _lastHasVirtualObjects = false;
    private int _lastActiveCount = -1;
    private uint _lastPixelCount = uint.MaxValue;

    public struct PreComputeData
    {
        public bool ShouldSkip;
        
        public Camera Camera;
        public UniversalResourceData ResourceData;
        public int ScreenWidth;
        public int ScreenHeight;

        public Matrix4x4 ViewMatrix;
        public Matrix4x4 ProjectionMatrix;
        public Matrix4x4 InverseProjectionMatrix;

        public ComputeBuffer ActiveBuffer;
        public int ActiveCount;

        public bool HasVirtualObjects;
        public bool HasVirtualDepth;
        public bool IsHalfMirrorEnabled;
    }

    public PreComputeData BuildPreComputeData(
        ContextContainer frameData,
        PCDRendererFeature.PCDRenderSettings settings,
        PCDPointBufferManager bufferManager)
    {
        var data = new PreComputeData();

        if (!Application.isPlaying)
        {
            data.ShouldSkip = true;
            return data;
        }

        // =========================================================================
        // 仮想接触ポイントの生成
        // =========================================================================
        bool shouldUseExternal = (PCDRendererFeature.Instance != null && PCDRendererFeature.Instance.IsGlobalBufferMode) || (RsGlobalPointCloudManager.Instance != null);

        if (settings.enableVirtualContactOcclusion && HCD_Pipeline.Instance != null)
        {
            var trackedClusters = HCD_Pipeline.Instance.GetTrackedClusters();
            int estimatedMaxPoints = 0;
            float radius = settings.virtualContactRadius;
            float spacing = Mathf.Max(0.001f, settings.virtualContactSpacing);
            int pointsPerCluster = (int)(Mathf.PI * radius * radius / (spacing * spacing)) + 100;

            foreach (var c in trackedClusters)
            {
                if (c.IsAlive) estimatedMaxPoints += pointsPerCluster;
            }

            if (_virtualContactPointsArray == null || _virtualContactPointsArray.Length < estimatedMaxPoints)
            {
                _virtualContactPointsArray = new PCDPointBufferManager.Point[Mathf.Max(1024, estimatedMaxPoints * 2)];
            }

            int idx = 0;
            foreach (var c in trackedClusters)
            {
                if (!c.IsAlive) continue;

                Vector3 centroid = c.Centroid;
                Vector3 normal = c.Normal.normalized;
                if (normal.sqrMagnitude < 0.1f) normal = Vector3.up;

                float offset = HCD_Pipeline.Instance.distanceProcessor != null ? HCD_Pipeline.Instance.distanceProcessor.surfaceDistanceThreshold : 0f;
                centroid += normal * offset;

                Vector3 tangent = Vector3.Cross(normal, Vector3.up);
                if (tangent.sqrMagnitude < 0.001f) tangent = Vector3.Cross(normal, Vector3.right);
                tangent.Normalize();
                Vector3 bitangent = Vector3.Cross(normal, tangent).normalized;

                int steps = Mathf.CeilToInt(radius / spacing);
                for (int x = -steps; x <= steps; x++)
                {
                    for (int y = -steps; y <= steps; y++)
                    {
                        float dx = x * spacing;
                        float dy = y * spacing;
                        if (dx * dx + dy * dy <= radius * radius)
                        {
                            if (idx >= _virtualContactPointsArray.Length) break;
                            _virtualContactPointsArray[idx++] = new PCDPointBufferManager.Point
                            {
                                position = centroid + tangent * dx + bitangent * dy,
                                color = new Vector3(settings.virtualContactColor.r, settings.virtualContactColor.g, settings.virtualContactColor.b),
                                originType = 0
                            };
                        }
                    }
                }
            }
            bufferManager.SetVirtualContactPoints(idx > 0 ? _virtualContactPointsArray : null, idx);
        }
        else
        {
            bufferManager.SetVirtualContactPoints(null, 0);
        }

        // =========================================================================
        // 外部バッファの設定
        // =========================================================================
        if (shouldUseExternal && RsGlobalPointCloudManager.Instance != null)
        {
            var globalBuffer = RsGlobalPointCloudManager.Instance.GetOcclusionGlobalBuffer();
            var globalCount = RsGlobalPointCloudManager.Instance.OcclusionTotalCount;
            bufferManager.SetExternalBuffer(globalBuffer, globalCount);
        }
        else
        {
            bufferManager.SetExternalBuffer(null, 0);
        }

        bufferManager.Update();

        // =========================================================================
        // アクティブバッファの決定
        // =========================================================================
        if (bufferManager.UseExternalBuffer && bufferManager.ExternalPointBuffer != null)
        {
            int extCount = bufferManager.ExternalPointCount >= 0 ? bufferManager.ExternalPointCount : bufferManager.ExternalPointBuffer.count;
            if (bufferManager.PointCount > 0)
            {
                int totalCount = extCount + bufferManager.PointCount;
                bufferManager.EnsureCombinedBuffer(totalCount);
                data.ActiveBuffer = bufferManager.CombinedBuffer;
                data.ActiveCount = totalCount;
            }
            else
            {
                data.ActiveBuffer = bufferManager.ExternalPointBuffer;
                data.ActiveCount = extCount;
            }
        }
        else
        {
            data.ActiveBuffer = bufferManager.PointBuffer;
            data.ActiveCount = bufferManager.PointCount;
        }

        // =========================================================================
        // カメラ・リソース情報の取得と行列計算
        // =========================================================================
        var cameraData = frameData.Get<UniversalCameraData>();
        data.ResourceData = frameData.Get<UniversalResourceData>();
        data.Camera = cameraData.camera;

        // CullingMask が 0 のカメラ、またはカメラターゲットモードのフィルタ条件に合致しない場合はスキップ
        bool isCullingMaskZero = data.Camera != null && data.Camera.cullingMask == 0;
        bool isCameraFiltered = false;
        if (data.Camera != null)
        {
            if (settings.cameraTargetMode == PCDRendererFeature.PCD_CameraTargetMode.VirtualCamerasOnly)
            {
                isCameraFiltered = string.IsNullOrEmpty(data.Camera.name) || !data.Camera.name.ToLowerInvariant().Contains("virtual");
            }
            else if (settings.cameraTargetMode == PCDRendererFeature.PCD_CameraTargetMode.CustomFilter && !string.IsNullOrEmpty(settings.cameraNameFilter))
            {
                isCameraFiltered = string.IsNullOrEmpty(data.Camera.name) || !data.Camera.name.ToLowerInvariant().Contains(settings.cameraNameFilter.ToLowerInvariant());
            }
        }

        if (isCullingMaskZero || isCameraFiltered)
        {
            data.ShouldSkip = true;
            if (AppLogger.IsEnabled(PCD_LogTriggers.TagContextBuilder))
            {
                if (_lastShouldSkip != data.ShouldSkip || Time.frameCount % 120 == 0)
                {
                    string reason = isCullingMaskZero ? "CullingMask is 0" : $"Camera filter mismatch ({data.Camera?.name})";
                    AppLogger.Log(PCD_LogTriggers.TagContextBuilder, $"[ContextBuilder] Rendering SKIPPED: {reason}");
                    _lastShouldSkip = data.ShouldSkip;
                }
            }
            return data;
        }

        // =========================================================================
        // 点群データ有無のスキップ判定
        // =========================================================================
        bool pointCloudHasData = data.ActiveBuffer != null && data.ActiveCount > 0 && data.ActiveBuffer.IsValid();

        if (!pointCloudHasData)
        {
            data.ShouldSkip = true;

            // スキップ時ログ
            if (AppLogger.IsEnabled(PCD_LogTriggers.TagContextBuilder))
            {
                bool stateChanged = (_lastShouldSkip != data.ShouldSkip || _lastActiveCount != data.ActiveCount);
                if (stateChanged || Time.frameCount % 120 == 0)
                {
                    AppLogger.Log(PCD_LogTriggers.TagContextBuilder,
                        $"[ContextBuilder] Rendering SKIPPED: PointCloudData={pointCloudHasData} (Count={data.ActiveCount})");
                    _lastShouldSkip = data.ShouldSkip;
                    _lastActiveCount = data.ActiveCount;
                }
            }

            return data;
        }

        data.ScreenWidth = cameraData.cameraTargetDescriptor.width;
        data.ScreenHeight = cameraData.cameraTargetDescriptor.height;

        data.HasVirtualDepth = data.ResourceData.cameraDepthTexture.IsValid();
        data.HasVirtualObjects = false;
        uint lastFramePixelCount = 0;
        if (data.HasVirtualDepth && settings.enableVirtualDepthIntegration)
        {
            if (PCDRendererFeature.Instance != null)
            {
                lastFramePixelCount = PCDRendererFeature.Instance.LastFrameVirtualMeshPixelCount;
                data.HasVirtualObjects = lastFramePixelCount > 0;
            }
        }

        Matrix4x4 vMatrix = data.Camera.worldToCameraMatrix;
        data.IsHalfMirrorEnabled = false;
        data.ViewMatrix = vMatrix;

        Matrix4x4 origProj = data.Camera.projectionMatrix;
        data.ProjectionMatrix = GL.GetGPUProjectionMatrix(origProj, false);
        data.InverseProjectionMatrix = data.ProjectionMatrix.inverse;

        // 評価モジュール (SICESI_MeshDepthGT 等) 向けに最新行列を同期保存
        PCDRendererFeature.SetLatestCameraMatrices(data.Camera, data.ViewMatrix, data.ProjectionMatrix);

        data.ShouldSkip = false;

        // URP入力・事前計算コンテキスト状態のログ出力
        if (AppLogger.IsEnabled(PCD_LogTriggers.TagContextBuilder))
        {
            bool stateChanged = (_lastShouldSkip != data.ShouldSkip ||
                                _lastHasVirtualObjects != data.HasVirtualObjects ||
                                _lastActiveCount != data.ActiveCount ||
                                _lastPixelCount != lastFramePixelCount);

            if (stateChanged || Time.frameCount % 120 == 0)
            {
                bool isColorValid = data.ResourceData.activeColorTexture.IsValid();
                float m00 = data.Camera.projectionMatrix.m00;

                string logMsg = $"[ContextBuilder] URP Input & PreCompute State:\n" +
                                $"  Camera: {data.Camera.name} (Res: {data.ScreenWidth}x{data.ScreenHeight}, CullingMask: 0x{data.Camera.cullingMask:X})\n" +
                                $"  Projection m00: {m00:F4} (Negated: {m00 < 0})\n" +
                                $"  URP Inputs: VirtualDepthTex={data.HasVirtualDepth}, ColorTex={isColorValid}\n" +
                                $"  PointCloud: Count={data.ActiveCount}\n" +
                                $"  VirtualMesh: LastPixelCount={lastFramePixelCount}\n" +
                                $"  Result: ShouldSkip={data.ShouldSkip}, HasVirtualObjects={data.HasVirtualObjects} (DepthIntegration={settings.enableVirtualDepthIntegration})";

                AppLogger.Log(PCD_LogTriggers.TagContextBuilder, logMsg);

                _lastShouldSkip = data.ShouldSkip;
                _lastHasVirtualObjects = data.HasVirtualObjects;
                _lastActiveCount = data.ActiveCount;
                _lastPixelCount = lastFramePixelCount;
            }
        }

        return data;
    }
}
