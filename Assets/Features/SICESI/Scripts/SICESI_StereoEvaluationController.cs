using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using RealSense.DummyPointCloud;

namespace SICESI
{
    /// <summary>
    /// SICE SI 発表用 臨時評価コントローラー
    /// 左右眼カメラ映像および Ground Truth (手メッシュ) / 各密度での点群遮蔽画像の自動キャプチャを統括します。
    /// 本体コードに影響を与えない独立クラスです。
    /// </summary>
    [DisallowMultipleComponent]
    public class SICESI_StereoEvaluationController : MonoBehaviour
    {
        [Header("Target Cameras")]
        [Tooltip("左目用カメラ (SRDisplay LeftEyeCamera または単体カメラ)")]
        public Camera leftEyeCamera;

        [Tooltip("右目用カメラ (SRDisplay RightEyeCamera または単体カメラ)")]
        public Camera rightEyeCamera;

        [Header("Scene Capture Settings (新実験仕様: 手メッシュ肌色化シーン画像保存)")]
        [Tooltip("配置説明画像を保存する撮影用Camera (インスペクターで指定)")]
        public Camera sceneCaptureCamera;

        [Tooltip("配置説明画像の横解像度")]
        public int sceneCaptureWidth = 1920;

        [Tooltip("配置説明画像の縦解像度")]
        public int sceneCaptureHeight = 1080;

        [Header("Objects To Toggle")]
        [Tooltip("Ground Truth (正解) として表示する手メッシュなどのオブジェクト")]
        public GameObject groundTruthObject;

        [Tooltip("オクルージョンされる仮想オブジェクト (位置・姿勢 Transform 記録用)")]
        public GameObject virtualObject;

        [Tooltip("ダミー点群の表示オブジェクト (RsDummyPointCloudRenderer が付いているオブジェクト)")]
        public GameObject pointCloudObject;

        [Tooltip("点群密度を制御するプロバイダー")]
        public RsDummyPointCloudProvider dummyPointCloudProvider;

        [Header("Ground Truth Layer & Material Settings")]
        [Tooltip("GT撮影時に手メッシュに一時的に割り当てるLayer名 (カメラのCullingMaskで弾かれないようにPCDなどに設定)")]
        public string groundTruthCaptureLayer = "PCD";

        [Tooltip("GT撮影時に手メッシュを真っ黒 (RGB: 0,0,0) にして仮想物体を隠すオクルージョンマスクにするかどうか")]
        public bool renderGroundTruthAsBlack = true;

        [Header("Density Sweep Settings")]
        [Tooltip("点群の物理密度の指定単位 (PointSpacingMm: 点間隔mm, PointsPerCm2: 1cm^2あたりの点数 など)")]
        public PointDensityUnit densityUnit = PointDensityUnit.PointSpacingMm;

        [Tooltip("評価実験でスイープする点群密度のリスト (上記 densityUnit に準拠した数値。例: PointSpacingMm の場合は 1.0, 2.0, 3.0 mm など)")]
        public float[] sweepDensities = new float[] { 1.0f, 2.0f, 3.0f, 4.0f, 5.0f };

        [Tooltip("密度変更後、点群生成とGPU描画の反映を待機するフレーム数")]
        [Range(1, 15)]
        public int waitFramesAfterDensityChange = 5;

        [Header("Color Space & Capture Quality")]
        [Tooltip("Linear色空間から追加でsRGBガンマ補正を行うか。通常URPの最終出力はすでにガンマ補正されているため、false(デフォルト)でGameViewと同一になります。trueにすると二重補正でコントラストが濃くなる場合があります。")]
        public bool applySRGBConversion = false;

        [Header("Sector Sweep Settings")]
        [Tooltip("オクルージョンパイプラインのコントローラー (未設定時は自動検索)")]
        public PCDOcclusionPipelineController occlusionPipelineController;

