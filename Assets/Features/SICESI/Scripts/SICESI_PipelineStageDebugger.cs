using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Core.Logging;
using SRD.Core;

namespace SICESI
{
    /// <summary>
    /// パイプライン各ステージにおける中間値・不一致発生地点の厳密特定および
    /// 左目単独排他制御による内部整合性・同一条件ペア撮影（通常 vs 白一色）を統括するデバッグマネージャー。
    /// </summary>
    [DisallowMultipleComponent]
    [AppLoggable("SICESI_StageDebug")]
    public class SICESI_PipelineStageDebugger : MonoBehaviour, IAppLoggable
    {
        [Header("References")]
        public SICESI_StereoEvaluationController controller;
        public PCDOcclusionPipelineController occlusionPipelineController;

        [Header("Diagnostic Settings")]
        [Tooltip("診断データの保存先サブフォルダ名")]
        public string stageDebugSubDir = "StageDiagnosis";

        private Material _unlitWhiteMaterial;
        private readonly Dictionary<Renderer, Material[]> _cachedOriginalMaterials = new Dictionary<Renderer, Material[]>();

        public void RegisterLogTriggers(LogCategoryGroup group, HashSet<string> existingLabels)
        {
            // AppLogger 登録用
        }

        private void Awake()
        {
            if (controller == null) controller = GetComponent<SICESI_StereoEvaluationController>();
            if (occlusionPipelineController == null && controller != null) occlusionPipelineController = controller.occlusionPipelineController;
        }

        private void OnDestroy()
        {
            if (_unlitWhiteMaterial != null)
            {
                Destroy(_unlitWhiteMaterial);
                _unlitWhiteMaterial = null;
            }
        }

        /// <summary>
        /// 左目単独排他制御のもとで全診断を一括実行します。
        /// </summary>
        public void RunFullDiagnosis(Action<bool, string> onComplete = null)
        {
            if (controller == null)
            {
                AppLogger.LogError("SICESI_StageDebug", "SICESI_StereoEvaluationController が未設定です。");
                onComplete?.Invoke(false, "Controller is null");
                return;
            }

            StartCoroutine(FullDiagnosisRoutine(onComplete));
        }

        private IEnumerator FullDiagnosisRoutine(Action<bool, string> onComplete)
        {
            string saveDir = Path.Combine(controller.ConditionRootDir, stageDebugSubDir);
            Directory.CreateDirectory(saveDir);

            Camera leftCam = controller.leftEyeCamera;
            Camera rightCam = controller.rightEyeCamera;

            if (leftCam == null)
            {
                AppLogger.LogError("SICESI_StageDebug", "LeftEyeCamera が見つかりません。");
                onComplete?.Invoke(false, "LeftEyeCamera is null");
                yield break;
            }

            bool leftOrig = leftCam.enabled;
            bool rightOrig = rightCam != null && rightCam.enabled;
            bool pcOrig = controller.pointCloudObject != null && controller.pointCloudObject.activeSelf;
            bool gtOrig = controller.groundTruthObject != null && controller.groundTruthObject.activeSelf;

            UniversalRenderPipelineAsset urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urpAsset == null)
            {
                urpAsset = QualitySettings.renderPipeline as UniversalRenderPipelineAsset;
            }

            int origMsaa = urpAsset != null ? urpAsset.msaaSampleCount : 1;
            bool origAllowMsaa = leftCam.allowMSAA;
            int origTargetRtMsaa = (leftCam.targetTexture != null) ? leftCam.targetTexture.antiAliasing : 1;

            AppLogger.Log("SICESI_StageDebug", $"=== [左目単独 厳密ステージ診断開始] 保存先: {saveDir} ===");
            AppLogger.Log("SICESI_StageDebug", $"=== [MSAA 実効サンプル数初期値確認] URP Asset={urpAsset?.name ?? "null"}, URP msaaSampleCount={origMsaa}, leftCam.allowMSAA={origAllowMsaa}, targetTexture.antiAliasing={origTargetRtMsaa} ===");

