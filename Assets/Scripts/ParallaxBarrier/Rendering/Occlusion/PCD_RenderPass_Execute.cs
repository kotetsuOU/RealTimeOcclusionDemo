using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

public partial class PCDRenderPass
{
    private static void ExecuteComputePass(ComputePassData passData, ComputeGraphContext context)
    {
        var cmd = context.cmd;
        var cs = passData.computeShader;

        if (passData.useExternal && passData.externalCount > 0 && passData.internalCount > 0)
        {
            cmd.SetComputeBufferParam(cs, passData.kernelMerge, ShaderIDs.MergeDstBuffer, passData.combinedBuffer);
            cmd.SetComputeBufferParam(cs, passData.kernelMerge, ShaderIDs.MergeSrcBuffer, passData.externalBuffer);
            cmd.SetComputeIntParam(cs, ShaderIDs.MergeSrcOffset, 0);
            cmd.SetComputeIntParam(cs, ShaderIDs.MergeDstOffset, 0);
            cmd.SetComputeIntParam(cs, ShaderIDs.MergeCopyCount, passData.externalCount);
            int mergeGroupsExt = Mathf.CeilToInt(passData.externalCount / 256.0f);
            cmd.DispatchCompute(cs, passData.kernelMerge, mergeGroupsExt, 1, 1);

            cmd.SetComputeBufferParam(cs, passData.kernelMerge, ShaderIDs.MergeSrcBuffer, passData.internalBuffer);
            cmd.SetComputeIntParam(cs, ShaderIDs.MergeSrcOffset, 0);
            cmd.SetComputeIntParam(cs, ShaderIDs.MergeDstOffset, passData.externalCount);
            cmd.SetComputeIntParam(cs, ShaderIDs.MergeCopyCount, passData.internalCount);
            int mergeGroupsInt = Mathf.CeilToInt(passData.internalCount / 256.0f);
            cmd.DispatchCompute(cs, passData.kernelMerge, mergeGroupsInt, 1, 1);
        }

        cmd.SetComputeIntParam(cs, ShaderIDs.PointCount, passData.pointCount);
        cmd.SetComputeVectorParam(cs, ShaderIDs.ScreenParams, passData.screenParams);
        cmd.SetComputeMatrixParam(cs, ShaderIDs.ViewMatrix, passData.viewMatrix);
        cmd.SetComputeMatrixParam(cs, ShaderIDs.ProjectionMatrix, passData.projectionMatrix);
        cmd.SetComputeFloatParam(cs, ShaderIDs.DensityThreshold_e, passData.settings.densityThreshold_e);
        cmd.SetComputeFloatParam(cs, ShaderIDs.NeighborhoodParam_p_prime, passData.settings.neighborhoodParam_p_prime);
        cmd.SetComputeFloatParam(cs, ShaderIDs.GradientThreshold_g_th, passData.settings.gradientThreshold_g_th);
        cmd.SetComputeFloatParam(cs, ShaderIDs.OcclusionThreshold, passData.settings.occlusionThreshold);
        cmd.SetComputeFloatParam(cs, ShaderIDs.OcclusionFadeWidth, passData.settings.occlusionFadeWidth);

        int sw = (int)passData.screenParams.x;
        int sh = (int)passData.screenParams.y;
        int threadGroupsX = Mathf.CeilToInt(sw / 8.0f);
        int threadGroupsY = Mathf.CeilToInt(sh / 8.0f);
        int gridGroupsX = Mathf.CeilToInt(sw / 16.0f);
        int gridGroupsY = Mathf.CeilToInt(sh / 16.0f);

        cmd.SetComputeTextureParam(cs, passData.kernelClear, ShaderIDs.ColorMap_RW, passData.colorMap);
        cmd.SetComputeTextureParam(cs, passData.kernelClear, ShaderIDs.DepthMap_RW, passData.depthMap);
        cmd.SetComputeTextureParam(cs, passData.kernelClear, ShaderIDs.ViewPositionMap_RW, passData.viewPositionMap);
        cmd.SetComputeTextureParam(cs, passData.kernelClear, ShaderIDs.OcclusionResultMap_RW, passData.occlusionResultMap);
        cmd.SetComputeTextureParam(cs, passData.kernelClear, ShaderIDs.OcclusionValueMap_RW, passData.occlusionValueMap);
        cmd.SetComputeTextureParam(cs, passData.kernelClear, ShaderIDs.FinalImage_RW, passData.finalImage);
        cmd.SetComputeTextureParam(cs, passData.kernelClear, ShaderIDs.OriginTypeMap_RW, passData.originTypeMap);
        cmd.SetComputeTextureParam(cs, passData.kernelClear, ShaderIDs.OriginMap_RW, passData.originDebugMap);
        cmd.DispatchCompute(cs, passData.kernelClear, threadGroupsX, threadGroupsY, 1);

        if (passData.hasVirtualDepth)
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
            cmd.SetComputeMatrixParam(cs, ShaderIDs.InverseProjectionMatrix, passData.inverseProjectionMatrix);
            cmd.DispatchCompute(cs, passData.kernelInitFromCamera, threadGroupsX, threadGroupsY, 1);
        }
        else
        {
            cmd.SetComputeIntParam(cs, ShaderIDs.UseVirtualDepth, 0);
        }

