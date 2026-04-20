using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Experimental.Rendering;

public partial class PCDRenderPass
{
    private void BindComputePassData(ref ComputePassData data, Camera camera, int screenWidth, int screenHeight, int activeCount, ComputeBuffer activeBuffer, bool depthMapOnlyMode, UniversalResourceData resourceData)
    {
        data.computeShader = pointCloudCompute;
        data.pointCount = activeCount;
        data.screenParams = new Vector4(screenWidth, screenHeight, 0, 0);
        data.viewMatrix = camera.worldToCameraMatrix;
        data.projectionMatrix = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false);
        data.settings = _settings;
        data.kernelClear = _kernelClear;
        data.kernelClearCounter = _kernelClearCounter;
        data.kernelProject = _kernelProject;
        data.kernelCalcGridZMin = _kernelCalcGridZMin;
        data.kernelCalcDensity = _kernelCalcDensity;
        data.kernelCalcGridLevel = _kernelCalcGridLevel;
        data.kernelGridMedianFilter = _kernelGridMedianFilter;
        data.kernelCalcNeighborhoodSize = _kernelCalcNeighborhoodSize;
        data.kernelBuildDepthPyramidL1 = _kernelBuildDepthPyramidL1;
        data.kernelBuildDepthPyramidL2 = _kernelBuildDepthPyramidL2;
        data.kernelBuildDepthPyramidL3 = _kernelBuildDepthPyramidL3;
        data.kernelBuildDepthPyramidL4 = _kernelBuildDepthPyramidL4;
        data.kernelApplyGradient = _kernelApplyGradient;
        data.kernelComputeOcclusion = _kernelComputeOcclusion;
        data.kernelFillHoles = _kernelFillHoles;
        data.kernelInterpolate = _kernelInterpolate;
        data.kernelMerge = _kernelMerge;
        data.kernelInitFromCamera = _kernelInitFromCamera;
        data.kernelVisualizeOcclusionDebug = _kernelVisualizeOcclusionDebug;
        data.useExternal = _bufferManager.UseExternalBuffer;
        data.externalBuffer = _bufferManager.ExternalPointBuffer;
        data.internalBuffer = _bufferManager.PointBuffer;
        data.externalCount = _bufferManager.ExternalPointCount;
        data.internalCount = _bufferManager.PointCount;
        data.combinedBuffer = _bufferManager.CombinedBuffer;
        data.pointBuffer = activeBuffer;
        data.staticMeshCounterBuffer = _staticMeshCounterBuffer;
        data.hasVirtualDepth = resourceData.cameraDepthTexture.IsValid();
        data.depthMapOnlyMode = depthMapOnlyMode;
        data.inverseProjectionMatrix = camera.projectionMatrix.inverse;
    }
}