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

        [Header("Objects To Toggle")]
        [Tooltip("Ground Truth (正解) として表示する手メッシュなどのオブジェクト")]
        public GameObject groundTruthObject;

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

        [Header("Output Settings")]
        [Tooltip("出力先ルートディレクトリ")]
        public string outputDirectory = @"C:\Users\hongo\Documents\tsutsumi\Estimation\SICESI_Dataset";

        [Tooltip("現在の実験条件名 (例: Proposed_Oct, Proposed_Hex, Bouchiba など)")]
        public string conditionName = "Proposed_Oct";

        [Header("Capture Status")]
        public bool isCapturing = false;
        public string statusMessage = "Ready";

        private Material _blackMaterialCache;

        private class GroundTruthStateBackup
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

            string conditionRootDir = Path.Combine(outputDirectory, conditionName);
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

            string folder = string.IsNullOrEmpty(subFolderName) ? conditionName : Path.Combine(conditionName, subFolderName);
            string targetDir = Path.Combine(outputDirectory, folder);
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
            string conditionRootDir = Path.Combine(outputDirectory, conditionName);
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
                statusMessage = $"Running sweep ({i + 1}/{sweepDensities.Length}): Density = {density:F2}{unitSuffix}";
                Debug.Log($"[SICESI] 密度設定変更: {density:F2} ({densityUnit})");

                dummyPointCloudProvider.densityUnit = densityUnit;
                dummyPointCloudProvider.densityValue = density;
                dummyPointCloudProvider.ForceUpdateSampling();

                Debug.Log($"[SICESI] 点群サンプリング強制更新完了: {dummyPointCloudProvider.LastSampledData.PointCount} 点 (密度: {density:F2}{unitSuffix})");

                // 点群サンプリングとGPU転送・URP描画の反映を待機
                for (int f = 0; f < waitFramesAfterDensityChange; f++)
                {
                    yield return null;
                }
                yield return new WaitForEndOfFrame();

                string densityFolder = $"density_{density:F2}{unitSuffix}";
                string targetDir = Path.Combine(conditionRootDir, densityFolder);
                CaptureStereoViews(targetDir, $"test_{density:F2}{unitSuffix}");

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
            // ConditionName を最上位フォルダーとする階層構造: SICESI_Dataset/{conditionName}/
            string conditionRootDir = Path.Combine(outputDirectory, conditionName);
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
                    string densitySubDir = $"density_{density:F2}{unitSuffix}";

                    if (dummyPointCloudProvider != null)
                    {
                        dummyPointCloudProvider.densityUnit = densityUnit;
                        dummyPointCloudProvider.densityValue = density;
                        dummyPointCloudProvider.ForceUpdateSampling();
                        Debug.Log($"[SICESI] 点群サンプリング強制更新完了: {dummyPointCloudProvider.LastSampledData.PointCount} 点 (密度: {density:F2}{unitSuffix})");
                    }

                    // (A) 従来の Average モード
                    if (includeAverageMode)
                    {
                        progress++;
                        statusMessage = $"Sector Sweep ({progress}/{totalCombinations}): Density={density:F2}{unitSuffix}, Mode=Average";
                        Debug.Log($"[SICESI] 設定変更 ({progress}/{totalCombinations}): 密度={density:F2}{unitSuffix}, 評価モード=Average");

                        occlusionPipelineController.evaluationMode = PCDRendererFeature.PCD_OcclusionEvaluationMode.Average;

                        for (int f = 0; f < waitFramesAfterDensityChange; f++) yield return null;
                        yield return new WaitForEndOfFrame();

                        string avgFolder = Path.Combine(densitySubDir, "Average");
                        string avgTargetDir = Path.Combine(conditionRootDir, avgFolder);
                        CaptureStereoViews(avgTargetDir, $"test_{density:F2}{unitSuffix}_Average");

                        Debug.Log($"[SICESI] [{progress}/{totalCombinations}] 撮影完了: {avgFolder}");
                    }

                    // (B) 各セクター閾値モード (SectorThreshold)
                    occlusionPipelineController.evaluationMode = PCDRendererFeature.PCD_OcclusionEvaluationMode.SectorThreshold;
                    for (int s = 0; s < sweepSectors.Length; s++)
                    {
                        int sector = sweepSectors[s];
                        progress++;
                        statusMessage = $"Sector Sweep ({progress}/{totalCombinations}): Density={density:F2}{unitSuffix}, Sector={sector}";
                        Debug.Log($"[SICESI] 設定変更 ({progress}/{totalCombinations}): 密度={density:F2}{unitSuffix}, セクター={sector}/8");

                        occlusionPipelineController.minOccludedSectors = sector;

                        for (int f = 0; f < waitFramesAfterDensityChange; f++) yield return null;
                        yield return new WaitForEndOfFrame();

                        string folder = Path.Combine(densitySubDir, $"sector_{sector}");
                        string targetDir = Path.Combine(conditionRootDir, folder);
                        CaptureStereoViews(targetDir, $"test_{density:F2}{unitSuffix}_sector_{sector}");

                        Debug.Log($"[SICESI] [{progress}/{totalCombinations}] 撮影完了: {folder}");
                    }
                }
            }
            else
            {
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

                    Debug.Log($"[SICESI] [{progress}/{totalCombinations}] 撮影完了: {sectorFolder}");
                }
            }

            statusMessage = "All Sector Sweeps Completed!";
            Debug.Log($"[SICESI] === セクタースイープキャプチャ完了! 保存先: {conditionRootDir} ===");
            isCapturing = false;
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

        private GroundTruthStateBackup SetGroundTruthState(bool enable)
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

        private void RestoreGroundTruthState(GroundTruthStateBackup backup)
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
        private void CaptureStereoViews(string baseDirectory, string filePrefix)
        {
            if (leftEyeCamera != null)
            {
                string leftPath = Path.Combine(baseDirectory, "Left", $"{filePrefix}_left.png");
                SaveCameraView(leftEyeCamera, leftPath);
            }
            else
            {
                Debug.LogWarning("[SICESI] LeftEyeCamera が未設定です。");
            }

            if (rightEyeCamera != null)
            {
                string rightPath = Path.Combine(baseDirectory, "Right", $"{filePrefix}_right.png");
                SaveCameraView(rightEyeCamera, rightPath);
            }
            else
            {
                Debug.LogWarning("[SICESI] RightEyeCamera が未設定です。");
            }
        }

        /// <summary>
        /// 指定カメラの描画結果 (targetTexture またはバックバッファ) からピクセルを読み出してPNG保存します。
        /// リニア色空間から sRGB ガンマ補正を正しく適用して保存します。
        /// </summary>
        private void SaveCameraView(Camera cam, string destinationPath)
        {
            int width = cam.pixelWidth > 0 ? cam.pixelWidth : Screen.width;
            int height = cam.pixelHeight > 0 ? cam.pixelHeight : Screen.height;

            RenderTexture prevActive = RenderTexture.active;
            Texture2D screenshot = new Texture2D(width, height, TextureFormat.RGB24, false);

            if (cam.targetTexture != null)
            {
                if (applySRGBConversion && QualitySettings.desiredColorSpace == ColorSpace.Linear)
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

            screenshot.Apply();
            RenderTexture.active = prevActive;

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
            File.WriteAllBytes(destinationPath, screenshot.EncodeToPNG());
            Destroy(screenshot);
        }
    }
}
