// =============================================================================
// PCDPostProcessStage.cs
// -----------------------------------------------------------------------------
// パイプライン後段処理ステージ。
// ホールフィリング後の結果マージ（Interpolate）とデバッグ可視化マップの生成、
// 仮想メッシュカウンターの非同期読み戻しを担う。
// =============================================================================
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 後段処理ステージ（Interpolate + DebugVisualize + StaticMeshCounterReadback）。
/// </summary>
internal class PCDPostProcessStage : IPCDPipelineStage
{
    public bool ShouldExecute(PCDPipelineContext ctx) => true; // 内部で分岐

    public void Execute(CommandBuffer cmd, PCDPipelineContext ctx)
    {
        ExecuteStageInterpolate(cmd, ctx);
        ExecuteStageDebugVisualize(cmd, ctx);
        RequestStaticMeshCounterReadback(ctx);
    }

    // =========================================================================
    // ステージ12: 補完とマージ
    // =========================================================================

    /// <summary>
    /// ホールフィリング後のオクルージョン結果を、仮想深度マップやカメラカラーバッファと
    /// マージして最終的な合成画像を生成する。
    /// </summary>
    private void ExecuteStageInterpolate(CommandBuffer cmd, PCDPipelineContext ctx)
    {
        if (ctx.Settings.holeFillingMethod == PCDRendererFeature.PCD_HoleFillingMethod.None)
            return;

        var cs = ctx.ComputeShader;
        var k = ctx.Kernels;
        var r = ctx.Resources;

        cmd.SetComputeTextureParam(cs, k.Interpolate, PCDShaderConstants.OcclusionResultMap, r.OcclusionResultMap);
        cmd.SetComputeTextureParam(cs, k.Interpolate, PCDShaderConstants.VirtualDepthMap, ctx.VirtualDepthTexture);
        cmd.SetComputeTextureParam(cs, k.Interpolate, PCDShaderConstants.CameraColorTexture, ctx.CameraColorTexture);
        cmd.SetComputeTextureParam(cs, k.Interpolate, PCDShaderConstants.OriginTypeMap, r.OriginTypeMap);
        cmd.SetComputeTextureParam(cs, k.Interpolate, PCDShaderConstants.FinalImage_RW, r.FinalImage);
        cmd.SetComputeTextureParam(cs, k.Interpolate, PCDShaderConstants.OriginMap_RW, r.DebugDisplayMap);
        cmd.DispatchCompute(cs, k.Interpolate, ctx.ThreadGroupsX, ctx.ThreadGroupsY, 1);
    }

    // =========================================================================
    // ステージ13: デバッグ可視化
    // =========================================================================

    /// <summary>
    /// PixelTag または OcclusionMap の可視化が有効な場合、
    /// OcclusionValueMap をカラー/グレースケールに変換してデバッグ表示用マップに書き込む。
    /// </summary>
    private void ExecuteStageDebugVisualize(CommandBuffer cmd, PCDPipelineContext ctx)
    {
        var s = ctx.Settings;
        bool isPatternMode = s.debugPatternId >= 0 && s.debugPatternId < 256;
        if (!s.enablePixelTagMap && !s.enableOcclusionMap && !isPatternMode)
            return;

        var cs = ctx.ComputeShader;
        var k = ctx.Kernels;
        var r = ctx.Resources;

        int displayMode = isPatternMode ? 3 : (s.enablePixelTagMap ? 1 : 2);
        cmd.SetComputeIntParam(cs, PCDShaderConstants.DebugDisplayMode, displayMode);

        if (isPatternMode)
        {
            cmd.SetComputeIntParam(cs, PCDShaderConstants.DebugPatternId, s.debugPatternId);
            cmd.SetComputeTextureParam(cs, k.VisualizeOcclusionDebug, PCDShaderConstants.NeighborCountMap_RW, r.NeighborCountMap);
        }

        cmd.SetComputeTextureParam(cs, k.VisualizeOcclusionDebug, PCDShaderConstants.OcclusionValueMap_RW, r.OcclusionValueMap);
        cmd.SetComputeTextureParam(cs, k.VisualizeOcclusionDebug, PCDShaderConstants.OriginMap_RW, r.DebugDisplayMap);
        cmd.DispatchCompute(cs, k.VisualizeOcclusionDebug, ctx.ThreadGroupsX, ctx.ThreadGroupsY, 1);
    }

    // =========================================================================
    // 非同期読み戻し
    // =========================================================================

    /// <summary>
    /// 仮想メッシュのピクセルカウントを非同期で読み戻す。
    /// 次フレームの密度倍率の計算に使用される。
    /// </summary>
    private void RequestStaticMeshCounterReadback(PCDPipelineContext ctx)
    {
        int totalPoints = ctx.PointCount;
        UnityEngine.Rendering.AsyncGPUReadback.Request(ctx.StaticMeshCounterBuffer, (request) =>
        {
            if (request.hasError || PCDRendererFeature.Instance == null)
            {
                return;
            }
            var data = request.GetData<uint>();
            uint virtualMeshPixelCount = data[0];
            PCDRendererFeature.Instance.LastFrameVirtualMeshPixelCount = virtualMeshPixelCount;

            if (totalPoints > 0)
            {
                uint multiplier = System.Math.Max(1u, virtualMeshPixelCount / (uint)totalPoints);
                PCDRendererFeature.Instance._internalDynamicMultiplier = multiplier;
            }
            else
            {
                PCDRendererFeature.Instance._internalDynamicMultiplier = 1;
            }
        });
    }
}
