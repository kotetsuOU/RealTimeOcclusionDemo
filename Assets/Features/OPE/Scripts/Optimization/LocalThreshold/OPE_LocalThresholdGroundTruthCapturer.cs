using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using Core.Logging;

namespace OPE
{
    /// <summary>
    /// 手メッシュ (groundTruthObject) および仮想物体 (virtualObject) から、
    /// 各ピクセルの中心と視点を結ぶ直線が初めて交差する点を仮想ダミー点として全ピクセルを埋め、
    /// 独自に再構築した理想深度ピラミッド (L0〜L6) と実測点群の評価キャッシュを構築・保持するクラス。
    /// </summary>
    public static class OPE_LocalThresholdGroundTruthCapturer
    {
        private const string Tag = "TLocOpt";

        public class GroundTruthCaptureData : IDisposable
        {
            public uint[] gtLeftData;
            public uint[] gtRightData;
            public OPE_IdealDepthPyramidBuilder.IdealPyramidData leftIdealPyramid;
            public OPE_IdealDepthPyramidBuilder.IdealPyramidData rightIdealPyramid;
            public OPE_SparsePointCloudPyramidEvaluator.EyeEvaluationCache leftTestCache;
            public OPE_SparsePointCloudPyramidEvaluator.EyeEvaluationCache rightTestCache;
            public int width;
            public int height;

            public void Dispose()
            {
                gtLeftData = null;
                gtRightData = null;
                leftIdealPyramid = null;
                rightIdealPyramid = null;
                leftTestCache = null;
                rightTestCache = null;
            }
        }

        /// <summary>
        /// 現在のプリセット配置において、
        /// 1. ピクセル中心とカメラ視点を結ぶ直線が手メッシュと初めて交差する点で全ピクセル (L0) を埋め、理想深度ピラミッド (L0〜L6) を独自に再構築
        /// 2. 疎・ノイズあり実測点群の投影とパラメータ依存探索半径 (level) および 8方向最小局所評価値 (minSectorOcclusion[0..7]) を算出
        /// 3. そのパラメータ依存探索半径 (level) を用いて、理想深度ピラミッドの最小値候補箇所に1つでも手前点があれば真 (白) とする 8方向理論正解 (GT) マスクを生成
        /// を一括実行します。
        /// </summary>
        public static IEnumerator CaptureGroundTruthRoutine(
            OPE_StereoEvaluationController controller,
            Action<GroundTruthCaptureData> onCompleted)
        {
            if (controller == null)
            {
                onCompleted?.Invoke(null);
                yield break;
            }

            var result = BuildSynchronously(controller);
            yield return null;
            onCompleted?.Invoke(result);
        }

        /// <summary>
        /// 理想深度ピラミッドの再構築、実測点群キャッシュ生成、および8方向理論GTマスクの算出を同期的に実行します。
        /// </summary>
        public static GroundTruthCaptureData BuildSynchronously(OPE_StereoEvaluationController controller)
        {
            var data = BuildIdealPyramidsOnly(controller);
            if (data == null) return null;

            UpdateEvaluationCacheForCurrentPointCloud(controller, data);
            return data;
        }

