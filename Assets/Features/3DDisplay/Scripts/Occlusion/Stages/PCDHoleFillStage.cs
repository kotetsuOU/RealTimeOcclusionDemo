// =============================================================================
// PCDHoleFillStage.cs
// -----------------------------------------------------------------------------
// ホールフィリング（穴埋め）ステージ。
// JointBilateral / PullPush / Morphology の3手法を内包する。
// =============================================================================
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// オクルージョン結果の穴を埋めるホールフィリングステージ。
/// </summary>
internal class PCDHoleFillStage : IPCDPipelineStage
{
    public bool ShouldExecute(PCDPipelineContext ctx) =>
        ctx.Settings.holeFillingMethod != PCDRendererFeature.PCD_HoleFillingMethod.None;

    public void Execute(CommandBuffer cmd, PCDPipelineContext ctx)
    {
        switch (ctx.Settings.holeFillingMethod)
        {
            case PCDRendererFeature.PCD_HoleFillingMethod.JointBilateral:
                ExecuteJointBilateral(cmd, ctx);
                break;
            case PCDRendererFeature.PCD_HoleFillingMethod.PullPush:
                ExecutePullPush(cmd, ctx);
                break;
            case PCDRendererFeature.PCD_HoleFillingMethod.Morphology_OC:
            case PCDRendererFeature.PCD_HoleFillingMethod.Morphology_CO:
                ExecuteMorphology(cmd, ctx);
                break;
        }
    }

    // =========================================================================
    // JointBilateral
    // =========================================================================

    /// <summary> JointBilateral ホールフィリング: エッジ保持型の両側フィルタで穴を補間する </summary>
    private void ExecuteJointBilateral(CommandBuffer cmd, PCDPipelineContext ctx)
    {
        var cs = ctx.ComputeShader;
        var k = ctx.Kernels;
        var r = ctx.Resources;
        var depthMap = ctx.Settings.recordIntegratedDepthMap ? r.IntegratedDepthMap : r.DepthMap;

        cmd.SetComputeTextureParam(cs, k.FillHoles, PCDShaderConstants.ColorMap, r.ColorMap);
        cmd.SetComputeTextureParam(cs, k.FillHoles, PCDShaderConstants.DepthMap, depthMap);
        cmd.SetComputeTextureParam(cs, k.FillHoles, PCDShaderConstants.VirtualDepthMap, ctx.VirtualDepthTexture);
        cmd.SetComputeTextureParam(cs, k.FillHoles, PCDShaderConstants.OriginTypeMap_RW, r.OriginTypeMap);
        cmd.SetComputeTextureParam(cs, k.FillHoles, PCDShaderConstants.OcclusionResultMap_RW, r.OcclusionResultMap);
        cmd.SetComputeTextureParam(cs, k.FillHoles, PCDShaderConstants.OriginMap_RW, r.DebugDisplayMap);
        cmd.SetComputeTextureParam(cs, k.FillHoles, PCDShaderConstants.FinalNeighborhoodSizeMap, ctx.ActiveNeighborhoodSizeMap);
        cmd.DispatchCompute(cs, k.FillHoles, ctx.ThreadGroupsX, ctx.ThreadGroupsY, 1);
    }

    // =========================================================================
    // PullPush
    // =========================================================================

