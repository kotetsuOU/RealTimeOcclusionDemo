using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using RealSense.DummyPointCloud;

namespace SICESI
{
    /// <summary>
    /// SICE SI 発表用 評価コントローラー (Facade)
    /// 左右眼カメラ映像および Ground Truth (手メッシュ) / 各密度での点群遮蔽画像の自動キャプチャを統括します。
    /// 内部ロジックは SICESI_MaterialSwapper, SICESI_ScreenCaptureUtil, SICESI_StereoSweepRunner に責務分離されています。
    /// </summary>
    [DisallowMultipleComponent]
    public class SICESI_StereoEvaluationController : MonoBehaviour
    {
        #region 設定パラメータ

        [Header("Target Cameras")]
        [Tooltip("左目用カメラ (SRDisplay LeftEyeCamera または単体カメラ)")]
        public Camera leftEyeCamera;

        [Tooltip("右目用カメラ (SRDisplay RightEyeCamera または単体カメラ)")]
        public Camera rightEyeCamera;

        [Tooltip("シーン全体の様子を撮影・保存するためのカメラ (俯瞰カメラや第三者視点カメラなど)")]
        public Camera sceneCaptureCamera;

        [Header("Scene Overview Capture Settings")]
        [Tooltip("シーン保存カメラでの撮影時に GroundTruthObject (手メッシュ) のマテリアルに適用する肌色")]
        public Color groundTruthSkinColor = new Color(241f / 255f, 187f / 255f, 147f / 255f, 1f);

        [Tooltip("シーン保存時に左右目線カメラの映像も一緒に保存するか (scene_left.png, scene_right.png)")]
        public bool captureStereoEyesWithScene = true;

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

        [Header("Virtual Object Material Settings")]
        [Tooltip("映像取得時に、仮想オブジェクトのマテリアルを Universal Render Pipeline/Unlit かつ完全な白に差し替えるかどうか")]
        public bool renderVirtualObjectAsUnlitWhite = true;

        [Header("Density Sweep Settings")]
        [Tooltip("点群の物理密度の指定単位")]
        public PointDensityUnit densityUnit = PointDensityUnit.PointSpacingMm;

        [Tooltip("評価実験でスイープする点群密度のリスト")]
        public float[] sweepDensities = new float[] { 1.0f, 2.0f, 3.0f, 4.0f, 5.0f };

        [Tooltip("密度変更後、点群生成とGPU描画の反映を待機するフレーム数")]
        [Range(1, 15)]
        public int waitFramesAfterDensityChange = 5;

        [Header("Color Space & Capture Quality")]
        [Tooltip("Linear色空間から追加でsRGBガンマ補正を行うか")]
        public bool applySRGBConversion = false;

        [Header("Sector Sweep Settings")]
        [Tooltip("オクルージョンパイプラインのコントローラー")]
        public PCDOcclusionPipelineController occlusionPipelineController;