        /// <summary>
        /// プリセットのメッシュから理想深度ピラミッド (L0〜L6) のみを構築します (点群密度に非依存)。
        /// </summary>
        public static GroundTruthCaptureData BuildIdealPyramidsOnly(OPE_StereoEvaluationController controller)
        {
            if (controller == null) return null;

            Camera leftCam = controller.leftEyeCamera;
            Camera rightCam = controller.rightEyeCamera;
            if (leftCam == null && rightCam == null) return null;

            Camera refCam = leftCam != null ? leftCam : rightCam;
            int w = GetCameraPixelWidth(refCam);
            int h = GetCameraPixelHeight(refCam);
            var settings = GetCurrentPipelineSettings(controller);

            var result = new GroundTruthCaptureData
            {
                width = w,
                height = h
            };

            // 左目カメラの理想深度ピラミッド再構築および密度非依存の真値(GT)マスク生成
            if (leftCam != null)
            {
                result.leftIdealPyramid = OPE_IdealDepthPyramidBuilder.BuildIdealPyramidForCamera(
                    leftCam,
                    controller.groundTruthObject,
                    controller.virtualObject,
                    w,
                    h);

                // 疎点群の巨大探索半径(lv=4..6)に引きずられず、手の真の近傍領域のみをGT=1とするため
                // searchLevels=null (minSearchLevelを自然探索範囲として利用) で真値を確定
                result.gtLeftData = OPE_IdealDepthPyramidBuilder.ComputeTheoreticalGroundTruthMasks(
                    result.leftIdealPyramid,
                    null,
                    settings.minSearchLevel);
            }

            // 右目カメラの理想深度ピラミッド再構築および密度非依存の真値(GT)マスク生成
            if (rightCam != null)
            {
                result.rightIdealPyramid = OPE_IdealDepthPyramidBuilder.BuildIdealPyramidForCamera(
                    rightCam,
                    controller.groundTruthObject,
                    controller.virtualObject,
                    w,
                    h);

                result.gtRightData = OPE_IdealDepthPyramidBuilder.ComputeTheoreticalGroundTruthMasks(
                    result.rightIdealPyramid,
                    null,
                    settings.minSearchLevel);
            }
            else
            {
                result.rightIdealPyramid = result.leftIdealPyramid;
                result.gtRightData = result.gtLeftData;
            }

            return result;
        }

        /// <summary>
        /// 既存の理想ピラミッドに対し、指定された疎点群から評価キャッシュおよび8方向理論GTマスクを構築・更新します。
        /// </summary>
        public static void UpdateEvaluationCacheForCurrentPointCloud(
            OPE_StereoEvaluationController controller,
            GroundTruthCaptureData gtData)
        {
            if (controller == null || gtData == null) return;

            Camera leftCam = controller.leftEyeCamera;
            Camera rightCam = controller.rightEyeCamera;
            var settings = GetCurrentPipelineSettings(controller);

            // 実測点群（疎＋ノイズあり）の座標配列を取得
            Vector3[] sparsePoints = null;
            int pointCount = 0;
            var provider = controller.dummyPointCloudProvider;
            if (provider != null)
            {
                var sampled = provider.LastSampledData;
                sparsePoints = sampled.Positions;
                pointCount = sampled.PointCount;
            }

            int w = gtData.width;
            int h = gtData.height;

            // 左目テストキャッシュ生成 (疎点群の投影・探索半径決定・局所評価値算出)
            if (leftCam != null && gtData.leftIdealPyramid != null)
            {
                gtData.leftTestCache = OPE_SparsePointCloudPyramidEvaluator.BuildEyeEvaluationCache(
                    leftCam,
                    sparsePoints,
                    pointCount,
                    gtData.leftIdealPyramid.virtualObjectMap,
                    settings,
                    w,
                    h);

                // GTマスクが未生成の場合のみフォールバック初期化 (疎点群のsearchLevelsではなく自然探索範囲で生成)
                if (gtData.gtLeftData == null)
                {
                    gtData.gtLeftData = OPE_IdealDepthPyramidBuilder.ComputeTheoreticalGroundTruthMasks(
                        gtData.leftIdealPyramid,
                        null,
                        settings.minSearchLevel);
                }
            }

            // 右目テストキャッシュ生成
            if (rightCam != null && gtData.rightIdealPyramid != null)
            {
                gtData.rightTestCache = OPE_SparsePointCloudPyramidEvaluator.BuildEyeEvaluationCache(
                    rightCam,
                    sparsePoints,
                    pointCount,
                    gtData.rightIdealPyramid.virtualObjectMap,
                    settings,
                    w,
                    h);

                if (gtData.gtRightData == null)
                {
                    gtData.gtRightData = OPE_IdealDepthPyramidBuilder.ComputeTheoreticalGroundTruthMasks(
                        gtData.rightIdealPyramid,
                        null,
                        settings.minSearchLevel);
                }
            }
            else
            {
                gtData.rightTestCache = gtData.leftTestCache;
                if (gtData.gtRightData == null)
                {
                    gtData.gtRightData = gtData.gtLeftData;
                }
            }
        }

