using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using RealSense.DummyPointCloud;
using Core.Logging;

namespace SICESI
{
    /// <summary>
    /// SICE SI 発表用 評価コントローラー (Facade)
    /// 左右眼カメラ映像および Ground Truth (手メッシュ) / 各密度での点群遮蔽画像の自動キャプチャを統括します。
    /// 内部ロジックは SICESI_MaterialSwapper, SICESI_ScreenCaptureUtil, SICESI_StereoSweepRunner に責務分離されています。
    /// </summary>
    [AppLoggable("SICESI")]
    [DisallowMultipleComponent]
    public class SICESI_StereoEvaluationController : MonoBehaviour, IAppLoggable
    {
        // --- ログサブタグ定数 ---
        public const string TagCore         = "SICESI_Core";
        public const string TagCapture      = "SICESI_Capture";
        public const string TagStereoSweep  = "SICESI_StereoSweep";
        public const string TagMaskSweep    = "SICESI_MaskSweep";
        public const string TagOptRunner    = "SICESI_OptRunner";

        public void RegisterLogTriggers(LogCategoryGroup group, HashSet<string> existingLabels)
        {
            AddSubTrigger(group, this, "[SICESI] Core Controller", TagCore, existingLabels);
            AddSubTrigger(group, this, "[SICESI] Capture & Camera", TagCapture, existingLabels);
            AddSubTrigger(group, this, "[SICESI] Stereo Sweep", TagStereoSweep, existingLabels);
            AddSubTrigger(group, this, "[SICESI] Sector Mask Sweep", TagMaskSweep, existingLabels);
            AddSubTrigger(group, this, "[SICESI] Python Optimization", TagOptRunner, existingLabels);
        }

        private static void AddSubTrigger(LogCategoryGroup group, UnityEngine.Object target, string label, string tag, HashSet<string> existing)
        {
            if (existing.Contains(label)) return;
            group.entries.Add(new LogInstanceEntry
            {
                label   = label,
                tag     = tag,
                target  = target,
                enabled = true
            });
            existing.Add(label);
        }

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

        [Tooltip("点群マテリアルおよびカラーを制御するコントローラー")]
        public RsMaterialController materialController;

        public RsMaterialController MaterialController
        {
            get
            {
                if (materialController == null)
                {
#if UNITY_2023_1_OR_NEWER
                    materialController = FindFirstObjectByType<RsMaterialController>();
#else
                    materialController = FindObjectOfType<RsMaterialController>();
#endif
                }
                return materialController;
            }
        }

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

        [Tooltip("スイープするオクルージョン判定閾値のリスト (0.0〜1.0)。固定評価モードが Average の場合は T_ave、その他手法の場合は T_loc に適用されます")]
        public float[] sweepOcclusionThresholds = new float[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f };

        [Header("Output Settings")]
        [Tooltip("出力先ルートディレクトリ")]
        public string outputDirectory = @"C:\Users\hongo\Documents\tsutsumi\Estimation\SICESI_Dataset";

        [HideInInspector]
        public string casesParentFolder = "RawTest_Cases";

        [Tooltip("現在の実験条件・ケース名 (例: RawTest, RawTest_case1 など)")]
        public string conditionName = "RawTest";

        [HideInInspector]
        public string customConditionRootDir = null;

        public string ConditionRootDir
        {
            get
            {
                if (!string.IsNullOrEmpty(customConditionRootDir))
                {
                    return customConditionRootDir;
                }
                if (!string.IsNullOrEmpty(casesParentFolder))
                {
                    return Path.Combine(outputDirectory, casesParentFolder, conditionName);
                }
                return Path.Combine(outputDirectory, conditionName);
            }
        }

        [Header("Dataset Configuration (Training 3-Objects & Generalization Sets)")]
        [Tooltip("最適化・ルール導出用 (3オブジェクト等) および新規評価用の仮想オブジェクト & 手メッシュ設定")]
        public SICESI_EvaluationDatasetConfig datasetConfig = new SICESI_EvaluationDatasetConfig();

        [Header("Capture Status")]
        public bool isCapturing = false;
        public string statusMessage = "Ready";

        #endregion

        #region 内部コンポーネント

