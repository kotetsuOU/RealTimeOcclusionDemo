using System;
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

    #region 定数・事前定義LUT (Fixed Classification 最適解)

    /// <summary> 256パターン独立最適LUTの最良解 (HEX: 64文字) </summary>
    public const string DEFAULT_HEX_256 = "000BC04F0000018B0003000D8001908D000100010001000080000001D000DD45";

    /// <summary> 36クラス幾何学回転不変LUTの最良解 (HEX: 64文字) </summary>
    public const string DEFAULT_HEX_36 = "130B008B0000808B000000008000808B5101000100000001D1010001D101D1DF";

    /// <summary> Mode 256 最適解の uint[8] ワード表現 </summary>
    private static readonly uint[] LUT_WORDS_256_DEFAULT = new uint[8]
    {
        0xD000DD45u, 0x80000001u, 0x00010000u, 0x00010001u,
        0x8001908Du, 0x0003000Du, 0x0000018Bu, 0x000BC04Fu
    };

    /// <summary> Mode 36 最適解の uint[8] ワード表現 </summary>
    private static readonly uint[] LUT_WORDS_36_DEFAULT = new uint[8]
    {
        0xD101D1DFu, 0xD1010001u, 0x00000001u, 0x51010001u,
        0x8000808Bu, 0x00000000u, 0x0000808Bu, 0x130B008Bu
    };

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

    [Tooltip("オクルージョン近傍探索のベース/最小レベル(0〜6)。OFF時は固定レベルとして使用され、ON時は探索レベルの下限および空領域のデフォルト値として使用されます。")]
    [Range(0, 6)]
    public int minSearchLevel = 6;

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
    }

    #endregion

    #region 設定出力 & LUT 生成ロジック

    /// <summary>
    /// 現在の評価モードおよび設定に応じた 256bit LUT の uint[8] ワード配列を取得します。
    /// </summary>
    public int[] GetActiveLutWords()
    {
        uint[] words = new uint[8];

        if (evaluationMode == PCD_OcclusionEvaluationMode.PatternLUT256)
        {
            if (!string.IsNullOrEmpty(customPatternLutHex) && TryParseHexToWords(customPatternLutHex, words))
            {
                return ConvertToIntArray(words);
            }
            return ConvertToIntArray(LUT_WORDS_256_DEFAULT);
        }
        else if (evaluationMode == PCD_OcclusionEvaluationMode.RotationLUT36)
        {
            if (!string.IsNullOrEmpty(customPatternLutHex) && TryParseHexToWords(customPatternLutHex, words))
            {
                return ConvertToIntArray(words);
            }
            return ConvertToIntArray(LUT_WORDS_36_DEFAULT);
        }
        else if (evaluationMode == PCD_OcclusionEvaluationMode.SectorThreshold) // Mode 8
        {
            // N_occ < minOccludedSectorsMode8 を満たすパターンを可視 (1) にセット
            for (int m = 0; m < 256; m++)
            {
                int occ = CountBits((byte)m);
                if (occ < minOccludedSectorsMode8)
                {
                    words[m >> 5] |= (1u << (m & 31));
                }
            }
            return ConvertToIntArray(words);
        }
        else if (evaluationMode == PCD_OcclusionEvaluationMode.SectorConsecutiveZeros) // Mode 20
        {
            // N_occ >= minOccludedSectorsMode20 && L_max <= maxConsecutiveEmptySectors で遮蔽 (可視は反転)
            for (int m = 0; m < 256; m++)
            {
                int occ = CountBits((byte)m);
                int lMax = ComputeLMax((byte)m);
                bool isOccluded = (occ >= minOccludedSectorsMode20) && (lMax <= maxConsecutiveEmptySectors);
                if (!isOccluded)
                {
                    words[m >> 5] |= (1u << (m & 31));
                }
            }
            return ConvertToIntArray(words);
        }

        // デフォルト (全可視)
        for (int i = 0; i < 8; i++) words[i] = 0xFFFFFFFFu;
        return ConvertToIntArray(words);
    }

    private static int[] ConvertToIntArray(uint[] words)
    {
        int[] result = new int[8];
        for (int i = 0; i < 8; i++)
        {
            result[i] = unchecked((int)words[i]);
        }
        return result;
    }

    private static bool TryParseHexToWords(string hex, uint[] outWords)
    {
        if (string.IsNullOrEmpty(hex)) return false;
        string clean = hex.Trim().Replace("0x", "").Replace("0X", "").Replace(" ", "");
        if (clean.Length < 64) clean = clean.PadLeft(64, '0');
        if (clean.Length > 64) clean = clean.Substring(clean.Length - 64, 64);

        try
        {
            // clean は上位ビットが先頭 (MSB=clean[0..7] -> word 7, LSB=clean[56..63] -> word 0)
            for (int w = 0; w < 8; w++)
            {
                int start = 64 - (w + 1) * 8;
                string sub = clean.Substring(start, 8);
                outWords[w] = Convert.ToUInt32(sub, 16);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static int CountBits(byte b)
    {
        int count = 0;
        while (b != 0)
        {
            b = (byte)(b & (b - 1));
            count++;
        }
        return count;
    }

    private static int ComputeLMax(byte m)
    {
        if (m == 0) return 8;
        if (m == 255) return 0;
        int maxZero = 0;
        int curZero = 0;
        for (int i = 0; i < 16; i++)
        {
            int bit = (m >> (i % 8)) & 1;
            if (bit == 0)
            {
                curZero++;
                if (curZero > maxZero) maxZero = curZero;
            }
            else
            {
                curZero = 0;
            }
        }
        return Mathf.Min(maxZero, 8);
    }

    /// <summary>
    /// レンダラーパスへ渡す設定スナップショットを生成します。
    /// </summary>
    public PCDRenderSettings GetSettings()
    {
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
            patternLutWords = GetActiveLutWords(),
            _dynamicMultiplierRuntimeValue = PCDRendererFeature.Instance != null ? PCDRendererFeature.Instance._internalDynamicMultiplier : 1
        };
    }

    #endregion

    #region Scene Gizmo

    private void OnDrawGizmos()
    {
        if (!enableVirtualContactOcclusion || !Application.isPlaying) return;
        if (HCD_Pipeline.Instance == null || HCD_Pipeline.Instance.distanceProcessor == null) return;

        var trackedClusters = HCD_Pipeline.Instance.GetTrackedClusters();
        if (trackedClusters == null) return;

        float radius = virtualContactRadius;
        float offset = HCD_Pipeline.Instance.distanceProcessor.surfaceDistanceThreshold;

        Gizmos.color = virtualContactColor;

        foreach (var c in trackedClusters)
        {
            if (!c.IsAlive) continue;

            Vector3 centroid = c.Centroid;
            Vector3 normal = c.Normal.normalized;
            if (normal.sqrMagnitude < 0.1f) normal = Vector3.up;

            centroid += normal * offset;

#if UNITY_EDITOR
            UnityEditor.Handles.color = virtualContactColor;
            UnityEditor.Handles.DrawWireDisc(centroid, normal, radius);
            Gizmos.DrawLine(centroid, centroid + normal * 0.05f);
#endif
        }
    }

    #endregion

    #region キャプチャトリガーAPI

    /// <summary>
    /// 次のフレームで占有セクターマスク (SectorMask DebugMap) を1フレーム出力します。
    /// 保存先: Assets/HandTrackingData/SectorMasks/ (.raw バイナリ, .csv サマリー, .png グレースケール)
    /// </summary>
    public void TriggerRecordSectorMask()
    {
        debugSettings.recordNeighborCountMap = true;
        Debug.Log("[PCD] 占有セクターマスク (SectorMask DebugMap) のキャプチャをリクエストしました (次フレームで出力されます)");
    }

    /// <summary>
    /// 次のフレームでオクルージョンマップ (OcclusionMap) を1フレーム出力します。
    /// </summary>
    public void TriggerRecordOcclusionMap()
    {
        debugSettings.recordOcclusionDebugMap = true;
        Debug.Log("[PCD] オクルージョンマップ (OcclusionMap) のキャプチャをリクエストしました");
    }

    /// <summary>
    /// 全てのデバッグマップを一括出力します。
    /// </summary>
    public void TriggerRecordAllDebugMaps()
    {
        debugSettings.RequestRecordAll();
        Debug.Log("[PCD] 全デバッグマップの一括キャプチャをリクエストしました");
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
