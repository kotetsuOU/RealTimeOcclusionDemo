using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using Core.Logging;

namespace SICESI
{
    /// <summary>
    /// 点群密度を自動的に切り替えながら、各密度の実測8ビット占有セクターマスク (SectorMask RAW/CSV/PNG) と
    /// GT画像、テストレンダリング画像を自動一括キャプチャ・保存するコレクタークラス。
    /// SICESI_StereoEvaluationController と連携し、独立した責務として動作します。
    /// </summary>
    [AppLoggable("SICESI")]
    [RequireComponent(typeof(SICESI_StereoEvaluationController))]
    public class SICESI_SectorMaskCollector : MonoBehaviour, IAppLoggable
    {
        /// <summary>
        /// セクターマスクのスイープ収集中かどうかを示すグローバルフラグ。
        /// PCDOcclusionStage でのデバッグバッファ常時記録および PCDDebugReadbackManager での不要リセット防止に使用します。
        /// </summary>
        public static bool IsCollectingActive { get; private set; } = false;

        [Header("Status")]
        public bool isCollecting = false;
        public string statusMessage = "Ready";

        private SICESI_StereoEvaluationController _controller;

        private void Awake()
        {
            _controller = GetComponent<SICESI_StereoEvaluationController>();
        }

        public void RegisterLogTriggers(LogCategoryGroup group, System.Collections.Generic.HashSet<string> existingLabels)
        {
            // AppLogger 管理用
        }

        /// <summary>
        /// 全密度の占有セクターマスクおよびステレオ評価画像の一括自動取得を開始します。
        /// </summary>
        public void RunSectorMaskDensitySweep()
        {
            if (isCollecting)
            {
                Debug.LogWarning("[SICESI] 既に収集処理が実行中です。");
                return;
            }

            if (_controller == null)
            {
                _controller = GetComponent<SICESI_StereoEvaluationController>();
            }

            if (_controller == null)
            {
                Debug.LogError("[SICESI] SICESI_StereoEvaluationController が見つかりません。");
                return;
            }

            StartCoroutine(SectorMaskDensitySweepRoutine());
        }

