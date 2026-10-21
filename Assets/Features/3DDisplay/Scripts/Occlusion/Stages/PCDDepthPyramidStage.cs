// =============================================================================
// PCDDepthPyramidStage.cs
// -----------------------------------------------------------------------------
// 深度ピラミッドの構築（L1〜L6）と勾配補正を担うステージ。
// ピラミッドの配列化により、旧実装のハードコード L1-L6 をループで処理する。
// =============================================================================
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 深度ピラミッド構築 + 勾配補正ステージ。
/// </summary>
internal class PCDDepthPyramidStage : IPCDPipelineStage
{
    public bool ShouldExecute(PCDPipelineContext ctx) => ctx.NeedsDepthPyramid;

    public void Execute(CommandBuffer cmd, PCDPipelineContext ctx)
    {
        BuildDepthPyramid(cmd, ctx);
        ApplyGradientCorrection(cmd, ctx);
    }

    // =========================================================================
    // ステージ9a: 深度ピラミッドの構築
    // =========================================================================

    /// <summary>
    /// 全画面の深度情報を段階的に1/2に縮小し、6レベルのピラミッドを構築する。
    /// L1 は ViewPositionMap と OriginTypeMap から物理点群のみを抽出する。
    /// L2以降は前レベルのダウンサンプルを行う。
    /// </summary>
    private void BuildDepthPyramid(CommandBuffer cmd, PCDPipelineContext ctx)
    {
        var cs = ctx.ComputeShader;
        var k = ctx.Kernels;
        var r = ctx.Resources;
        int sw = ctx.ScreenWidth;
        int sh = ctx.ScreenHeight;

        // L1: ViewPositionMap → DepthPyramidL1 (物理点群のみ)
        int l1_w = Mathf.Max(1, (sw + 1) / 2);
        int l1_h = Mathf.Max(1, (sh + 1) / 2);
        cmd.SetComputeTextureParam(cs, k.BuildDepthPyramid[0], PCDShaderConstants.ViewPositionMap, r.ViewPositionMap);
        cmd.SetComputeTextureParam(cs, k.BuildDepthPyramid[0], PCDShaderConstants.OriginTypeMap, r.OriginTypeMap);
        cmd.SetComputeTextureParam(cs, k.BuildDepthPyramid[0], PCDShaderConstants.DepthPyramidWrite[0], r.DepthPyramid[0]);
        cmd.DispatchCompute(cs, k.BuildDepthPyramid[0], (l1_w + 7) / 8, (l1_h + 7) / 8, 1);

        // L2〜L6: 前レベルのダウンサンプル
        int prevW = l1_w, prevH = l1_h;
        for (int i = 1; i < PCDShaderConstants.PYRAMID_LEVELS; i++)
        {
            int curW = Mathf.Max(1, (prevW + 1) / 2);
            int curH = Mathf.Max(1, (prevH + 1) / 2);
            cmd.SetComputeTextureParam(cs, k.BuildDepthPyramid[i], PCDShaderConstants.DepthPyramidRead[i - 1], r.DepthPyramid[i - 1]);
            cmd.SetComputeTextureParam(cs, k.BuildDepthPyramid[i], PCDShaderConstants.DepthPyramidWrite[i], r.DepthPyramid[i]);
            cmd.DispatchCompute(cs, k.BuildDepthPyramid[i], (curW + 7) / 8, (curH + 7) / 8, 1);
            prevW = curW;
            prevH = curH;
        }
    }

    // =========================================================================
    // ステージ9b: 勾配補正
    // =========================================================================

    /// <summary>
    /// 急な深度勾配がある領域の近傍サイズを補正する。
    /// </summary>
    private void ApplyGradientCorrection(CommandBuffer cmd, PCDPipelineContext ctx)
    {
        if (!ctx.Settings.enableGradientCorrection)
            return;

        var cs = ctx.ComputeShader;
        var k = ctx.Kernels;
        var r = ctx.Resources;

        // 深度ピラミッド全レベルをバインド
        for (int i = 0; i < PCDShaderConstants.PYRAMID_LEVELS; i++)
        {
            cmd.SetComputeTextureParam(cs, k.ApplyGradient, PCDShaderConstants.DepthPyramidRead[i], r.DepthPyramid[i]);
        }

        // グリッドLODレベルマップおよびOriginTypeMapを直接バインド
        cmd.SetComputeTextureParam(cs, k.ApplyGradient, PCDShaderConstants.FilteredGridLevelMap, r.FilteredGridLevelMap);
        cmd.SetComputeTextureParam(cs, k.ApplyGradient, PCDShaderConstants.OriginTypeMap, r.OriginTypeMap);

        var neighborhoodOut = ctx.Settings.recordNeighborhoodMap && ctx.Settings.enableGradientCorrection
            ? r.NeighborhoodMap : r.CorrectedNeighborhoodSizeMap;

        cmd.SetComputeTextureParam(cs, k.ApplyGradient, PCDShaderConstants.CorrectedNeighborhoodSizeMap_RW, neighborhoodOut);
        cmd.DispatchCompute(cs, k.ApplyGradient, ctx.ThreadGroupsX, ctx.ThreadGroupsY, 1);
    }
}