        [Tooltip("Bouchibaオクルージョン評価でスイープするセクター閾値のリスト (1〜8)")]
        public int[] sweepSectors = new int[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        [Tooltip("セクタースイープ時に点群密度 (sweepDensities) も全パターン組み合わせて一括撮影するか (ONの場合: 密度数 × セクター数 の全組み合わせ)")]
        public bool sweepDensitiesAcrossSectors = true;

        [Tooltip("セクタースイープ時に、比較用として従来の Average (平均値判定) モードも各密度で一緒に撮影するか")]
        public bool includeAverageMode = true;

        [Header("Consecutive Unoccupied Sectors Sweep Settings (SICE 2026 Proposed)")]
        [Tooltip("新手法: 許容最大連続非占有セクター数 L_th のスイープリスト (0〜8)")]
        public int[] sweepMaxConsecutiveZeros = new int[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 };

        [Tooltip("数学的・幾何学的に等価な重複条件を自動スキップする (72組 -> 20組に短縮)")]
        public bool skipRedundantConditions = true;

        [Tooltip("連続非占有数スイープ時に点群密度 (sweepDensities) も全パターン組み合わせて一括撮影するか (OFF時は現在の密度のみで撮影)")]
        public bool sweepDensitiesAcrossConsecutive = false;

        [Header("Density & Occlusion Threshold Sweep Settings")]
        [Tooltip("固定する評価モード (Average: 平均値判定, SectorThreshold: セクター分割判定)")]
        public PCDRendererFeature.PCD_OcclusionEvaluationMode fixedEvaluationMode = PCDRendererFeature.PCD_OcclusionEvaluationMode.SectorThreshold;

        [Tooltip("SectorThreshold モード時に固定するセクター閾値 R_th (1〜8)")]
        [Range(1, 8)]
        public int fixedMinOccludedSectors = 1;

        [Tooltip("スイープするオクルージョン判定閾値のリスト (0.0〜1.0)")]
        public float[] sweepOcclusionThresholds = new float[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f };

        [Header("Output Settings")]
        [Tooltip("出力先ルートディレクトリ")]
        public string outputDirectory = @"C:\Users\hongo\Documents\tsutsumi\Estimation\SICESI_Dataset";

        [HideInInspector]
        public string casesParentFolder = "RawTest_Cases";

        [Tooltip("現在の実験条件・ケース名 (例: RawTest, RawTest_case1, RawTest_case2 など)")]
        public string conditionName = "RawTest";

        /// <summary>
        /// 現在の実験条件のルートディレクトリを取得します (casesParentFolder が設定されている場合はその配下)。
        /// </summary>
        public string ConditionRootDir
        {
            get
            {
                if (!string.IsNullOrEmpty(casesParentFolder))
                {
                    return Path.Combine(outputDirectory, casesParentFolder, conditionName);
                }
                return Path.Combine(outputDirectory, conditionName);
            }
        }

        [Header("Capture Status")]
        public bool isCapturing = false;
        public string statusMessage = "Ready";

        private Material _blackMaterialCache;

        public class GroundTruthStateBackup
        {
            public Dictionary<Renderer, Material[]> Materials = new Dictionary<Renderer, Material[]>();
            public Dictionary<GameObject, int> Layers = new Dictionary<GameObject, int>();
        }

        private void Reset()
        {
            FindCameras();
            FindDummyComponents();
        }

        /// <summary>
        /// シーン内の LeftEyeCamera / RightEyeCamera を自動検索して割り当てます。
        /// </summary>
        public void FindCameras()
        {
#if UNITY_2023_1_OR_NEWER
            var cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            var cameras = FindObjectsOfType<Camera>(true);
#endif
            foreach (var cam in cameras)
            {
                if (cam == null) continue;
                string camName = cam.name.ToLower();
                if (camName.Contains("lefteye") || camName.EndsWith("_l") || camName.Contains("left"))
                {
                    leftEyeCamera = cam;
                }
                else if (camName.Contains("righteye") || camName.EndsWith("_r") || camName.Contains("right"))
                {
                    rightEyeCamera = cam;
                }
            }

            if (leftEyeCamera != null || rightEyeCamera != null)
            {
                Debug.Log($"[SICESI] カメラを自動検出しました: Left={leftEyeCamera?.name}, Right={rightEyeCamera?.name}");
            }
        }

        /// <summary>
        /// シーン内の RsDummyPointCloudProvider を自動検索します。
        /// </summary>
        public void FindDummyComponents()
        {
#if UNITY_2023_1_OR_NEWER
            dummyPointCloudProvider = FindFirstObjectByType<RsDummyPointCloudProvider>();
            var renderer = FindFirstObjectByType<RsDummyPointCloudRenderer>();
            if (occlusionPipelineController == null)
            {
                occlusionPipelineController = FindFirstObjectByType<PCDOcclusionPipelineController>();
            }
#else
            dummyPointCloudProvider = FindObjectOfType<RsDummyPointCloudProvider>();
            var renderer = FindObjectOfType<RsDummyPointCloudRenderer>();
            if (occlusionPipelineController == null)
            {
                occlusionPipelineController = FindObjectOfType<PCDOcclusionPipelineController>();
            }
#endif
            if (renderer != null && pointCloudObject == null)
            {
                pointCloudObject = renderer.gameObject;
            }

            if (virtualObject == null)
            {
                var vo = GameObject.Find("VirtualObjects");
                if (vo != null)
                {
                    virtualObject = vo;
                    Debug.Log($"[SICESI] 仮想オブジェクトを自動検出しました: {vo.name}");
                }
            }
        }

        /// <summary>
        /// Ground Truth (手メッシュのみ有効) の左右眼画像を撮影・保存します。
        /// </summary>
        public void CaptureGroundTruth()
        {
            if (isCapturing) return;
            StartCoroutine(CaptureGroundTruthRoutine());
        }

        private IEnumerator CaptureGroundTruthRoutine()
        {
            isCapturing = true;
            statusMessage = "Capturing Ground Truth...";

            var backup = SetGroundTruthState(true);
            if (pointCloudObject != null) pointCloudObject.SetActive(false);

            // 描画の安定を待つ
            for (int i = 0; i < 3; i++) yield return null;
            yield return new WaitForEndOfFrame();

            string conditionRootDir = ConditionRootDir;
            SaveSceneTransformsJson(conditionRootDir);
            string gtDir = Path.Combine(conditionRootDir, "GT");
            CaptureStereoViews(gtDir, "gt");

            // 共通フォールバック用として outputDirectory/GT にも保存
            string commonGtDir = Path.Combine(outputDirectory, "GT");
            if (commonGtDir != gtDir)
            {
                CaptureStereoViews(commonGtDir, "gt");
            }

            RestoreGroundTruthState(backup);

            statusMessage = "Ground Truth Capture Completed!";
            Debug.Log($"[SICESI] GT撮影完了: {gtDir} (共通: {commonGtDir})");
            isCapturing = false;
        }

        /// <summary>
        /// 撮影用Camera (sceneCaptureCamera) を使用し、手メッシュを一時的に肌色Materialへ変更した
        /// 配置説明画像 (scene.png) とメタデータJSONを保存します。
        /// 撮影完了後は元のマテリアル・レイヤー・カメラ状態が確実に復元されます。
        /// </summary>
        public void CaptureSceneImage(Action<bool, string> onComplete = null)
        {
            if (sceneCaptureCamera == null)
            {
                string warnMsg = "[SICESI] sceneCaptureCamera が未設定です。インスペクターで撮影用Cameraを設定してください。";
                Debug.LogWarning(warnMsg);
                onComplete?.Invoke(false, warnMsg);
                return;
            }

            var capturer = GetComponent<SICESI_SceneCapturer>();
            if (capturer == null)
            {
                capturer = gameObject.AddComponent<SICESI_SceneCapturer>();
            }

            capturer.sceneCaptureCamera = sceneCaptureCamera;
            capturer.sceneCaptureWidth = sceneCaptureWidth;
            capturer.sceneCaptureHeight = sceneCaptureHeight;

            string sceneDir = Path.Combine(ConditionRootDir, "Scene");
            StartCoroutine(capturer.CaptureSceneRoutine(
                sceneDir,
                conditionName,
                groundTruthObject,
                virtualObject,
                onComplete));
        }

        /// <summary>
        /// 現在の点群条件で左右眼画像を1組撮影・保存します。
        /// </summary>
        public void CaptureCurrentCondition(string subFolderName = "")
        {
            if (isCapturing) return;
            StartCoroutine(CaptureCurrentRoutine(subFolderName));
        }

        private IEnumerator CaptureCurrentRoutine(string subFolderName)
        {
            isCapturing = true;
            statusMessage = $"Capturing condition: {conditionName}...";

            if (pointCloudObject != null) pointCloudObject.SetActive(true);
            if (dummyPointCloudProvider != null) dummyPointCloudProvider.densityUnit = densityUnit;

            for (int i = 0; i < 3; i++) yield return null;
            yield return new WaitForEndOfFrame();

            string targetDir = string.IsNullOrEmpty(subFolderName) ? ConditionRootDir : Path.Combine(ConditionRootDir, subFolderName);
            CaptureStereoViews(targetDir, "test");

            statusMessage = $"Condition {conditionName} Capture Completed!";
            Debug.Log($"[SICESI] 条件撮影完了: {targetDir}");
            isCapturing = false;
        }

        /// <summary>
        /// 設定された密度リスト (sweepDensities) を順に変更しながら、全密度の左右画像を自動一括キャプチャします。
        /// </summary>
        public void RunDensitySweep()
        {
            if (isCapturing) return;
            if (dummyPointCloudProvider == null)
            {
                Debug.LogError("[SICESI] RsDummyPointCloudProvider が設定されていません。");
                return;
            }
            StartCoroutine(DensitySweepRoutine());
        }

        private IEnumerator DensitySweepRoutine()
        {
            isCapturing = true;
            string conditionRootDir = ConditionRootDir;
            SaveSceneTransformsJson(conditionRootDir);
            Debug.Log($"[SICESI] === 点群密度スイープキャプチャ開始 (条件: {conditionName}) 保存先: {conditionRootDir} ===");

            // Step 1: まず GT を撮影
            var backup = SetGroundTruthState(true);
            if (pointCloudObject != null) pointCloudObject.SetActive(false);

            for (int i = 0; i < 3; i++) yield return null;
            yield return new WaitForEndOfFrame();

            string gtDir = Path.Combine(conditionRootDir, "GT");
            CaptureStereoViews(gtDir, "gt");
            Debug.Log($"[SICESI] [1/2] Ground Truth 撮影完了: {gtDir}");

            RestoreGroundTruthState(backup);

            // Step 2: 点群表示に切り替え
            if (pointCloudObject != null) pointCloudObject.SetActive(true);

            // Step 3: 各密度で順次撮影
            string unitSuffix = GetDensityUnitSuffix();
            for (int i = 0; i < sweepDensities.Length; i++)
            {
                float density = sweepDensities[i];
                string densityStr = FormatFloat(density);
                statusMessage = $"Running sweep ({i + 1}/{sweepDensities.Length}): Density = {densityStr}{unitSuffix}";
                Debug.Log($"[SICESI] 密度設定変更: {densityStr} ({densityUnit})");

                dummyPointCloudProvider.densityUnit = densityUnit;
                dummyPointCloudProvider.densityValue = density;
                dummyPointCloudProvider.ForceUpdateSampling();

                Debug.Log($"[SICESI] 点群サンプリング強制更新完了: {dummyPointCloudProvider.LastSampledData.PointCount} 点 (密度: {densityStr}{unitSuffix})");

                // 点群サンプリングとGPU転送・URP描画の反映を待機
                for (int f = 0; f < waitFramesAfterDensityChange; f++)
                {
                    yield return null;
                }
                yield return new WaitForEndOfFrame();

                string densityFolder = $"density_{densityStr}{unitSuffix}";
                string targetDir = Path.Combine(conditionRootDir, densityFolder);
                CaptureStereoViews(targetDir, $"test_{densityStr}{unitSuffix}");

                float curThreshold = occlusionPipelineController != null ? occlusionPipelineController.occlusionThreshold : 0.8f;
                var curMode = occlusionPipelineController != null ? occlusionPipelineController.evaluationMode : PCDRendererFeature.PCD_OcclusionEvaluationMode.Average;
                int curSectors = occlusionPipelineController != null ? occlusionPipelineController.minOccludedSectors : 1;
                SaveEvaluationParamsJson(targetDir, density, curThreshold, curMode, curSectors);

                Debug.Log($"[SICESI] [{i + 1}/{sweepDensities.Length}] 撮影完了: {densityFolder}");
            }

            statusMessage = "All Density Sweeps Completed!";
            Debug.Log($"[SICESI] === 点群密度スイープキャプチャ完了! 保存先: {conditionRootDir} ===");
            isCapturing = false;
        }

        /// <summary>
        /// Bouchibaオクルージョンのセクター閾値 (sweepSectors: 0〜8) を順に変更しながら、全セクターの左右画像を自動一括キャプチャします。
        /// 保存先は outputDirectory/SectorSweep/ 配下に自動整理されます。
        /// </summary>
        public void RunSectorSweep()
        {
            if (isCapturing) return;
            if (occlusionPipelineController == null)
            {
                FindDummyComponents();
            }
            if (occlusionPipelineController == null)
            {
                Debug.LogError("[SICESI] PCDOcclusionPipelineController が見つかりません。");
                return;
            }
            StartCoroutine(SectorSweepRoutine());
        }

        private IEnumerator SectorSweepRoutine()
        {
            isCapturing = true;
            // ConditionName を最上位フォルダーとする階層構造: SICESI_Dataset/{casesParentFolder}/{conditionName}/
            string conditionRootDir = ConditionRootDir;
            SaveSceneTransformsJson(conditionRootDir);
            Debug.Log($"[SICESI] === Bouchiba セクタースイープキャプチャ開始 (条件: {conditionName}) 保存先: {conditionRootDir} ===");

            // Step 1: まず GT を撮影 (conditionRootDir/GT に保存)
            var backup = SetGroundTruthState(true);
            if (pointCloudObject != null) pointCloudObject.SetActive(false);

            for (int i = 0; i < 3; i++) yield return null;
            yield return new WaitForEndOfFrame();

            string gtDir = Path.Combine(conditionRootDir, "GT");
            CaptureStereoViews(gtDir, "gt");
            Debug.Log($"[SICESI] [1/2] Ground Truth 撮影完了: {gtDir}");

            RestoreGroundTruthState(backup);

            // Step 2: 点群表示に切り替え & パイプラインを Bouchiba + SectorThreshold に設定
            if (pointCloudObject != null) pointCloudObject.SetActive(true);

            occlusionPipelineController.kernelType = PCDRendererFeature.PCD_OcclusionKernel.Bouchiba;
            occlusionPipelineController.evaluationMode = PCDRendererFeature.PCD_OcclusionEvaluationMode.SectorThreshold;

            // Step 3: 点群密度 × (Average + セクター閾値) の組み合わせ撮影
            bool runAllDensities = sweepDensitiesAcrossSectors && sweepDensities != null && sweepDensities.Length > 0;
            int perDensityCount = sweepSectors.Length + (includeAverageMode ? 1 : 0);
            int totalCombinations = runAllDensities ? (sweepDensities.Length * perDensityCount) : perDensityCount;
            int progress = 0;
            string unitSuffix = GetDensityUnitSuffix();

            if (runAllDensities)
            {
                for (int d = 0; d < sweepDensities.Length; d++)
                {
                    float density = sweepDensities[d];
                    string densityStr = FormatFloat(density);
                    string densitySubDir = $"density_{densityStr}{unitSuffix}";

                    if (dummyPointCloudProvider != null)
                    {
                        dummyPointCloudProvider.densityUnit = densityUnit;
                        dummyPointCloudProvider.densityValue = density;
                        dummyPointCloudProvider.ForceUpdateSampling();
                        Debug.Log($"[SICESI] 点群サンプリング強制更新完了: {dummyPointCloudProvider.LastSampledData.PointCount} 点 (密度: {densityStr}{unitSuffix})");
                    }

                    // (A) 従来の Average モード
                    if (includeAverageMode)
                    {
                        progress++;
                        statusMessage = $"Sector Sweep ({progress}/{totalCombinations}): Density={densityStr}{unitSuffix}, Mode=Average";
                        Debug.Log($"[SICESI] 設定変更 ({progress}/{totalCombinations}): 密度={densityStr}{unitSuffix}, 評価モード=Average");

                        occlusionPipelineController.evaluationMode = PCDRendererFeature.PCD_OcclusionEvaluationMode.Average;

                        for (int f = 0; f < waitFramesAfterDensityChange; f++) yield return null;
                        yield return new WaitForEndOfFrame();

                        string avgFolder = Path.Combine(densitySubDir, "Average");
                        string avgTargetDir = Path.Combine(conditionRootDir, avgFolder);
                        CaptureStereoViews(avgTargetDir, $"test_{densityStr}{unitSuffix}_Average");
                        SaveEvaluationParamsJson(avgTargetDir, density, occlusionPipelineController.occlusionThreshold, PCDRendererFeature.PCD_OcclusionEvaluationMode.Average, 1);

                        Debug.Log($"[SICESI] [{progress}/{totalCombinations}] 撮影完了: {avgFolder}");
                    }

                    // (B) 各セクター閾値モード (SectorThreshold)
                    occlusionPipelineController.evaluationMode = PCDRendererFeature.PCD_OcclusionEvaluationMode.SectorThreshold;
                    for (int s = 0; s < sweepSectors.Length; s++)
                    {
                        int sector = sweepSectors[s];
                        progress++;
                        statusMessage = $"Sector Sweep ({progress}/{totalCombinations}): Density={densityStr}{unitSuffix}, Sector={sector}";
                        Debug.Log($"[SICESI] 設定変更 ({progress}/{totalCombinations}): 密度={densityStr}{unitSuffix}, セクター={sector}/8");

                        occlusionPipelineController.minOccludedSectors = sector;

                        for (int f = 0; f < waitFramesAfterDensityChange; f++) yield return null;
                        yield return new WaitForEndOfFrame();

                        string folder = Path.Combine(densitySubDir, $"sector_{sector}");
                        string targetDir = Path.Combine(conditionRootDir, folder);
                        CaptureStereoViews(targetDir, $"test_{densityStr}{unitSuffix}_sector_{sector}");
                        SaveEvaluationParamsJson(targetDir, density, occlusionPipelineController.occlusionThreshold, PCDRendererFeature.PCD_OcclusionEvaluationMode.SectorThreshold, sector);

                        Debug.Log($"[SICESI] [{progress}/{totalCombinations}] 撮影完了: {folder}");
                    }
                }
            }
            else
            {
                float curDensity = dummyPointCloudProvider != null ? dummyPointCloudProvider.densityValue : 1.0f;

                // 現在の密度設定のまま (Average + セクター閾値) をスイープ
                if (includeAverageMode)
                {
                    progress++;
                    statusMessage = $"Sector Sweep ({progress}/{totalCombinations}): Mode=Average";
                    Debug.Log($"[SICESI] 設定変更 ({progress}/{totalCombinations}): 評価モード=Average");

                    occlusionPipelineController.evaluationMode = PCDRendererFeature.PCD_OcclusionEvaluationMode.Average;

                    for (int f = 0; f < waitFramesAfterDensityChange; f++) yield return null;
                    yield return new WaitForEndOfFrame();

                    string avgFolder = "Average";
                    string avgTargetDir = Path.Combine(conditionRootDir, avgFolder);
                    CaptureStereoViews(avgTargetDir, "test_Average");
                    SaveEvaluationParamsJson(avgTargetDir, curDensity, occlusionPipelineController.occlusionThreshold, PCDRendererFeature.PCD_OcclusionEvaluationMode.Average, 1);

                    Debug.Log($"[SICESI] [{progress}/{totalCombinations}] 撮影完了: {avgFolder}");
                }

                occlusionPipelineController.evaluationMode = PCDRendererFeature.PCD_OcclusionEvaluationMode.SectorThreshold;
                for (int s = 0; s < sweepSectors.Length; s++)
                {
                    int sector = sweepSectors[s];
                    progress++;
                    statusMessage = $"Sector Sweep ({progress}/{totalCombinations}): Sector={sector}";
                    Debug.Log($"[SICESI] セクター閾値設定変更: {sector} / 8");

                    occlusionPipelineController.minOccludedSectors = sector;

                    for (int f = 0; f < waitFramesAfterDensityChange; f++) yield return null;
                    yield return new WaitForEndOfFrame();

                    string sectorFolder = $"sector_{sector}";
                    string targetDir = Path.Combine(conditionRootDir, sectorFolder);
                    CaptureStereoViews(targetDir, $"test_sector_{sector}");
                    SaveEvaluationParamsJson(targetDir, curDensity, occlusionPipelineController.occlusionThreshold, PCDRendererFeature.PCD_OcclusionEvaluationMode.SectorThreshold, sector);

                    Debug.Log($"[SICESI] [{progress}/{totalCombinations}] 撮影完了: {sectorFolder}");
                }
            }

            statusMessage = "All Sector Sweeps Completed!";
            Debug.Log($"[SICESI] === セクタースイープキャプチャ完了! 保存先: {conditionRootDir} ===");
            isCapturing = false;
        }

        /// <summary>
        /// 最低占有数 (sweepSectors: 1〜8) × 許容最大連続0数 (sweepMaxConsecutiveZeros: 0〜8) の計72設定
        /// （および必要に応じて各点群密度）の左右画像を自動一括キャプチャします。
        /// 保存先: outputDirectory / conditionName / ConsecutiveSweep / density_{X} / sector_{s}_maxzero_{z}
        /// </summary>
        public void RunConsecutiveSectorSweep()
        {
            if (isCapturing) return;
            if (occlusionPipelineController == null)
            {
                FindDummyComponents();
            }
            if (occlusionPipelineController == null)
            {
                Debug.LogError("[SICESI] PCDOcclusionPipelineController が見つかりません。");
                return;
            }
            StartCoroutine(ConsecutiveSectorSweepRoutine());
        }

        private IEnumerator ConsecutiveSectorSweepRoutine()
        {
            isCapturing = true;
            string conditionRootDir = ConditionRootDir;
            SaveSceneTransformsJson(conditionRootDir);
            Debug.Log($"[SICESI] === 占有数 × 最大連続0数 スイープキャプチャ開始 (条件: {conditionName}) 保存先: {conditionRootDir} ===");

            // Step 1: まず GT を撮影 (conditionRootDir/GT に保存)
            var backup = SetGroundTruthState(true);
            if (pointCloudObject != null) pointCloudObject.SetActive(false);

            for (int i = 0; i < 3; i++) yield return null;
            yield return new WaitForEndOfFrame();

            string gtDir = Path.Combine(conditionRootDir, "GT");
            CaptureStereoViews(gtDir, "gt");
            Debug.Log($"[SICESI] [1/2] Ground Truth 撮影完了: {gtDir}");

            RestoreGroundTruthState(backup);

            // Step 2: 点群表示に切り替え & パイプラインを Bouchiba + SectorConsecutiveZeros に設定
            if (pointCloudObject != null) pointCloudObject.SetActive(true);

            occlusionPipelineController.kernelType = PCDRendererFeature.PCD_OcclusionKernel.Bouchiba;
            occlusionPipelineController.evaluationMode = PCDRendererFeature.PCD_OcclusionEvaluationMode.SectorConsecutiveZeros;

            var pairs = GetValidConsecutivePairs();
            int perGridCount = pairs.Count;
            bool runAllDensities = sweepDensitiesAcrossConsecutive && sweepDensities != null && sweepDensities.Length > 0;
            int totalCombinations = runAllDensities ? (sweepDensities.Length * perGridCount) : perGridCount;
            int progress = 0;
            string unitSuffix = GetDensityUnitSuffix();

            if (runAllDensities)
            {
                for (int d = 0; d < sweepDensities.Length; d++)
                {
                    float density = sweepDensities[d];
                    string densityStr = FormatFloat(density);
                    string densitySubDir = $"density_{densityStr}{unitSuffix}";

                    if (dummyPointCloudProvider != null)
                    {
                        dummyPointCloudProvider.densityUnit = densityUnit;
                        dummyPointCloudProvider.densityValue = density;
                        dummyPointCloudProvider.ForceUpdateSampling();
                        Debug.Log($"[SICESI] 点群サンプリング強制更新完了: {dummyPointCloudProvider.LastSampledData.PointCount} 点 (密度: {densityStr}{unitSuffix})");
                    }

                    for (int p = 0; p < pairs.Count; p++)
                    {
                        int sector = pairs[p].sector;
                        int maxZero = pairs[p].maxZero;
                        occlusionPipelineController.minOccludedSectors = sector;
                        occlusionPipelineController.maxConsecutiveEmptySectors = maxZero;
                        progress++;

                        statusMessage = $"Consecutive Sweep ({progress}/{totalCombinations}): Density={densityStr}{unitSuffix}, MinOcc={sector}, MaxZero={maxZero}";
                        Debug.Log($"[SICESI] 設定変更 ({progress}/{totalCombinations}): 密度={densityStr}{unitSuffix}, 最低占有={sector}/8, 許容連続0={maxZero}/8");

                        for (int f = 0; f < waitFramesAfterDensityChange; f++) yield return null;
                        yield return new WaitForEndOfFrame();

                        string folder = Path.Combine("ConsecutiveSweep", densitySubDir, $"sector_{sector}_maxzero_{maxZero}");
                        string targetDir = Path.Combine(conditionRootDir, folder);
                        CaptureStereoViews(targetDir, $"test_{densityStr}{unitSuffix}_sector_{sector}_maxzero_{maxZero}");
                        SaveEvaluationParamsJson(targetDir, density, occlusionPipelineController.occlusionThreshold, PCDRendererFeature.PCD_OcclusionEvaluationMode.SectorConsecutiveZeros, sector, maxZero);

                        Debug.Log($"[SICESI] [{progress}/{totalCombinations}] 撮影完了: {folder}");
                    }
                }
            }
            else
            {
                float curDensity = dummyPointCloudProvider != null ? dummyPointCloudProvider.densityValue : 1.0f;
                string densityStr = FormatFloat(curDensity);
                string densitySubDir = $"density_{densityStr}{unitSuffix}";

                for (int p = 0; p < pairs.Count; p++)
                {
                    int sector = pairs[p].sector;
                    int maxZero = pairs[p].maxZero;
                    occlusionPipelineController.minOccludedSectors = sector;
                    occlusionPipelineController.maxConsecutiveEmptySectors = maxZero;
                    progress++;

                    statusMessage = $"Consecutive Sweep ({progress}/{totalCombinations}): MinOcc={sector}, MaxZero={maxZero}";
                    Debug.Log($"[SICESI] 設定変更 ({progress}/{totalCombinations}): 最低占有={sector}/8, 許容連続0={maxZero}/8");

                    for (int f = 0; f < waitFramesAfterDensityChange; f++) yield return null;
                    yield return new WaitForEndOfFrame();

                    string folder = Path.Combine("ConsecutiveSweep", densitySubDir, $"sector_{sector}_maxzero_{maxZero}");
                    string targetDir = Path.Combine(conditionRootDir, folder);
                    CaptureStereoViews(targetDir, $"test_sector_{sector}_maxzero_{maxZero}");
                    SaveEvaluationParamsJson(targetDir, curDensity, occlusionPipelineController.occlusionThreshold, PCDRendererFeature.PCD_OcclusionEvaluationMode.SectorConsecutiveZeros, sector, maxZero);

                    Debug.Log($"[SICESI] [{progress}/{totalCombinations}] 撮影完了: {folder}");
                }
            }

            statusMessage = "All Consecutive Sector Sweeps Completed!";
            Debug.Log($"[SICESI] === 占有数 × 最大連続0数 スイープキャプチャ完了! 保存先: {conditionRootDir} ===");
            isCapturing = false;
        }

        /// <summary>
        /// 8セクター円環において、数学的・幾何学的に重複（等価）なパラメータ組み合わせかどうかを判定します。
        /// </summary>
        public static bool IsRedundantCombination(int rTh, int lTh, int K = 8)
        {
            // 1. L_th = 0 は全セクター占有 (N_occ = 8) と等価。
            //    R_th = 8, L_th = 8 (全占有) が代表として存在するため、それ以外の L_th = 0 はスキップ。
            if (lTh == 0)
            {
                return true;
            }

            // 2. L_th >= K - rTh の領域は、すべて「占有数 rTh のみ」と等価。
            //    代表値として L_th = K (8: 方向条件無効化) のみを残し、それ以外の K - rTh <= lTh < K はスキップ。
            if (lTh >= K - rTh && lTh < K)
            {
                return true;
            }

            // 3. 最大連続0数が lTh 以下という幾何学的制約により、数学的に必然的に保証される最小占有数 minOccForL:
            //    - lTh = 1: 0同士が隣接不可 -> 0は最大4個 -> N_occ >= 4. (R_th < 4 は R_th = 4 と同一)
            //    - lTh = 2: 0が最大2連続 -> 0は最大5個 (00100101) -> N_occ >= 3. (R_th < 3 は R_th = 3 と同一)
            //    - lTh = 3: 0が最大3連続 -> 0は最大6個 (00010001) -> N_occ >= 2. (R_th < 2 は R_th = 2 と同一)
            if (lTh < K)
            {
                int maxZerosPossible = (K * lTh) / (lTh + 1);
                int implicitMinOcc = K - maxZerosPossible;
                if (rTh < implicitMinOcc)
                {
                    return true; // より大きい rTh と全く同じ条件になるため重複
                }
            }

            return false;
        }

        /// <summary>
        /// 現在の設定において評価対象となる (R_th, L_th) の有効ペアリストを取得します。
        /// </summary>
        public List<(int sector, int maxZero)> GetValidConsecutivePairs()
        {
            var pairs = new List<(int sector, int maxZero)>();
            if (sweepSectors == null || sweepMaxConsecutiveZeros == null) return pairs;

            for (int s = 0; s < sweepSectors.Length; s++)
            {
                int sector = sweepSectors[s];
                for (int z = 0; z < sweepMaxConsecutiveZeros.Length; z++)
                {
                    int maxZero = sweepMaxConsecutiveZeros[z];
                    if (skipRedundantConditions && IsRedundantCombination(sector, maxZero))
                    {
                        continue;
                    }
                    pairs.Add((sector, maxZero));
                }
            }
            return pairs;
        }

        /// <summary>
        /// 分割の R_th (セクター閾値: 1〜8) または Average モードを固定したまま、
        /// 点群密度 (sweepDensities) とオクルージョン判定閾値 (sweepOcclusionThresholds) を順次変更しながら
        /// 全パターンの左右画像を自動一括キャプチャします。
        /// 保存先: outputDirectory / conditionName / Fixed_{Mode} / density_{X} / occ_{Y}
        /// </summary>
        public void RunDensityOcclusionThresholdSweep()
        {
            if (isCapturing) return;
            if (occlusionPipelineController == null)
            {
                FindDummyComponents();
            }
            if (occlusionPipelineController == null)
            {
                Debug.LogError("[SICESI] PCDOcclusionPipelineController が見つかりません。");
                return;
            }
            if (dummyPointCloudProvider == null)
            {
                Debug.LogError("[SICESI] RsDummyPointCloudProvider が設定されていません。");
                return;
            }
            if (sweepDensities == null || sweepDensities.Length == 0)
            {
                Debug.LogError("[SICESI] sweepDensities が設定されていません。");
                return;
            }
            if (sweepOcclusionThresholds == null || sweepOcclusionThresholds.Length == 0)
            {
                Debug.LogError("[SICESI] sweepOcclusionThresholds が設定されていません。");
                return;
            }

            StartCoroutine(DensityOcclusionThresholdSweepRoutine());
        }

        private IEnumerator DensityOcclusionThresholdSweepRoutine()
        {
            isCapturing = true;

            // 実験前の設定をバックアップ (終了時に確実に復元)
            var prevEvalMode = occlusionPipelineController.evaluationMode;
            var prevMinSectors = occlusionPipelineController.minOccludedSectors;
            var prevThreshold = occlusionPipelineController.occlusionThreshold;

            string modeFolderName = (fixedEvaluationMode == PCDRendererFeature.PCD_OcclusionEvaluationMode.Average)
                ? "Fixed_Average"
                : $"Fixed_Sector_{fixedMinOccludedSectors}";

            // 保存先ルート: SICESI_Dataset/{casesParentFolder}/{conditionName}/{modeFolderName}
            string conditionRootDir = ConditionRootDir;
            SaveSceneTransformsJson(conditionRootDir);
            string sweepTargetRootDir = Path.Combine(conditionRootDir, modeFolderName);

            string modeDesc = (fixedEvaluationMode == PCDRendererFeature.PCD_OcclusionEvaluationMode.Average)
                ? "Average (平均値)"
                : $"SectorThreshold (R_th = {fixedMinOccludedSectors})";

            int totalCombinations = sweepDensities.Length * sweepOcclusionThresholds.Length;
            Debug.Log($"[SICESI] === 密度 × オクルージョン閾値 スイープ開始 (条件: {conditionName}, 固定: {modeDesc}, 全{totalCombinations}組) ===");
            Debug.Log($"[SICESI] 出力先: {sweepTargetRootDir}");

            try
            {
                // Step 1: まず GT を撮影
                // sweepTargetRootDir/GT と conditionRootDir/GT の両方に保存して Python スクリプトの自動探索を確実化
                var backup = SetGroundTruthState(true);
                if (pointCloudObject != null) pointCloudObject.SetActive(false);

                for (int i = 0; i < 3; i++) yield return null;
                yield return new WaitForEndOfFrame();

                string gtDir = Path.Combine(sweepTargetRootDir, "GT");
                CaptureStereoViews(gtDir, "gt");

                string commonGtDir = Path.Combine(conditionRootDir, "GT");
                if (commonGtDir != gtDir)
                {
                    CaptureStereoViews(commonGtDir, "gt");
                }
                Debug.Log($"[SICESI] [1/2] Ground Truth 撮影完了: {gtDir}");

                RestoreGroundTruthState(backup);

                // Step 2: 点群表示に切り替え & 評価モードと R_th を設定
                if (pointCloudObject != null) pointCloudObject.SetActive(true);

                occlusionPipelineController.evaluationMode = fixedEvaluationMode;
                if (fixedEvaluationMode == PCDRendererFeature.PCD_OcclusionEvaluationMode.SectorThreshold)
                {
                    occlusionPipelineController.minOccludedSectors = fixedMinOccludedSectors;
                }

                string unitSuffix = GetDensityUnitSuffix();
                int progress = 0;

                // Step 3: 密度 × 閾値 のグリッドスイープ
                for (int d = 0; d < sweepDensities.Length; d++)
                {
                    float density = sweepDensities[d];
                    string densityStr = FormatFloat(density);
                    string densitySubDir = $"density_{densityStr}{unitSuffix}";

                    dummyPointCloudProvider.densityUnit = densityUnit;
                    dummyPointCloudProvider.densityValue = density;
                    dummyPointCloudProvider.ForceUpdateSampling();
                    Debug.Log($"[SICESI] 点群サンプリング更新: {dummyPointCloudProvider.LastSampledData.PointCount} 点 (密度: {densityStr}{unitSuffix})");

                    for (int t = 0; t < sweepOcclusionThresholds.Length; t++)
                    {
                        float threshold = sweepOcclusionThresholds[t];
                        string threshStr = FormatFloat(threshold);
                        progress++;

                        statusMessage = $"Sweep ({progress}/{totalCombinations}): Density={densityStr}{unitSuffix}, Thresh={threshStr}";
                        Debug.Log($"[SICESI] 設定適用 ({progress}/{totalCombinations}): 密度={densityStr}{unitSuffix}, オクルージョン閾値={threshStr}");

                        occlusionPipelineController.occlusionThreshold = threshold;

                        // 点群更新とURP描画の安定待機
                        for (int f = 0; f < waitFramesAfterDensityChange; f++) yield return null;
                        yield return new WaitForEndOfFrame();

                        string occFolder = $"occ_{threshStr}";
                        string targetDir = Path.Combine(sweepTargetRootDir, densitySubDir, occFolder);
                        string filePrefix = $"test_{densityStr}{unitSuffix}_occ_{threshStr}";
                        CaptureStereoViews(targetDir, filePrefix);
                        SaveEvaluationParamsJson(targetDir, density, threshold, fixedEvaluationMode, fixedMinOccludedSectors);

                        Debug.Log($"[SICESI] [{progress}/{totalCombinations}] 撮影完了: {densitySubDir}/{occFolder}");
                    }
                }

                statusMessage = "Density & Occlusion Sweep Completed!";
                Debug.Log($"[SICESI] === 密度 × オクルージョン閾値 スイープ完了! 保存先: {sweepTargetRootDir} ===");
            }
            finally
            {
                // 実験前の設定をリストア
                if (occlusionPipelineController != null)
                {
                    occlusionPipelineController.evaluationMode = prevEvalMode;
                    occlusionPipelineController.minOccludedSectors = prevMinSectors;
                    occlusionPipelineController.occlusionThreshold = prevThreshold;
                }
                isCapturing = false;
            }
        }

        private string GetDensityUnitSuffix()
        {
            switch (densityUnit)
            {
                case PointDensityUnit.PointSpacingMm:
                    return "mm";
                case PointDensityUnit.PointsPerCm2:
                    return "pts_cm2";
                case PointDensityUnit.PointsPerMm2:
                    return "pts_mm2";
                case PointDensityUnit.TotalPointCount:
                    return "pts";
                default:
                    return "";
            }
        }

        private static string FormatFloat(float val)
        {
            return val.ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture);
        }

