// =============================================================================
// PCDOcclusionStage.cs
// -----------------------------------------------------------------------------
// オクルージョン判定コア処理を担うステージ。
// 各ピクセルについて、仮想オブジェクトが点群の手前/奥にあるかを判定する。
// =============================================================================
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// オクルージョン判定ステージ。
/// </summary>
internal class PCDOcclusionStage : IPCDPipelineStage
{
    public bool ShouldExecute(PCDPipelineContext ctx) => true; // 常に実行（内部で分岐）

    public void Execute(CommandBuffer cmd, PCDPipelineContext ctx)
    {
        var cs = ctx.ComputeShader;
        var k = ctx.Kernels;
        var r = ctx.Resources;
        var s = ctx.Settings;

        if (!ctx.HasVirtualObjects)
        {
            // 仮想オブジェクトが存在しないが、ホールフィリングが有効な場合は ColorMap をそのままコピー
            bool useHoleFilling = s.holeFillingMethod != PCDRendererFeature.PCD_HoleFillingMethod.None;
            if (useHoleFilling)
            {
                cmd.SetComputeTextureParam(cs, k.CopyColorToOcclusion, PCDShaderConstants.ColorMap, r.ColorMap);
                cmd.SetComputeTextureParam(cs, k.CopyColorToOcclusion, PCDShaderConstants.ViewPositionMap, r.ViewPositionMap);
                cmd.SetComputeTextureParam(cs, k.CopyColorToOcclusion, PCDShaderConstants.OriginTypeMap_RW, r.OriginTypeMap);
                cmd.SetComputeTextureParam(cs, k.CopyColorToOcclusion, PCDShaderConstants.OcclusionResultMap_RW, r.OcclusionResultMap);
                cmd.DispatchCompute(cs, k.CopyColorToOcclusion, ctx.ThreadGroupsX, ctx.ThreadGroupsY, 1);
            }
            return;
        }

        if (s.kernelType == PCDRendererFeature.PCD_OcclusionKernel.Skip)
        {
            // オクルージョン判定をスキップし、色情報をそのままコピー
            cmd.SetComputeTextureParam(cs, k.CopyColorToOcclusion, PCDShaderConstants.ColorMap, r.ColorMap);
            cmd.SetComputeTextureParam(cs, k.CopyColorToOcclusion, PCDShaderConstants.ViewPositionMap, r.ViewPositionMap);
            cmd.SetComputeTextureParam(cs, k.CopyColorToOcclusion, PCDShaderConstants.OriginTypeMap_RW, r.OriginTypeMap);
            cmd.SetComputeTextureParam(cs, k.CopyColorToOcclusion, PCDShaderConstants.OcclusionResultMap_RW, r.OcclusionResultMap);
            cmd.DispatchCompute(cs, k.CopyColorToOcclusion, ctx.ThreadGroupsX, ctx.ThreadGroupsY, 1);
        }
        else
        {
            // フルオクルージョン判定を実行
            cmd.SetComputeTextureParam(cs, k.ComputeOcclusion, PCDShaderConstants.ColorMap, r.ColorMap);
            cmd.SetComputeTextureParam(cs, k.ComputeOcclusion, PCDShaderConstants.ViewPositionMap, r.ViewPositionMap);
            cmd.SetComputeTextureParam(cs, k.ComputeOcclusion, PCDShaderConstants.VirtualDepthMap, ctx.VirtualDepthTexture);
            cmd.SetComputeTextureParam(cs, k.ComputeOcclusion, PCDShaderConstants.OriginTypeMap_RW, r.OriginTypeMap);
            cmd.SetComputeTextureParam(cs, k.ComputeOcclusion, PCDShaderConstants.FinalNeighborhoodSizeMap, ctx.ActiveNeighborhoodSizeMap);

            // 深度ピラミッド全レベルをバインド
            for (int i = 0; i < PCDShaderConstants.PYRAMID_LEVELS; i++)
            {
                cmd.SetComputeTextureParam(cs, k.ComputeOcclusion, PCDShaderConstants.DepthPyramidRead[i], r.DepthPyramid[i]);
            }
            cmd.SetComputeTextureParam(cs, k.ComputeOcclusion, PCDShaderConstants.OcclusionResultMap_RW, r.OcclusionResultMap);

            // デバッグ出力用のフラグとマップ (セクターマスク収集スイープ中も常に有効化)
            bool isSectorMaskSweep = SICESI.SICESI_SectorMaskCollector.IsCollectingActive;
            int shouldRecordDebug = (s.recordOcclusionDebugMap || s.recordPixelTagMap || s.enablePixelTagMap || s.enableOcclusionMap || s.recordNeighborCountMap || isSectorMaskSweep) ? 1 : 0;
            cmd.SetComputeIntParam(cs, PCDShaderConstants.RecordOcclusionDebug, shouldRecordDebug);

            cmd.SetComputeTextureParam(cs, k.ComputeOcclusion, PCDShaderConstants.NeighborCountMap_RW, r.NeighborCountMap);
            cmd.SetComputeTextureParam(cs, k.ComputeOcclusion, PCDShaderConstants.OcclusionValueMap_RW, r.OcclusionValueMap);
            cmd.SetComputeTextureParam(cs, k.ComputeOcclusion, PCDShaderConstants.OriginMap_RW, r.DebugDisplayMap);
            cmd.DispatchCompute(cs, k.ComputeOcclusion, ctx.ThreadGroupsX, ctx.ThreadGroupsY, 1);
        }
    }
}
