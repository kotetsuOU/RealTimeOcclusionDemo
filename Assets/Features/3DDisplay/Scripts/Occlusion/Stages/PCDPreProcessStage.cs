// =============================================================================
// PCDPreProcessStage.cs
// -----------------------------------------------------------------------------
// オクルージョンパイプラインの前段処理を担うステージ。
//
// 実行するサブステージ:
//   1. バッファマージ（外部+内部バッファの結合）
//   2. グローバルパラメータ設定
//   3. マップクリア
//   4. 仮想深度マップからの初期化
//   5. 3D点群のスクリーンスペースへの投影
//   6. 密度計算とLOD
// =============================================================================
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// クリア・投影・密度/LOD計算を行う前段処理ステージ。
/// </summary>
internal class PCDPreProcessStage : IPCDPipelineStage
{
    public bool ShouldExecute(PCDPipelineContext ctx) => true; // 常に実行

    public void Execute(CommandBuffer cmd, PCDPipelineContext ctx)
    {
        var cs = ctx.ComputeShader;
        var k = ctx.Kernels;
        var r = ctx.Resources;
        var s = ctx.Settings;

        // リバースZバッファへの対応フラグをセット
        cmd.SetComputeIntParam(cs, Shader.PropertyToID("_IsReversedZ"), SystemInfo.usesReversedZBuffer ? 1 : 0);

        // バッファの結合（外部＋内部が両方存在する場合）
        MergeExternalAndInternalBuffers(cmd, cs, ctx);

        // コンピュートシェーダーのグローバルパラメータを設定
        SetGlobalComputeParams(cmd, cs, ctx);

        bool runInitFromCamera = ctx.HasVirtualDepth && s.enableVirtualDepthIntegration;

        // ステージ1: 中間RTテクスチャのクリア
        ExecuteStageClearMaps(cmd, cs, ctx, runInitFromCamera);

        // ステージ2: 仮想深度マップからの初期化
        ExecuteStageInitFromCamera(cmd, cs, ctx, runInitFromCamera);

        // ステージ3: 3D点群のスクリーンスペースへの投影
        ExecuteStageProjectPoints(cmd, cs, ctx);

        // ステージ4-8: 密度計算とLOD
        ExecuteStageDensityAndLOD(cmd, cs, ctx);
    }

    // =========================================================================
    // バッファ結合
    // =========================================================================

    private void MergeExternalAndInternalBuffers(CommandBuffer cmd, ComputeShader cs, PCDPipelineContext ctx)
    {
        if (!ctx.UseExternal || ctx.ExternalCount <= 0 || ctx.InternalCount <= 0)
            return;

        var k = ctx.Kernels;

        // 外部バッファを結合先の先頭にコピー
        cmd.SetComputeBufferParam(cs, k.MergeBuffer, PCDShaderConstants.MergeDstBuffer, ctx.CombinedBuffer);
        cmd.SetComputeBufferParam(cs, k.MergeBuffer, PCDShaderConstants.MergeSrcBuffer, ctx.ExternalBuffer);
        cmd.SetComputeIntParam(cs, PCDShaderConstants.MergeSrcOffset, 0);
        cmd.SetComputeIntParam(cs, PCDShaderConstants.MergeDstOffset, 0);
        cmd.SetComputeIntParam(cs, PCDShaderConstants.MergeCopyCount, ctx.ExternalCount);
        int mergeGroupsExt = (ctx.ExternalCount + 255) / 256;
        cmd.DispatchCompute(cs, k.MergeBuffer, mergeGroupsExt, 1, 1);

        // 内部バッファを結合先の外部バッファの後ろにコピー
        cmd.SetComputeBufferParam(cs, k.MergeBuffer, PCDShaderConstants.MergeSrcBuffer, ctx.InternalBuffer);
        cmd.SetComputeIntParam(cs, PCDShaderConstants.MergeSrcOffset, 0);
        cmd.SetComputeIntParam(cs, PCDShaderConstants.MergeDstOffset, ctx.ExternalCount);
        cmd.SetComputeIntParam(cs, PCDShaderConstants.MergeCopyCount, ctx.InternalCount);
        int mergeGroupsInt = (ctx.InternalCount + 255) / 256;
        cmd.DispatchCompute(cs, k.MergeBuffer, mergeGroupsInt, 1, 1);
    }

    // =========================================================================
    // グローバルパラメータ設定
    // =========================================================================