        [System.Serializable]
        public class SerializableTransformData
        {
            public string name;
            public Vector3 position;
            public Vector3 rotationEuler;
            public Quaternion rotationQuaternion;
            public Vector3 lossyScale;
            public Vector3 localPosition;
            public Vector3 localEulerAngles;
            public Vector3 localScale;

            public static SerializableTransformData FromTransform(Transform t)
            {
                if (t == null) return null;
                return new SerializableTransformData
                {
                    name = t.name,
                    position = t.position,
                    rotationEuler = t.eulerAngles,
                    rotationQuaternion = t.rotation,
                    lossyScale = t.lossyScale,
                    localPosition = t.localPosition,
                    localEulerAngles = t.localEulerAngles,
                    localScale = t.localScale
                };
            }
        }

        [System.Serializable]
        public class SceneTransformsData
        {
            public string timestamp;
            public string conditionName;
            public SerializableTransformData handMesh;
            public SerializableTransformData virtualObject;
            public SerializableTransformData leftCamera;
            public SerializableTransformData rightCamera;
        }

        /// <summary>
        /// 現在の手メッシュ (groundTruthObject) と仮想オブジェクト (virtualObject) およびカメラの Transform 情報を JSON に保存します。
        /// </summary>
        public void SaveSceneTransformsJson(string targetDir)
        {
            try
            {
                var data = new SceneTransformsData
                {
                    timestamp = System.DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                    conditionName = this.conditionName,
                    handMesh = SerializableTransformData.FromTransform(groundTruthObject != null ? groundTruthObject.transform : null),
                    virtualObject = SerializableTransformData.FromTransform(virtualObject != null ? virtualObject.transform : null),
                    leftCamera = SerializableTransformData.FromTransform(leftEyeCamera != null ? leftEyeCamera.transform : null),
                    rightCamera = SerializableTransformData.FromTransform(rightEyeCamera != null ? rightEyeCamera.transform : null)
                };

                string json = JsonUtility.ToJson(data, true);
                Directory.CreateDirectory(targetDir);
                string savePath = Path.Combine(targetDir, "scene_transforms.json");
                File.WriteAllText(savePath, json);
                Debug.Log($"[SICESI] Transform 情報を保存しました: {savePath}");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[SICESI] scene_transforms.json 保存失敗: {ex.Message}");
            }
        }