            try
            {
                // =============================================================
                // 重要: 右目カメラを完全に無効化し、左目カメラのみを排他レンダリング
                // これにより、グローバルな _NeighborCountMap が右目に上書きされるのを完全に防ぐ
                // =============================================================
                if (rightCam != null) rightCam.enabled = false;
                leftCam.enabled = true;

                // -------------------------------------------------------------
                // Step 0: 同一左目カメラでの正確な VO シルエット取得
                // ※ 点群なし・白一色VOにて「表示補正前 (Pre-Correction)」と「表示補正後 (Post-Correction)」の両方を保存！
                // -------------------------------------------------------------
                AppLogger.Log("SICESI_StageDebug", "--- [Step 0] 同一左目カメラでの VO シルエット取得 (表示補正前 vs 表示補正後) ---");
                if (controller.pointCloudObject != null) controller.pointCloudObject.SetActive(false);
                if (controller.groundTruthObject != null) controller.groundTruthObject.SetActive(false);
                SetVirtualObjectWhiteState(controller.virtualObject);

                SRDStageDebugBridge.RequestStageDebugCapture = true;
                SRDStageDebugBridge.StageDebugCaptureDir = saveDir;
                SRDStageDebugBridge.StageDebugFilePrefix = "vo_silhouette";

                yield return null;
                yield return new WaitForEndOfFrame();

                controller.SaveCameraView(leftCam, Path.Combine(saveDir, "vo_silhouette_left_exact.png"), true);
                controller.SaveCameraView(leftCam, Path.Combine(saveDir, "vo_silhouette_post_correction.png"), true);

                string preVoSrc = Path.Combine(saveDir, "vo_silhouette_SRD_0_PreCorrection.png");
                string preVoDest = Path.Combine(saveDir, "vo_silhouette_pre_correction.png");
                if (File.Exists(preVoSrc))
                {
                    File.Copy(preVoSrc, preVoDest, true);
                }

                // -------------------------------------------------------------
                // Step 1: 【通常条件: 4x MSAA】での同一フレーム A, B, C 一括診断
                // -------------------------------------------------------------
                AppLogger.Log("SICESI_StageDebug", "--- [Step 1] 【通常条件: 4x MSAA】同一フレーム A(生判定), B(表示前実画像), C(表示後最終画像) 一括取得 ---");
                string dir4x = Path.Combine(saveDir, "MSAA_4x");
                yield return StartCoroutine(RunCaptureUnderMsaaCondition(dir4x, leftCam, urpAsset, 4, true));

                // -------------------------------------------------------------
                // Step 2: 【診断条件: MSAA OFF (1x)】での同一フレーム A, B, C 一括診断
                // -------------------------------------------------------------
                AppLogger.Log("SICESI_StageDebug", "--- [Step 2] 【診断条件: MSAA OFF (1x)】同一フレーム A(生判定), B(表示前実画像), C(表示後最終画像) 一括取得 ---");
                string dirOff = Path.Combine(saveDir, "MSAA_OFF");
                yield return StartCoroutine(RunCaptureUnderMsaaCondition(dirOff, leftCam, urpAsset, 1, false));

                // 親ディレクトリにも MSAA_OFF の成果物を配置して後方互換性を確保
                CopyDiagnosticFiles(dirOff, saveDir);

                // -------------------------------------------------------------
                // Step 2.5: 【無損失Blitモード診断】
                // PCDBlitPassBuilder.LosslessBlitMode = true による実描画
                // -------------------------------------------------------------
                AppLogger.Log("SICESI_StageDebug", "--- [Step 2.5] 無損失Blitモード (LosslessPointBlit) 実行 & 撮影 ---");
                try
                {
                    PCDBlitPassBuilder.LosslessBlitMode = true;
                    yield return null;
                    yield return new WaitForEndOfFrame();
                    controller.SaveCameraView(leftCam, Path.Combine(saveDir, "C_lossless_display_output.png"), true);
                    controller.SaveCameraView(leftCam, Path.Combine(dirOff, "C_lossless_display_output.png"), true);
                }
                finally
                {
                    PCDBlitPassBuilder.LosslessBlitMode = false;
                }
                RestoreVirtualObjectMaterialState();

                // -------------------------------------------------------------
                // Step 3: 通常描画 (Normal) 撮影
                // -------------------------------------------------------------
                AppLogger.Log("SICESI_StageDebug", "--- [Step 3] 通常描画 (Normal) 撮影 ---");
                yield return null;
                yield return new WaitForEndOfFrame();

                string normPath = Path.Combine(saveDir, "test_left_normal.png");
                controller.SaveCameraView(leftCam, normPath);

                // メタデータの記録
                SaveMetaJson(saveDir, leftCam);
            }
            finally
            {
                // MSAA 設定の復元
                if (urpAsset != null) urpAsset.msaaSampleCount = origMsaa;
                leftCam.allowMSAA = origAllowMsaa;

                // カメラおよびオブジェクト状態の復元
                leftCam.enabled = leftOrig;
                if (rightCam != null) rightCam.enabled = rightOrig;
                if (controller.pointCloudObject != null) controller.pointCloudObject.SetActive(pcOrig);
                if (controller.groundTruthObject != null) controller.groundTruthObject.SetActive(gtOrig);
            }