    private void SetGlobalComputeParams(CommandBuffer cmd, ComputeShader cs, PCDPipelineContext ctx)
    {
        var s = ctx.Settings;
        cmd.SetComputeIntParam(cs, PCDShaderConstants.PointCount, ctx.PointCount);
        cmd.SetComputeVectorParam(cs, PCDShaderConstants.ScreenParams, ctx.ScreenParams);
        cmd.SetComputeMatrixParam(cs, PCDShaderConstants.ViewMatrix, ctx.ViewMatrix);
        cmd.SetComputeMatrixParam(cs, PCDShaderConstants.ProjectionMatrix, ctx.ProjectionMatrix);
        cmd.SetComputeFloatParam(cs, PCDShaderConstants.DensityThreshold_e, s.densityThreshold_e);
        cmd.SetComputeFloatParam(cs, PCDShaderConstants.NeighborhoodParam_p_prime, s.neighborhoodParam_p_prime);
        cmd.SetComputeFloatParam(cs, PCDShaderConstants.GradientThreshold_g_th, s.gradientThreshold_g_th);
        cmd.SetComputeIntParam(cs, PCDShaderConstants.KernelType, (int)s.kernelType);
        cmd.SetComputeIntParam(cs, PCDShaderConstants.EvaluationMode, (int)s.evaluationMode);
        cmd.SetComputeIntParam(cs, PCDShaderConstants.MinOccludedSectors, s.minOccludedSectors);
        cmd.SetComputeIntParam(cs, PCDShaderConstants.MaxConsecutiveEmptySectors, s.maxConsecutiveEmptySectors);
        cmd.SetComputeIntParam(cs, PCDShaderConstants.MinSearchLevel, s.minSearchLevel);
        cmd.SetComputeFloatParam(cs, PCDShaderConstants.Alpha, s.exponentAlpha);
        cmd.SetComputeFloatParam(cs, PCDShaderConstants.OcclusionThreshold, s.occlusionThreshold);
        cmd.SetComputeFloatParam(cs, PCDShaderConstants.OcclusionFadeWidth, s.occlusionFadeWidth);

        // 提案手法の各最適化フラグ
        cmd.SetComputeIntParam(cs, Shader.PropertyToID("_EnableTagBasedOptimization"), s.enableTagBasedOptimization ? 1 : 0);
        cmd.SetComputeIntParam(cs, Shader.PropertyToID("_EnableTypeAwareDensity"), s.enableTypeAwareDensity ? 1 : 0);
        cmd.SetComputeIntParam(cs, Shader.PropertyToID("_EnableSoftOcclusionFade"), s.enableSoftOcclusionFade ? 1 : 0);
        cmd.SetComputeIntParam(cs, Shader.PropertyToID("_EnableJointBilateralHoleFilling"), (s.holeFillingMethod != PCDRendererFeature.PCD_HoleFillingMethod.None) ? 1 : 0);

        // リバースZバッファへの対応フラグをセット
        cmd.SetComputeIntParam(cs, Shader.PropertyToID("_IsReversedZ"), SystemInfo.usesReversedZBuffer ? 1 : 0);
        cmd.SetComputeIntParam(cs, PCDShaderConstants.IsHalfMirrorEnabled, ctx.IsHalfMirrorEnabled ? 1 : 0);

        // 仮想物体における密度倍率
        uint densityMultiplier = System.Math.Max(1u, s._dynamicMultiplierRuntimeValue);
        cmd.SetComputeIntParam(cs, Shader.PropertyToID("_StaticMeshDensityMultiplier"), (int)densityMultiplier);

        // グリッドサイズに対応するシェーダーキーワードを切り替え
        int gs = (int)s.gridSize;
        if (gs == 0) gs = 16;
        cmd.DisableShaderKeyword("GRID_SIZE_8");
        cmd.DisableShaderKeyword("GRID_SIZE_16");
        cmd.DisableShaderKeyword("GRID_SIZE_32");
        cmd.EnableShaderKeyword($"GRID_SIZE_{gs}");
    }

    // =========================================================================
    // ステージ1: マップクリア
    // =========================================================================