    /// <summary> Pull-Push ホールフィリング: ピラミッド縮小(Pull)→拡大(Push)で階層的に穴を補間する </summary>
    private void ExecutePullPush(CommandBuffer cmd, PCDPipelineContext ctx)
    {
        var cs = ctx.ComputeShader;
        var k = ctx.Kernels;
        var r = ctx.Resources;
        int sw = ctx.ScreenWidth;
        int sh = ctx.ScreenHeight;
        int maxLevel = 5;

        // Phase 0: ベースレベルの初期化
        cmd.SetComputeTextureParam(cs, k.FillHolesPullPushInit, PCDShaderConstants.OcclusionResultMap, r.OcclusionResultMap);
        cmd.SetComputeTextureParam(cs, k.FillHolesPullPushInit, PCDShaderConstants.OriginTypeMap, r.OriginTypeMap);
        cmd.SetComputeTextureParam(cs, k.FillHolesPullPushInit, PCDShaderConstants.PullPushLevel_Out_RW, r.PullPushPyramid[0]);
        cmd.DispatchCompute(cs, k.FillHolesPullPushInit, ctx.ThreadGroupsX, ctx.ThreadGroupsY, 1);

        // Phase 1: Pull（ダウンサンプル）
        int pw = sw, ph = sh;
        for (int i = 0; i < maxLevel - 1; i++)
        {
            pw = Mathf.Max(1, (pw + 1) / 2);
            ph = Mathf.Max(1, (ph + 1) / 2);
            int pullGroupsX = (pw + 7) / 8;
            int pullGroupsY = (ph + 7) / 8;

            cmd.SetComputeTextureParam(cs, k.FillHolesPull, PCDShaderConstants.PullPushLevel_In, r.PullPushPyramid[i]);
            cmd.SetComputeTextureParam(cs, k.FillHolesPull, PCDShaderConstants.PullPushLevel_Out_RW, r.PullPushPyramid[i + 1]);
            cmd.DispatchCompute(cs, k.FillHolesPull, pullGroupsX, pullGroupsY, 1);
        }

        // Phase 2: Push（アップサンプル）
        for (int i = maxLevel - 2; i >= 0; i--)
        {
            int curr_w = sw; int curr_h = sh;
            for (int j = 0; j < i; j++) { curr_w = Mathf.Max(1, (curr_w + 1) / 2); curr_h = Mathf.Max(1, (curr_h + 1) / 2); }
            int pushGroupsX = (curr_w + 7) / 8;
            int pushGroupsY = (curr_h + 7) / 8;

            cmd.SetComputeTextureParam(cs, k.FillHolesPush, PCDShaderConstants.PullPushLevel_In, r.PullPushPyramid[i + 1]);
            cmd.SetComputeTextureParam(cs, k.FillHolesPush, PCDShaderConstants.PullPushLevel_Out_RW, r.PullPushPyramid[i]);
            cmd.DispatchCompute(cs, k.FillHolesPush, pushGroupsX, pushGroupsY, 1);
        }

        // Phase 3: 結果を OcclusionResultMap に書き戻し
        cmd.SetComputeTextureParam(cs, k.FillHolesPullPushFinalize, PCDShaderConstants.PullPushLevel_In, r.PullPushPyramid[0]);
        cmd.SetComputeTextureParam(cs, k.FillHolesPullPushFinalize, PCDShaderConstants.OcclusionResultMap_RW, r.OcclusionResultMap);
        cmd.SetComputeTextureParam(cs, k.FillHolesPullPushFinalize, PCDShaderConstants.OriginTypeMap_RW, r.OriginTypeMap);
        cmd.SetComputeTextureParam(cs, k.FillHolesPullPushFinalize, PCDShaderConstants.OriginMap_RW, r.DebugDisplayMap);
        cmd.DispatchCompute(cs, k.FillHolesPullPushFinalize, ctx.ThreadGroupsX, ctx.ThreadGroupsY, 1);
    }

    // =========================================================================
    // Morphology
    // =========================================================================

