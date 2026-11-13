// =============================================================================
// PCDKernelRegistry.cs
// -----------------------------------------------------------------------------
// コンピュートシェーダーのカーネルIDを管理する独立クラス。
// Initialize() で一度だけカーネルインデックスを取得し、プロパティで公開する。
// =============================================================================
using UnityEngine;
using Core.Logging;

/// <summary>
/// コンピュートシェーダーの全カーネルIDを保持・初期化するレジストリ。
/// </summary>
internal class PCDKernelRegistry
{
    // =========================================================================
    // 初期化状態
    // =========================================================================
    public bool IsInitialized { get; private set; }

    // =========================================================================
    // クリア・投影
    // =========================================================================
    public int ClearMaps { get; private set; }
    public int ClearCounter { get; private set; }
    public int ProjectPoints { get; private set; }

    // =========================================================================
    // 密度・LOD 計算
    // =========================================================================
    public int CalcDensity { get; private set; }
    public int GridMedianFilter { get; private set; }
    public int CalcNeighborhoodSize { get; private set; }
    public int FillNeighborhoodSizeWithMinLevel { get; private set; }

    // =========================================================================
    // 深度ピラミッド構築 (L1〜L6) — 配列でアクセス可能
    // =========================================================================
    public int[] BuildDepthPyramid { get; private set; }

    // =========================================================================
    // 勾配補正
    // =========================================================================
    public int ApplyGradient { get; private set; }

    // =========================================================================
    // オクルージョン判定
    // =========================================================================
    public int ComputeOcclusion { get; private set; }
    public int CopyColorToOcclusion { get; private set; }

    // =========================================================================
    // ホールフィリング
    // =========================================================================
    public int FillHoles { get; private set; }
    public int FillHolesPullPushInit { get; private set; }
    public int FillHolesPull { get; private set; }
    public int FillHolesPush { get; private set; }
    public int FillHolesPullPushFinalize { get; private set; }
    public int Interpolate { get; private set; }

    // =========================================================================
    // バッファマージ・カメラ初期化・デバッグ
    // =========================================================================
    public int MergeBuffer { get; private set; }
    public int InitFromCamera { get; private set; }
    public int VisualizeOcclusionDebug { get; private set; }

    // =========================================================================
    // モルフォロジー演算
    // =========================================================================
    public int MorphologyErode { get; private set; }
    public int MorphologyDilate { get; private set; }
    public int MorphologyCopy { get; private set; }

    // =========================================================================
    // モルフォロジーピラミッド構築 (L1〜L6) — 配列でアクセス可能
    // =========================================================================
    public int[] BuildMorphPyramid { get; private set; }

    // =========================================================================
    // 初期化
    // =========================================================================

    /// <summary> コンピュートシェーダーからカーネルのインデックスIDを取得して初期化する。 </summary>
    public void Initialize(ComputeShader cs)
    {
        if (cs == null)
        {
            AppLogger.LogError(PCD_LogTriggers.TagPipeline, "ComputeShader is null. Initialization failed.");
            IsInitialized = false;
            return;
        }

        // --- クリア・投影 ---
        ClearMaps = cs.FindKernel("ClearMaps");
        ClearCounter = cs.FindKernel("ClearCounter");
        ProjectPoints = cs.FindKernel("ProjectPoints");

        // --- 密度・LOD 計算 ---
        CalcDensity = cs.FindKernel("CalculateDensity");
        GridMedianFilter = cs.FindKernel("GridMedianFilter");
        CalcNeighborhoodSize = cs.FindKernel("CalculateNeighborhoodSize");
        FillNeighborhoodSizeWithMinLevel = cs.FindKernel("FillNeighborhoodSizeWithMinLevel");

        // --- 深度ピラミッド構築 (L1〜L6) ---
        BuildDepthPyramid = new int[PCDShaderConstants.PYRAMID_LEVELS];
        for (int i = 0; i < PCDShaderConstants.PYRAMID_LEVELS; i++)
        {
            BuildDepthPyramid[i] = cs.FindKernel($"BuildDepthPyramidL{i + 1}");
        }

        // --- 勾配補正 ---
        ApplyGradient = cs.FindKernel("ApplyAdaptiveGradientCorrection");

        // --- オクルージョン判定 ---
        ComputeOcclusion = cs.FindKernel("ComputeOcclusion");
        CopyColorToOcclusion = cs.FindKernel("CopyColorToOcclusion");

        // --- ホールフィリング ---
        FillHoles = cs.FindKernel("FillHoles");
        FillHolesPullPushInit = cs.FindKernel("FillHolesPullPushInit");
        FillHolesPull = cs.FindKernel("FillHolesPull");
        FillHolesPush = cs.FindKernel("FillHolesPush");
        FillHolesPullPushFinalize = cs.FindKernel("FillHolesPullPushFinalize");
        Interpolate = cs.FindKernel("Interpolate");

        // --- バッファマージ・カメラ初期化・デバッグ ---
        MergeBuffer = cs.FindKernel("MergeBuffer");
        InitFromCamera = cs.FindKernel("InitFromCamera");
        VisualizeOcclusionDebug = cs.FindKernel("VisualizeOcclusionDebug");

        // --- モルフォロジー演算 ---
        MorphologyErode = cs.FindKernel("MorphologyErode");
        MorphologyDilate = cs.FindKernel("MorphologyDilate");
        MorphologyCopy = cs.FindKernel("MorphologyCopy");

        // --- モルフォロジーピラミッド構築 (L1〜L6) ---
        BuildMorphPyramid = new int[PCDShaderConstants.PYRAMID_LEVELS];
        for (int i = 0; i < PCDShaderConstants.PYRAMID_LEVELS; i++)
        {
            BuildMorphPyramid[i] = cs.FindKernel($"BuildMorphPyramidL{i + 1}");
        }

        IsInitialized = true;
    }
}