            AppLogger.Log("SICESI_StageDebug", $"=== [左目単独 厳密ステージ診断完了] 保存先: {saveDir} ===");
            onComplete?.Invoke(true, saveDir);
        }

        /// <summary>
        /// 指定された MSAA サンプル数および allowMSAA 設定のもとで、同一フレーム A, B, C を一括キャプチャします。
        /// </summary>
        private IEnumerator RunCaptureUnderMsaaCondition(string targetDir, Camera leftCam, UniversalRenderPipelineAsset urpAsset, int targetMsaaSamples, bool allowMsaa)
        {
            Directory.CreateDirectory(targetDir);

            if (urpAsset != null)
            {
                urpAsset.msaaSampleCount = targetMsaaSamples;
            }
            leftCam.allowMSAA = allowMsaa;

            int actualUrpMsaa = urpAsset != null ? urpAsset.msaaSampleCount : 1;
            bool actualAllowMsaa = leftCam.allowMSAA;
            int actualTargetRtMsaa = leftCam.targetTexture != null ? leftCam.targetTexture.antiAliasing : 1;
            int actualEffectiveSamples = actualAllowMsaa ? actualUrpMsaa : 1;

            AppLogger.Log("SICESI_StageDebug", $"[MSAA条件適用] 目標: {targetMsaaSamples}x | URP msaaSampleCount={actualUrpMsaa}, leftCam.allowMSAA={actualAllowMsaa}, targetTexture.antiAliasing={actualTargetRtMsaa} => 実効サンプル数={actualEffectiveSamples}");

            var msaaMeta = new MsaaDiagnosticMeta
            {
                conditionLabel = targetMsaaSamples > 1 ? $"{targetMsaaSamples}x MSAA" : "MSAA OFF (1x)",
                targetMsaaSamples = targetMsaaSamples,
                urpAssetMsaaSampleCount = actualUrpMsaa,
                cameraAllowMsaa = actualAllowMsaa,
                targetTextureAntiAliasing = actualTargetRtMsaa,
                effectiveCameraMsaaSamples = actualEffectiveSamples
            };
            File.WriteAllText(Path.Combine(targetDir, "msaa_diagnostic_meta.json"), JsonUtility.ToJson(msaaMeta, true));

            // パイプライン設定
            if (controller.pointCloudObject != null) controller.pointCloudObject.SetActive(true);
            if (occlusionPipelineController != null) occlusionPipelineController.recordNeighborCountMap = true;
            if (PCDRendererFeature.Instance != null && PCDRendererFeature.Instance.settings != null)
            {
                PCDRendererFeature.Instance.settings.recordNeighborCountMap = true;
                PCDRendererFeature.Instance.settings.debugSectorId = -1;
            }

            yield return null;

            SRDStageDebugBridge.RequestStageDebugCapture = true;
            SRDStageDebugBridge.StageDebugCaptureDir = targetDir;
            SRDStageDebugBridge.StageDebugFilePrefix = "";

            yield return new WaitForEndOfFrame();

            MirrorRendererFeature.RequestStageDebugCapture = true;
            MirrorRendererFeature.StageDebugCaptureDir = targetDir;
            yield return StartCoroutine(CaptureSynchronousABCLeftRoutine(targetDir, leftCam));
        }

        private static void CopyDiagnosticFiles(string sourceDir, string targetDir)
        {
            if (!Directory.Exists(sourceDir)) return;
            string[] files = Directory.GetFiles(sourceDir);
            foreach (var f in files)
            {
                string fileName = Path.GetFileName(f);
                string dest = Path.Combine(targetDir, fileName);
                File.Copy(f, dest, true);
            }
        }


