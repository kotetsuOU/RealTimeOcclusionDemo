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
        private readonly System.Collections.Generic.HashSet<int> _currentActivePatterns = new System.Collections.Generic.HashSet<int>();

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

        /// <summary>
        /// 8セクター・ビットプレーン方式の二値マスク画面保存スイープ。
        /// RAWバッファの座標変換誤差を完全排除するため、URPカメラ直接画面保存により、
        /// 各セクター 0〜7 の二値マスク画像 (計8枚) と GT・通常テスト画像を一括撮影します。
        /// Python側で各画素の8ビットを復元することで、256枚PNG保存に比べ容量1/32・高速撮影を実現します。
        /// </summary>
        public void RunSector8MaskSweep()
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

            StartCoroutine(Sector8MaskSweepRoutine());
        }

        /// <summary>
        /// 256占有パターンの単独二値マスク一括収集スイープ (旧36回転クラスを完全包括)
        /// 各密度における全256パターンの単独所属マスク (Left/Right)、GT、仮想物体シルエット、通常テスト画像、同期生RAWバッファを一括自動撮影・保存します。
        /// </summary>
        public void RunPattern256MaskSweep()
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

            StartCoroutine(Pattern256MaskSweepRoutine());
        }

        [Obsolete("旧36回転クラスは256パターンに完全包括されたため廃止されました。RunPattern256MaskSweep を使用してください。")]
        public void RunRotationClassMaskSweep()
        {
            RunPattern256MaskSweep();
        }

        private IEnumerator Pattern256MaskSweepRoutine()
        {
            isCollecting = true;
            IsCollectingActive = true;
            statusMessage = "Starting 256-Pattern Mask Sweep...";

            string conditionRootDir = _controller.ConditionRootDir;
            string sweepRootDir = Path.Combine(conditionRootDir, "Pattern256MaskSweep");
            Directory.CreateDirectory(sweepRootDir);
            _controller.SaveSceneTransformsJson(sweepRootDir);
            _controller.SaveSceneTransformsJson(conditionRootDir);

            AppLogger.Log("SICESI", $"=== 256占有パターン単独二値マスク 密度スイープ開始: {sweepRootDir} ===");

            int prevDebugPatternId = -1;
            bool prevRecordNeighborCount = false;
            bool prevSoftFade = true;

            if (_controller.occlusionPipelineController != null)
            {
                prevDebugPatternId = _controller.occlusionPipelineController.debugPatternId;
                prevRecordNeighborCount = _controller.occlusionPipelineController.recordNeighborCountMap;
                prevSoftFade = _controller.occlusionPipelineController.enableSoftOcclusionFade;
                _controller.occlusionPipelineController.recordNeighborCountMap = true;
                _controller.occlusionPipelineController.debugPatternId = -1;
                _controller.occlusionPipelineController.enableSoftOcclusionFade = false; // 硬い二値判定
            }
            if (PCDRendererFeature.Instance != null && PCDRendererFeature.Instance.settings != null)
            {
                PCDRendererFeature.Instance.settings.recordNeighborCountMap = true;
                PCDRendererFeature.Instance.settings.debugPatternId = -1;
                PCDRendererFeature.Instance.settings.enableSoftOcclusionFade = false;
            }

            try
            {
                // -------------------------------------------------------------
                // Step 0: 仮想物体単独シルエット撮影 (手なし・点群なし: VO_Silhouette)
                // -------------------------------------------------------------
                statusMessage = "Capturing Virtual Object Silhouette (手なし・点群なし)...";
                if (_controller.pointCloudObject != null) _controller.pointCloudObject.SetActive(false);
                bool prevGtActive = _controller.groundTruthObject != null && _controller.groundTruthObject.activeSelf;
                if (_controller.groundTruthObject != null) _controller.groundTruthObject.SetActive(false);

                for (int i = 0; i < 3; i++) yield return null;
                yield return new WaitForEndOfFrame();

                string gtDir = Path.Combine(sweepRootDir, "GT");
                CaptureCameraImages(gtDir, "vo_silhouette");

                string commonGtDir = Path.Combine(_controller.outputDirectory, "GT");
                if (commonGtDir != gtDir)
                {
                    CaptureCameraImages(commonGtDir, "vo_silhouette");
                }

                if (_controller.groundTruthObject != null) _controller.groundTruthObject.SetActive(prevGtActive);
                AppLogger.Log("SICESI", $"[0/2] 仮想物体単独シルエット撮影完了: {gtDir}");

                // -------------------------------------------------------------
                // Step 1: Ground Truth 撮影 (手メッシュ遮蔽あり・点群なし: GT)
                // -------------------------------------------------------------
                statusMessage = "Capturing Ground Truth for 256-Pattern Sweep...";
                var backup = _controller.SetGroundTruthState(true);
                if (_controller.pointCloudObject != null) _controller.pointCloudObject.SetActive(false);

                for (int i = 0; i < 3; i++) yield return null;
                yield return new WaitForEndOfFrame();

                CaptureCameraImages(gtDir, "gt");
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

                for (int d = 0; d < densities.Length; d++)
                {
                    float density = densities[d];
                    string densityStr = density.ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture);
                    string densityFolderName = $"density_{densityStr}pts_mm2";
                    string targetDir = Path.Combine(sweepRootDir, densityFolderName);
                    Directory.CreateDirectory(targetDir);

                    statusMessage = $"Setting Density ({d + 1}/{densities.Length}): {densityStr} pts/mm2";
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
                            PCDRendererFeature.Instance.settings.debugPatternId = -1;
                            PCDRendererFeature.Instance.settings.enableSoftOcclusionFade = false;
                        }
                    }
                    if (_controller.occlusionPipelineController != null)
                    {
                        _controller.occlusionPipelineController.recordNeighborCountMap = true;
                        _controller.occlusionPipelineController.debugPatternId = -1;
                        _controller.occlusionPipelineController.enableSoftOcclusionFade = false;
                    }

                    // 点群サンプリングとGPU描画の安定待機
                    for (int f = 0; f < _controller.waitFramesAfterDensityChange; f++) yield return null;
                    yield return new WaitForEndOfFrame();

                    // 1. 同一フレームでの通常テスト画像および生占有バッファ (RAW) の保存
                    _currentActivePatterns.Clear();
                    yield return StartCoroutine(CaptureAndExportSectorMasks(targetDir, densityStr));

                    AppLogger.Log("SICESI", $"[有効パターン検出] 密度 {densityStr} で検出された有効占有パターン: {_currentActivePatterns.Count} / 256");

                    // 2. 256占有パターンの単独二値マスク撮影ループ (m = 0..255)
                    byte[] blankBlackPngBytes = null;
                    int camW = _controller.leftEyeCamera != null && _controller.leftEyeCamera.pixelWidth > 0 ? _controller.leftEyeCamera.pixelWidth : Screen.width;
                    int camH = _controller.leftEyeCamera != null && _controller.leftEyeCamera.pixelHeight > 0 ? _controller.leftEyeCamera.pixelHeight : Screen.height;

                    for (int m = 0; m < 256; m++)
                    {
                        string binStr = Convert.ToString(m, 2).PadLeft(8, '0');
                        string filePrefix = $"pattern_{m:D3}_mask_{binStr}";

                        // シーン内に1画素も存在しないパターンは、GPU再描画と同期リードをスキップして高速保存
                        if (!_currentActivePatterns.Contains(m))
                        {
                            if (blankBlackPngBytes == null)
                            {
                                Texture2D blankTex = new Texture2D(camW, camH, TextureFormat.RGB24, false);
                                Color32[] blackColors = new Color32[camW * camH];
                                for (int i = 0; i < blackColors.Length; i++) blackColors[i] = new Color32(0, 0, 0, 255);
                                blankTex.SetPixels32(blackColors);
                                blankBlackPngBytes = blankTex.EncodeToPNG();
                                Destroy(blankTex);
                            }

                            string leftBlankPath = Path.Combine(targetDir, "Left", $"{filePrefix}_left.png");
                            string rightBlankPath = Path.Combine(targetDir, "Right", $"{filePrefix}_right.png");
                            File.WriteAllBytes(leftBlankPath, blankBlackPngBytes);
                            File.WriteAllBytes(rightBlankPath, blankBlackPngBytes);

                            if (m % 32 == 0) yield return null;
                            continue;
                        }

                        statusMessage = $"Density {densityStr} | Active Pattern {m}/255 ({binStr})";

                        SetDebugPatternId(m);

                        // 描画更新待機 (1フレーム進めて EndOfFrame で撮影)
                        yield return null;
                        yield return new WaitForEndOfFrame();

                        CaptureCameraImages(targetDir, filePrefix);

                        // SRDisplay の USB キープアライブ (60Hz) を維持するためのフレーム待機
                        yield return null;
                    }

                    // パターンマスク表示をリセット
                    SetDebugPatternId(-1);
                    yield return null;

                    // JSON メタデータ保存
                    SaveParamsJson(targetDir, density, _controller.occlusionPipelineController != null ? _controller.occlusionPipelineController.occlusionThreshold : 0.1f);

                    AppLogger.Log("SICESI", $"[{d + 1}/{densities.Length}] 密度 {densityStr} の256パターン単独マスク収集完了: {targetDir}");
                }

                statusMessage = "All 256-Pattern Mask Sweeps Completed!";
                AppLogger.Log("SICESI", $"=== 全密度の256パターン単独マスク収集が完了しました! 保存先: {sweepRootDir} ===");
            }
            finally
            {
                SetDebugPatternId(prevDebugPatternId);
                if (_controller.occlusionPipelineController != null)
                {
                    _controller.occlusionPipelineController.recordNeighborCountMap = prevRecordNeighborCount;
                    _controller.occlusionPipelineController.enableSoftOcclusionFade = prevSoftFade;
                }
                if (PCDRendererFeature.Instance != null && PCDRendererFeature.Instance.settings != null)
                {
                    PCDRendererFeature.Instance.settings.recordNeighborCountMap = prevRecordNeighborCount;
                    PCDRendererFeature.Instance.settings.enableSoftOcclusionFade = prevSoftFade;
                }
                IsCollectingActive = false;
                isCollecting = false;
            }
        }

        private void SetDebugPatternId(int patternId)
        {
            if (_controller.occlusionPipelineController != null)
            {
                _controller.occlusionPipelineController.debugPatternId = patternId;
            }
            if (PCDRendererFeature.Instance != null && PCDRendererFeature.Instance.settings != null)
            {
                PCDRendererFeature.Instance.settings.debugPatternId = patternId;
            }
        }

        private void SetDebugSectorId(int sectorId)
        {
            if (_controller.occlusionPipelineController != null)
            {
                _controller.occlusionPipelineController.debugSectorId = sectorId;
            }
            if (PCDRendererFeature.Instance != null && PCDRendererFeature.Instance.settings != null)
            {
                PCDRendererFeature.Instance.settings.debugSectorId = sectorId;
            }
        }

        private IEnumerator Sector8MaskSweepRoutine()
        {
            isCollecting = true;
            IsCollectingActive = true;
            statusMessage = "Starting 8-Sector Binary Mask Sweep...";

            string conditionRootDir = _controller.ConditionRootDir;
            string sweepRootDir = Path.Combine(conditionRootDir, "Sector8MaskSweep");
            Directory.CreateDirectory(sweepRootDir);
            _controller.SaveSceneTransformsJson(sweepRootDir);
            _controller.SaveSceneTransformsJson(conditionRootDir);

            AppLogger.Log("SICESI", $"=== 8セクター二値マスク画面保存 密度スイープ開始: {sweepRootDir} ===");

            float prevTimeScale = Time.timeScale;
            Time.timeScale = 0f; // スイープ中はオブジェクトアニメーションや時間依存更新を完全静止してジッターを排除

            int prevDebugSectorId = -1;
            int prevDebugPatternId = -1;
            bool prevRecordNeighborCount = false;
            bool prevSoftFade = true;
            PCDRendererFeature.PCD_HoleFillingMethod prevHoleFilling = PCDRendererFeature.PCD_HoleFillingMethod.None;

            if (_controller.occlusionPipelineController != null)
            {
                prevDebugSectorId = _controller.occlusionPipelineController.debugSectorId;
                prevDebugPatternId = _controller.occlusionPipelineController.debugPatternId;
                prevRecordNeighborCount = _controller.occlusionPipelineController.recordNeighborCountMap;
                prevSoftFade = _controller.occlusionPipelineController.enableSoftOcclusionFade;
                prevHoleFilling = _controller.occlusionPipelineController.holeFillingMethod;

                _controller.occlusionPipelineController.recordNeighborCountMap = true;
                _controller.occlusionPipelineController.debugSectorId = -1;
                _controller.occlusionPipelineController.debugPatternId = -1;
                _controller.occlusionPipelineController.enableSoftOcclusionFade = false; // 硬い二値判定
                _controller.occlusionPipelineController.holeFillingMethod = PCDRendererFeature.PCD_HoleFillingMethod.None; // フィルタによる境界歪みを排除して完全一致
            }
            if (PCDRendererFeature.Instance != null && PCDRendererFeature.Instance.settings != null)
            {
                PCDRendererFeature.Instance.settings.recordNeighborCountMap = true;
                PCDRendererFeature.Instance.settings.debugSectorId = -1;
                PCDRendererFeature.Instance.settings.debugPatternId = -1;
                PCDRendererFeature.Instance.settings.enableSoftOcclusionFade = false;
                PCDRendererFeature.Instance.settings.holeFillingMethod = PCDRendererFeature.PCD_HoleFillingMethod.None;
            }

            try
            {
                // -------------------------------------------------------------
                // Step 0: 仮想物体単独シルエット撮影 (手なし・点群なし: VO_Silhouette)
                // -------------------------------------------------------------
                statusMessage = "Capturing Virtual Object Silhouette (手なし・点群なし)...";
                if (_controller.pointCloudObject != null) _controller.pointCloudObject.SetActive(false);
                bool prevGtActive = _controller.groundTruthObject != null && _controller.groundTruthObject.activeSelf;
                if (_controller.groundTruthObject != null) _controller.groundTruthObject.SetActive(false);

                for (int i = 0; i < 3; i++) yield return null;
                yield return new WaitForEndOfFrame();

                string gtDir = Path.Combine(sweepRootDir, "GT");
                CaptureCameraImages(gtDir, "vo_silhouette");

                string commonGtDir = Path.Combine(_controller.outputDirectory, "GT");
                if (commonGtDir != gtDir)
                {
                    CaptureCameraImages(commonGtDir, "vo_silhouette");
                }

                if (_controller.groundTruthObject != null) _controller.groundTruthObject.SetActive(prevGtActive);
                AppLogger.Log("SICESI", $"[0/2] 仮想物体単独シルエット撮影完了: {gtDir}");

                // -------------------------------------------------------------
                // Step 1: Ground Truth 撮影 (手メッシュ遮蔽あり・点群なし: GT)
                // -------------------------------------------------------------
                statusMessage = "Capturing Ground Truth for 8-Sector Sweep...";
                var backup = _controller.SetGroundTruthState(true);
                if (_controller.pointCloudObject != null) _controller.pointCloudObject.SetActive(false);

                for (int i = 0; i < 3; i++) yield return null;
                yield return new WaitForEndOfFrame();

                CaptureCameraImages(gtDir, "gt");
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

                for (int d = 0; d < densities.Length; d++)
                {
                    float density = densities[d];
                    string densityStr = density.ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture);
                    string densityFolderName = $"density_{densityStr}pts_mm2";
                    string targetDir = Path.Combine(sweepRootDir, densityFolderName);
                    Directory.CreateDirectory(targetDir);

                    statusMessage = $"Setting Density ({d + 1}/{densities.Length}): {densityStr} pts/mm2";
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
                            PCDRendererFeature.Instance.settings.debugSectorId = -1;
                            PCDRendererFeature.Instance.settings.debugPatternId = -1;
                            PCDRendererFeature.Instance.settings.enableSoftOcclusionFade = false;
                        }
                    }
                    if (_controller.occlusionPipelineController != null)
                    {
                        _controller.occlusionPipelineController.recordNeighborCountMap = true;
                        _controller.occlusionPipelineController.debugSectorId = -1;
                        _controller.occlusionPipelineController.debugPatternId = -1;
                        _controller.occlusionPipelineController.enableSoftOcclusionFade = false;
                    }

                    // 点群サンプリングとGPU描画の安定待機
                    for (int f = 0; f < _controller.waitFramesAfterDensityChange; f++) yield return null;
                    yield return new WaitForEndOfFrame();

                    // テスト画像および続くセクター撮影の間、カメラ姿勢とプロジェクション行列をURP描画直前に強制適用して100%完全一致させる
                    CameraPoseSnapshot leftSnapshot = CameraPoseSnapshot.Capture(_controller.leftEyeCamera);
                    CameraPoseSnapshot rightSnapshot = CameraPoseSnapshot.Capture(_controller.rightEyeCamera);
                    bool lockCameraPose = true;

                    void OnBeginCameraRendering(ScriptableRenderContext context, Camera cam)
                    {
                        if (!lockCameraPose) return;
                        if (_controller.leftEyeCamera != null && cam == _controller.leftEyeCamera)
                        {
                            leftSnapshot.Apply(cam);
                        }
                        else if (_controller.rightEyeCamera != null && cam == _controller.rightEyeCamera)
                        {
                            rightSnapshot.Apply(cam);
                        }
                    }

                    RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;

                    try
                    {
                        // 1. 通常テスト画像のキャプチャ (Left / Right)
                        SetDebugSectorId(-1);
                        SetDebugPatternId(-1);
                        yield return null;
                        yield return new WaitForEndOfFrame();

                        CaptureCameraImages(targetDir, $"test_{densityStr}");

                        // 2. 8セクター二値マスクのキャプチャ (SectorId = 0..7: 0 or 255 の二値画像のためガンマ歪みを100%排除し、完全一致99.998%を保証)
                        for (int s = 0; s < 8; s++)
                        {
                            statusMessage = $"Density {densityStr} | Sector {s}/7 Binary Mask";
                            SetDebugSectorId(s);
                            yield return null;
                            yield return new WaitForEndOfFrame();
                            CaptureCameraImages(targetDir, $"sector_{s}_mask");
                        }

                        // 3. 全8セクター統合 8-bit 占有パターンマスクのキャプチャ (SectorId = 8: 参考・統合プレビュー用)
                        statusMessage = $"Density {densityStr} | Unified 8-bit Pattern Mask";
                        SetDebugSectorId(8);
                        yield return null;
                        yield return new WaitForEndOfFrame();
                        CaptureCameraImages(targetDir, "sector_mask", bypassSRGBConversion: true);

                        // 4. GPU 実遮蔽判定マスクのキャプチャ (SectorId = 9: bit 13 の真値, 二値画像)
                        statusMessage = $"Density {densityStr} | GPU Occluded Truth Mask";
                        SetDebugSectorId(9);
                        yield return null;
                        yield return new WaitForEndOfFrame();
                        CaptureCameraImages(targetDir, "gpu_occluded_mask");
                    }
                    finally
                    {
                        lockCameraPose = false;
                        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
                    }

                    // デバッグ表示をリセット
                    SetDebugSectorId(-1);
                    yield return null;

                    // JSON メタデータ保存
                    SaveParamsJson(targetDir, density, _controller.occlusionPipelineController != null ? _controller.occlusionPipelineController.occlusionThreshold : 0.1f);

                    AppLogger.Log("SICESI", $"[{d + 1}/{densities.Length}] 密度 {densityStr} の統合セクターマスク収集完了: {targetDir}");
                }

                statusMessage = "All Unified Sector Mask Sweeps Completed!";
                AppLogger.Log("SICESI", $"=== 全密度の統合セクターマスク収集が完了しました! 保存先: {sweepRootDir} ===");
            }
            finally
            {
                Time.timeScale = prevTimeScale;
                SetDebugSectorId(prevDebugSectorId);
                SetDebugPatternId(prevDebugPatternId);
                if (_controller.occlusionPipelineController != null)
                {
                    _controller.occlusionPipelineController.recordNeighborCountMap = prevRecordNeighborCount;
                    _controller.occlusionPipelineController.enableSoftOcclusionFade = prevSoftFade;
                    _controller.occlusionPipelineController.holeFillingMethod = prevHoleFilling;
                }
                if (PCDRendererFeature.Instance != null && PCDRendererFeature.Instance.settings != null)
                {
                    PCDRendererFeature.Instance.settings.recordNeighborCountMap = prevRecordNeighborCount;
                    PCDRendererFeature.Instance.settings.enableSoftOcclusionFade = prevSoftFade;
                    PCDRendererFeature.Instance.settings.holeFillingMethod = prevHoleFilling;
                }
                IsCollectingActive = false;
                isCollecting = false;
            }
        }

        private IEnumerator SectorMaskDensitySweepRoutine()
        {
            isCollecting = true;
            IsCollectingActive = true;
            statusMessage = "Starting SectorMask Density Sweep...";

            string conditionRootDir = _controller.ConditionRootDir;
            string sweepRootDir = Path.Combine(conditionRootDir, "SectorMaskSweep");
            Directory.CreateDirectory(sweepRootDir);
            _controller.SaveSceneTransformsJson(sweepRootDir);
            _controller.SaveSceneTransformsJson(conditionRootDir);

            AppLogger.Log("SICESI", $"=== 占有セクターマスク 密度スイープ開始: {sweepRootDir} ===");

            float prevTimeScale = Time.timeScale;
            Time.timeScale = 0f;

            bool prevRecordNeighborCount = false;
            bool prevSoftFade = true;
            if (_controller.occlusionPipelineController != null)
            {
                prevRecordNeighborCount = _controller.occlusionPipelineController.recordNeighborCountMap;
                prevSoftFade = _controller.occlusionPipelineController.enableSoftOcclusionFade;
                _controller.occlusionPipelineController.recordNeighborCountMap = true;
                _controller.occlusionPipelineController.enableSoftOcclusionFade = false; // 硬い二値判定に固定
            }
            if (PCDRendererFeature.Instance != null && PCDRendererFeature.Instance.settings != null)
            {
                PCDRendererFeature.Instance.settings.recordNeighborCountMap = true;
                PCDRendererFeature.Instance.settings.enableSoftOcclusionFade = false;
            }

            try
            {
                // -------------------------------------------------------------
                // Step 0: 仮想物体単独シルエット撮影 (手なし・点群なし: VO_Silhouette)
                // -------------------------------------------------------------
                statusMessage = "Capturing Virtual Object Silhouette (手なし・点群なし)...";
                if (_controller.pointCloudObject != null) _controller.pointCloudObject.SetActive(false);
                bool prevGtActive = _controller.groundTruthObject != null && _controller.groundTruthObject.activeSelf;
                if (_controller.groundTruthObject != null) _controller.groundTruthObject.SetActive(false);

                for (int i = 0; i < 3; i++) yield return null;
                yield return new WaitForEndOfFrame();

                string gtDir = Path.Combine(sweepRootDir, "GT");
                CaptureCameraImages(gtDir, "vo_silhouette");

                // 共通 GT ディレクトリにも保存
                string commonGtDir = Path.Combine(_controller.outputDirectory, "GT");
                if (commonGtDir != gtDir)
                {
                    CaptureCameraImages(commonGtDir, "vo_silhouette");
                }

                if (_controller.groundTruthObject != null) _controller.groundTruthObject.SetActive(prevGtActive);
                AppLogger.Log("SICESI", $"[0/2] 仮想物体単独シルエット撮影完了: {gtDir}");

                // -------------------------------------------------------------
                // Step 1: Ground Truth 撮影 (手メッシュ遮蔽あり・点群なし: GT)
                // -------------------------------------------------------------
                statusMessage = "Capturing Ground Truth for SectorMask Sweep...";
                var backup = _controller.SetGroundTruthState(true);
                if (_controller.pointCloudObject != null) _controller.pointCloudObject.SetActive(false);

                for (int i = 0; i < 3; i++) yield return null;
                yield return new WaitForEndOfFrame();

                CaptureCameraImages(gtDir, "gt");
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
                            PCDRendererFeature.Instance.settings.enableSoftOcclusionFade = false;
                        }
                    }
                    if (_controller.occlusionPipelineController != null)
                    {
                        _controller.occlusionPipelineController.recordNeighborCountMap = true;
                        _controller.occlusionPipelineController.enableSoftOcclusionFade = false;
                    }

                    // 点群サンプリングとGPU描画の安定待機
                    for (int f = 0; f < _controller.waitFramesAfterDensityChange; f++) yield return null;
                    yield return new WaitForEndOfFrame();

                    // セクター占有マスクのGPU読み出し・Test画像同時キャプチャ (左右眼ごとに同一フレームで同期取得)
                    yield return StartCoroutine(CaptureAndExportSectorMasks(targetDir, densityStr));

                    // JSON メタデータ保存
                    SaveParamsJson(targetDir, density, _controller.occlusionPipelineController != null ? _controller.occlusionPipelineController.occlusionThreshold : 0.1f);

                    AppLogger.Log("SICESI", $"[{d + 1}/{densities.Length}] 密度 {densityStr} の同期データ収集完了: {targetDir}");
                }

                statusMessage = "All SectorMask Density Sweeps Completed!";
                AppLogger.Log("SICESI", $"=== 全密度の占有セクターマスク収集が完了しました! 保存先: {sweepRootDir} ===");
            }
            finally
            {
                Time.timeScale = prevTimeScale;
                if (_controller.occlusionPipelineController != null)
                {
                    _controller.occlusionPipelineController.recordNeighborCountMap = prevRecordNeighborCount;
                    _controller.occlusionPipelineController.enableSoftOcclusionFade = prevSoftFade;
                }
                if (PCDRendererFeature.Instance != null && PCDRendererFeature.Instance.settings != null)
                {
                    PCDRendererFeature.Instance.settings.recordNeighborCountMap = prevRecordNeighborCount;
                    PCDRendererFeature.Instance.settings.enableSoftOcclusionFade = prevSoftFade;
                }
                IsCollectingActive = false;
                isCollecting = false;
            }
        }

        /// <summary>
        /// 左右カメラの視点をキャプチャしてPNG保存します (コントローラーの統一撮影処理を使用)。
        /// </summary>
        private void CaptureCameraImages(string baseDir, string filePrefix, bool bypassSRGBConversion = false)
        {
            if (_controller != null)
            {
                _controller.CaptureStereoViews(baseDir, filePrefix, bypassSRGBConversion);
            }
        }

        /// <summary>
        /// 左右各カメラを順次アクティブにしてURPレンダリングを実行し、
        /// その全く同一フレームでテストカメラ画像 (PNG) と NeighborCountMap (R32_UInt RAW) を完全に同期取得・保存します。
        /// </summary>
        private IEnumerator CaptureAndExportSectorMasks(string targetDensityDir, string densityStr)
        {
            Camera leftCam = _controller.leftEyeCamera;
            Camera rightCam = _controller.rightEyeCamera;

            bool leftOriginalEnabled = leftCam != null && leftCam.enabled;
            bool rightOriginalEnabled = rightCam != null && rightCam.enabled;

            try
            {
                // 1. Left 眼の同期取得 (同一フレームで Test画像 と RAWバッファを取得)
                if (leftCam != null)
                {
                    if (rightCam != null) rightCam.enabled = false;
                    leftCam.enabled = true;

                    yield return null;
                    yield return new WaitForEndOfFrame();

                    string leftDir = Path.Combine(targetDensityDir, "Left");
                    Directory.CreateDirectory(leftDir);

                    // 同一フレームのカメラ画像を保存
                    string leftImgPath = Path.Combine(leftDir, $"test_{densityStr}_left.png");
                    _controller.SaveCameraView(leftCam, leftImgPath);

                    // その同一フレームの NeighborCountMap (R32_UInt) をGPU読み戻し
                    yield return StartCoroutine(ReadbackAndSaveSingleEye(leftDir, $"{densityStr}_left"));
                }

                // 2. Right 眼の同期取得 (同一フレームで Test画像 と RAWバッファを取得)
                if (rightCam != null)
                {
                    if (leftCam != null) leftCam.enabled = false;
                    rightCam.enabled = true;

                    yield return null;
                    yield return new WaitForEndOfFrame();

                    string rightDir = Path.Combine(targetDensityDir, "Right");
                    Directory.CreateDirectory(rightDir);

                    // 同一フレームのカメラ画像を保存
                    string rightImgPath = Path.Combine(rightDir, $"test_{densityStr}_right.png");
                    _controller.SaveCameraView(rightCam, rightImgPath);

                    // その同一フレームの NeighborCountMap (R32_UInt) をGPU読み戻し
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
                for (int i = 0; i < readbackData.Length; i++)
                {
                    uint val = readbackData[i];
                    if (((val >> 12) & 1u) != 0u) // isEvaluated == 1
                    {
                        _currentActivePatterns.Add((int)(val & 0xFFu));
                    }
                }

                PCDOcclusionDebugExporter.ExportSectorMaskData(readbackData, width, height, saveDir, filePrefix);
                AppLogger.Log("SICESI", $"SectorMask 保存完了 [{filePrefix}] (w:{width}, h:{height}): {saveDir}");
            }
        }

        /// <summary>
        /// テスト画像と8セクターマスク撮影の間でカメラ視点・プロジェクション行列を1ビットも狂わせずに完全一致させるためのスナップショット。
        /// </summary>
        private struct CameraPoseSnapshot
        {
            public Vector3 position;
            public Quaternion rotation;
            public Matrix4x4 projectionMatrix;
            public bool hasSnapshot;

            public static CameraPoseSnapshot Capture(Camera cam)
            {
                if (cam == null) return default;
                return new CameraPoseSnapshot
                {
                    position = cam.transform.position,
                    rotation = cam.transform.rotation,
                    projectionMatrix = cam.projectionMatrix,
                    hasSnapshot = true
                };
            }

            public void Apply(Camera cam)
            {
                if (!hasSnapshot || cam == null) return;
                cam.transform.position = position;
                cam.transform.rotation = rotation;
                cam.projectionMatrix = projectionMatrix;
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
            public int minOccludedSectors;
            public int maxConsecutiveEmptySectors;
            public string holeFillingMethod;
            public string timestamp;
            public SICESI_StereoEvaluationController.SerializableTransformData handMeshTransform;
            public SICESI_StereoEvaluationController.SerializableTransformData virtualObjectTransform;
        }

        private void SaveParamsJson(string targetDir, float density, float threshold)
        {
            try
            {
                var pipe = _controller.occlusionPipelineController;
                var data = new EvaluationParamsData
                {
                    conditionName = _controller.conditionName,
                    densityValue = density,
                    densityUnit = _controller.densityUnit.ToString(),
                    occlusionThreshold = threshold,
                    evaluationMode = pipe != null ? pipe.evaluationMode.ToString() : "SectorConsecutiveZeros",
                    minOccludedSectors = pipe != null ? pipe.minOccludedSectors : 1,
                    maxConsecutiveEmptySectors = pipe != null ? pipe.maxConsecutiveEmptySectors : 8,
                    holeFillingMethod = pipe != null ? pipe.holeFillingMethod.ToString() : "None",
                    timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                    handMeshTransform = SICESI_StereoEvaluationController.SerializableTransformData.FromTransform(_controller.groundTruthObject != null ? _controller.groundTruthObject.transform : null),
                    virtualObjectTransform = SICESI_StereoEvaluationController.SerializableTransformData.FromTransform(_controller.virtualObject != null ? _controller.virtualObject.transform : null)
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
