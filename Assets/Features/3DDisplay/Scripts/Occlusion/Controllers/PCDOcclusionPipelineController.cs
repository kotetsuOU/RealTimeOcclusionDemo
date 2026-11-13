using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using Core.Logging;
using static PCDRendererFeature;

/// <summary>
/// PCD リアルタイムオクルージョンパイプラインの全体設定および実行制御を統括するコントローラー。
/// 評価モード（Mode 8, 20, 36, 256）の切り替え、LUTの管理、コアアルゴリズムパラメータの調整を行います。
/// デバッグ関連パラメータは PCDOcclusionDebugSettings に責務分離されています。
/// </summary>
[ExecuteInEditMode]
[AppLoggable("PCD (Occlusion)")]
public class PCDOcclusionPipelineController : MonoBehaviour, IAppLoggable
{
    public static PCDOcclusionPipelineController Instance { get; private set; }

    #region 定数エイリアス & LUT プロセッサ

    /// <summary> 256パターン独立最適LUTの最良解 (HEX: 64文字) </summary>
    public const string DEFAULT_HEX_256 = PCDPatternLutProcessor.DEFAULT_HEX_256;

    /// <summary> 36クラス幾何学回転不変LUTの最良解 (HEX: 64文字) </summary>
    public const string DEFAULT_HEX_36 = PCDPatternLutProcessor.DEFAULT_HEX_36;

    /// <summary> 密度別 LUT の最大ビン数 </summary>
    public const int MAX_DENSITY_LUT_BINS = PCDPatternLutProcessor.MAX_DENSITY_LUT_BINS;

    private readonly PCDPatternLutProcessor _lutProcessor = new PCDPatternLutProcessor();

    #endregion

    #region カメラターゲット & カリング設定

    [Header("Camera Target & Culling Settings")]
    [Tooltip("オクルージョン計算対象のカメラ制限（AllValidCameras: CullingMask!=0の全カメラ, VirtualCamerasOnly: 名前がVirtualを含むカメラのみ, CustomFilter: 指定キーワード）")]
    public PCD_CameraTargetMode cameraTargetMode = PCD_CameraTargetMode.AllValidCameras;

    [Tooltip("CustomFilter モード時に判定に使用するカメラ名キーワード")]
    public string cameraNameFilter = "Virtual";

    #endregion

    #region オクルージョンコア設定 (Mode 8 / 20 / 36 / 256)

    [Header("Occlusion Core Settings")]
    [Tooltip("オクルージョン計算に用いるカーネル関数")]
    public PCD_OcclusionKernel kernelType = PCD_OcclusionKernel.Bouchiba;

    [Tooltip("オクルージョン判定モード (Mode 8: 占有数判定, Mode 20: 連続非占有規則, Mode 36: 36クラスLUT, Mode 256: 256パターンLUT [デフォルト・推奨])")]
    public PCD_OcclusionEvaluationMode evaluationMode = PCD_OcclusionEvaluationMode.PatternLUT256;

    [Tooltip("【Mode 8 用】オクルージョン判定となるために必要な最小占有セクター数 R_th (1〜8。最適値: 7)")]
    [Range(1, 8)]
    public int minOccludedSectorsMode8 = 7;

    [Tooltip("【Mode 20 用】オクルージョン判定となるために必要な最小占有セクター数 R_th (1〜8。最新最適値: 3)")]
    [Range(1, 8)]
    public int minOccludedSectorsMode20 = 3;

    /// <summary>
    /// 後方互換性プロパティ。現在の評価モードに応じた R_th を取得・設定します。
    /// </summary>
    public int minOccludedSectors
    {
        get => evaluationMode == PCD_OcclusionEvaluationMode.SectorThreshold ? minOccludedSectorsMode8 : minOccludedSectorsMode20;
        set
        {
            if (evaluationMode == PCD_OcclusionEvaluationMode.SectorThreshold)
                minOccludedSectorsMode8 = value;
            else
                minOccludedSectorsMode20 = value;
        }
    }