        [System.Serializable]
        private class EvaluationParamsData
        {
            public string conditionName;
            public float densityValue;
            public string densityUnit;
            public float occlusionThreshold;
            public string evaluationMode;
            public int minOccludedSectors;
            public int maxConsecutiveEmptySectors;
            public string timestamp;
            public SerializableTransformData handMeshTransform;
            public SerializableTransformData virtualObjectTransform;
        }

        /// <summary>
        /// 撮影時の正確な設定値 (丸めなしの float) を JSON として記録します。
        /// </summary>
        private void SaveEvaluationParamsJson(string targetDir, float density, float threshold, PCDRendererFeature.PCD_OcclusionEvaluationMode mode, int sectors, int maxZeros = 8)
        {
            try
            {
                var data = new EvaluationParamsData
                {
                    conditionName = this.conditionName,
                    densityValue = density,
                    densityUnit = this.densityUnit.ToString(),
                    occlusionThreshold = threshold,
                    evaluationMode = mode.ToString(),
                    minOccludedSectors = sectors,
                    maxConsecutiveEmptySectors = maxZeros,
                    timestamp = System.DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                    handMeshTransform = SerializableTransformData.FromTransform(groundTruthObject != null ? groundTruthObject.transform : null),
                    virtualObjectTransform = SerializableTransformData.FromTransform(virtualObject != null ? virtualObject.transform : null)
                };
                string json = JsonUtility.ToJson(data, true);
                Directory.CreateDirectory(targetDir);
                File.WriteAllText(Path.Combine(targetDir, "evaluation_params.json"), json);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[SICESI] evaluation_params.json 保存失敗: {ex.Message}");
            }
        }

