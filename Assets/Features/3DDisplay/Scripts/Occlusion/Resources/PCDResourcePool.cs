// =============================================================================
// PCDResourcePool.cs
// -----------------------------------------------------------------------------
// 点群オクルージョンパイプラインで使用する全 RTHandle を一元管理するクラス。
//
// 画面サイズが変わった場合にのみ全テクスチャを再生成し、
// 同一サイズであれば既存のハンドルを再利用する。
//
// ピラミッド系テクスチャは配列で管理し、ループアクセスを可能にする。
// =============================================================================
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

/// <summary>
/// オクルージョンパイプラインで使用する全 RTHandle のライフサイクルを管理する。
/// </summary>
internal class PCDResourcePool : System.IDisposable
{
    // =========================================================================
    // メインマップ（フル解像度）
    // =========================================================================
    public RTHandle ColorMap { get; private set; }
    public RTHandle DepthMap { get; private set; }
    public RTHandle ViewPositionMap { get; private set; }
    public RTHandle OriginTypeMap { get; private set; }

    // =========================================================================
    // グリッド・密度マップ（グリッド解像度）
    // =========================================================================
    public RTHandle GridZMinMap { get; private set; }
    public RTHandle DensityMap { get; private set; }
    public RTHandle GridLevelMap { get; private set; }
    public RTHandle FilteredGridLevelMap { get; private set; }

    // =========================================================================
    // 近傍サイズマップ（フル解像度）
    // =========================================================================
    public RTHandle NeighborhoodSizeMap { get; private set; }
    public RTHandle CorrectedNeighborhoodSizeMap { get; private set; }

    // =========================================================================
    // 結果・デバッグマップ（フル解像度）
    // =========================================================================
    public RTHandle OcclusionResultMap { get; private set; }
    public RTHandle OcclusionValueMap { get; private set; }
    public RTHandle DebugDisplayMap { get; private set; }
    public RTHandle NeighborCountMap { get; private set; }
    public RTHandle AverageScoreMap { get; private set; }
    public RTHandle IntegratedDepthMap { get; private set; }
    public RTHandle NeighborhoodMap { get; private set; }
    public RTHandle FinalImage { get; private set; }

    // =========================================================================
    // 深度ピラミッド (L1〜L6) — 配列でアクセス
    // =========================================================================
    public RTHandle[] DepthPyramid { get; private set; }

    // =========================================================================
    // Pull-Push ピラミッド (5レベル)
    // =========================================================================
    public RTHandle[] PullPushPyramid { get; private set; }

    // =========================================================================
    // モルフォロジー一時バッファ
    // =========================================================================
    public RTHandle MorphColorTemp { get; private set; }
    public RTHandle MorphTypeTemp { get; private set; }

    // =========================================================================
    // モルフォロジーピラミッド (L1〜L6) — 配列でアクセス
    // =========================================================================
    public RTHandle[] MorphTypePyramid { get; private set; }
    public RTHandle[] MorphColorPyramid { get; private set; }

    // =========================================================================
    // 状態管理
    // =========================================================================
    private int _lastScreenWidth = -1;
    private int _lastScreenHeight = -1;

    // =========================================================================
    // アロケーション
    // =========================================================================