    [Tooltip("【Mode 20 用】許容最大連続非占有セクター数 L_th (0〜8。最新最適値: 2, 8で方向制限無効)")]
    [Range(0, 8)]
    public int maxConsecutiveEmptySectors = 2;

    [Tooltip("【Mode 36 / 256 用】カスタム LUT 16進数文字列 (空欄の場合は最適化済みデフォルト解が使用されます)")]
    public string customPatternLutHex = "";

    [Tooltip("【Mode 36 / 256 用】タイルごとのスクリーン点群密度に応じて最適 LUT を動的に切り替えます。")]
    public bool useDensityAdaptiveLut = false;

    [Tooltip("スクリーン密度ビン別の最適 LUT リスト（最大 8 ビン、maxScreenDensity 昇順で評価されます）")]
    public List<PCD_DensityLutEntry> densityLutEntries = CreateDefaultDensityLutEntries();

    [Tooltip("オクルージョン近傍探索のベース/最小レベル(0〜6)。OFF時は固定レベルとして使用され、ON時は探索レベルの下限および空領域のデフォルト値として使用されます。")]
    [Range(0, 6)]
    public int minSearchLevel = 1;

    #endregion

    #region アルゴリズムパラメータ

    [Header("Algorithm Parameters")]
    [Tooltip("指数関数の減衰係数 (Expモード専用)")]
    public float exponentAlpha = 1.0f;

    [Tooltip("密度計算に用いる深度のしきい値 e")]
    public float densityThreshold_e = 0.04f;

    [Tooltip("近傍領域サイズを決定するための調整パラメータ p' ")]
    public float neighborhoodParam_p_prime = 4.8f;

    #endregion

    #region 勾配補正 & 動的LOD

    [Header("Gradient Correction & Dynamic LOD")]
    [Tooltip("密度に基づく動的LOD探索を有効にするか。OFFの場合は画面全域でminSearchLevelを固定探索レベルとして使用します。")]
    public bool enableDensityBasedLOD = true;

    [Tooltip("勾配を用いた補正を有効にする")]
    public bool enableGradientCorrection = true;

    [Tooltip("勾配しきい値 g_th")]
    public float gradientThreshold_g_th = 0.05f;

    #endregion

    #region オクルージョンフィルタリング & フェード

    [Header("Occlusion Filtering")]
    [FormerlySerializedAs("occlusionThreshold")]
    [Tooltip("Averageモード時のオクルージョン判定閾値 T_ave (論文 2.4.2節)")]
    [Range(0f, 1f)]
    public float occlusionThreshold_T_ave = 0.5f;

    [Tooltip("セクター占有判定（その他全手法）用の局所閾値 T_loc")]
    [Range(0f, 1f)]
    public float occlusionThreshold_T_loc = 0.1f;

    /// <summary> Average手法用閾値 T_ave のエイリアスプロパティ </summary>
    public float T_ave
    {
        get => occlusionThreshold_T_ave;
        set => occlusionThreshold_T_ave = value;
    }

    /// <summary> セクター占有判定・その他手法用閾値 T_loc のエイリアスプロパティ </summary>
    public float T_loc
    {
        get => occlusionThreshold_T_loc;
        set => occlusionThreshold_T_loc = value;
    }

    /// <summary>
    /// 現在の評価モードに応じたオクルージョン閾値。
    /// Averageモードなら T_ave、その他モードなら T_loc を返します。
    /// （後方互換性用プロパティ）
    /// </summary>
    public float occlusionThreshold
    {
        get => evaluationMode == PCD_OcclusionEvaluationMode.Average ? occlusionThreshold_T_ave : occlusionThreshold_T_loc;
        set
        {
            if (evaluationMode == PCD_OcclusionEvaluationMode.Average)
            {
                occlusionThreshold_T_ave = value;
            }
            else
            {
                occlusionThreshold_T_loc = value;
            }
        }
    }

    [Tooltip("8セクター個別の局所閾値 T_loc を使用するかどうか (OFFの場合は共通の T_loc を使用)")]
    public bool useSectorSpecificThreshold = false;