        private static int GetCameraPixelWidth(Camera cam)
        {
            if (PCDRendererFeature.Instance != null && PCDRendererFeature.Instance.CurrentResources != null)
            {
                var rt = PCDRendererFeature.Instance.CurrentResources.ViewPositionMap?.rt;
                if (rt != null && rt.width > 16) return rt.width;
            }
            if (cam != null && cam.targetTexture != null && cam.targetTexture.width > 16)
            {
                return cam.targetTexture.width;
            }
            if (cam != null && cam.pixelWidth > 16)
            {
                return cam.pixelWidth;
            }
            return Mathf.Max(640, Screen.width);
        }

        private static int GetCameraPixelHeight(Camera cam)
        {
            if (PCDRendererFeature.Instance != null && PCDRendererFeature.Instance.CurrentResources != null)
            {
                var rt = PCDRendererFeature.Instance.CurrentResources.ViewPositionMap?.rt;
                if (rt != null && rt.height > 16) return rt.height;
            }
            if (cam != null && cam.targetTexture != null && cam.targetTexture.height > 16)
            {
                return cam.targetTexture.height;
            }
            if (cam != null && cam.pixelHeight > 16)
            {
                return cam.pixelHeight;
            }
            return Mathf.Max(480, Screen.height);
        }

        private static PCDRendererFeature.PCDRenderSettings GetCurrentPipelineSettings(OPE_StereoEvaluationController controller)
        {
            if (controller != null && controller.occlusionPipelineController != null)
            {
                return controller.occlusionPipelineController.GetSettings();
            }
            if (PCDRendererFeature.Instance != null && PCDRendererFeature.Instance.settings != null)
            {
                return PCDRendererFeature.Instance.settings.GetSettings(PCDRendererFeature.Instance._internalDynamicMultiplier);
            }
            return new PCDRendererFeature.PCDRenderSettings
            {
                kernelType = PCDRendererFeature.PCD_OcclusionKernel.Bouchiba,
                minSearchLevel = 1,
                exponentAlpha = 1.0f,
                densityThreshold_e = 0.04f,
                neighborhoodParam_p_prime = 4.8f,
                enableDensityBasedLOD = true,
                enableGradientCorrection = true,
                gradientThreshold_g_th = 0.05f,
                enableTagBasedOptimization = true,
                enableTypeAwareDensity = true,
                gridSize = PCDRendererFeature.PCD_GridSize.Grid16x16
            };
        }

        /// <summary>
        /// 後方互換性用: 現在バインドされている NeighborCountMap (RT) から uint[] を非同期に読み出します。
        /// </summary>
        public static IEnumerator ReadbackNeighborCountMap(Action<uint[]> onDone)
        {
            if (PCDRendererFeature.Instance == null || PCDRendererFeature.Instance.CurrentResources == null)
            {
                onDone?.Invoke(null);
                yield break;
            }

            var rtHandle = PCDRendererFeature.Instance.CurrentResources.NeighborCountMap;
            if (rtHandle == null || rtHandle.rt == null)
            {
                onDone?.Invoke(null);
                yield break;
            }

            var rt = rtHandle.rt;
            bool done = false;
            uint[] resultData = null;

            AsyncGPUReadback.Request(rt, 0, request =>
            {
                if (!request.hasError)
                {
                    resultData = request.GetData<uint>().ToArray();
                }
                done = true;
            });

            float timeout = Time.realtimeSinceStartup + 2.0f;
            while (!done && Time.realtimeSinceStartup < timeout)
            {
                yield return null;
            }

            onDone?.Invoke(resultData);
        }
    }
}