    private void ExecuteStageClearMaps(CommandBuffer cmd, ComputeShader cs, PCDPipelineContext ctx, bool runInitFromCamera)
    {
        var k = ctx.Kernels;
        var r = ctx.Resources;
        var s = ctx.Settings;

        if (!runInitFromCamera)
        {
            cmd.SetComputeTextureParam(cs, k.ClearMaps, PCDShaderConstants.ColorMap_RW, r.ColorMap);
            cmd.SetComputeTextureParam(cs, k.ClearMaps, PCDShaderConstants.DepthMap_RW,
                s.recordIntegratedDepthMap ? r.IntegratedDepthMap : r.DepthMap);
            cmd.SetComputeTextureParam(cs, k.ClearMaps, PCDShaderConstants.ViewPositionMap_RW, r.ViewPositionMap);
            cmd.SetComputeTextureParam(cs, k.ClearMaps, PCDShaderConstants.OcclusionResultMap_RW, r.OcclusionResultMap);
            cmd.SetComputeTextureParam(cs, k.ClearMaps, PCDShaderConstants.OcclusionValueMap_RW, r.OcclusionValueMap);

            var clearFinalImageTarget = (s.holeFillingMethod != PCDRendererFeature.PCD_HoleFillingMethod.None) ? r.FinalImage : r.OcclusionResultMap;
            cmd.SetComputeTextureParam(cs, k.ClearMaps, PCDShaderConstants.FinalImage_RW, clearFinalImageTarget);

            cmd.SetComputeTextureParam(cs, k.ClearMaps, PCDShaderConstants.OriginTypeMap_RW, r.OriginTypeMap);
            cmd.SetComputeTextureParam(cs, k.ClearMaps, PCDShaderConstants.OriginMap_RW, r.DebugDisplayMap);
            cmd.DispatchCompute(cs, k.ClearMaps, ctx.ThreadGroupsX, ctx.ThreadGroupsY, 1);
        }

        // カウンターのクリアは常に実行
        cmd.SetComputeBufferParam(cs, k.ClearCounter, PCDShaderConstants.StaticMeshCounter_RW, ctx.StaticMeshCounterBuffer);
        cmd.DispatchCompute(cs, k.ClearCounter, 1, 1, 1);
    }

    // =========================================================================
    // ステージ2: 仮想深度マップからの初期化
    // =========================================================================

    private void ExecuteStageInitFromCamera(CommandBuffer cmd, ComputeShader cs, PCDPipelineContext ctx, bool runInitFromCamera)
    {
        var k = ctx.Kernels;
        var r = ctx.Resources;

        if (runInitFromCamera)
        {
            cmd.SetComputeIntParam(cs, PCDShaderConstants.UseVirtualDepth, 1);
            cmd.SetComputeTextureParam(cs, k.InitFromCamera, PCDShaderConstants.VirtualDepthMap, ctx.VirtualDepthTexture);

            if (ctx.CameraColorTexture.IsValid())
            {
                cmd.SetComputeTextureParam(cs, k.InitFromCamera, PCDShaderConstants.CameraColorTexture, ctx.CameraColorTexture);
            }

            var depthMap = ctx.Settings.recordIntegratedDepthMap ? r.IntegratedDepthMap : r.DepthMap;
            cmd.SetComputeTextureParam(cs, k.InitFromCamera, PCDShaderConstants.DepthMap_RW, depthMap);
            cmd.SetComputeTextureParam(cs, k.InitFromCamera, PCDShaderConstants.ColorMap_RW, r.ColorMap);
            cmd.SetComputeTextureParam(cs, k.InitFromCamera, PCDShaderConstants.ViewPositionMap_RW, r.ViewPositionMap);
            cmd.SetComputeTextureParam(cs, k.InitFromCamera, PCDShaderConstants.OriginTypeMap_RW, r.OriginTypeMap);
            cmd.SetComputeTextureParam(cs, k.InitFromCamera, PCDShaderConstants.OriginMap_RW, r.DebugDisplayMap);

            cmd.SetComputeBufferParam(cs, k.InitFromCamera, PCDShaderConstants.StaticMeshCounter_RW, ctx.StaticMeshCounterBuffer);
            cmd.SetComputeMatrixParam(cs, PCDShaderConstants.InverseProjectionMatrix, ctx.InverseProjectionMatrix);
            cmd.DispatchCompute(cs, k.InitFromCamera, ctx.ThreadGroupsX, ctx.ThreadGroupsY, 1);
        }
        else
        {
            cmd.SetComputeIntParam(cs, PCDShaderConstants.UseVirtualDepth, 0);
        }
    }

    // =========================================================================
    // ステージ3: 3D点群のスクリーンスペースへの投影
    // =========================================================================

    private void ExecuteStageProjectPoints(CommandBuffer cmd, ComputeShader cs, PCDPipelineContext ctx)
    {
        var k = ctx.Kernels;
        var r = ctx.Resources;
        var depthMap = ctx.Settings.recordIntegratedDepthMap ? r.IntegratedDepthMap : r.DepthMap;

        cmd.SetComputeBufferParam(cs, k.ProjectPoints, PCDShaderConstants.PointBuffer, ctx.PointBuffer);
        cmd.SetComputeTextureParam(cs, k.ProjectPoints, PCDShaderConstants.ColorMap_RW, r.ColorMap);
        cmd.SetComputeTextureParam(cs, k.ProjectPoints, PCDShaderConstants.DepthMap_RW, depthMap);
        cmd.SetComputeTextureParam(cs, k.ProjectPoints, PCDShaderConstants.ViewPositionMap_RW, r.ViewPositionMap);
        cmd.SetComputeTextureParam(cs, k.ProjectPoints, PCDShaderConstants.OriginTypeMap_RW, r.OriginTypeMap);
        int projectGroups = (ctx.PointCount + 255) / 256;
        cmd.DispatchCompute(cs, k.ProjectPoints, projectGroups, 1, 1);
    }

