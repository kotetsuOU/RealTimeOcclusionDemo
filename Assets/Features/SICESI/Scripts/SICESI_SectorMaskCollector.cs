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
        /// 8セクター二値マスク＋生データ直接保存スイープ。
        /// SRDisplay 表示補正前（Point A: NeighborCountMap / OriginTypeMap）を AsyncGPUReadback で同期取得し、
        /// 1フレームのリードバックから以下をすべて一括生成・保存します。
        ///   - A_raw_uint32.bin / origin_type_raw_uint32.bin (生バイナリ)
        ///   - evaluated_mask_pre_correction.png (bit 12: 評価領域 E)
        ///   - sector_occlusion_mask_direct.png (bit 13: セクター遮蔽)
        ///   - final_occlusion_mask_direct.png (bit 13 | D)
        ///   - origin_type_map_direct.png (最前面タグ)
        ///   - sector_mask.png (統合 8-bit パターン)
        ///   - sector_0_mask.png 〜 sector_7_mask.png (各セクター二値)
        /// 画面切り替え不要で1密度あたり1回のリードバックで完結します。
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
                // Step 0 & 1: Ground Truth 撮影 (GPU生深度直接比較 MeshDepthGT または従来カラー)
                // -------------------------------------------------------------
                string gtDir = Path.Combine(sweepRootDir, "GT");
                statusMessage = "Capturing Ground Truth for 256-Pattern Sweep...";
                yield return StartCoroutine(CaptureGroundTruthUnified(gtDir));
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
            statusMessage = "Starting Point A Raw Data Sweep...";

            string conditionRootDir = _controller.ConditionRootDir;
            string sweepRootDir = Path.Combine(conditionRootDir, "Sector8MaskSweep");
            Directory.CreateDirectory(sweepRootDir);
            _controller.SaveSceneTransformsJson(sweepRootDir);
            _controller.SaveSceneTransformsJson(conditionRootDir);

            AppLogger.Log("SICESI", $"=== Point A 生データ直接保存 密度スイープ開始: {sweepRootDir} ===");

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

            // スイープ全体の開始時にカメラ姿勢・投影行列をキャプチャし、GT撮影と全密度スイープを通じて完全固定
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
                // -------------------------------------------------------------
                // Step 0 & 1: Ground Truth 撮影 (GPU生深度直接比較 MeshDepthGT または従来カラー)
                // -------------------------------------------------------------
                string gtDir = Path.Combine(sweepRootDir, "GT");
                statusMessage = "Capturing Ground Truth for Point A Raw Data Sweep...";

                // PCDRendererFeature に最新の描画行列 (View/Projection) を確実にキャプチャさせるため待機
                for (int i = 0; i < 3; i++) yield return null;
                yield return new WaitForEndOfFrame();

                yield return StartCoroutine(CaptureGroundTruthUnified(gtDir));
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

                int minOccSectors = _controller.fixedMinOccludedSectors > 0 ? _controller.fixedMinOccludedSectors : 6;

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

                    // 確認用テスト画像は従来通り画面保存 (見た目確認用のみ)
                    SetDebugSectorId(-1);
                    SetDebugPatternId(-1);
                    yield return null;
                    yield return new WaitForEndOfFrame();

                    // Point A 生データ直接リードバック: 左目・右目をそれぞれ同期取得して保存
                    string leftDir = Path.Combine(targetDir, "Left");
                    string rightDir = Path.Combine(targetDir, "Right");
                    Directory.CreateDirectory(leftDir);
                    Directory.CreateDirectory(rightDir);

                    statusMessage = $"Density {densityStr} | Readback Left Eye Point A...";
                    yield return StartCoroutine(ReadbackAndSavePointA(leftDir, minOccSectors, "Left"));

                    statusMessage = $"Density {densityStr} | Readback Right Eye Point A...";
                    yield return StartCoroutine(ReadbackAndSavePointA(rightDir, minOccSectors, "Right"));

                    // JSON メタデータ保存
                    SaveParamsJson(targetDir, density, _controller.occlusionPipelineController != null ? _controller.occlusionPipelineController.occlusionThreshold : 0.1f);

                    AppLogger.Log("SICESI", $"[{d + 1}/{densities.Length}] 密度 {densityStr} の Point A 直接保存完了: {targetDir}");
                }

                statusMessage = "All Point A Raw Data Sweeps Completed!";
                AppLogger.Log("SICESI", $"=== 全密度の Point A 生データ直接保存が完了しました! 保存先: {sweepRootDir} ===");
            }
            finally
            {
                lockCameraPose = false;
                RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;

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

        /// <summary>
        /// NeighborCountMap (uint32) と OriginTypeMap (uint32) を AsyncGPUReadback で同期取得し、
        /// 評価マスク・遮蔽マスク・8セクター二値マスク・生バイナリをすべて直接生成・保存します。
        /// </summary>
        private IEnumerator ReadbackAndSavePointA(string saveDir, int minOccSectors, string eyeLabel)
        {
            if (PCDRendererFeature.Instance == null || PCDRendererFeature.Instance.CurrentResources == null)
            {
                AppLogger.LogError("SICESI", $"[{eyeLabel}] PCDRendererFeature.CurrentResources が利用できません。");
                yield break;
            }

            var resources = PCDRendererFeature.Instance.CurrentResources;
            RenderTexture rtA = resources.NeighborCountMap != null ? resources.NeighborCountMap.rt : null;
            RenderTexture rtOrigin = resources.OriginTypeMap != null ? resources.OriginTypeMap.rt : null;

            if (rtA == null)
            {
                AppLogger.LogError("SICESI", $"[{eyeLabel}] NeighborCountMap (rtA) が null です。");
                yield break;
            }

            int width = rtA.width;
            int height = rtA.height;
            int totalPixels = width * height;

            bool doneA = false;
            bool doneOrigin = (rtOrigin == null); // rtOrigin が null なら即完了扱い
            uint[] dataA = null;
            uint[] dataOrigin = null;

            // NeighborCountMap を非同期リードバック
            AsyncGPUReadback.Request(rtA, 0, req =>
            {
                if (!req.hasError) dataA = req.GetData<uint>().ToArray();
                doneA = true;
            });

            // OriginTypeMap を非同期リードバック
            if (rtOrigin != null)
            {
                AsyncGPUReadback.Request(rtOrigin, 0, req =>
                {
                    if (!req.hasError) dataOrigin = req.GetData<uint>().ToArray();
                    doneOrigin = true;
                });
            }

            float timeout = Time.realtimeSinceStartup + 5.0f;
            while ((!doneA || !doneOrigin) && Time.realtimeSinceStartup < timeout)
            {
                yield return null;
            }

            if (dataA == null)
            {
                AppLogger.LogError("SICESI", $"[{eyeLabel}] AsyncGPUReadback タイムアウトまたはエラー (dataA={dataA != null})。");
                yield break;
            }

            // --- バッファ解析 ---
            byte[] occDirectBytes  = new byte[totalPixels]; // bit 13: セクター遮蔽
            byte[] evaluatedBytes  = new byte[totalPixels]; // bit 12: 評価領域 E
            byte[] originTypeBytes = new byte[totalPixels]; // 最前面タグ
            byte[] finalOccBytes   = new byte[totalPixels]; // 最終遮蔽 = bit13 | D
            byte[] patternBytes    = new byte[totalPixels]; // 統合 8-bit 占有パターン (bit 0-7)
            byte[][] sectorBytes   = new byte[8][];         // 各セクター二値
            for (int s = 0; s < 8; s++) sectorBytes[s] = new byte[totalPixels];

            for (int y = 0; y < height; y++)
            {
                int rowOffset = y * width;
                for (int x = 0; x < width; x++)
                {
                    int idx = rowOffset + x;
                    uint val = dataA[idx];

                    uint isEvaluated = (val >> 12) & 1u;
                    uint pattern     = val & 0xFFu;
                    uint bit13Occ    = (val >> 13) & 1u;
                    uint origin      = (dataOrigin != null && idx < dataOrigin.Length) ? dataOrigin[idx] : 1u;

                    occDirectBytes[idx]  = bit13Occ != 0u ? (byte)255 : (byte)0;
                    evaluatedBytes[idx]  = isEvaluated != 0u ? (byte)255 : (byte)0;
                    originTypeBytes[idx] = (byte)(origin == 0u ? 0 : (origin == 1u ? 128 : 255));
                    patternBytes[idx]    = (byte)pattern;

                    // 最終遮蔽: セクター判定遮蔽 (bit13) OR 手前の物理点群による直接遮蔽 (origin==0 && isEvaluated==0)
                    bool isDirectPointOcc = (origin == 0u && isEvaluated == 0u);
                    finalOccBytes[idx] = (bit13Occ != 0u || isDirectPointOcc) ? (byte)255 : (byte)0;

                    // 各セクター二値マスク (bit s が立っているかどうか)
                    for (int s = 0; s < 8; s++)
                    {
                        sectorBytes[s][idx] = ((pattern >> s) & 1u) != 0u ? (byte)255 : (byte)0;
                    }
                }
            }

            // --- PNG 保存 ---
            Directory.CreateDirectory(saveDir);
            SaveGrayscalePNG(occDirectBytes,  width, height, Path.Combine(saveDir, "sector_occlusion_mask_direct.png"));
            SaveGrayscalePNG(occDirectBytes,  width, height, Path.Combine(saveDir, "A_raw_gpu_mask_direct.png"));
            SaveGrayscalePNG(finalOccBytes,   width, height, Path.Combine(saveDir, "final_occlusion_mask_direct.png"));
            SaveGrayscalePNG(evaluatedBytes,  width, height, Path.Combine(saveDir, "evaluated_mask_pre_correction.png"));
            SaveGrayscalePNG(originTypeBytes, width, height, Path.Combine(saveDir, "origin_type_map_direct.png"));
            SaveGrayscalePNG(patternBytes,    width, height, Path.Combine(saveDir, "sector_mask.png"));

            for (int s = 0; s < 8; s++)
            {
                SaveGrayscalePNG(sectorBytes[s], width, height, Path.Combine(saveDir, $"sector_{s}_mask.png"));
            }

            // --- 生バイナリ保存 ---
            byte[] rawBytes = new byte[totalPixels * 4];
            Buffer.BlockCopy(dataA, 0, rawBytes, 0, rawBytes.Length);
            File.WriteAllBytes(Path.Combine(saveDir, "A_raw_uint32.bin"), rawBytes);

            if (dataOrigin != null)
            {
                byte[] rawOriginBytes = new byte[totalPixels * 4];
                Buffer.BlockCopy(dataOrigin, 0, rawOriginBytes, 0, rawOriginBytes.Length);
                File.WriteAllBytes(Path.Combine(saveDir, "origin_type_raw_uint32.bin"), rawOriginBytes);
            }

            AppLogger.Log("SICESI", $"[{eyeLabel}] Point A 直接保存完了 (w:{width}, h:{height}): {saveDir}");
        }

        /// <summary>
        /// グレースケール PNG を生成して保存します。入力バイト配列は R8 フォーマットで 1 画素 1 バイトです。
        /// </summary>
        private static void SaveGrayscalePNG(byte[] bytes, int width, int height, string path)
        {
            Texture2D tex = new Texture2D(width, height, TextureFormat.R8, false);
            tex.SetPixelData(bytes, 0);
            tex.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
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
                // Step 0 & 1: Ground Truth 撮影 (GPU生深度直接比較 MeshDepthGT または従来カラー)
                // -------------------------------------------------------------
                string gtDir = Path.Combine(sweepRootDir, "GT");
                statusMessage = "Capturing Ground Truth for SectorMask Sweep...";
                yield return StartCoroutine(CaptureGroundTruthUnified(gtDir));
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
        /// 仮想物体シルエットおよび Ground Truth マスクを撮影・生成します。
        /// _controller.useMeshDepthGT が true の場合は、GPU生深度マップ直接比較 (MeshDepthGT) を行い、
        /// カラー描画時の境界ブレ・アンチエイリアシング誤差のない真のGTマスクを生成します。
        /// </summary>
        private IEnumerator CaptureGroundTruthUnified(string gtDir)
        {
            Directory.CreateDirectory(gtDir);
            string commonGtDir = Path.Combine(_controller.outputDirectory, "GT");

            if (_controller != null && _controller.useMeshDepthGT)
            {
                var manager = _controller.GetComponent<SICESI_MeshDepthGTManager>();
                if (manager == null) manager = _controller.gameObject.AddComponent<SICESI_MeshDepthGTManager>();

                if (_controller.leftEyeCamera != null)
                {
                    yield return StartCoroutine(manager.GenerateMeshDepthGTRoutine(
                        _controller.leftEyeCamera, "Left", gtDir,
                        _controller.virtualObject, _controller.groundTruthObject, _controller.pointCloudObject,
                        null, _controller.groundTruthCaptureLayer));
                }
                if (_controller.rightEyeCamera != null)
                {
                    yield return StartCoroutine(manager.GenerateMeshDepthGTRoutine(
                        _controller.rightEyeCamera, "Right", gtDir,
                        _controller.virtualObject, _controller.groundTruthObject, _controller.pointCloudObject,
                        null, _controller.groundTruthCaptureLayer));
                }

                if (commonGtDir != gtDir)
                {
                    Directory.CreateDirectory(commonGtDir);
                    foreach (var file in Directory.GetFiles(gtDir, "*.png"))
                    {
                        string dst = Path.Combine(commonGtDir, Path.GetFileName(file));
                        File.Copy(file, dst, true);
                    }
                }
            }
            else
            {
                // 従来のカラーラスタライズフォールバック
                if (_controller.pointCloudObject != null) _controller.pointCloudObject.SetActive(false);
                bool prevGtActive = _controller.groundTruthObject != null && _controller.groundTruthObject.activeSelf;
                if (_controller.groundTruthObject != null) _controller.groundTruthObject.SetActive(false);

                for (int i = 0; i < 3; i++) yield return null;
                yield return new WaitForEndOfFrame();

                CaptureCameraImages(gtDir, "vo_silhouette");
                if (commonGtDir != gtDir) CaptureCameraImages(commonGtDir, "vo_silhouette");

                if (_controller.groundTruthObject != null) _controller.groundTruthObject.SetActive(prevGtActive);

                var backup = _controller.SetGroundTruthState(true);
                for (int i = 0; i < 3; i++) yield return null;
                yield return new WaitForEndOfFrame();

                CaptureCameraImages(gtDir, "gt");
                if (commonGtDir != gtDir) CaptureCameraImages(commonGtDir, "gt");

                _controller.RestoreGroundTruthState(backup);
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