        /// <summary>
        /// 同一フレームにおいて、A(GPU生判定), B(遮蔽適用直後FinalImage), C(表示後Camera.targetTexture)を同時にリードバックします。
        /// </summary>
        private IEnumerator CaptureSynchronousABCLeftRoutine(string saveDir, Camera leftCam)
        {
            if (PCDRendererFeature.Instance == null || PCDRendererFeature.Instance.CurrentResources == null)
            {
                AppLogger.LogError("SICESI_StageDebug", "PCDRendererFeature.CurrentResources が利用できません。");
                yield break;
            }

            var resources = PCDRendererFeature.Instance.CurrentResources;
            var settings = PCDRendererFeature.Instance.settings;

            // 1. バッファの取得
            RenderTexture rtA = resources.NeighborCountMap != null ? resources.NeighborCountMap.rt : null;
            RTHandle handleB = (settings != null && settings.holeFillingMethod != PCDRendererFeature.PCD_HoleFillingMethod.None)
                ? resources.FinalImage
                : resources.OcclusionResultMap;
            RenderTexture rtB = handleB != null ? handleB.rt : null;
            RenderTexture rtC = leftCam != null ? leftCam.targetTexture : null;
            RenderTexture rtOrigin = resources.OriginTypeMap != null ? resources.OriginTypeMap.rt : null;

            if (rtA == null || rtB == null || rtC == null)
            {
                AppLogger.LogError("SICESI_StageDebug", $"バッファ取得失敗: rtA={rtA != null}, rtB={rtB != null}, rtC={rtC != null}");
                yield break;
            }

            // 2. 同時非同期リードバックの要求
            bool doneA = false, doneB = false, doneC = false, doneOrigin = false;
            uint[] dataA = null;
            Color32[] dataB = null;
            Color32[] dataC = null;
            uint[] dataOrigin = null;

            AsyncGPUReadback.Request(rtA, 0, req =>
            {
                if (!req.hasError) dataA = req.GetData<uint>().ToArray();
                doneA = true;
            });

            AsyncGPUReadback.Request(rtB, 0, TextureFormat.RGBA32, req =>
            {
                if (!req.hasError) dataB = req.GetData<Color32>().ToArray();
                doneB = true;
            });

            AsyncGPUReadback.Request(rtC, 0, TextureFormat.RGBA32, req =>
            {
                if (!req.hasError) dataC = req.GetData<Color32>().ToArray();
                doneC = true;
            });

            if (rtOrigin != null)
            {
                AsyncGPUReadback.Request(rtOrigin, 0, req =>
                {
                    if (!req.hasError) dataOrigin = req.GetData<uint>().ToArray();
                    doneOrigin = true;
                });
            }
            else
            {
                doneOrigin = true;
            }

            // 3. D2 (ReadPixels) の即時同期取得
            int width = rtC.width;
            int height = rtC.height;
            RenderTexture prevActive = RenderTexture.active;
            Texture2D texD2 = new Texture2D(width, height, TextureFormat.RGB24, false);
            RenderTexture.active = rtC;
            texD2.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texD2.Apply();
            RenderTexture.active = prevActive;
            Color32[] dataD2 = texD2.GetPixels32();
            string d2Path = Path.Combine(saveDir, "D2_ReadPixels_Saved.png");
            File.WriteAllBytes(d2Path, texD2.EncodeToPNG());
            Destroy(texD2);

            // リードバック完了待機
            float timeout = Time.realtimeSinceStartup + 5.0f;
            while ((!doneA || !doneB || !doneC || !doneOrigin) && Time.realtimeSinceStartup < timeout)
            {
                yield return null;
            }

            if (dataA == null || dataB == null || dataC == null)
            {
                AppLogger.LogError("SICESI_StageDebug", $"AsyncReadback タイムアウトまたはエラー: dataA={dataA != null}, dataB={dataB != null}, dataC={dataC != null}");
                yield break;
            }

            AppLogger.Log("SICESI_StageDebug", "🟢 同一フレーム A, B, C の全バッファを完全同期取得しました！");

            // 3.5 同一Bスナップショットに対する「通常Blit vs 無損失整数Load Blit」比較実験
            RenderTexture tempNormal = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            RenderTexture tempLossless = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);

            // 通常転送 (バイリニア補間 Blit)
            Graphics.Blit(rtB, tempNormal);

            // 無損失転送 (整数画素座標 Load() による完全1:1転送)
            Material losslessMat = null;
            Shader losslessShader = Shader.Find("Hidden/SICESI/LosslessPointBlit");
            if (losslessShader != null)
            {
                losslessMat = new Material(losslessShader) { hideFlags = HideFlags.HideAndDontSave };
                losslessMat.SetFloat("_FlipX", 0.0f);
            }

            if (losslessMat != null)
            {
                Graphics.Blit(rtB, tempLossless, losslessMat);
            }
            else
            {
                Graphics.Blit(rtB, tempLossless);
            }

