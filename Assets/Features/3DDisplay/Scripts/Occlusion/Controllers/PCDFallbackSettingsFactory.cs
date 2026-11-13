using UnityEngine;
using static PCDRendererFeature;

/// <summary>
/// <see cref="PCDSettingsBridge"/> が <see cref="PCDOcclusionPipelineController"/> 不在時に使用する
/// 既定の <see cref="PCDRenderSettings"/> 生成とフェード幅バリデーションを提供する Pure C# ファクトリー。
/// </summary>
internal static class PCDFallbackSettingsFactory
{
    public static PCDRenderSettings CreateDefault()
    {
        return new PCDRenderSettings
        {
            cameraTargetMode = PCD_CameraTargetMode.AllValidCameras,
            cameraNameFilter = "Virtual",
            kernelType = PCD_OcclusionKernel.Bouchiba,
            evaluationMode = PCD_OcclusionEvaluationMode.PatternLUT256,
            minOccludedSectors = 7,
            maxConsecutiveEmptySectors = 1,
            minSearchLevel = 1,
            exponentAlpha = 0f,
            densityThreshold_e = 0.04f,
            neighborhoodParam_p_prime = 4.8f,
            enableDensityBasedLOD = true,
            enableGradientCorrection = true,
            gradientThreshold_g_th = 0.05f,
            occlusionThreshold_T_ave = 0.8f,
            occlusionThreshold_T_loc = 0.8f,
            useSectorSpecificThreshold = false,
            sectorThreshold_T_loc = new float[8] { 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f },
            occlusionThreshold = 0.8f,
            occlusionFadeWidth = 0.1f,
            enableVirtualContactOcclusion = false,
            virtualContactRadius = 0.03f,
            virtualContactSpacing = 0.005f,
            virtualContactColor = new Color(0.8f, 0f, 0.4f, 1f),
            enablePixelTagMap = false,
            enableOcclusionMap = false,
            recordOcclusionDebugMap = false,
            recordPixelTagMap = false,
            recordIntegratedDepthMap = false,
            recordNeighborhoodMap = false,
            recordNeighborCountMap = false,
            debugPatternId = -1,
            debugSectorId = -1,
            enableVirtualDepthIntegration = true,
            enableTagBasedOptimization = true,
            enableTypeAwareDensity = true,
            enableSoftOcclusionFade = true,
            holeFillingMethod = PCD_HoleFillingMethod.JointBilateral,
            morphKernelHalfSize = 1,
            morphErodeIterations = 0,
            morphDilateIterations = 1,
            gridSize = PCD_GridSize.Grid16x16,
            useDensityAdaptiveLut = false,
            densityLutCount = 0
        };
    }

    public static float ClampOcclusionFadeWidth(
        PCD_OcclusionEvaluationMode mode, float tAve, float tLoc, float fadeWidth)
    {
        float currentTh = mode == PCD_OcclusionEvaluationMode.Average ? tAve : tLoc;
        float maxFadeWidth = Mathf.Min(currentTh, 1.0f - currentTh) * 2.0f;
        return Mathf.Clamp(fadeWidth, 0f, maxFadeWidth);
    }
}