        [Tooltip("オクルージョン評価でスイープするセクター閾値のリスト (1〜8)")]
        public int[] sweepSectors = new int[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        [Tooltip("セクタースイープ時に点群密度も全パターン組み合わせて一括撮影するか")]
        public bool sweepDensitiesAcrossSectors = true;

        [Tooltip("セクタースイープ時に、比較用として従来の Average (平均値判定) モードも各密度で一緒に撮影するか")]
        public bool includeAverageMode = true;

        [Header("Consecutive Unoccupied Sectors Sweep Settings (SICE 2026 Proposed)")]
        [Tooltip("新手法: 許容最大連続非占有セクター数 L_th のスイープリスト (0〜8)")]
        public int[] sweepMaxConsecutiveZeros = new int[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 };

        [Tooltip("数学的・幾何学的に等価な重複条件を自動スキップする")]
        public bool skipRedundantConditions = true;

        [Tooltip("連続非占有数スイープ時に点群密度も全パターン組み合わせて一括撮影するか")]
        public bool sweepDensitiesAcrossConsecutive = false;

        [Header("Density & Occlusion Threshold Sweep Settings")]
        [Tooltip("固定する評価モード")]
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

        [Tooltip("現在の実験条件・ケース名 (例: RawTest, RawTest_case1 など)")]
        public string conditionName = "RawTest";

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

        #endregion

        #region 内部コンポーネント

        private SICESI_MaterialSwapper _materialSwapper;
        private SICESI_SnapshotRunner _snapshotRunner;
        private SICESI_StereoSweepRunner _stereoSweepRunner;

        public SICESI_MaterialSwapper MaterialSwapper => _materialSwapper ?? (_materialSwapper = new SICESI_MaterialSwapper());
        public SICESI_SnapshotRunner SnapshotRunner => _snapshotRunner ?? (_snapshotRunner = new SICESI_SnapshotRunner(this, MaterialSwapper));
        public SICESI_StereoSweepRunner StereoSweepRunner => _stereoSweepRunner ?? (_stereoSweepRunner = new SICESI_StereoSweepRunner(this, MaterialSwapper));

        // 後方互換性エイリアス
        public SICESI_StereoSweepRunner SweepRunner => StereoSweepRunner;

        #endregion

        #region 後方互換用型定義・エイリアス

        public class GroundTruthStateBackup : SICESI_MaterialSwapper.GroundTruthStateBackup { }
        public class GroundTruthSkinColorBackup : SICESI_MaterialSwapper.GroundTruthSkinColorBackup { }
        public class VirtualObjectStateBackup : SICESI_MaterialSwapper.VirtualObjectStateBackup { }
        public class SerializableTransformData : SICESI_ScreenCaptureUtil.SerializableTransformData { }
        public class SceneTransformsData : SICESI_ScreenCaptureUtil.SceneTransformsData { }

        #endregion

        #region Unity ライフサイクル

        private void Awake()
        {
            _materialSwapper = new SICESI_MaterialSwapper();
            _snapshotRunner = new SICESI_SnapshotRunner(this, _materialSwapper);
            _stereoSweepRunner = new SICESI_StereoSweepRunner(this, _materialSwapper);
        }

        private void OnDestroy()
        {
            if (_materialSwapper != null)
            {
                _materialSwapper.Dispose();
                _materialSwapper = null;
            }
        }

        private void Reset()
        {
            FindCameras();
            FindDummyComponents();
        }

        #endregion

        #region コンポーネント自動検索

        public void FindCameras()
        {
            SICESI_SceneComponentLocator.LocateCameras(ref leftEyeCamera, ref rightEyeCamera, ref sceneCaptureCamera);
        }

        public void FindDummyComponents()
        {
            SICESI_SceneComponentLocator.LocateDummyComponents(ref dummyPointCloudProvider, ref pointCloudObject, ref occlusionPipelineController, ref virtualObject);
        }

        #endregion

        #region スイープ実行 API (Facade)

        public void CaptureGroundTruth()
        {
            if (isCapturing) return;
            StartCoroutine(SnapshotRunner.CaptureGroundTruthRoutine());
        }

        public void CaptureCurrentCondition(string subFolderName = "")
        {
            if (isCapturing) return;
            StartCoroutine(SnapshotRunner.CaptureCurrentRoutine(subFolderName));
        }

        public void RunDensitySweep()
        {
            if (isCapturing) return;
            if (dummyPointCloudProvider == null)
            {
                Debug.LogError("[SICESI] RsDummyPointCloudProvider が設定されていません。");
                return;
            }
            StartCoroutine(StereoSweepRunner.DensitySweepRoutine());
        }

        public void RunSectorSweep()
        {
            if (isCapturing) return;
            if (occlusionPipelineController == null)
            {
                Debug.LogError("[SICESI] PCDOcclusionPipelineController が設定されていません。");
                return;
            }
            StartCoroutine(StereoSweepRunner.SectorSweepRoutine());
        }

        public void RunConsecutiveSectorSweep()
        {
            if (isCapturing) return;
            if (occlusionPipelineController == null)
            {
                Debug.LogError("[SICESI] PCDOcclusionPipelineController が設定されていません。");
                return;
            }
            StartCoroutine(StereoSweepRunner.ConsecutiveSectorSweepRoutine());
        }

        public void RunDensityOcclusionThresholdSweep()
        {
            if (isCapturing) return;
            StartCoroutine(StereoSweepRunner.DensityOcclusionThresholdSweepRoutine());
        }

        public void CaptureSceneOverview()
        {
            if (isCapturing) return;
            StartCoroutine(SnapshotRunner.CaptureSceneViewRoutine());
        }

        public IEnumerator CaptureSceneViewRoutine(string primaryPath, string[] duplicatePaths = null)
        {
            isCapturing = true;
            statusMessage = "Capturing Scene Overview...";

            var skinBackup = SetGroundTruthSkinState(true);
            try
            {
                if (pointCloudObject != null) pointCloudObject.SetActive(true);

                for (int i = 0; i < 3; i++) yield return null;
                yield return new WaitForEndOfFrame();

                if (sceneCaptureCamera != null)
                {
                    SaveCameraView(sceneCaptureCamera, primaryPath);
                    if (duplicatePaths != null)
                    {
                        foreach (var dp in duplicatePaths)
                        {
                            SaveCameraView(sceneCaptureCamera, dp);
                        }
                    }
                }

                if (captureStereoEyesWithScene)
                {
                    string sceneDir = Path.GetDirectoryName(primaryPath);
                    SaveEyeViewsForScene(sceneDir);
                }

                statusMessage = "Scene Overview Capture Completed!";
            }
            finally
            {
                RestoreGroundTruthSkinState(skinBackup);
                isCapturing = false;
            }
        }

        #endregion

        #region マテリアル・レイヤー委譲メソッド

        public SICESI_MaterialSwapper.GroundTruthStateBackup SetGroundTruthState(bool enable)
        {
            return MaterialSwapper.SetGroundTruthState(groundTruthObject, groundTruthCaptureLayer, renderGroundTruthAsBlack);
        }

        public void RestoreGroundTruthState(SICESI_MaterialSwapper.GroundTruthStateBackup backup)
        {
            MaterialSwapper.RestoreGroundTruthState(backup);
        }

        public SICESI_MaterialSwapper.VirtualObjectStateBackup SetVirtualObjectUnlitWhiteState(bool enable)
        {
            return MaterialSwapper.SetVirtualObjectUnlitWhiteState(virtualObject, renderVirtualObjectAsUnlitWhite);
        }

        public void RestoreVirtualObjectState(SICESI_MaterialSwapper.VirtualObjectStateBackup backup)
        {
            MaterialSwapper.RestoreVirtualObjectState(backup);
        }

        public SICESI_MaterialSwapper.GroundTruthSkinColorBackup SetGroundTruthSkinState(bool enable)
        {
            return MaterialSwapper.SetGroundTruthSkinState(groundTruthObject, groundTruthCaptureLayer, groundTruthSkinColor);
        }

        public void RestoreGroundTruthSkinState(SICESI_MaterialSwapper.GroundTruthSkinColorBackup backup)
        {
            MaterialSwapper.RestoreGroundTruthSkinState(groundTruthObject, backup);
        }

        #endregion

        #region キャプチャ・JSON 委譲メソッド

        public void SaveCameraView(Camera cam, string destinationPath, bool bypassSRGBConversion = false)
        {
            SICESI_ScreenCaptureUtil.SaveCameraView(cam, destinationPath, applySRGBConversion, bypassSRGBConversion);
        }

        public void CaptureStereoViews(string targetDir, string filePrefix, bool bypassSRGBConversion = false)
        {
            string leftDir = Path.Combine(targetDir, "Left");
            string rightDir = Path.Combine(targetDir, "Right");
            Directory.CreateDirectory(leftDir);
            Directory.CreateDirectory(rightDir);

            if (leftEyeCamera != null)
            {
                string leftPath = Path.Combine(leftDir, $"{filePrefix}_left.png");
                SaveCameraView(leftEyeCamera, leftPath, bypassSRGBConversion);
            }

            if (rightEyeCamera != null)
            {
                string rightPath = Path.Combine(rightDir, $"{filePrefix}_right.png");
                SaveCameraView(rightEyeCamera, rightPath, bypassSRGBConversion);
            }
        }

        public void SaveEyeViewsForScene(string sceneDir)
        {
            SICESI_ScreenCaptureUtil.SaveEyeViewsForScene(leftEyeCamera, rightEyeCamera, sceneDir, applySRGBConversion);
        }

        public void SaveSceneTransformsJson(string targetDir)
        {
            SICESI_ScreenCaptureUtil.SaveSceneTransformsJson(targetDir, conditionName, groundTruthObject, virtualObject, leftEyeCamera, rightEyeCamera, sceneCaptureCamera);
        }

        public bool LoadAndApplySceneTransformsJson(string jsonPath)
        {
            return SICESI_ScreenCaptureUtil.LoadAndApplySceneTransformsJson(jsonPath, groundTruthObject, virtualObject, leftEyeCamera, rightEyeCamera, sceneCaptureCamera, out conditionName);
        }

        public string GetDensityUnitSuffix()
        {
            switch (densityUnit)
            {
                case PointDensityUnit.PointSpacingMm: return "mm";
                case PointDensityUnit.PointsPerCm2: return "pts_cm2";
                case PointDensityUnit.PointsPerMm2: return "pts_mm2";
                case PointDensityUnit.TotalPointCount: return "pts";
                default: return "";
            }
        }

        /// <summary>
        /// 連続非占有セクター許容規則において、幾何学的に重複・等価な条件を判定します。
        /// </summary>
        public static bool IsRedundantCombination(int rTh, int lTh, int K = 8)
        {
            return SICESI_ConsecutivePairEvaluator.IsRedundantCombination(rTh, lTh, K);
        }

        /// <summary>
        /// 有効な（重複スキップ設定が反映された）占有数と最大連続非占有数の組み合わせペアリストを取得します。
        /// </summary>
        public List<(int sector, int maxZero)> GetValidConsecutivePairs()
        {
            return SICESI_ConsecutivePairEvaluator.GenerateValidPairs(sweepSectors, sweepMaxConsecutiveZeros, skipRedundantConditions);
        }

        #endregion
    }
}