            // 両方の結果を読み戻し
            Texture2D texNormal = new Texture2D(width, height, TextureFormat.RGBA32, false);
            RenderTexture.active = tempNormal;
            texNormal.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texNormal.Apply();

            Texture2D texLossless = new Texture2D(width, height, TextureFormat.RGBA32, false);
            RenderTexture.active = tempLossless;
            texLossless.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texLossless.Apply();
            RenderTexture.active = prevActive;

            Color32[] pixelsNormal = texNormal.GetPixels32();
            Color32[] pixelsLossless = texLossless.GetPixels32();

            int normalIntermediates = CountIntermediates(pixelsNormal);
            int losslessIntermediates = CountIntermediates(pixelsLossless);

            File.WriteAllBytes(Path.Combine(saveDir, "C_normal_blit_from_B.png"), texNormal.EncodeToPNG());
            File.WriteAllBytes(Path.Combine(saveDir, "C_lossless_blit_from_B.png"), texLossless.EncodeToPNG());

            Destroy(texNormal);
            Destroy(texLossless);
            if (losslessMat != null) Destroy(losslessMat);
            RenderTexture.ReleaseTemporary(tempNormal);
            RenderTexture.ReleaseTemporary(tempLossless);

            AppLogger.Log("SICESI_StageDebug", $"[同一Bスナップショット比較] 通常Blit中間値={normalIntermediates:,} px, 無損失Load中間値={losslessIntermediates:,} px");

            // 4. A (GPU生判定) の解析と保存
            int minOccupiedSectors = controller.fixedMinOccludedSectors > 0 ? controller.fixedMinOccludedSectors : 6;
            int totalPixels = width * height;
            int evaluatedCount = 0;
            int bit13OccCount = 0;
            int recomputedOccCount = 0;
            int internalMismatchCount = 0;

            byte[] occDirectBytes = new byte[totalPixels];
            byte[] occFlippedBytes = new byte[totalPixels];
            byte[] patternBytes = new byte[totalPixels];
            byte[] evaluatedBytes = new byte[totalPixels];
            byte[] originTypeBytes = new byte[totalPixels];
            byte[] finalOccBytes = new byte[totalPixels];

            int directPointOccCount = 0;
            int unevalPhysicalCount = 0;
            int unevalBackgroundCount = 0;
            int unevalOtherCount = 0;

            for (int y = 0; y < height; y++)
            {
                int rowOffset = y * width;
                for (int x = 0; x < width; x++)
                {
                    int idx = rowOffset + x;
                    uint val = dataA[idx];

                    uint isEvaluated = (val >> 12) & 1u;
                    uint pattern = val & 0xFFu;
                    uint bit13Occ = (val >> 13) & 1u;

                    uint origin = (dataOrigin != null && idx < dataOrigin.Length) ? dataOrigin[idx] : 1u;
                    originTypeBytes[idx] = (byte)(origin == 0u ? 0 : (origin == 1u ? 128 : 255));

                    byte occVal = (bit13Occ != 0u) ? (byte)255 : (byte)0;
                    byte evalVal = (isEvaluated != 0u) ? (byte)255 : (byte)0;

                    // 最終遮蔽判定: セクター判定遮蔽 (bit13) OR 手前の物理点群による直接遮蔽 (originType==0 && isEvaluated==0)
                    bool isDirectPointOcc = (origin == 0u && isEvaluated == 0u);
                    bool isFinalOcc = (bit13Occ != 0u) || isDirectPointOcc;
                    if (isDirectPointOcc) directPointOccCount++;
                    finalOccBytes[idx] = isFinalOcc ? (byte)255 : (byte)0;

                    occDirectBytes[idx] = occVal;
                    patternBytes[idx] = (byte)pattern;
                    evaluatedBytes[idx] = evalVal;

                    int flippedIdx = rowOffset + (width - 1 - x);
                    occFlippedBytes[flippedIdx] = occVal;

                    if (isEvaluated != 0u)
                    {
                        evaluatedCount++;
                        if (bit13Occ != 0u) bit13OccCount++;

                        int popcount = CountBits(pattern);
                        bool isRecomputedOcc = popcount >= minOccupiedSectors;
                        if (isRecomputedOcc) recomputedOccCount++;

                        if ((bit13Occ != 0u) != isRecomputedOcc)
                        {
                            internalMismatchCount++;
                        }
                    }
                    else
                    {
                        if (origin == 0u) unevalPhysicalCount++;
                        else if (origin == 2u) unevalBackgroundCount++;
                        else unevalOtherCount++;
                    }
                }
            }