        public GroundTruthStateBackup SetGroundTruthState(bool enable)
        {
            var backup = new GroundTruthStateBackup();
            if (groundTruthObject == null) return backup;

            if (enable)
            {
                // 1. Layer を一時変更 (PCDレイヤー等)
                if (!string.IsNullOrEmpty(groundTruthCaptureLayer))
                {
                    int targetLayer = LayerMask.NameToLayer(groundTruthCaptureLayer);
                    if (targetLayer >= 0)
                    {
                        var transforms = groundTruthObject.GetComponentsInChildren<Transform>(true);
                        foreach (var t in transforms)
                        {
                            backup.Layers[t.gameObject] = t.gameObject.layer;
                            t.gameObject.layer = targetLayer;
                        }
                        Debug.Log($"[SICESI] GT撮影のため一時的に Layer を '{groundTruthCaptureLayer}' (ID: {targetLayer}) に変更しました。");
                    }
                    else
                    {
                        Debug.LogWarning($"[SICESI] 指定レイヤー '{groundTruthCaptureLayer}' が見つかりません。ProjectSettings > Tags and Layers を確認してください。");
                    }
                }

                // 2. マテリアルを黒に一時変更
                if (renderGroundTruthAsBlack)
                {
                    if (_blackMaterialCache == null)
                    {
                        Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                        _blackMaterialCache = new Material(unlitShader)
                        {
                            color = Color.black,
                            name = "SICESI_Black_GT_Mat"
                        };
                        if (_blackMaterialCache.HasProperty("_BaseColor"))
                        {
                            _blackMaterialCache.SetColor("_BaseColor", Color.black);
                        }
                    }

                    var renderers = groundTruthObject.GetComponentsInChildren<Renderer>(true);
                    foreach (var r in renderers)
                    {
                        backup.Materials[r] = r.sharedMaterials;
                        Material[] blackMats = new Material[r.sharedMaterials.Length];
                        for (int m = 0; m < blackMats.Length; m++)
                        {
                            blackMats[m] = _blackMaterialCache;
                        }
                        r.sharedMaterials = blackMats;
                    }
                }
            }

            return backup;
        }

