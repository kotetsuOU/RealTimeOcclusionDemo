// =============================================================================
// PCDResourcePool.cs
// -----------------------------------------------------------------------------
// 点群オクルージョンパイプラインで使用する全 RTHandle を一元管理するクラス。
//
// 画面サイズが変わった場合にのみ常時使用テクスチャを再生成し、
// 同一サイズであれば既存のハンドルを再利用する。
//
// ピラミッド系テクスチャは配列で管理し、ループアクセスを可能にする。
//
// 【Phase 1 メモリ最適化】
//   - デバッグ・記録用マップ（DebugDisplayMap, OcclusionValueMap, NeighborCountMap,
//     IntegratedDepthMap, NeighborhoodMap）は、必要フレームのみフル解像度を確保し、
//     それ以外は 1×1 ダミーテクスチャをバインドする。
//     これにより、通常時の多重書き込み（16B/px × 数回）によるメモリ帯域浪費を完全排除。
//   - ホールフィリング用作業バッファ（PullPushPyramid, Morphology バッファ）は、
//     選択中の手法でのみ確保し、非選択時は即時解放して VRAM を節約。
// =============================================================================
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using Core.Logging;

/// <summary>
/// オクルージョンパイプラインで使用する全 RTHandle のライフサイクルを管理する。
/// </summary>
internal class PCDResourcePool : System.IDisposable
{
    // =========================================================================
    // フォーマット定数
    // =========================================================================
    private static readonly GraphicsFormat FormatARGB = GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.ARGBFloat, false);
    private static readonly GraphicsFormat FormatRGBAHalf = GraphicsFormat.R16G16B16A16_SFloat;
    private static readonly GraphicsFormat FormatRFloat = GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.RFloat, false);
    private static readonly GraphicsFormat FormatRGFloat = GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.RGFloat, false);
    private const GraphicsFormat FormatUInt = GraphicsFormat.R32_UInt;
    private const GraphicsFormat FormatSInt = GraphicsFormat.R32_SInt;
    private const GraphicsFormat FormatR16 = GraphicsFormat.R16G16B16A16_SFloat;
    private const GraphicsFormat FormatR8UInt = GraphicsFormat.R8_UInt;

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
    // 近傍サイズマップ（フル解像度: R8_UInt 1B/px）
    // =========================================================================
    /// <summary> 冗長マップ削除に伴う後方互換用エイリアス。実体は CorrectedNeighborhoodSizeMap です。 </summary>
    public RTHandle NeighborhoodSizeMap => CorrectedNeighborhoodSizeMap;
    public RTHandle CorrectedNeighborhoodSizeMap { get; private set; }

    // =========================================================================
    // 結果マップ（フル解像度: RGBA16F 8B/px）
    // =========================================================================
    public RTHandle OcclusionResultMap { get; private set; }
    public RTHandle FinalImage { get; private set; }

    // =========================================================================
    // デバッグ・記録マップ（必要時のみフル解像度、それ以外は 1×1 ダミー）
    // =========================================================================
    private readonly PCDOptionalTexture _debugDisplayMap = new PCDOptionalTexture(FormatRGBAHalf, "PCD_DebugDisplay");
    private readonly PCDOptionalTexture _occlusionValueMap = new PCDOptionalTexture(FormatRGFloat, "PCD_OcclusionValue");
    private readonly PCDOptionalTexture _neighborCountMap = new PCDOptionalTexture(FormatUInt, "PCD_NeighborCountMapDebug");
    private readonly PCDOptionalTexture _integratedDepthMap = new PCDOptionalTexture(FormatUInt, "PCD_IntegratedDepthMap");
    private readonly PCDOptionalTexture _neighborhoodMap = new PCDOptionalTexture(FormatSInt, "PCD_NeighborhoodMapDebug");

    public RTHandle DebugDisplayMap => _debugDisplayMap.Handle;
    public RTHandle OcclusionValueMap => _occlusionValueMap.Handle;
    public RTHandle NeighborCountMap => _neighborCountMap.Handle;
    public RTHandle IntegratedDepthMap => _integratedDepthMap.Handle;
    public RTHandle NeighborhoodMap => _neighborhoodMap.Handle;

    // =========================================================================
    // 深度ピラミッド (L1〜L6) — 配列でアクセス
    // =========================================================================
    public RTHandle[] DepthPyramid { get; private set; }

    // =========================================================================
    // Pull-Push ピラミッド (5レベル) — PullPush 選択時のみ確保
    // =========================================================================
    public RTHandle[] PullPushPyramid { get; private set; }

    // =========================================================================
    // モルフォロジー一時バッファ / ピラミッド (L1〜L6) — Morphology 選択時のみ確保
    // =========================================================================
    public RTHandle MorphColorTemp { get; private set; }
    public RTHandle MorphTypeTemp { get; private set; }
    public RTHandle[] MorphTypePyramid { get; private set; }
    public RTHandle[] MorphColorPyramid { get; private set; }

    // =========================================================================
    // 状態管理
    // =========================================================================
    private int _lastScreenWidth = -1;
    private int _lastScreenHeight = -1;
    private int _lastGridSize = -1;

    // =========================================================================
    // アロケーション
    // =========================================================================

    /// <summary>
    /// 後方互換性用オーバーロード。
    /// </summary>
    public void EnsureAllocated(int screenWidth, int screenHeight, PCDRendererFeature.PCD_GridSize gridSize)
    {
        PCDResourceRequirements req;
        if (PCDRendererFeature.Instance != null && PCDRendererFeature.Instance.settings != null)
        {
            req = PCDResourceRequirements.FromSettings(PCDRendererFeature.Instance.settings.GetSettings(PCDRendererFeature.Instance._internalDynamicMultiplier));
        }
        else if (PCDOcclusionPipelineController.Instance != null)
        {
            req = PCDResourceRequirements.FromSettings(PCDOcclusionPipelineController.Instance.GetSettings());
        }
        else
        {
            req = PCDResourceRequirements.All;
        }

        EnsureAllocated(screenWidth, screenHeight, gridSize, req);
    }

    /// <summary>
    /// 画面サイズ・グリッドサイズ・必要リソースに応じて中間テクスチャを確保する。
    /// 同一サイズかつ必要リソースが確保済みであれば何もせずに返る（毎フレーム呼ばれても安全）。
    /// </summary>
    public void EnsureAllocated(int screenWidth, int screenHeight, PCDRendererFeature.PCD_GridSize gridSize, in PCDResourceRequirements req)
    {
        int gs = (int)gridSize;
        if (gs == 0) gs = 16;

        bool sizeChanged = (_lastScreenWidth != screenWidth || _lastScreenHeight != screenHeight);
        bool gridChanged = (_lastGridSize != gs);

        if (ColorMap == null || sizeChanged)
        {
            AllocateCoreMaps(screenWidth, screenHeight, gs);
            ReleasePullPush();
            ReleaseMorphology();
        }
        else if (gridChanged)
        {
            AllocateGridMaps(screenWidth, screenHeight, gs);
        }

        _lastScreenWidth = screenWidth;
        _lastScreenHeight = screenHeight;
        _lastGridSize = gs;

        // --- 手法別作業バッファ（不要時は解放して VRAM 節約） ---
        if (req.PullPush)
        {
            EnsurePullPush(screenWidth, screenHeight);
        }
        else
        {
            ReleasePullPush();
        }

        if (req.Morphology)
        {
            EnsureMorphology(screenWidth, screenHeight);
        }
        else
        {
            ReleaseMorphology();
        }

        // --- デバッグ・記録マップ（必要時のみフル解像度、それ以外は 1×1 ダミー） ---
        bool debugAllocated = false;
        debugAllocated |= _debugDisplayMap.Update(req.DebugDisplay, screenWidth, screenHeight);
        debugAllocated |= _occlusionValueMap.Update(req.OcclusionDebug, screenWidth, screenHeight);
        debugAllocated |= _neighborCountMap.Update(req.OcclusionDebug, screenWidth, screenHeight);
        debugAllocated |= _integratedDepthMap.Update(req.IntegratedDepth, screenWidth, screenHeight);
        debugAllocated |= _neighborhoodMap.Update(req.NeighborhoodRecord, screenWidth, screenHeight);

        if (debugAllocated)
        {
            AppLogger.Log(PCD_LogTriggers.TagBuffer, $"[PCDResourcePool] Debug/Record RTHandles updated for {screenWidth}x{screenHeight}");
        }
    }

    /// <summary> 常時使用するフル解像度マップと深度ピラミッドを生成する。 </summary>
    private void AllocateCoreMaps(int screenWidth, int screenHeight, int gs)
    {
        ReleaseCoreMaps();

        // --- メインマップ（フル解像度: Color=RGBA16F, Type=R8_UInt） ---
        ColorMap = Alloc(screenWidth, screenHeight, FormatRGBAHalf, "PCD_ColorMap");
        DepthMap = Alloc(screenWidth, screenHeight, FormatUInt, "PCD_DepthMap");
        ViewPositionMap = Alloc(screenWidth, screenHeight, FormatARGB, "PCD_ViewPosMap");
        OriginTypeMap = Alloc(screenWidth, screenHeight, FormatR8UInt, "PCD_OriginTypeMap");

        // --- グリッド・密度マップ（グリッド解像度） ---
        AllocateGridMaps(screenWidth, screenHeight, gs);

        // --- 近傍サイズマップ（フル解像度: R8_UInt 1B/px） ---
        // ※ 冗長な NeighborhoodSizeMap は削除し、CorrectedNeighborhoodSizeMap 1枚に統合
        CorrectedNeighborhoodSizeMap = Alloc(screenWidth, screenHeight, FormatR8UInt, "PCD_CorrectedNeighborhood");

        // --- 結果マップ（フル解像度: RGBA16F 8B/px） ---
        OcclusionResultMap = Alloc(screenWidth, screenHeight, FormatRGBAHalf, "PCD_OcclusionResult");
        FinalImage = Alloc(screenWidth, screenHeight, FormatRGBAHalf, "PCD_FinalImage");

        // --- 深度ピラミッド (L1〜L6: 各レベルで1/2に縮小) ---
        DepthPyramid = new RTHandle[PCDShaderConstants.PYRAMID_LEVELS];
        int pw = screenWidth, ph = screenHeight;
        for (int i = 0; i < PCDShaderConstants.PYRAMID_LEVELS; i++)
        {
            pw = Mathf.Max(1, pw / 2);
            ph = Mathf.Max(1, ph / 2);
            DepthPyramid[i] = Alloc(pw, ph, FormatR16, $"PCD_DepthPyramidL{i + 1}");
        }
    }

    /// <summary> グリッド解像度のマップを生成する。 </summary>
    private void AllocateGridMaps(int screenWidth, int screenHeight, int gs)
    {
        ReleaseGridMaps();

        int gridGroupsX = (screenWidth + gs - 1) / gs;
        int gridGroupsY = (screenHeight + gs - 1) / gs;

        GridZMinMap = Alloc(gridGroupsX, gridGroupsY, FormatUInt, "PCD_GridZMin");
        DensityMap = Alloc(gridGroupsX, gridGroupsY, FormatRFloat, "PCD_Density");
        GridLevelMap = Alloc(gridGroupsX, gridGroupsY, FormatSInt, "PCD_GridLevel");
        FilteredGridLevelMap = Alloc(gridGroupsX, gridGroupsY, FormatSInt, "PCD_FilteredGridLevel");
    }

    /// <summary> Pull-Push ピラミッド (5レベル: フル解像度から段階的に縮小) を確保する。 </summary>
    private void EnsurePullPush(int screenWidth, int screenHeight)
    {
        if (PullPushPyramid != null) return;

        PullPushPyramid = new RTHandle[5];
        int pw = screenWidth, ph = screenHeight;
        for (int i = 0; i < 5; i++)
        {
            PullPushPyramid[i] = Alloc(pw, ph, FormatRGBAHalf, $"PCD_PP_{i}");
            pw = Mathf.Max(1, (pw + 1) / 2);
            ph = Mathf.Max(1, (ph + 1) / 2);
        }
    }

    /// <summary> モルフォロジー一時バッファとピラミッド (L1〜L6) を確保する。 </summary>
    private void EnsureMorphology(int screenWidth, int screenHeight)
    {
        if (MorphColorTemp != null) return;

        MorphColorTemp = Alloc(screenWidth, screenHeight, FormatRGBAHalf, "PCD_MorphColorTemp");
        MorphTypeTemp = Alloc(screenWidth, screenHeight, FormatR8UInt, "PCD_MorphTypeTemp");

        MorphTypePyramid = new RTHandle[PCDShaderConstants.PYRAMID_LEVELS];
        MorphColorPyramid = new RTHandle[PCDShaderConstants.PYRAMID_LEVELS];
        int pw = screenWidth, ph = screenHeight;
        for (int i = 0; i < PCDShaderConstants.PYRAMID_LEVELS; i++)
        {
            pw = Mathf.Max(1, pw / 2);
            ph = Mathf.Max(1, ph / 2);
            MorphTypePyramid[i] = Alloc(pw, ph, FormatR8UInt, $"PCD_MorphTL{i + 1}");
            MorphColorPyramid[i] = Alloc(pw, ph, FormatRGBAHalf, $"PCD_MorphCL{i + 1}");
        }
    }

    private static RTHandle Alloc(int width, int height, GraphicsFormat format, string name)
    {
        return RTHandles.Alloc(width, height, colorFormat: format, enableRandomWrite: true, name: name);
    }

    // =========================================================================
    // 解放
    // =========================================================================

    /// <summary> 全 RTHandle を解放する。 </summary>
    public void ReleaseAll()
    {
        ReleaseCoreMaps();
        ReleasePullPush();
        ReleaseMorphology();

        _debugDisplayMap.Release();
        _occlusionValueMap.Release();
        _neighborCountMap.Release();
        _integratedDepthMap.Release();
        _neighborhoodMap.Release();

        _lastScreenWidth = -1;
        _lastScreenHeight = -1;
        _lastGridSize = -1;
    }

    private void ReleaseCoreMaps()
    {
        ColorMap?.Release(); ColorMap = null;
        DepthMap?.Release(); DepthMap = null;
        ViewPositionMap?.Release(); ViewPositionMap = null;
        OriginTypeMap?.Release(); OriginTypeMap = null;

        ReleaseGridMaps();

        CorrectedNeighborhoodSizeMap?.Release(); CorrectedNeighborhoodSizeMap = null;

        OcclusionResultMap?.Release(); OcclusionResultMap = null;
        FinalImage?.Release(); FinalImage = null;

        ReleaseArray(DepthPyramid); DepthPyramid = null;
    }

    private void ReleaseGridMaps()
    {
        GridZMinMap?.Release(); GridZMinMap = null;
        DensityMap?.Release(); DensityMap = null;
        GridLevelMap?.Release(); GridLevelMap = null;
        FilteredGridLevelMap?.Release(); FilteredGridLevelMap = null;
    }

    private void ReleasePullPush()
    {
        ReleaseArray(PullPushPyramid); PullPushPyramid = null;
    }

    private void ReleaseMorphology()
    {
        MorphColorTemp?.Release(); MorphColorTemp = null;
        MorphTypeTemp?.Release(); MorphTypeTemp = null;
        ReleaseArray(MorphTypePyramid); MorphTypePyramid = null;
        ReleaseArray(MorphColorPyramid); MorphColorPyramid = null;
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