        private IEnumerator SectorMaskDensitySweepRoutine()
        {
            isCollecting = true;
            IsCollectingActive = true;
            statusMessage = "Starting SectorMask Density Sweep...";

            string conditionRootDir = Path.Combine(_controller.outputDirectory, _controller.conditionName);
            string sweepRootDir = Path.Combine(conditionRootDir, "SectorMaskSweep");
            Directory.CreateDirectory(sweepRootDir);

            AppLogger.Log("SICESI", $"=== 占有セクターマスク 密度スイープ開始: {sweepRootDir} ===");

            bool prevRecordNeighborCount = false;
            if (_controller.occlusionPipelineController != null)
            {
                prevRecordNeighborCount = _controller.occlusionPipelineController.recordNeighborCountMap;
                _controller.occlusionPipelineController.recordNeighborCountMap = true;
            }
            if (PCDRendererFeature.Instance != null && PCDRendererFeature.Instance.settings != null)
            {
                PCDRendererFeature.Instance.settings.recordNeighborCountMap = true;
            }

            try
            {
                // -------------------------------------------------------------
                // Step 1: Ground Truth 撮影 (手メッシュのみ)
                // -------------------------------------------------------------
                statusMessage = "Capturing Ground Truth for SectorMask Sweep...";
                var backup = _controller.SetGroundTruthState(true);
                if (_controller.pointCloudObject != null) _controller.pointCloudObject.SetActive(false);

                for (int i = 0; i < 3; i++) yield return null;
                yield return new WaitForEndOfFrame();

                string gtDir = Path.Combine(sweepRootDir, "GT");
                CaptureCameraImages(gtDir, "gt");

                // 共通 GT ディレクトリにも保存
                string commonGtDir = Path.Combine(_controller.outputDirectory, "GT");
                if (commonGtDir != gtDir)
                {
                    CaptureCameraImages(commonGtDir, "gt");
                }

                _controller.RestoreGroundTruthState(backup);
                AppLogger.Log("SICESI", $"[1/2] Ground Truth 撮影完了: {gtDir}");

                // -------------------------------------------------------------
                // Step 2: 点群表示に切り替え & 密度変更ループ
                // -------------------------------------------------------------
                if (_controller.pointCloudObject != null) _controller.pointCloudObject.SetActive(true);

                float[] densities = _controller.sweepDensities;
                if (densities == null || densities.Length == 0)
                {
                    densities = new float[] { _controller.dummyPointCloudProvider != null ? _controller.dummyPointCloudProvider.densityValue : 4.0f };
                }

                string unitSuffix = _controller.densityUnit.ToString();

                for (int d = 0; d < densities.Length; d++)
                {
                    float density = densities[d];
                    string densityStr = density.ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture);
                    string densityFolderName = $"density_{densityStr}pts_mm2";
                    string targetDir = Path.Combine(sweepRootDir, densityFolderName);
                    Directory.CreateDirectory(targetDir);

                    statusMessage = $"Collecting ({d + 1}/{densities.Length}): Density = {densityStr} ({_controller.densityUnit})";
                    AppLogger.Log("SICESI", $"[2/2] 密度設定変更 ({d + 1}/{densities.Length}): {densityStr}");

                    if (_controller.dummyPointCloudProvider != null)
                    {
                        _controller.dummyPointCloudProvider.densityUnit = _controller.densityUnit;
                        _controller.dummyPointCloudProvider.densityValue = density;
                        _controller.dummyPointCloudProvider.ForceUpdateSampling();
                    }

                    if (PCDRendererFeature.Instance != null)
                    {
                        PCDRendererFeature.Instance.MarkPointCloudDataDirty();
                        if (PCDRendererFeature.Instance.settings != null)
                        {
                            PCDRendererFeature.Instance.settings.recordNeighborCountMap = true;
                        }
                    }
                    if (_controller.occlusionPipelineController != null)
                    {
                        _controller.occlusionPipelineController.recordNeighborCountMap = true;
                    }

                    // 点群サンプリングとGPU描画の安定待機
                    for (int f = 0; f < _controller.waitFramesAfterDensityChange; f++) yield return null;
                    yield return new WaitForEndOfFrame();

                    // テスト画像の保存 (Left / Right)
                    CaptureCameraImages(targetDir, $"test_{densityStr}");

                    // カメラ描画とComputePass完了待機
                    yield return null;
                    yield return new WaitForEndOfFrame();

                    // セクター占有マスクのGPU読み出し・保存 (左右眼ごとに個別レンダリング)
                    yield return StartCoroutine(CaptureAndExportSectorMasks(targetDir, densityStr));

                    // JSON メタデータ保存
                    SaveParamsJson(targetDir, density, _controller.occlusionPipelineController != null ? _controller.occlusionPipelineController.occlusionThreshold : 0.1f);

                    AppLogger.Log("SICESI", $"[{d + 1}/{densities.Length}] 密度 {densityStr} のデータ収集完了: {targetDir}");
                }

                statusMessage = "All SectorMask Density Sweeps Completed!";
                AppLogger.Log("SICESI", $"=== 全密度の占有セクターマスク収集が完了しました! 保存先: {sweepRootDir} ===");
            }
            finally
            {
                if (_controller.occlusionPipelineController != null)
                {
                    _controller.occlusionPipelineController.recordNeighborCountMap = prevRecordNeighborCount;
                }
                if (PCDRendererFeature.Instance != null && PCDRendererFeature.Instance.settings != null)
                {
                    PCDRendererFeature.Instance.settings.recordNeighborCountMap = prevRecordNeighborCount;
                }
                IsCollectingActive = false;
                isCollecting = false;
            }
        }

        /// <summary>
        /// 左右カメラの視点をキャプチャしてPNG保存します (コントローラーの統一撮影処理を使用)。
        /// </summary>
        private void CaptureCameraImages(string baseDir, string filePrefix)
        {
            if (_controller != null)
            {
                _controller.CaptureStereoViews(baseDir, filePrefix);
            }
        }