        public void RestoreGroundTruthState(GroundTruthStateBackup backup)
        {
            if (backup == null) return;

            // 1. レイヤーを元に戻す
            foreach (var kvp in backup.Layers)
            {
                if (kvp.Key != null)
                {
                    kvp.Key.layer = kvp.Value;
                }
            }

            // 2. マテリアルを元に戻す
            foreach (var kvp in backup.Materials)
            {
                if (kvp.Key != null)
                {
                    kvp.Key.sharedMaterials = kvp.Value;
                }
            }
        }

        /// <summary>
        /// 左右のカメラから画像を取得して保存します。
        /// </summary>
        public void CaptureStereoViews(string baseDirectory, string filePrefix, bool bypassSRGBConversion = false)
        {
            CaptureBothEyeImages(baseDirectory, filePrefix, bypassSRGBConversion);
        }

        /// <summary>
        /// 左右のカメラから画像を取得して保存します (CaptureStereoViews と同一)。
        /// </summary>
        public void CaptureBothEyeImages(string baseDirectory, string filePrefix, bool bypassSRGBConversion = false)
        {
            if (leftEyeCamera != null)
            {
                string leftPath = Path.Combine(baseDirectory, "Left", $"{filePrefix}_left.png");
                SaveCameraView(leftEyeCamera, leftPath, bypassSRGBConversion);
            }
            else
            {
                Debug.LogWarning("[SICESI] LeftEyeCamera が未設定です。");
            }

            if (rightEyeCamera != null)
            {
                string rightPath = Path.Combine(baseDirectory, "Right", $"{filePrefix}_right.png");
                SaveCameraView(rightEyeCamera, rightPath, bypassSRGBConversion);
            }
            else
            {
                Debug.LogWarning("[SICESI] RightEyeCamera が未設定です。");
            }
        }