    /// <summary>
    /// モルフォロジーホールフィリング: ピラミッドベースの膨張/収縮演算で穴を埋める。
    /// </summary>
    private void ExecuteMorphology(CommandBuffer cmd, PCDPipelineContext ctx)
    {
        var cs = ctx.ComputeShader;
        var k = ctx.Kernels;
        var r = ctx.Resources;
        var s = ctx.Settings;

        int halfSize = s.morphKernelHalfSize;
        cmd.SetComputeIntParam(cs, PCDShaderConstants.MorphKernelHalfSize, halfSize);

        bool currentInTemp = false;

        // --- 指定された手法に応じてモルフォロジー演算を適用 ---
        if (s.holeFillingMethod == PCDRendererFeature.PCD_HoleFillingMethod.Morphology_OC)
        {
            // Opening (Erode → Dilate) then Closing (Dilate → Erode)
            for (int i = 0; i < s.morphErodeIterations; i++) RunMorphPass(cmd, ctx, k.MorphologyErode, ref currentInTemp);
            for (int i = 0; i < s.morphDilateIterations; i++) RunMorphPass(cmd, ctx, k.MorphologyDilate, ref currentInTemp);
            for (int i = 0; i < s.morphDilateIterations; i++) RunMorphPass(cmd, ctx, k.MorphologyDilate, ref currentInTemp);
            for (int i = 0; i < s.morphErodeIterations; i++) RunMorphPass(cmd, ctx, k.MorphologyErode, ref currentInTemp);
        }
        else if (s.holeFillingMethod == PCDRendererFeature.PCD_HoleFillingMethod.Morphology_CO)
        {
            // Closing (Dilate → Erode) then Opening (Erode → Dilate)
            for (int i = 0; i < s.morphDilateIterations; i++) RunMorphPass(cmd, ctx, k.MorphologyDilate, ref currentInTemp);
            for (int i = 0; i < s.morphErodeIterations; i++) RunMorphPass(cmd, ctx, k.MorphologyErode, ref currentInTemp);
            for (int i = 0; i < s.morphErodeIterations; i++) RunMorphPass(cmd, ctx, k.MorphologyErode, ref currentInTemp);
            for (int i = 0; i < s.morphDilateIterations; i++) RunMorphPass(cmd, ctx, k.MorphologyDilate, ref currentInTemp);
        }
        else
        {
            // フォールバック
            for (int i = 0; i < s.morphDilateIterations; i++) RunMorphPass(cmd, ctx, k.MorphologyDilate, ref currentInTemp);
            for (int i = 0; i < s.morphErodeIterations; i++) RunMorphPass(cmd, ctx, k.MorphologyErode, ref currentInTemp);
        }

        // 最終結果が一時バッファに残っている場合、メインバッファにコピーバック
        if (currentInTemp)
        {
            cmd.SetComputeTextureParam(cs, k.MorphologyCopy, PCDShaderConstants.MorphColorIn, r.MorphColorTemp);
            cmd.SetComputeTextureParam(cs, k.MorphologyCopy, PCDShaderConstants.MorphTypeIn, r.MorphTypeTemp);
            cmd.SetComputeTextureParam(cs, k.MorphologyCopy, PCDShaderConstants.MorphColorOut_RW, r.OcclusionResultMap);
            cmd.SetComputeTextureParam(cs, k.MorphologyCopy, PCDShaderConstants.MorphTypeOut_RW, r.OriginTypeMap);
            cmd.DispatchCompute(cs, k.MorphologyCopy, ctx.ThreadGroupsX, ctx.ThreadGroupsY, 1);
        }
    }

    /// <summary>
    /// モルフォロジーパス1回分を実行する（ラムダ捕捉による毎フレーム GC Alloc を回避）。
    /// </summary>
    private void RunMorphPass(CommandBuffer cmd, PCDPipelineContext ctx, int kernelId, ref bool currentInTemp)
    {
        var cs = ctx.ComputeShader;
        var r = ctx.Resources;

        RTHandle colorIn = currentInTemp ? r.MorphColorTemp : r.OcclusionResultMap;
        RTHandle typeIn = currentInTemp ? r.MorphTypeTemp : r.OriginTypeMap;
        RTHandle colorOut = currentInTemp ? r.OcclusionResultMap : r.MorphColorTemp;
        RTHandle typeOut = currentInTemp ? r.OriginTypeMap : r.MorphTypeTemp;

        // モルフォロジーピラミッドの構築
        BuildMorphPyramid(cmd, ctx, colorIn, typeIn);

        // 実際の膨張/収縮カーネルを実行
        cmd.SetComputeTextureParam(cs, kernelId, PCDShaderConstants.MorphColorIn, colorIn);
        cmd.SetComputeTextureParam(cs, kernelId, PCDShaderConstants.MorphTypeIn, typeIn);
        cmd.SetComputeTextureParam(cs, kernelId, PCDShaderConstants.MorphColorOut_RW, colorOut);
        cmd.SetComputeTextureParam(cs, kernelId, PCDShaderConstants.MorphTypeOut_RW, typeOut);
        cmd.SetComputeTextureParam(cs, kernelId, PCDShaderConstants.FinalNeighborhoodSizeMap, ctx.ActiveNeighborhoodSizeMap);

        // ピラミッドテクスチャをバインド（ループ化）
        for (int i = 0; i < PCDShaderConstants.PYRAMID_LEVELS; i++)
        {
            cmd.SetComputeTextureParam(cs, kernelId, PCDShaderConstants.MorphTypePyramidRead[i], r.MorphTypePyramid[i]);
            cmd.SetComputeTextureParam(cs, kernelId, PCDShaderConstants.MorphColorPyramidRead[i], r.MorphColorPyramid[i]);
        }

        cmd.DispatchCompute(cs, kernelId, ctx.ThreadGroupsX, ctx.ThreadGroupsY, 1);
        currentInTemp = !currentInTemp;
    }