        if (!passData.depthMapOnlyMode)
        {
            cmd.SetComputeBufferParam(cs, passData.kernelProject, ShaderIDs.PointBuffer, passData.pointBuffer);
            cmd.SetComputeTextureParam(cs, passData.kernelProject, ShaderIDs.ColorMap_RW, passData.colorMap);
            cmd.SetComputeTextureParam(cs, passData.kernelProject, ShaderIDs.DepthMap_RW, passData.depthMap);
            cmd.SetComputeTextureParam(cs, passData.kernelProject, ShaderIDs.ViewPositionMap_RW, passData.viewPositionMap);
            cmd.SetComputeTextureParam(cs, passData.kernelProject, ShaderIDs.OriginTypeMap_RW, passData.originTypeMap);
            int projectGroups = Mathf.CeilToInt(passData.pointCount / 256.0f);
            cmd.DispatchCompute(cs, passData.kernelProject, projectGroups, 1, 1);
        }

        cmd.SetComputeTextureParam(cs, passData.kernelCalcGridZMin, ShaderIDs.DepthMap, passData.depthMap);
        cmd.SetComputeTextureParam(cs, passData.kernelCalcGridZMin, ShaderIDs.GridZMinMap_RW, passData.gridZMinMap);
        cmd.DispatchCompute(cs, passData.kernelCalcGridZMin, gridGroupsX, gridGroupsY, 1);

        cmd.SetComputeTextureParam(cs, passData.kernelCalcDensity, ShaderIDs.DepthMap, passData.depthMap);
        cmd.SetComputeTextureParam(cs, passData.kernelCalcDensity, ShaderIDs.GridZMinMap, passData.gridZMinMap);
        cmd.SetComputeTextureParam(cs, passData.kernelCalcDensity, ShaderIDs.OriginTypeMap, passData.originTypeMap);
        cmd.SetComputeTextureParam(cs, passData.kernelCalcDensity, ShaderIDs.DensityMap_RW, passData.densityMap);
        cmd.DispatchCompute(cs, passData.kernelCalcDensity, gridGroupsX, gridGroupsY, 1);

        cmd.SetComputeTextureParam(cs, passData.kernelCalcGridLevel, ShaderIDs.DensityMap, passData.densityMap);
        cmd.SetComputeTextureParam(cs, passData.kernelCalcGridLevel, ShaderIDs.GridLevelMap_RW, passData.gridLevelMap);
        int gridThreadX = Mathf.CeilToInt(gridGroupsX / 16.0f);
        int gridThreadY = Mathf.CeilToInt(gridGroupsY / 16.0f);
        cmd.DispatchCompute(cs, passData.kernelCalcGridLevel, Mathf.Max(1, gridThreadX), Mathf.Max(1, gridThreadY), 1);

        cmd.SetComputeTextureParam(cs, passData.kernelGridMedianFilter, ShaderIDs.GridLevelMap, passData.gridLevelMap);
        cmd.SetComputeTextureParam(cs, passData.kernelGridMedianFilter, ShaderIDs.FilteredGridLevelMap_RW, passData.filteredGridLevelMap);
        cmd.DispatchCompute(cs, passData.kernelGridMedianFilter, Mathf.Max(1, gridThreadX), Mathf.Max(1, gridThreadY), 1);