    [Tooltip("8セクター個別の局所閾値 T_loc 配列 (0:N, 1:NE, 2:E, 3:SE, 4:S, 5:SW, 6:W, 7:NW)")]
    public float[] sectorThreshold_T_loc = new float[8] { 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f };

    [Tooltip("境界を滑らかにするためのフェード幅（閾値からの減衰範囲）")]
    [Range(0f, 1f)]
    public float occlusionFadeWidth = 0.1f;

    #endregion

    #region 仮想接触オクルージョン (Virtual Contact)

    [Header("Virtual Contact Occlusion")]
    [Tooltip("HCDの接触検知をもとに仮想的な遮蔽点群を生成します")]
    public bool enableVirtualContactOcclusion = false;

    [Tooltip("仮想点群を生成する円盤の半径 (m)")]
    public float virtualContactRadius = 0.03f;

    [Tooltip("仮想点群の配置間隔 (m)")]
    public float virtualContactSpacing = 0.005f;

    [Tooltip("仮想点群のデバッグ描画色 (SceneビューのGizmoおよびPixelTagMap用)")]
    public Color virtualContactColor = new Color(0.8f, 0f, 0.4f, 0.8f);

    #endregion

    #region 提案手法・最適化トグル (Ablation Study)

    [Header("SICE FES 2026 Novel Methods Toggles (Ablation Study)")]
    [Tooltip("仮想・現実の「相互オクルージョン」の統合を有効にするか")]
    public bool enableVirtualDepthIntegration = true;

    [Tooltip("①タグによる近傍探索の最適化 (ONで不要な自己遮蔽計算をスキップ)")]
    public bool enableTagBasedOptimization = true;

    [Tooltip("②仮想物体を区別した密度計算 (ONで従来手法のカウント漏れや過剰を補正)")]
    public bool enableTypeAwareDensity = true;

    [Tooltip("③ソフトオクルージョン (ONでグラデーションによる境界のスムージング)")]
    public bool enableSoftOcclusionFade = true;

    [Tooltip("④エッジ保持型ホールフィリング手法の選択")]
    public PCD_HoleFillingMethod holeFillingMethod = PCD_HoleFillingMethod.JointBilateral;

    [Tooltip("⑤処理の最適化と検証のためのグリッドサイズ")]
    public PCD_GridSize gridSize = PCD_GridSize.Grid16x16;

    #endregion

    #region モルフォロジー設定 (Morphology)

    [Header("Morphology Settings")]
    [Tooltip("モルフォロジーカーネルの半径（1 = 3×3, 2 = 5×5。大きいほど強く重い）")]
    [Range(1, 25)]
    public int morphKernelHalfSize = 1;

    [Tooltip("Opening の収縮回数（0 でスキップ）。孤立ノイズや細いトゲを除去する。")]
    [Range(0, 5)]
    public int morphErodeIterations = 0;

    [Tooltip("Closing の膨張回数。多いほど疎な手の甲など深い隙間まで色が伝播する。")]
    [Range(1, 5)]
    public int morphDilateIterations = 1;

    #endregion

    #region デバッグ & キャプチャ設定 (PCDOcclusionDebugSettings)

    [Header("Debug & Capture Settings")]
    [Tooltip("デバッグ表示およびキャプチャ関連パラメータの集約設定")]
    public PCDOcclusionDebugSettings debugSettings = new PCDOcclusionDebugSettings();

    // -------------------------------------------------------------------------
    // 後方互換性アクセサ (外部スクリプトや既存コードの破損を防止)
    // -------------------------------------------------------------------------
    public bool enablePixelTagMap { get => debugSettings.enablePixelTagMap; set => debugSettings.enablePixelTagMap = value; }
    public bool enableOcclusionMap { get => debugSettings.enableOcclusionMap; set => debugSettings.enableOcclusionMap = value; }
    public int debugPatternId { get => debugSettings.debugPatternId; set => debugSettings.debugPatternId = value; }
    public int debugSectorId { get => debugSettings.debugSectorId; set => debugSettings.debugSectorId = value; }
    public bool recordOcclusionDebugMap { get => debugSettings.recordOcclusionDebugMap; set => debugSettings.recordOcclusionDebugMap = value; }
    public bool recordPixelTagMap { get => debugSettings.recordPixelTagMap; set => debugSettings.recordPixelTagMap = value; }
    public bool recordIntegratedDepthMap { get => debugSettings.recordIntegratedDepthMap; set => debugSettings.recordIntegratedDepthMap = value; }
    public bool recordNeighborhoodMap { get => debugSettings.recordNeighborhoodMap; set => debugSettings.recordNeighborhoodMap = value; }
    public bool recordNeighborCountMap { get => debugSettings.recordNeighborCountMap; set => debugSettings.recordNeighborCountMap = value; }
    public bool enableBufferManagerLog { get => debugSettings.enableBufferManagerLog; set => debugSettings.enableBufferManagerLog = value; }