        private SICESI_MaterialSwapper _materialSwapper;
        private SICESI_SnapshotRunner _snapshotRunner;
        private SICESI_StereoSweepRunner _stereoSweepRunner;
        private SICESI_EvaluationDispatcher _dispatcher;
        private SICESI_BatchReSweepRunner _batchReSweepRunner;
        private SICESI_GeneralizationBenchmarkRunner _generalizationRunner;
        private SICESI_PythonOptimizationRunner _pythonOptimizationRunner;
        private SICESI_OptimizationPresetRunner _optimizationPresetRunner;
        private SICESI_AverageThresholdOptimizationRunner _averageThresholdOptimizationRunner;

        public SICESI_MaterialSwapper MaterialSwapper => _materialSwapper ?? (_materialSwapper = new SICESI_MaterialSwapper());
        public SICESI_SnapshotRunner SnapshotRunner => _snapshotRunner ?? (_snapshotRunner = new SICESI_SnapshotRunner(this, MaterialSwapper));
        public SICESI_StereoSweepRunner StereoSweepRunner => _stereoSweepRunner ?? (_stereoSweepRunner = new SICESI_StereoSweepRunner(this, MaterialSwapper));
        public SICESI_EvaluationDispatcher Dispatcher => _dispatcher ?? (_dispatcher = new SICESI_EvaluationDispatcher(this));
        public SICESI_BatchReSweepRunner BatchReSweepRunner => _batchReSweepRunner ?? (_batchReSweepRunner = new SICESI_BatchReSweepRunner(this));
        public SICESI_GeneralizationBenchmarkRunner GeneralizationRunner => _generalizationRunner ?? (_generalizationRunner = new SICESI_GeneralizationBenchmarkRunner(this));
        public SICESI_PythonOptimizationRunner PythonOptimizationRunner => _pythonOptimizationRunner ?? (_pythonOptimizationRunner = new SICESI_PythonOptimizationRunner(this));
        public SICESI_OptimizationPresetRunner OptimizationPresetRunner => _optimizationPresetRunner ?? (_optimizationPresetRunner = new SICESI_OptimizationPresetRunner(this));
        public SICESI_AverageThresholdOptimizationRunner AverageThresholdOptimizationRunner => _averageThresholdOptimizationRunner ?? (_averageThresholdOptimizationRunner = new SICESI_AverageThresholdOptimizationRunner(this));

        // 後方互換性エイリアス
        public SICESI_StereoSweepRunner SweepRunner => StereoSweepRunner;

        #endregion


        #region Unity ライフサイクル

        private void Awake()
        {
            _materialSwapper = new SICESI_MaterialSwapper();
            _snapshotRunner = new SICESI_SnapshotRunner(this, _materialSwapper);
            _stereoSweepRunner = new SICESI_StereoSweepRunner(this, _materialSwapper);
            _dispatcher = new SICESI_EvaluationDispatcher(this);
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

        #region コンポーネント自動検索 & 点群ターゲット同期

        public void FindCameras()
            => SICESI_SceneComponentLocator.LocateCameras(ref leftEyeCamera, ref rightEyeCamera, ref sceneCaptureCamera);

        public void FindDummyComponents()
            => SICESI_SceneComponentLocator.LocateDummyComponents(ref dummyPointCloudProvider, ref pointCloudObject, ref occlusionPipelineController, ref virtualObject, ref materialController);

        /// <summary>
        /// groundTruthObject を dummyPointCloudProvider の targetObjects に自動同期し、点群を即時更新します。
        /// </summary>
        public void SyncDummyPointCloudWithGroundTruth(bool forceUpdateNow = true)
        {
            if (dummyPointCloudProvider == null)
            {
                FindDummyComponents();
            }

            if (dummyPointCloudProvider == null || groundTruthObject == null) return;

            bool needUpdate = false;
            if (dummyPointCloudProvider.targetObjects == null ||
                dummyPointCloudProvider.targetObjects.Count != 1 ||
                dummyPointCloudProvider.targetObjects[0] != groundTruthObject)
            {
                needUpdate = true;
            }

            if (needUpdate)
            {
                dummyPointCloudProvider.SetTargetObject(groundTruthObject, forceUpdateNow);
                AppLogger.Log(this, $"[SICESI] dummyPointCloudProvider のターゲットを groundTruthObject [{groundTruthObject.name}] に自動同期しました。", TagCore);
            }
            else if (forceUpdateNow)
            {
                dummyPointCloudProvider.ForceUpdateSampling();
            }
        }

        #endregion

    }
}