        cmd.SetComputeTextureParam(cs, passData.kernelCalcNeighborhoodSize, ShaderIDs.FilteredGridLevelMap, passData.filteredGridLevelMap);
        cmd.SetComputeTextureParam(cs, passData.kernelCalcNeighborhoodSize, ShaderIDs.NeighborhoodSizeMap_RW, passData.neighborhoodSizeMap);
        cmd.DispatchCompute(cs, passData.kernelCalcNeighborhoodSize, threadGroupsX, threadGroupsY, 1);

        if (passData.settings.enableGradientCorrection)
        {
            int l1_w = Mathf.Max(1, Mathf.CeilToInt(sw / 2.0f));
            int l1_h = Mathf.Max(1, Mathf.CeilToInt(sh / 2.0f));
            cmd.SetComputeTextureParam(cs, passData.kernelBuildDepthPyramidL1, ShaderIDs.DepthMap, passData.depthMap);
            cmd.SetComputeTextureParam(cs, passData.kernelBuildDepthPyramidL1, ShaderIDs.DepthPyramidL1_RW, passData.depthPyramidL1);
            cmd.DispatchCompute(cs, passData.kernelBuildDepthPyramidL1, Mathf.CeilToInt(l1_w / 8.0f), Mathf.CeilToInt(l1_h / 8.0f), 1);

            int l2_w = Mathf.Max(1, Mathf.CeilToInt(l1_w / 2.0f));
            int l2_h = Mathf.Max(1, Mathf.CeilToInt(l1_h / 2.0f));
            cmd.SetComputeTextureParam(cs, passData.kernelBuildDepthPyramidL2, ShaderIDs.DepthPyramidL1, passData.depthPyramidL1);
            cmd.SetComputeTextureParam(cs, passData.kernelBuildDepthPyramidL2, ShaderIDs.DepthPyramidL2_RW, passData.depthPyramidL2);
            cmd.DispatchCompute(cs, passData.kernelBuildDepthPyramidL2, Mathf.CeilToInt(l2_w / 8.0f), Mathf.CeilToInt(l2_h / 8.0f), 1);

            int l3_w = Mathf.Max(1, Mathf.CeilToInt(l2_w / 2.0f));
            int l3_h = Mathf.Max(1, Mathf.CeilToInt(l2_h / 2.0f));
            cmd.SetComputeTextureParam(cs, passData.kernelBuildDepthPyramidL3, ShaderIDs.DepthPyramidL2, passData.depthPyramidL2);
            cmd.SetComputeTextureParam(cs, passData.kernelBuildDepthPyramidL3, ShaderIDs.DepthPyramidL3_RW, passData.depthPyramidL3);
            cmd.DispatchCompute(cs, passData.kernelBuildDepthPyramidL3, Mathf.CeilToInt(l3_w / 8.0f), Mathf.CeilToInt(l3_h / 8.0f), 1);

            int l4_w = Mathf.Max(1, Mathf.CeilToInt(l3_w / 2.0f));
            int l4_h = Mathf.Max(1, Mathf.CeilToInt(l3_h / 2.0f));
            cmd.SetComputeTextureParam(cs, passData.kernelBuildDepthPyramidL4, ShaderIDs.DepthPyramidL3, passData.depthPyramidL3);
            cmd.SetComputeTextureParam(cs, passData.kernelBuildDepthPyramidL4, ShaderIDs.DepthPyramidL4_RW, passData.depthPyramidL4);
            cmd.DispatchCompute(cs, passData.kernelBuildDepthPyramidL4, Mathf.CeilToInt(l4_w / 8.0f), Mathf.CeilToInt(l4_h / 8.0f), 1);

            cmd.SetComputeTextureParam(cs, passData.kernelApplyGradient, ShaderIDs.DepthPyramidL1, passData.depthPyramidL1);
            cmd.SetComputeTextureParam(cs, passData.kernelApplyGradient, ShaderIDs.DepthPyramidL2, passData.depthPyramidL2);
            cmd.SetComputeTextureParam(cs, passData.kernelApplyGradient, ShaderIDs.DepthPyramidL3, passData.depthPyramidL3);
            cmd.SetComputeTextureParam(cs, passData.kernelApplyGradient, ShaderIDs.DepthPyramidL4, passData.depthPyramidL4);
            cmd.SetComputeTextureParam(cs, passData.kernelApplyGradient, ShaderIDs.NeighborhoodSizeMap, passData.neighborhoodSizeMap);
            cmd.SetComputeTextureParam(cs, passData.kernelApplyGradient, ShaderIDs.CorrectedNeighborhoodSizeMap_RW, passData.correctedNeighborhoodSizeMap);
            cmd.DispatchCompute(cs, passData.kernelApplyGradient, threadGroupsX, threadGroupsY, 1);
        }