            double consistencyRate = evaluatedCount > 0 ? 100.0 * (1.0 - (double)internalMismatchCount / evaluatedCount) : 100.0;
            AppLogger.Log("SICESI_StageDebug", $"[同一フレーム 内部整合性] 評価画素={evaluatedCount:,}, bit13={bit13OccCount:,}, 直接点群遮蔽={directPointOccCount:,}, 最終遮蔽={bit13OccCount + directPointOccCount:,}");
            AppLogger.Log("SICESI_StageDebug", $"[未評価タグ内訳] 物理点群(0)={unevalPhysicalCount:,}, 背景(2)={unevalBackgroundCount:,}, その他={unevalOtherCount:,}");

            var consistencyReport = new InternalConsistencyData
            {
                totalPixels = totalPixels,
                width = width,
                height = height,
                evaluatedPixels = evaluatedCount,
                minOccupiedSectorsThreshold = minOccupiedSectors,
                bit13OccludedPixels = bit13OccCount,
                recomputedOccludedPixels = recomputedOccCount,
                internalMismatchPixels = internalMismatchCount,
                consistencyRatePercent = consistencyRate
            };
            File.WriteAllText(Path.Combine(saveDir, "internal_consistency_result.json"), JsonUtility.ToJson(consistencyReport, true));

            SaveGrayscalePNG(occDirectBytes, width, height, Path.Combine(saveDir, "A_raw_gpu_mask_direct.png"));
            SaveGrayscalePNG(occDirectBytes, width, height, Path.Combine(saveDir, "sector_occlusion_mask_direct.png"));
            SaveGrayscalePNG(finalOccBytes, width, height, Path.Combine(saveDir, "final_occlusion_mask_direct.png"));
            SaveGrayscalePNG(occFlippedBytes, width, height, Path.Combine(saveDir, "A_raw_gpu_mask_flipped.png"));
            SaveGrayscalePNG(evaluatedBytes, width, height, Path.Combine(saveDir, "evaluated_mask_pre_correction.png"));
            SaveGrayscalePNG(originTypeBytes, width, height, Path.Combine(saveDir, "origin_type_map_direct.png"));
            SaveGrayscalePNG(patternBytes, width, height, Path.Combine(saveDir, "raw_sector_pattern_direct.png"));

            // 将来の任意bit検証用に raw dataA (uint32) および dataOrigin (uint32) をバイナリ保存
            byte[] rawBytes = new byte[totalPixels * 4];
            Buffer.BlockCopy(dataA, 0, rawBytes, 0, rawBytes.Length);
            File.WriteAllBytes(Path.Combine(saveDir, "A_raw_uint32.bin"), rawBytes);

            if (dataOrigin != null)
            {
                byte[] rawOriginBytes = new byte[totalPixels * 4];
                Buffer.BlockCopy(dataOrigin, 0, rawOriginBytes, 0, rawOriginBytes.Length);
                File.WriteAllBytes(Path.Combine(saveDir, "origin_type_raw_uint32.bin"), rawOriginBytes);
            }

            // 互換用エイリアス
            SaveGrayscalePNG(occDirectBytes, width, height, Path.Combine(saveDir, "raw_gpu_occluded_direct.png"));
            SaveGrayscalePNG(occFlippedBytes, width, height, Path.Combine(saveDir, "raw_gpu_occluded_cpu_flipped.png"));

            // 5. B (表示前実画像) の保存
            SaveColor32PNG(dataB, width, height, Path.Combine(saveDir, "B_pre_blit_final_image.png"));

            // 6. C (表示後最終画像) の保存
            SaveColor32PNG(dataC, width, height, Path.Combine(saveDir, "C_final_display_output.png"));
            SaveColor32PNG(dataC, width, height, Path.Combine(saveDir, "D1_CameraTargetTexture.png")); // 互換用
            SaveColor32PNG(dataC, width, height, Path.Combine(saveDir, "test_left_white.png"));        // 互換用

            // 7. 中間値統計
            var stats = new D0D1D2Stats
            {
                d0TotalPixels = totalPixels,
                d0IntermediatePixels = 0, // A生判定は完全二値
                d1TotalPixels = dataC.Length,
                d1IntermediatePixels = CountIntermediates(dataC),
                d2TotalPixels = dataD2.Length,
                d2IntermediatePixels = CountIntermediates(dataD2),
                normalBlitIntermediatePixels = normalIntermediates,
                losslessBlitIntermediatePixels = losslessIntermediates
            };
            File.WriteAllText(Path.Combine(saveDir, "stage_d0_d1_d2_stats.json"), JsonUtility.ToJson(stats, true));