    #endregion

    #region Unity ライフサイクル

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            if (Application.isPlaying)
            {
                Destroy(this);
            }
            else
            {
                AppLogger.LogWarning(PCD_LogTriggers.TagPipeline, $"Duplicate PCDOcclusionPipelineController found on {gameObject.name}. Please remove it.");
            }
            return;
        }
        Instance = this;
    }

    private void OnEnable()
    {
        if (Instance == null || Instance == this)
        {
            Instance = this;
        }
    }

    private void OnDisable()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void OnValidate()
    {
        float currentTh = evaluationMode == PCD_OcclusionEvaluationMode.Average ? occlusionThreshold_T_ave : occlusionThreshold_T_loc;
        float maxFadeWidth = Mathf.Min(currentTh, 1.0f - currentTh) * 2.0f;
        occlusionFadeWidth = Mathf.Clamp(occlusionFadeWidth, 0f, maxFadeWidth);
        MarkLutDirty();
    }

    #endregion

    #region 設定出力 & LUT 委譲

    [Serializable]
    public class PCD_DensityLutEntry
    {
        [Tooltip("表示用ラベル（例: Sparse / Medium / Dense）")]
        public string label = "Bin";
        [Tooltip("参照用の物理点群密度 (pts/mm^2)。OPE 連携時に自動設定されます。")]
        public float sourcePhysicalDensity = 1.0f;
        [Tooltip("このビンに割り当てる GPU スクリーン密度の上限閾値 (0.0〜1.5)。最終ビンは残りすべてを受け持ちます。")]
        [Range(0f, 1.5f)]
        public float maxScreenDensity = 1.0f;
        [Tooltip("この密度帯で適用する 256-bit LUT HEX (64文字)。空欄時は通常の単一 LUT へフォールバックします。")]
        public string patternLutHex = "";
    }

    public static List<PCD_DensityLutEntry> CreateDefaultDensityLutEntries() =>
        PCDPatternLutProcessor.CreateDefaultDensityLutEntries();

    /// <summary>
    /// 密度別 LUT リストを既定の 3 段階設定（0.25 / 1.0 / 4.0+）にリセットします。
    /// </summary>
    public void ResetDefaultDensityLutEntries()
    {
        densityLutEntries = PCDPatternLutProcessor.CreateDefaultDensityLutEntries();
        MarkLutDirty();
    }

    /// <summary>
    /// LUT の再計算フラグを立てます。設定パラメータを変更した際に呼び出してください。
    /// </summary>
    public void MarkLutDirty() => _lutProcessor.MarkDirty();

    /// <summary>
    /// 現在の評価モードおよび設定に応じた 256bit LUT の int[8] ワード配列を取得します。
    /// </summary>
    public int[] GetActiveLutWords() =>
        _lutProcessor.GetActiveLutWords(
            evaluationMode,
            minOccludedSectorsMode8,
            minOccludedSectorsMode20,
            maxConsecutiveEmptySectors,
            customPatternLutHex);

    /// <summary>
    /// 密度適応型 LUT 用のビン数・上限閾値配列・LUT ワード配列を取得します。
    /// </summary>
    public void GetDensityLutData(out int count, out Vector4[] thresholds, out int[] words) =>
        _lutProcessor.GetDensityLutData(
            useDensityAdaptiveLut,
            densityLutEntries,
            evaluationMode,
            minOccludedSectorsMode8,
            minOccludedSectorsMode20,
            maxConsecutiveEmptySectors,
            customPatternLutHex,
            out count,
            out thresholds,
            out words);

    /// <summary>
    /// レンダラーパスへ渡す設定スナップショットを生成します。
    /// </summary>
    public PCDRenderSettings GetSettings()
    {
        int[] activeLut = GetActiveLutWords();
        GetDensityLutData(out int densityCount, out Vector4[] densityThresholds, out int[] densityWords);

        return new PCDRenderSettings
        {
            cameraTargetMode = this.cameraTargetMode,
            cameraNameFilter = this.cameraNameFilter,
            kernelType = this.kernelType,
            evaluationMode = this.evaluationMode,
            minOccludedSectors = this.minOccludedSectors,
            maxConsecutiveEmptySectors = this.maxConsecutiveEmptySectors,
            minSearchLevel = this.minSearchLevel,
            exponentAlpha = this.exponentAlpha,
            densityThreshold_e = this.densityThreshold_e,
            neighborhoodParam_p_prime = this.neighborhoodParam_p_prime,
            enableDensityBasedLOD = this.enableDensityBasedLOD,
            enableGradientCorrection = this.enableGradientCorrection,
            gradientThreshold_g_th = this.gradientThreshold_g_th,
            occlusionThreshold_T_ave = this.occlusionThreshold_T_ave,
            occlusionThreshold_T_loc = this.occlusionThreshold_T_loc,
            useSectorSpecificThreshold = this.useSectorSpecificThreshold,
            sectorThreshold_T_loc = this.sectorThreshold_T_loc,
            occlusionThreshold = this.occlusionThreshold,
            occlusionFadeWidth = this.occlusionFadeWidth,
            enableVirtualContactOcclusion = this.enableVirtualContactOcclusion,
            virtualContactRadius = this.virtualContactRadius,
            virtualContactSpacing = this.virtualContactSpacing,
            virtualContactColor = this.virtualContactColor,
            enablePixelTagMap = debugSettings.enablePixelTagMap,
            enableOcclusionMap = debugSettings.enableOcclusionMap,
            enableBufferManagerLog = debugSettings.enableBufferManagerLog,
            recordOcclusionDebugMap = debugSettings.recordOcclusionDebugMap,
            recordPixelTagMap = debugSettings.recordPixelTagMap,
            recordIntegratedDepthMap = debugSettings.recordIntegratedDepthMap,
            recordNeighborhoodMap = debugSettings.recordNeighborhoodMap,
            recordNeighborCountMap = debugSettings.recordNeighborCountMap,
            debugPatternId = debugSettings.debugPatternId,
            debugSectorId = debugSettings.debugSectorId,
            enableVirtualDepthIntegration = this.enableVirtualDepthIntegration,
            enableTagBasedOptimization = this.enableTagBasedOptimization,
            enableTypeAwareDensity = this.enableTypeAwareDensity,
            enableSoftOcclusionFade = this.enableSoftOcclusionFade,
            holeFillingMethod = this.holeFillingMethod,
            gridSize = this.gridSize,
            morphKernelHalfSize = this.morphKernelHalfSize,
            morphErodeIterations = this.morphErodeIterations,
            morphDilateIterations = this.morphDilateIterations,
            patternLutWords = activeLut,
            useDensityAdaptiveLut = this.useDensityAdaptiveLut && densityCount > 0,
            densityLutCount = densityCount,
            densityLutThresholds = densityThresholds,
            densityLutWords = densityWords,
            _dynamicMultiplierRuntimeValue = PCDRendererFeature.Instance != null ? PCDRendererFeature.Instance._internalDynamicMultiplier : 1
        };
    }

    #endregion

    #region 統一ログ登録

    public void RegisterLogTriggers(LogCategoryGroup group, System.Collections.Generic.HashSet<string> existingLabels)
    {
        var triggers = GetComponent<PCD_LogTriggers>() ?? gameObject.AddComponent<PCD_LogTriggers>();
        triggers.RegisterLogTriggers(group, existingLabels);
    }

    #endregion
}