    /// <summary>
    /// モルフォロジーピラミッド(L1〜L6)を構築する。
    /// </summary>
    private void BuildMorphPyramid(CommandBuffer cmd, PCDPipelineContext ctx, RTHandle colorIn, RTHandle typeIn)
    {
        var cs = ctx.ComputeShader;
        var k = ctx.Kernels;
        var r = ctx.Resources;
        int sw = ctx.ScreenWidth;
        int sh = ctx.ScreenHeight;

        // L1: 入力マップからのダウンサンプル
        int l1_w = Mathf.Max(1, (sw + 1) / 2);
        int l1_h = Mathf.Max(1, (sh + 1) / 2);
        cmd.SetComputeTextureParam(cs, k.BuildMorphPyramid[0], PCDShaderConstants.MorphTypeIn, typeIn);
        cmd.SetComputeTextureParam(cs, k.BuildMorphPyramid[0], PCDShaderConstants.MorphColorIn, colorIn);
        cmd.SetComputeTextureParam(cs, k.BuildMorphPyramid[0], PCDShaderConstants.MorphTypePyramidWrite[0], r.MorphTypePyramid[0]);
        cmd.SetComputeTextureParam(cs, k.BuildMorphPyramid[0], PCDShaderConstants.MorphColorPyramidWrite[0], r.MorphColorPyramid[0]);
        cmd.DispatchCompute(cs, k.BuildMorphPyramid[0], (l1_w + 7) / 8, (l1_h + 7) / 8, 1);

        // L2〜L6: 前レベルからのダウンサンプル
        int prevW = l1_w, prevH = l1_h;
        for (int i = 1; i < PCDShaderConstants.PYRAMID_LEVELS; i++)
        {
            int curW = Mathf.Max(1, (prevW + 1) / 2);
            int curH = Mathf.Max(1, (prevH + 1) / 2);
            cmd.SetComputeTextureParam(cs, k.BuildMorphPyramid[i], PCDShaderConstants.MorphTypePyramidRead[i - 1], r.MorphTypePyramid[i - 1]);
            cmd.SetComputeTextureParam(cs, k.BuildMorphPyramid[i], PCDShaderConstants.MorphColorPyramidRead[i - 1], r.MorphColorPyramid[i - 1]);
            cmd.SetComputeTextureParam(cs, k.BuildMorphPyramid[i], PCDShaderConstants.MorphTypePyramidWrite[i], r.MorphTypePyramid[i]);
            cmd.SetComputeTextureParam(cs, k.BuildMorphPyramid[i], PCDShaderConstants.MorphColorPyramidWrite[i], r.MorphColorPyramid[i]);
            cmd.DispatchCompute(cs, k.BuildMorphPyramid[i], (curW + 7) / 8, (curH + 7) / 8, 1);
            prevW = curW;
            prevH = curH;
        }
    }
}