    /// <summary>
    /// 画面サイズが変化した場合に全ての中間テクスチャを再生成する。
    /// 同一サイズであれば何もせずに返る（毎フレーム呼ばれても安全）。
    /// </summary>
    public void EnsureAllocated(int screenWidth, int screenHeight, PCDRendererFeature.PCD_GridSize gridSize)
    {
        bool sizeChanged = (_lastScreenWidth != screenWidth || _lastScreenHeight != screenHeight);

        if (ColorMap != null && !sizeChanged)
            return;

        ReleaseAll();

        _lastScreenWidth = screenWidth;
        _lastScreenHeight = screenHeight;

        var colorFormatARGB = GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.ARGBFloat, false);
        var colorFormatRFloat = GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.RFloat, false);
        var fmtUInt = GraphicsFormat.R32_UInt;
        var fmtSInt = GraphicsFormat.R32_SInt;
        var fmtR16 = GraphicsFormat.R16G16B16A16_SFloat;

        // --- メインマップ（フル解像度） ---
        ColorMap = RTHandles.Alloc(screenWidth, screenHeight, colorFormat: colorFormatARGB, enableRandomWrite: true, name: "PCD_ColorMap");
        DepthMap = RTHandles.Alloc(screenWidth, screenHeight, colorFormat: fmtUInt, enableRandomWrite: true, name: "PCD_DepthMap");
        ViewPositionMap = RTHandles.Alloc(screenWidth, screenHeight, colorFormat: colorFormatARGB, enableRandomWrite: true, name: "PCD_ViewPosMap");
        OriginTypeMap = RTHandles.Alloc(screenWidth, screenHeight, colorFormat: fmtUInt, enableRandomWrite: true, name: "PCD_OriginTypeMap");

        // --- グリッド・密度マップ（グリッド解像度） ---
        int gs = (int)gridSize;
        if (gs == 0) gs = 16;
        int gridGroupsX = (screenWidth + gs - 1) / gs;
        int gridGroupsY = (screenHeight + gs - 1) / gs;

        GridZMinMap = RTHandles.Alloc(gridGroupsX, gridGroupsY, colorFormat: fmtUInt, enableRandomWrite: true, name: "PCD_GridZMin");
        DensityMap = RTHandles.Alloc(gridGroupsX, gridGroupsY, colorFormat: colorFormatRFloat, enableRandomWrite: true, name: "PCD_Density");
        GridLevelMap = RTHandles.Alloc(gridGroupsX, gridGroupsY, colorFormat: fmtSInt, enableRandomWrite: true, name: "PCD_GridLevel");
        FilteredGridLevelMap = RTHandles.Alloc(gridGroupsX, gridGroupsY, colorFormat: fmtSInt, enableRandomWrite: true, name: "PCD_FilteredGridLevel");

        // --- 近傍サイズマップ（フル解像度） ---
        NeighborhoodSizeMap = RTHandles.Alloc(screenWidth, screenHeight, colorFormat: fmtSInt, enableRandomWrite: true, name: "PCD_Neighborhood");
        CorrectedNeighborhoodSizeMap = RTHandles.Alloc(screenWidth, screenHeight, colorFormat: fmtSInt, enableRandomWrite: true, name: "PCD_CorrectedNeighborhood");

        // --- 結果・デバッグマップ（フル解像度） ---
        OcclusionResultMap = RTHandles.Alloc(screenWidth, screenHeight, colorFormat: colorFormatARGB, enableRandomWrite: true, name: "PCD_OcclusionResult");
        OcclusionValueMap = RTHandles.Alloc(screenWidth, screenHeight, colorFormat: GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.RGFloat, false), enableRandomWrite: true, name: "PCD_OcclusionValue");
        DebugDisplayMap = RTHandles.Alloc(screenWidth, screenHeight, colorFormat: colorFormatARGB, enableRandomWrite: true, name: "PCD_DebugDisplay");
        NeighborCountMap = RTHandles.Alloc(screenWidth, screenHeight, colorFormat: fmtUInt, enableRandomWrite: true, name: "PCD_NeighborCountMapDebug");
        AverageScoreMap = RTHandles.Alloc(screenWidth, screenHeight, colorFormat: colorFormatRFloat, enableRandomWrite: true, name: "PCD_AverageScoreMap");
        IntegratedDepthMap = RTHandles.Alloc(screenWidth, screenHeight, colorFormat: fmtUInt, enableRandomWrite: true, name: "PCD_IntegratedDepthMap");
        NeighborhoodMap = RTHandles.Alloc(screenWidth, screenHeight, colorFormat: fmtSInt, enableRandomWrite: true, name: "PCD_NeighborhoodMapDebug");
        FinalImage = RTHandles.Alloc(screenWidth, screenHeight, colorFormat: colorFormatARGB, enableRandomWrite: true, name: "PCD_FinalImage");

        // --- 深度ピラミッド (L1〜L6: 各レベルで1/2に縮小) ---
        DepthPyramid = new RTHandle[PCDShaderConstants.PYRAMID_LEVELS];
        int pw = screenWidth, ph = screenHeight;
        for (int i = 0; i < PCDShaderConstants.PYRAMID_LEVELS; i++)
        {
            pw = Mathf.Max(1, pw / 2);
            ph = Mathf.Max(1, ph / 2);
            DepthPyramid[i] = RTHandles.Alloc(pw, ph, colorFormat: fmtR16, enableRandomWrite: true, name: $"PCD_DepthPyramidL{i + 1}");
        }

        // --- Pull-Push ピラミッド (5レベル: フル解像度から段階的に縮小) ---
        PullPushPyramid = new RTHandle[5];
        pw = screenWidth; ph = screenHeight;
        for (int i = 0; i < 5; i++)
        {
            PullPushPyramid[i] = RTHandles.Alloc(pw, ph, colorFormat: colorFormatARGB, enableRandomWrite: true, name: $"PCD_PP_{i}");
            pw = Mathf.Max(1, (pw + 1) / 2);
            ph = Mathf.Max(1, (ph + 1) / 2);
        }

        // --- モルフォロジー一時バッファ ---
        MorphColorTemp = RTHandles.Alloc(screenWidth, screenHeight, colorFormat: colorFormatARGB, enableRandomWrite: true, name: "PCD_MorphColorTemp");
        MorphTypeTemp = RTHandles.Alloc(screenWidth, screenHeight, colorFormat: fmtUInt, enableRandomWrite: true, name: "PCD_MorphTypeTemp");

        // --- モルフォロジーピラミッド (L1〜L6) ---
        MorphTypePyramid = new RTHandle[PCDShaderConstants.PYRAMID_LEVELS];
        MorphColorPyramid = new RTHandle[PCDShaderConstants.PYRAMID_LEVELS];
        pw = screenWidth; ph = screenHeight;
        for (int i = 0; i < PCDShaderConstants.PYRAMID_LEVELS; i++)
        {
            pw = Mathf.Max(1, pw / 2);
            ph = Mathf.Max(1, ph / 2);
            MorphTypePyramid[i] = RTHandles.Alloc(pw, ph, colorFormat: fmtUInt, enableRandomWrite: true, name: $"PCD_MorphTL{i + 1}");
            MorphColorPyramid[i] = RTHandles.Alloc(pw, ph, colorFormat: fmtR16, enableRandomWrite: true, name: $"PCD_MorphCL{i + 1}");
        }
    }

    // =========================================================================
    // 解放
    // =========================================================================

    /// <summary> 全 RTHandle を解放する。 </summary>
    public void ReleaseAll()
    {
        ColorMap?.Release(); ColorMap = null;
        DepthMap?.Release(); DepthMap = null;
        ViewPositionMap?.Release(); ViewPositionMap = null;
        OriginTypeMap?.Release(); OriginTypeMap = null;

        GridZMinMap?.Release(); GridZMinMap = null;
        DensityMap?.Release(); DensityMap = null;
        GridLevelMap?.Release(); GridLevelMap = null;
        FilteredGridLevelMap?.Release(); FilteredGridLevelMap = null;

        NeighborhoodSizeMap?.Release(); NeighborhoodSizeMap = null;
        CorrectedNeighborhoodSizeMap?.Release(); CorrectedNeighborhoodSizeMap = null;

        OcclusionResultMap?.Release(); OcclusionResultMap = null;
        OcclusionValueMap?.Release(); OcclusionValueMap = null;
        DebugDisplayMap?.Release(); DebugDisplayMap = null;
        NeighborCountMap?.Release(); NeighborCountMap = null;
        AverageScoreMap?.Release(); AverageScoreMap = null;
        IntegratedDepthMap?.Release(); IntegratedDepthMap = null;
        NeighborhoodMap?.Release(); NeighborhoodMap = null;
        FinalImage?.Release(); FinalImage = null;

        ReleaseArray(DepthPyramid); DepthPyramid = null;
        ReleaseArray(PullPushPyramid); PullPushPyramid = null;

        MorphColorTemp?.Release(); MorphColorTemp = null;
        MorphTypeTemp?.Release(); MorphTypeTemp = null;

        ReleaseArray(MorphTypePyramid); MorphTypePyramid = null;
        ReleaseArray(MorphColorPyramid); MorphColorPyramid = null;

        _lastScreenWidth = -1;
        _lastScreenHeight = -1;
    }

    /// <summary> RTHandle 配列を解放するヘルパー。 </summary>
    private static void ReleaseArray(RTHandle[] array)
    {
        if (array != null)
        {
            for (int i = 0; i < array.Length; i++)
            {
                array[i]?.Release();
                array[i] = null;
            }
        }
    }

    public void Dispose() => ReleaseAll();
}