        /// <summary>
        /// 指定カメラの描画結果 (targetTexture またはバックバッファ) からピクセルを読み出してPNG保存します。
        /// 通常画像ではリニア色空間から sRGB ガンマ補正を正しく適用して保存します。
        /// 占有マスク・デバッグ判定マスク等の数値データ画像では bypassSRGBConversion=true によりガンマ歪みを防止します。
        /// </summary>
        public void SaveCameraView(Camera cam, string destinationPath, bool bypassSRGBConversion = false)
        {
            int width = cam.pixelWidth > 0 ? cam.pixelWidth : Screen.width;
            int height = cam.pixelHeight > 0 ? cam.pixelHeight : Screen.height;

            RenderTexture prevActive = RenderTexture.active;
            Texture2D screenshot = new Texture2D(width, height, TextureFormat.RGB24, false);

            if (cam.targetTexture != null)
            {
                if (!bypassSRGBConversion && applySRGBConversion && QualitySettings.desiredColorSpace == ColorSpace.Linear)
                {
                    // Linear RT から sRGB 補正をかけて一時RTへBlit
                    RenderTexture tempSRGB = RenderTexture.GetTemporary(
                        width, height, 0,
                        RenderTextureFormat.ARGB32,
                        RenderTextureReadWrite.sRGB
                    );
                    Graphics.Blit(cam.targetTexture, tempSRGB);
                    RenderTexture.active = tempSRGB;
                    screenshot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    RenderTexture.ReleaseTemporary(tempSRGB);
                }
                else
                {
                    RenderTexture.active = cam.targetTexture;
                    screenshot.ReadPixels(new Rect(0, 0, cam.targetTexture.width, cam.targetTexture.height), 0, 0);
                }
            }
            else
            {
                RenderTexture.active = null;
                Rect screenRect = cam.rect;
                int x = Mathf.RoundToInt(screenRect.x * Screen.width);
                int y = Mathf.RoundToInt(screenRect.y * Screen.height);
                screenshot.ReadPixels(new Rect(x, y, width, height), 0, 0);
            }

            RenderTexture.active = prevActive;

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
            byte[] pngBytes = screenshot.EncodeToPNG();
            Destroy(screenshot);

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    File.WriteAllBytes(destinationPath, pngBytes);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[SICESI] 非同期画像保存エラー ({destinationPath}): {ex.Message}");
                }
            });
        }
    }
}