            AppLogger.Log("SICESI_StageDebug", $"[中間値統計] A生判定=0 px, B表示前={CountIntermediates(dataB)} px, C表示後={stats.d1IntermediatePixels} px, D2={stats.d2IntermediatePixels} px, B直接通常Blit={normalIntermediates} px, B直接無損失Load={losslessIntermediates} px");
        }


        private IEnumerator MeasureD0D1D2LeftRoutine(string saveDir, Camera leftCam)
        {
            var resources = PCDRendererFeature.Instance.CurrentResources;
            var stats = new D0D1D2Stats();

            // D0: DebugDisplayMap
            if (resources != null && resources.DebugDisplayMap != null && resources.DebugDisplayMap.rt != null)
            {
                RenderTexture d0Rt = resources.DebugDisplayMap.rt;
                bool d0Done = false;
                AsyncGPUReadback.Request(d0Rt, 0, TextureFormat.RGBA32, req =>
                {
                    if (!req.hasError)
                    {
                        var data = req.GetData<Color32>().ToArray();
                        stats.d0TotalPixels = data.Length;
                        stats.d0IntermediatePixels = CountIntermediates(data);
                        SaveColor32PNG(data, d0Rt.width, d0Rt.height, Path.Combine(saveDir, "D0_DebugDisplayMap.png"));
                    }
                    d0Done = true;
                });
                float t0 = Time.realtimeSinceStartup + 3.0f;
                while (!d0Done && Time.realtimeSinceStartup < t0) yield return null;
            }

            // D1: leftCam.targetTexture
            if (leftCam.targetTexture != null)
            {
                RenderTexture d1Rt = leftCam.targetTexture;
                bool d1Done = false;
                AsyncGPUReadback.Request(d1Rt, 0, TextureFormat.RGBA32, req =>
                {
                    if (!req.hasError)
                    {
                        var data = req.GetData<Color32>().ToArray();
                        stats.d1TotalPixels = data.Length;
                        stats.d1IntermediatePixels = CountIntermediates(data);
                        SaveColor32PNG(data, d1Rt.width, d1Rt.height, Path.Combine(saveDir, "D1_CameraTargetTexture.png"));
                    }
                    d1Done = true;
                });
                float t1 = Time.realtimeSinceStartup + 3.0f;
                while (!d1Done && Time.realtimeSinceStartup < t1) yield return null;
            }

            // D2: ReadPixels 直後
            int width = leftCam.pixelWidth > 0 ? leftCam.pixelWidth : Screen.width;
            int height = leftCam.pixelHeight > 0 ? leftCam.pixelHeight : Screen.height;
            RenderTexture prevActive = RenderTexture.active;
            Texture2D texD2 = new Texture2D(width, height, TextureFormat.RGB24, false);

            if (leftCam.targetTexture != null)
            {
                RenderTexture.active = leftCam.targetTexture;
                texD2.ReadPixels(new Rect(0, 0, leftCam.targetTexture.width, leftCam.targetTexture.height), 0, 0);
            }
            else
            {
                RenderTexture.active = null;
                texD2.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            }
            RenderTexture.active = prevActive;

            Color32[] d2Pixels = texD2.GetPixels32();
            stats.d2TotalPixels = d2Pixels.Length;
            stats.d2IntermediatePixels = CountIntermediates(d2Pixels);

            string pngPath = Path.Combine(saveDir, "D2_ReadPixels_Saved.png");
            byte[] pngBytes = texD2.EncodeToPNG();
            File.WriteAllBytes(pngPath, pngBytes);
            Destroy(texD2);

            Texture2D pngReloaded = new Texture2D(2, 2);
            pngReloaded.LoadImage(File.ReadAllBytes(pngPath));
            Color32[] pngPixels = pngReloaded.GetPixels32();
            stats.pngTotalPixels = pngPixels.Length;
            stats.pngIntermediatePixels = CountIntermediates(pngPixels);
            Destroy(pngReloaded);

            AppLogger.Log("SICESI_StageDebug", $"[左目 D0-D2 実測結果] D0={stats.d0IntermediatePixels} px, D1={stats.d1IntermediatePixels} px, D2={stats.d2IntermediatePixels} px, PNG={stats.pngIntermediatePixels} px");
            File.WriteAllText(Path.Combine(saveDir, "stage_d0_d1_d2_stats.json"), JsonUtility.ToJson(stats, true));
        }

        private void SaveMetaJson(string saveDir, Camera leftCam)
        {
            var meta = new PairedExperimentMeta
            {
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                conditionName = controller.conditionName,
                densityUnit = controller.densityUnit.ToString(),
                cameraName = leftCam.name,
                cameraPosition = leftCam.transform.position,
                cameraRotation = leftCam.transform.rotation.eulerAngles,
                virtualObjectName = controller.virtualObject != null ? controller.virtualObject.name : "None",
                virtualObjectPosition = controller.virtualObject != null ? controller.virtualObject.transform.position : Vector3.zero,
                virtualObjectRotation = controller.virtualObject != null ? controller.virtualObject.transform.rotation.eulerAngles : Vector3.zero,
                screenWidth = leftCam.pixelWidth,
                screenHeight = leftCam.pixelHeight
            };
            File.WriteAllText(Path.Combine(saveDir, "paired_experiment_meta.json"), JsonUtility.ToJson(meta, true));
        }

        private static int CountIntermediates(Color32[] pixels)
        {
            int count = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                byte r = pixels[i].r;
                if (r > 0 && r < 255) count++;
            }
            return count;
        }

        private static int CountBits(uint v)
        {
            v = v - ((v >> 1) & 0x55555555u);
            v = (v & 0x33333333u) + ((v >> 2) & 0x33333333u);
            return (int)(((v + (v >> 4)) & 0x0F0F0F0Fu) * 0x01010101u) >> 24;
        }

        private void SetVirtualObjectWhiteState(GameObject vo)
        {
            if (vo == null) return;
            _cachedOriginalMaterials.Clear();

            if (_unlitWhiteMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) shader = Shader.Find("Unlit/Color");
                _unlitWhiteMaterial = new Material(shader) { color = Color.white };
            }

            Renderer[] renderers = vo.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                if (r == null) continue;
                _cachedOriginalMaterials[r] = r.sharedMaterials;

                Material[] whiteMats = new Material[r.sharedMaterials.Length];
                for (int m = 0; m < whiteMats.Length; m++) whiteMats[m] = _unlitWhiteMaterial;
                r.sharedMaterials = whiteMats;
            }
        }

        private void RestoreVirtualObjectMaterialState()
        {
            foreach (var kv in _cachedOriginalMaterials)
            {
                if (kv.Key != null)
                {
                    kv.Key.sharedMaterials = kv.Value;
                }
            }
            _cachedOriginalMaterials.Clear();
        }

        private static void SaveGrayscalePNG(byte[] bytes, int width, int height, string path)
        {
            Texture2D tex = new Texture2D(width, height, TextureFormat.R8, false);
            tex.SetPixelData(bytes, 0);
            tex.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
        }

        private static void SaveColor32PNG(Color32[] colors, int width, int height, string path)
        {
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.SetPixels32(colors);
            tex.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
        }

        [Serializable]
        public class InternalConsistencyData
        {
            public int totalPixels;
            public int width;
            public int height;
            public int evaluatedPixels;
            public int minOccupiedSectorsThreshold;
            public int bit13OccludedPixels;
            public int recomputedOccludedPixels;
            public int internalMismatchPixels;
            public double consistencyRatePercent;
        }

        [Serializable]
        public class D0D1D2Stats
        {
            public int d0TotalPixels;
            public int d0IntermediatePixels;
            public int d1TotalPixels;
            public int d1IntermediatePixels;
            public int d2TotalPixels;
            public int d2IntermediatePixels;
            public int pngTotalPixels;
            public int pngIntermediatePixels;
            public int normalBlitIntermediatePixels;
            public int losslessBlitIntermediatePixels;
        }

        [Serializable]
        public class PairedExperimentMeta
        {
            public string timestamp;
            public string conditionName;
            public string densityUnit;
            public string cameraName;
            public Vector3 cameraPosition;
            public Vector3 cameraRotation;
            public string virtualObjectName;
            public Vector3 virtualObjectPosition;
            public Vector3 virtualObjectRotation;
            public int screenWidth;
            public int screenHeight;
        }

        [Serializable]
        public class MsaaDiagnosticMeta
        {
            public string conditionLabel;
            public int targetMsaaSamples;
            public int urpAssetMsaaSampleCount;
            public bool cameraAllowMsaa;
            public int targetTextureAntiAliasing;
            public int effectiveCameraMsaaSamples;
        }
    }
}