        /// <summary>
        /// 左右各カメラを順次アクティブにしてURPレンダリングを実行し、
        /// 各眼の真の視差に応じた NeighborCountMap (R32_UInt) をGPUから非同期読み戻して個別保存します。
        /// </summary>
        private IEnumerator CaptureAndExportSectorMasks(string targetDensityDir, string densityStr)
        {
            Camera leftCam = _controller.leftEyeCamera;
            Camera rightCam = _controller.rightEyeCamera;

            bool leftOriginalEnabled = leftCam != null && leftCam.enabled;
            bool rightOriginalEnabled = rightCam != null && rightCam.enabled;

            try
            {
                // 1. Left 眼のセクターマスク取得
                if (leftCam != null)
                {
                    if (rightCam != null) rightCam.enabled = false;
                    leftCam.enabled = true;

                    yield return null;
                    yield return new WaitForEndOfFrame();

                    string leftDir = Path.Combine(targetDensityDir, "Left");
                    yield return StartCoroutine(ReadbackAndSaveSingleEye(leftDir, $"{densityStr}_left"));
                }

                // 2. Right 眼のセクターマスク取得
                if (rightCam != null)
                {
                    if (leftCam != null) leftCam.enabled = false;
                    rightCam.enabled = true;

                    yield return null;
                    yield return new WaitForEndOfFrame();

                    string rightDir = Path.Combine(targetDensityDir, "Right");
                    yield return StartCoroutine(ReadbackAndSaveSingleEye(rightDir, $"{densityStr}_right"));
                }
            }
            finally
            {
                if (leftCam != null) leftCam.enabled = leftOriginalEnabled;
                if (rightCam != null) rightCam.enabled = rightOriginalEnabled;
            }
        }

        private IEnumerator ReadbackAndSaveSingleEye(string saveDir, string filePrefix)
        {
            if (PCDRendererFeature.Instance == null || PCDRendererFeature.Instance.CurrentResources == null)
            {
                yield break;
            }

            var neighborCountRt = PCDRendererFeature.Instance.CurrentResources.NeighborCountMap;
            if (neighborCountRt == null || neighborCountRt.rt == null)
            {
                yield break;
            }

            var rt = neighborCountRt.rt;
            int width = rt.width;
            int height = rt.height;

            bool readbackDone = false;
            uint[] readbackData = null;

            AsyncGPUReadback.Request(rt, 0, request =>
            {
                if (!request.hasError)
                {
                    readbackData = request.GetData<uint>().ToArray();
                }
                readbackDone = true;
            });

            float timeout = Time.realtimeSinceStartup + 2.0f;
            while (!readbackDone && Time.realtimeSinceStartup < timeout)
            {
                yield return null;
            }

            if (readbackData != null)
            {
                PCDOcclusionDebugExporter.ExportSectorMaskData(readbackData, width, height, saveDir, filePrefix);
                AppLogger.Log("SICESI", $"SectorMask 保存完了 [{filePrefix}] (w:{width}, h:{height}): {saveDir}");
            }
        }

        [Serializable]
        private struct EvaluationParamsData
        {
            public string conditionName;
            public float densityValue;
            public string densityUnit;
            public float occlusionThreshold;
            public string evaluationMode;
            public string timestamp;
        }

        private void SaveParamsJson(string targetDir, float density, float threshold)
        {
            try
            {
                var data = new EvaluationParamsData
                {
                    conditionName = _controller.conditionName,
                    densityValue = density,
                    densityUnit = _controller.densityUnit.ToString(),
                    occlusionThreshold = threshold,
                    evaluationMode = _controller.occlusionPipelineController != null ? _controller.occlusionPipelineController.evaluationMode.ToString() : "SectorConsecutiveZeros",
                    timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss")
                };
                string json = JsonUtility.ToJson(data, true);
                File.WriteAllText(Path.Combine(targetDir, "evaluation_params.json"), json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SICESI] JSON保存エラー: {ex.Message}");
            }
        }
    }
}
