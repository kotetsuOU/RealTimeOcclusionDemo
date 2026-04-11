using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

public partial class PCDRenderPass
{
    private static void ExecuteComputePass(ComputePassData passData, ComputeGraphContext context)
    {
        var cmd = context.cmd;
        var cs = passData.computeShader;

        // 外部バッファと内部バッファの両方が存在する場合、それらを結合します
        if (passData.useExternal && passData.externalCount > 0 && passData.internalCount > 0)
        {
            cmd.SetComputeBufferParam(cs, passData.kernelMerge, ShaderIDs.MergeDstBuffer, passData.combinedBuffer);
            cmd.SetComputeBufferParam(cs, passData.kernelMerge, ShaderIDs.MergeSrcBuffer, passData.externalBuffer);
            cmd.SetComputeIntParam(cs, ShaderIDs.MergeSrcOffset, 0);
            cmd.SetComputeIntParam(cs, ShaderIDs.MergeDstOffset, 0);
            cmd.SetComputeIntParam(cs, ShaderIDs.MergeCopyCount, passData.externalCount);
            int mergeGroupsExt = (passData.externalCount + 255) / 256;
            cmd.DispatchCompute(cs, passData.kernelMerge, mergeGroupsExt, 1, 1);

            cmd.SetComputeBufferParam(cs, passData.kernelMerge, ShaderIDs.MergeSrcBuffer, passData.internalBuffer);
            cmd.SetComputeIntParam(cs, ShaderIDs.MergeSrcOffset, 0);
            cmd.SetComputeIntParam(cs, ShaderIDs.MergeDstOffset, passData.externalCount);
            cmd.SetComputeIntParam(cs, ShaderIDs.MergeCopyCount, passData.internalCount);
            int mergeGroupsInt = (passData.internalCount + 255) / 256;
            cmd.DispatchCompute(cs, passData.kernelMerge, mergeGroupsInt, 1, 1);
        }

        // --- コンピュートシェーダーのグローバルパラメータを設定 ---
        cmd.SetComputeIntParam(cs, ShaderIDs.PointCount, passData.pointCount);
        cmd.SetComputeVectorParam(cs, ShaderIDs.ScreenParams, passData.screenParams);
        cmd.SetComputeMatrixParam(cs, ShaderIDs.ViewMatrix, passData.viewMatrix);
        cmd.SetComputeMatrixParam(cs, ShaderIDs.ProjectionMatrix, passData.projectionMatrix);
        cmd.SetComputeFloatParam(cs, ShaderIDs.DensityThreshold_e, passData.settings.densityThreshold_e);
        cmd.SetComputeFloatParam(cs, ShaderIDs.NeighborhoodParam_p_prime, passData.settings.neighborhoodParam_p_prime);
        cmd.SetComputeFloatParam(cs, ShaderIDs.GradientThreshold_g_th, passData.settings.gradientThreshold_g_th);
        cmd.SetComputeFloatParam(cs, ShaderIDs.OcclusionThreshold, passData.settings.occlusionThreshold);
        cmd.SetComputeFloatParam(cs, ShaderIDs.OcclusionFadeWidth, passData.settings.occlusionFadeWidth);
        cmd.SetComputeIntParam(cs, Shader.PropertyToID("_EnableJointBilateralHoleFilling"), passData.settings.enableJointBilateralHoleFilling ? 1 : 0);

        // 仮想物体など、スクリーン全体を埋めているメッシュにおける仮想的な密度倍率(x = 深度バッファ/実点群数などを加味)を設定
        uint densityMultiplier = System.Math.Max(1u, passData.settings._dynamicMultiplierRuntimeValue);
        cmd.SetComputeIntParam(cs, Shader.PropertyToID("_StaticMeshDensityMultiplier"), (int)densityMultiplier);

        // --- 最適なスレッドグループ数を計算 ---
        int sw = (int)passData.screenParams.x;
        int sh = (int)passData.screenParams.y;
        int threadGroupsX = (sw + 7) / 8;
        int threadGroupsY = (sh + 7) / 8;
        int gridGroupsX = (sw + 15) / 16;
        int gridGroupsY = (sh + 15) / 16;

        // --- ステージ1: 中間RTテクスチャのクリア ---
        cmd.SetComputeTextureParam(cs, passData.kernelClear, ShaderIDs.ColorMap_RW, passData.colorMap);
        cmd.SetComputeTextureParam(cs, passData.kernelClear, ShaderIDs.DepthMap_RW, passData.depthMap);
        cmd.SetComputeTextureParam(cs, passData.kernelClear, ShaderIDs.ViewPositionMap_RW, passData.viewPositionMap);
        cmd.SetComputeTextureParam(cs, passData.kernelClear, ShaderIDs.OcclusionResultMap_RW, passData.occlusionResultMap);
        cmd.SetComputeTextureParam(cs, passData.kernelClear, ShaderIDs.OcclusionValueMap_RW, passData.occlusionValueMap);
        cmd.SetComputeTextureParam(cs, passData.kernelClear, ShaderIDs.FinalImage_RW, passData.finalImage);
        cmd.SetComputeTextureParam(cs, passData.kernelClear, ShaderIDs.OriginTypeMap_RW, passData.originTypeMap);
        cmd.SetComputeTextureParam(cs, passData.kernelClear, ShaderIDs.OriginMap_RW, passData.originDebugMap);
        cmd.DispatchCompute(cs, passData.kernelClear, threadGroupsX, threadGroupsY, 1);

        // カウンターのクリア
        cmd.SetComputeBufferParam(cs, passData.kernelClearCounter, ShaderIDs.StaticMeshCounter_RW, passData.staticMeshCounterBuffer);
        cmd.DispatchCompute(cs, passData.kernelClearCounter, 1, 1, 1);

        // --- ステージ2: 仮想深度マップからの初期化（提供されている場合） ---
        if (passData.hasVirtualDepth && passData.settings.enableVirtualDepthIntegration)
        {
            cmd.SetComputeIntParam(cs, ShaderIDs.UseVirtualDepth, 1);
            cmd.SetComputeTextureParam(cs, passData.kernelInitFromCamera, ShaderIDs.VirtualDepthMap, passData.virtualDepthTexture);

            if (passData.cameraColorTexture.IsValid())
            {
                cmd.SetComputeTextureParam(cs, passData.kernelInitFromCamera, ShaderIDs.CameraColorTexture, passData.cameraColorTexture);
            }

            cmd.SetComputeTextureParam(cs, passData.kernelInitFromCamera, ShaderIDs.DepthMap_RW, passData.depthMap);
            cmd.SetComputeTextureParam(cs, passData.kernelInitFromCamera, ShaderIDs.ColorMap_RW, passData.colorMap);
            cmd.SetComputeTextureParam(cs, passData.kernelInitFromCamera, ShaderIDs.ViewPositionMap_RW, passData.viewPositionMap);
            cmd.SetComputeTextureParam(cs, passData.kernelInitFromCamera, ShaderIDs.OriginTypeMap_RW, passData.originTypeMap);
            cmd.SetComputeBufferParam(cs, passData.kernelInitFromCamera, ShaderIDs.StaticMeshCounter_RW, passData.staticMeshCounterBuffer);
            cmd.SetComputeMatrixParam(cs, ShaderIDs.InverseProjectionMatrix, passData.inverseProjectionMatrix);
            cmd.DispatchCompute(cs, passData.kernelInitFromCamera, threadGroupsX, threadGroupsY, 1);
        }
        else
        {
            cmd.SetComputeIntParam(cs, ShaderIDs.UseVirtualDepth, 0);
        }

        // --- ステージ3: 3D点群データのスクリーンスペースへの投影 ---
        if (!passData.depthMapOnlyMode)
        {
            cmd.SetComputeBufferParam(cs, passData.kernelProject, ShaderIDs.PointBuffer, passData.pointBuffer);
            cmd.SetComputeTextureParam(cs, passData.kernelProject, ShaderIDs.ColorMap_RW, passData.colorMap);
            cmd.SetComputeTextureParam(cs, passData.kernelProject, ShaderIDs.DepthMap_RW, passData.depthMap);
            cmd.SetComputeTextureParam(cs, passData.kernelProject, ShaderIDs.ViewPositionMap_RW, passData.viewPositionMap);
            cmd.SetComputeTextureParam(cs, passData.kernelProject, ShaderIDs.OriginTypeMap_RW, passData.originTypeMap);
            int projectGroups = (passData.pointCount + 255) / 256;
            cmd.DispatchCompute(cs, passData.kernelProject, projectGroups, 1, 1);
        }

        // --- ステージ4: 各グリッドセルの最小深度を計算 ---
        cmd.SetComputeTextureParam(cs, passData.kernelCalcGridZMin, ShaderIDs.DepthMap, passData.depthMap);
        cmd.SetComputeTextureParam(cs, passData.kernelCalcGridZMin, ShaderIDs.GridZMinMap_RW, passData.gridZMinMap);
        cmd.DispatchCompute(cs, passData.kernelCalcGridZMin, gridGroupsX, gridGroupsY, 1);

        // --- ステージ5: グリッド解像度の要件を評価するために画面上のサンプル密度を計算 ---
        cmd.SetComputeTextureParam(cs, passData.kernelCalcDensity, ShaderIDs.DepthMap, passData.depthMap);
        cmd.SetComputeTextureParam(cs, passData.kernelCalcDensity, ShaderIDs.GridZMinMap, passData.gridZMinMap);
        cmd.SetComputeTextureParam(cs, passData.kernelCalcDensity, ShaderIDs.OriginTypeMap, passData.originTypeMap);
        cmd.SetComputeTextureParam(cs, passData.kernelCalcDensity, ShaderIDs.DensityMap_RW, passData.densityMap);
        cmd.DispatchCompute(cs, passData.kernelCalcDensity, gridGroupsX, gridGroupsY, 1);

        // --- ステージ6: ポイントの密度に応じて必要な詳細レベル（グリッドレベル）を決定 ---
        cmd.SetComputeTextureParam(cs, passData.kernelCalcGridLevel, ShaderIDs.DensityMap, passData.densityMap);
        cmd.SetComputeTextureParam(cs, passData.kernelCalcGridLevel, ShaderIDs.GridLevelMap_RW, passData.gridLevelMap);
        int gridThreadX = (gridGroupsX + 15) / 16;
        int gridThreadY = (gridGroupsY + 15) / 16;
        cmd.DispatchCompute(cs, passData.kernelCalcGridLevel, Mathf.Max(1, gridThreadX), Mathf.Max(1, gridThreadY), 1);

        // --- ステージ7: 穴やアーティファクトを防ぐために、メディアンフィルターを用いてグリッドレベルを平滑化 ---
        cmd.SetComputeTextureParam(cs, passData.kernelGridMedianFilter, ShaderIDs.GridLevelMap, passData.gridLevelMap);
        cmd.SetComputeTextureParam(cs, passData.kernelGridMedianFilter, ShaderIDs.FilteredGridLevelMap_RW, passData.filteredGridLevelMap);
        cmd.DispatchCompute(cs, passData.kernelGridMedianFilter, Mathf.Max(1, gridThreadX), Mathf.Max(1, gridThreadY), 1);

        // --- ステージ8: フィルター処理されたLODに基づいて基本的な近傍の半径サイズを算出 ---
        cmd.SetComputeTextureParam(cs, passData.kernelCalcNeighborhoodSize, ShaderIDs.FilteredGridLevelMap, passData.filteredGridLevelMap);
        cmd.SetComputeTextureParam(cs, passData.kernelCalcNeighborhoodSize, ShaderIDs.NeighborhoodSizeMap_RW, passData.neighborhoodSizeMap);
        cmd.DispatchCompute(cs, passData.kernelCalcNeighborhoodSize, threadGroupsX, threadGroupsY, 1);

        // --- ステージ9: （オプション）急な深度勾配がある部分の近傍サイズを補正 ---
        if (passData.settings.enableGradientCorrection)
        {
            // 階層的な深度レベル（L1 〜 L4）を構築
            int l1_w = Mathf.Max(1, (sw + 1) / 2);
            int l1_h = Mathf.Max(1, (sh + 1) / 2);
            cmd.SetComputeTextureParam(cs, passData.kernelBuildDepthPyramidL1, ShaderIDs.DepthMap, passData.depthMap);
            cmd.SetComputeTextureParam(cs, passData.kernelBuildDepthPyramidL1, ShaderIDs.DepthPyramidL1_RW, passData.depthPyramidL1);
            cmd.DispatchCompute(cs, passData.kernelBuildDepthPyramidL1, (l1_w + 7) / 8, (l1_h + 7) / 8, 1);

            int l2_w = Mathf.Max(1, (l1_w + 1) / 2);
            int l2_h = Mathf.Max(1, (l1_h + 1) / 2);
            cmd.SetComputeTextureParam(cs, passData.kernelBuildDepthPyramidL2, ShaderIDs.DepthPyramidL1, passData.depthPyramidL1);
            cmd.SetComputeTextureParam(cs, passData.kernelBuildDepthPyramidL2, ShaderIDs.DepthPyramidL2_RW, passData.depthPyramidL2);
            cmd.DispatchCompute(cs, passData.kernelBuildDepthPyramidL2, (l2_w + 7) / 8, (l2_h + 7) / 8, 1);

            int l3_w = Mathf.Max(1, (l2_w + 1) / 2);
            int l3_h = Mathf.Max(1, (l2_h + 1) / 2);
            cmd.SetComputeTextureParam(cs, passData.kernelBuildDepthPyramidL3, ShaderIDs.DepthPyramidL2, passData.depthPyramidL2);
            cmd.SetComputeTextureParam(cs, passData.kernelBuildDepthPyramidL3, ShaderIDs.DepthPyramidL3_RW, passData.depthPyramidL3);
            cmd.DispatchCompute(cs, passData.kernelBuildDepthPyramidL3, (l3_w + 7) / 8, (l3_h + 7) / 8, 1);

            int l4_w = Mathf.Max(1, (l3_w + 1) / 2);
            int l4_h = Mathf.Max(1, (l3_h + 1) / 2);
            cmd.SetComputeTextureParam(cs, passData.kernelBuildDepthPyramidL4, ShaderIDs.DepthPyramidL3, passData.depthPyramidL3);
            cmd.SetComputeTextureParam(cs, passData.kernelBuildDepthPyramidL4, ShaderIDs.DepthPyramidL4_RW, passData.depthPyramidL4);
            cmd.DispatchCompute(cs, passData.kernelBuildDepthPyramidL4, (l4_w + 7) / 8, (l4_h + 7) / 8, 1);

            cmd.SetComputeTextureParam(cs, passData.kernelApplyGradient, ShaderIDs.DepthPyramidL1, passData.depthPyramidL1);
            cmd.SetComputeTextureParam(cs, passData.kernelApplyGradient, ShaderIDs.DepthPyramidL2, passData.depthPyramidL2);
            cmd.SetComputeTextureParam(cs, passData.kernelApplyGradient, ShaderIDs.DepthPyramidL3, passData.depthPyramidL3);
            cmd.SetComputeTextureParam(cs, passData.kernelApplyGradient, ShaderIDs.DepthPyramidL4, passData.depthPyramidL4);
            cmd.SetComputeTextureParam(cs, passData.kernelApplyGradient, ShaderIDs.NeighborhoodSizeMap, passData.neighborhoodSizeMap);
            cmd.SetComputeTextureParam(cs, passData.kernelApplyGradient, ShaderIDs.CorrectedNeighborhoodSizeMap_RW, passData.correctedNeighborhoodSizeMap);
            cmd.DispatchCompute(cs, passData.kernelApplyGradient, threadGroupsX, threadGroupsY, 1);
        }

        // リバースZバッファへの対応フラグをセット（DX11等で正しいオクルージョン判定を行うため）
        cmd.SetComputeIntParam(cs, Shader.PropertyToID("_IsReversedZ"), SystemInfo.usesReversedZBuffer ? 1 : 0);

        // --- ステージ10: 近傍オクルージョンテストを実行し、奥にあるポイントを破棄し、手前にあるポイントをフィルタリング ---
        cmd.SetComputeTextureParam(cs, passData.kernelComputeOcclusion, ShaderIDs.ColorMap, passData.colorMap);
        cmd.SetComputeTextureParam(cs, passData.kernelComputeOcclusion, ShaderIDs.DepthMap, passData.depthMap);
        cmd.SetComputeTextureParam(cs, passData.kernelComputeOcclusion, ShaderIDs.ViewPositionMap, passData.viewPositionMap);
        cmd.SetComputeTextureParam(cs, passData.kernelComputeOcclusion, ShaderIDs.VirtualDepthMap, passData.virtualDepthTexture);
        cmd.SetComputeTextureParam(cs, passData.kernelComputeOcclusion, ShaderIDs.OriginTypeMap, passData.originTypeMap);
        cmd.SetComputeTextureParam(cs, passData.kernelComputeOcclusion, ShaderIDs.OriginTypeMap_RW, passData.originTypeMap);
        cmd.SetComputeTextureParam(cs, passData.kernelComputeOcclusion, ShaderIDs.FinalNeighborhoodSizeMap, passData.settings.enableGradientCorrection ? passData.correctedNeighborhoodSizeMap : passData.neighborhoodSizeMap);
        cmd.SetComputeTextureParam(cs, passData.kernelComputeOcclusion, ShaderIDs.OcclusionResultMap_RW, passData.occlusionResultMap);

        cmd.SetComputeIntParam(cs, ShaderIDs.RecordOcclusionDebug, passData.settings.recordOcclusionDebugMap ? 1 : 0);
        cmd.SetComputeTextureParam(cs, passData.kernelComputeOcclusion, ShaderIDs.OcclusionValueMap_RW, passData.occlusionValueMap);

        cmd.SetComputeTextureParam(cs, passData.kernelComputeOcclusion, ShaderIDs.OriginMap_RW, passData.originDebugMap);
        cmd.DispatchCompute(cs, passData.kernelComputeOcclusion, threadGroupsX, threadGroupsY, 1);

        // --- ステージ11: 点群が描画されなかったピクセルに対する穴埋め（バイラテラル） ---
        if (passData.settings.enableJointBilateralHoleFilling)
        {
            cmd.SetComputeTextureParam(cs, passData.kernelFillHoles, ShaderIDs.ColorMap, passData.colorMap);
            cmd.SetComputeTextureParam(cs, passData.kernelFillHoles, ShaderIDs.DepthMap, passData.depthMap);
            cmd.SetComputeTextureParam(cs, passData.kernelFillHoles, ShaderIDs.VirtualDepthMap, passData.virtualDepthTexture);
            cmd.SetComputeTextureParam(cs, passData.kernelFillHoles, ShaderIDs.OriginTypeMap, passData.originTypeMap);
            cmd.SetComputeTextureParam(cs, passData.kernelFillHoles, ShaderIDs.OriginTypeMap_RW, passData.originTypeMap);
            cmd.SetComputeTextureParam(cs, passData.kernelFillHoles, ShaderIDs.OcclusionResultMap_RW, passData.occlusionResultMap);
            cmd.SetComputeTextureParam(cs, passData.kernelFillHoles, ShaderIDs.OriginMap_RW, passData.originDebugMap);
            cmd.DispatchCompute(cs, passData.kernelFillHoles, threadGroupsX, threadGroupsY, 1);
        }

        // --- ステージ12: オクルージョンによってできた穴を補完し、仮想深度マップやカメラバッファとマージ ---
        cmd.SetComputeTextureParam(cs, passData.kernelInterpolate, ShaderIDs.OcclusionResultMap, passData.occlusionResultMap);
        cmd.SetComputeTextureParam(cs, passData.kernelInterpolate, ShaderIDs.VirtualDepthMap, passData.virtualDepthTexture);
        cmd.SetComputeTextureParam(cs, passData.kernelInterpolate, ShaderIDs.CameraColorTexture, passData.cameraColorTexture);
        cmd.SetComputeTextureParam(cs, passData.kernelInterpolate, ShaderIDs.OriginTypeMap, passData.originTypeMap);
        cmd.SetComputeTextureParam(cs, passData.kernelInterpolate, ShaderIDs.FinalImage_RW, passData.finalImage);
        cmd.SetComputeTextureParam(cs, passData.kernelInterpolate, ShaderIDs.OriginMap_RW, passData.originDebugMap);
        cmd.DispatchCompute(cs, passData.kernelInterpolate, threadGroupsX, threadGroupsY, 1);

        int totalPoints = passData.pointCount;
        UnityEngine.Rendering.AsyncGPUReadback.Request(passData.staticMeshCounterBuffer, (request) =>
        {
            if (request.hasError || PCDRendererFeature.Instance == null)
            {
                return;
            }
            var data = request.GetData<uint>();
            uint virtualMeshPixelCount = data[0];

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

    private static void ExecuteBlitPass(BlitPassData passData, RasterGraphContext context)
    {
        // アルファブレンドや特定のレンダーキューに対応するため、
        // カスタムマテリアルを用いて生成された画像をメインのフレームバッファに合成します
        if (passData.blendMaterial != null && !passData.enableOriginDebugMap)
        {
            Blitter.BlitTexture(context.cmd, passData.sourceImage, new Vector4(1, 1, 0, 0), passData.blendMaterial, 0);
            return;
        }

        // デバッグ出力または通常の出力用の標準的なBlitのフォールバック
        Blitter.BlitTexture(context.cmd, passData.sourceImage, new Vector4(1, 1, 0, 0), 0.0f, false);
    }
}