        cmd.SetComputeTextureParam(cs, passData.kernelOcclusion, ShaderIDs.ColorMap, passData.colorMap);
        cmd.SetComputeTextureParam(cs, passData.kernelOcclusion, ShaderIDs.DepthMap, passData.depthMap);
        cmd.SetComputeTextureParam(cs, passData.kernelOcclusion, ShaderIDs.ViewPositionMap, passData.viewPositionMap);
        cmd.SetComputeTextureParam(cs, passData.kernelOcclusion, ShaderIDs.VirtualDepthMap, passData.virtualDepthTexture);
        cmd.SetComputeTextureParam(cs, passData.kernelOcclusion, ShaderIDs.OriginTypeMap, passData.originTypeMap);
        cmd.SetComputeTextureParam(cs, passData.kernelOcclusion, ShaderIDs.OriginTypeMap_RW, passData.originTypeMap);
        cmd.SetComputeTextureParam(cs, passData.kernelOcclusion, ShaderIDs.FinalNeighborhoodSizeMap, passData.settings.enableGradientCorrection ? passData.correctedNeighborhoodSizeMap : passData.neighborhoodSizeMap);
        cmd.SetComputeTextureParam(cs, passData.kernelOcclusion, ShaderIDs.OcclusionResultMap_RW, passData.occlusionResultMap);
        
        cmd.SetComputeIntParam(cs, ShaderIDs.RecordOcclusionDebug, passData.settings.recordOcclusionDebugMap ? 1 : 0);
        cmd.SetComputeTextureParam(cs, passData.kernelOcclusion, ShaderIDs.OcclusionValueMap_RW, passData.occlusionValueMap);

        cmd.SetComputeTextureParam(cs, passData.kernelOcclusion, ShaderIDs.OriginMap_RW, passData.originDebugMap);
        cmd.DispatchCompute(cs, passData.kernelOcclusion, threadGroupsX, threadGroupsY, 1);

        cmd.SetComputeTextureParam(cs, passData.kernelInterpolate, ShaderIDs.OcclusionResultMap, passData.occlusionResultMap);
        cmd.SetComputeTextureParam(cs, passData.kernelInterpolate, ShaderIDs.VirtualDepthMap, passData.virtualDepthTexture);
        cmd.SetComputeTextureParam(cs, passData.kernelInterpolate, ShaderIDs.CameraColorTexture, passData.cameraColorTexture);
        cmd.SetComputeTextureParam(cs, passData.kernelInterpolate, ShaderIDs.OriginTypeMap, passData.originTypeMap);
        cmd.SetComputeTextureParam(cs, passData.kernelInterpolate, ShaderIDs.FinalImage_RW, passData.finalImage);
        cmd.SetComputeTextureParam(cs, passData.kernelInterpolate, ShaderIDs.OriginMap_RW, passData.originDebugMap);
        cmd.DispatchCompute(cs, passData.kernelInterpolate, threadGroupsX, threadGroupsY, 1);
    }

    private static void ExecuteBlitPass(BlitPassData passData, RasterGraphContext context)
    {
        if (passData.blendMaterial != null && !passData.enableOriginDebugMap)
        {
            Blitter.BlitTexture(context.cmd, passData.sourceImage, new Vector4(1, 1, 0, 0), passData.blendMaterial, 0);
            return;
        }
        Blitter.BlitTexture(context.cmd, passData.sourceImage, new Vector4(1, 1, 0, 0), 0.0f, false);
    }
}