    // =========================================================================
    // ステージ4〜8: 密度計算とLOD
    // =========================================================================

    private void ExecuteStageDensityAndLOD(CommandBuffer cmd, ComputeShader cs, PCDPipelineContext ctx)
    {
        if (!ctx.NeedsNeighborhoodSize)
            return;

        var k = ctx.Kernels;
        var r = ctx.Resources;
        var s = ctx.Settings;
        var depthMap = s.recordIntegratedDepthMap ? r.IntegratedDepthMap : r.DepthMap;

        if (s.enableDensityBasedLOD)
        {
            // ステージ4: 各グリッドセルの最小深度を計算
            cmd.SetComputeTextureParam(cs, k.CalcGridZMin, PCDShaderConstants.DepthMap, depthMap);
            cmd.SetComputeTextureParam(cs, k.CalcGridZMin, PCDShaderConstants.GridZMinMap_RW, r.GridZMinMap);
            cmd.DispatchCompute(cs, k.CalcGridZMin, ctx.GridGroupsX, ctx.GridGroupsY, 1);

            // ステージ5: 画面上のサンプル密度を計算
            cmd.SetComputeTextureParam(cs, k.CalcDensity, PCDShaderConstants.DepthMap, depthMap);
            cmd.SetComputeTextureParam(cs, k.CalcDensity, PCDShaderConstants.GridZMinMap, r.GridZMinMap);
            cmd.SetComputeTextureParam(cs, k.CalcDensity, PCDShaderConstants.OriginTypeMap, r.OriginTypeMap);
            cmd.SetComputeTextureParam(cs, k.CalcDensity, PCDShaderConstants.DensityMap_RW, r.DensityMap);
            cmd.DispatchCompute(cs, k.CalcDensity, ctx.GridGroupsX, ctx.GridGroupsY, 1);

            // ステージ6: 密度に応じたグリッドレベルを決定
            cmd.SetComputeTextureParam(cs, k.CalcGridLevel, PCDShaderConstants.DensityMap, r.DensityMap);
            cmd.SetComputeTextureParam(cs, k.CalcGridLevel, PCDShaderConstants.GridLevelMap_RW, r.GridLevelMap);
            int gridThreadX = (ctx.GridGroupsX + 15) / 16;
            int gridThreadY = (ctx.GridGroupsY + 15) / 16;
            cmd.DispatchCompute(cs, k.CalcGridLevel, Mathf.Max(1, gridThreadX), Mathf.Max(1, gridThreadY), 1);

            // ステージ7: メディアンフィルターでグリッドレベルを平滑化
            cmd.SetComputeTextureParam(cs, k.GridMedianFilter, PCDShaderConstants.GridLevelMap, r.GridLevelMap);
            cmd.SetComputeTextureParam(cs, k.GridMedianFilter, PCDShaderConstants.FilteredGridLevelMap_RW, r.FilteredGridLevelMap);
            cmd.DispatchCompute(cs, k.GridMedianFilter, Mathf.Max(1, gridThreadX), Mathf.Max(1, gridThreadY), 1);

            // ステージ8: フィルター処理されたLODに基づいて近傍サイズを算出
            cmd.SetComputeTextureParam(cs, k.CalcNeighborhoodSize, PCDShaderConstants.FilteredGridLevelMap, r.FilteredGridLevelMap);
            cmd.SetComputeTextureParam(cs, k.CalcNeighborhoodSize, PCDShaderConstants.NeighborhoodSizeMap_RW,
                s.recordNeighborhoodMap && !s.enableGradientCorrection ? r.NeighborhoodMap : r.NeighborhoodSizeMap);
            cmd.DispatchCompute(cs, k.CalcNeighborhoodSize, ctx.ThreadGroupsX, ctx.ThreadGroupsY, 1);
        }
        else
        {
            // ステージ8（代替）: 密度計算をスキップし、MinSearchLevel で一括初期化
            cmd.SetComputeTextureParam(cs, k.FillNeighborhoodSizeWithMinLevel, PCDShaderConstants.NeighborhoodSizeMap_RW,
                s.recordNeighborhoodMap && !s.enableGradientCorrection ? r.NeighborhoodMap : r.NeighborhoodSizeMap);
            cmd.DispatchCompute(cs, k.FillNeighborhoodSizeWithMinLevel, ctx.ThreadGroupsX, ctx.ThreadGroupsY, 1);
        }
    }
}
