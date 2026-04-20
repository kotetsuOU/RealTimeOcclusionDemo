using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Experimental.Rendering;

public partial class PCDRenderPass
{
    private void EnqueueDebugReadbackPasses(RenderGraph renderGraph, int screenWidth, int screenHeight, 
        TextureHandle occlusionValueMapHandle_RG, TextureHandle integratedDepthMapHandle_RG, 
        TextureHandle neighborhoodMapHandle_RG, TextureHandle neighborCountMapHandle_RG)
    {
        // --- デバッグ用のOcclusionMap(色付き16palette)と値を非同期出力するパス ---
        if (_settings.recordOcclusionDebugMap || _settings.recordPixelTagMap)
        {
            bool shouldExportOcclusionMap = _settings.recordOcclusionDebugMap;
            bool shouldExportPixelTagMap = _settings.recordPixelTagMap;

            using (var builder = renderGraph.AddUnsafePass<BlitPassData>("PCD Extract Occlusion Debug", out var debugData))
            {
                builder.UseTexture(occlusionValueMapHandle_RG, AccessFlags.Read);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((BlitPassData passData, UnsafeGraphContext context) =>
                {
                    if (_occlusionValueMapHandle == null || _occlusionValueMapHandle.rt == null)
                    {
                        return;
                    }

                    var rt = _occlusionValueMapHandle.rt;
                    context.cmd.RequestAsyncReadback(rt, 0, 0, screenWidth, 0, screenHeight, 0, 1, GraphicsFormat.R32G32_SFloat, request =>
                    {
                        if (request.hasError)
                        {
                            Debug.LogError("[PCD Export] AsyncGPUReadback error.");
                            return;
                        }

                        int w = request.width;
                        int h = request.height;
                        var rawData = request.GetData<float>();
                        float[] fData = new float[w * h];
                        float[] rawValues = new float[w * h];
                        for(int i = 0; i < w * h; i++)
                        {
                            fData[i] = rawData[i * 2];
                            rawValues[i] = rawData[i * 2 + 1];
                        }

                        string methodPrefix = GetMethodPrefix();

                        if (shouldExportPixelTagMap)
                        {
                            Debug.Log($"[PCD PixelTag Export] AsyncGPUReadback success! w:{w}, h:{h}");
                            // PixelTagMap: 閾値判定後のアルファ値 (0か1かなど) で色付け＆CSV出力
                            PCDOcclusionDebugExporter.ExportOcclusionMap16PaletteFromData(fData, fData, w, h, "Assets/HandTrackingData/PixelTagMaps", "PixelTag_" + methodPrefix);
                        }
                        if (shouldExportOcclusionMap)
                        {
                            Debug.Log($"[PCD Occlusion Export] AsyncGPUReadback success! w:{w}, h:{h}");
                            // OcclusionMap: occlusionAverage (0~1) を可視化/CSV出力
                            PCDOcclusionDebugExporter.ExportOcclusionMap16PaletteFromData(fData, rawValues, w, h, "Assets/HandTrackingData/OcclusionMaps", "Occlusion_" + methodPrefix, preferRawValuesInCsv: true);
                        }
                    });
                });
            }

            if (PCDRendererFeature.Instance != null)
            {
                PCDRendererFeature.Instance.recordOcclusionDebugMap = false;
                PCDRendererFeature.Instance.recordPixelTagMap = false;
            }
        }

        // --- デバッグ用の統合DepthMapを非同期出力するパス ---
        if (_settings.recordIntegratedDepthMap)
        {
            using (var builder = renderGraph.AddUnsafePass<BlitPassData>("PCD Extract Integrated Depth", out var debugDepthData))
            {
                builder.UseTexture(integratedDepthMapHandle_RG, AccessFlags.Read);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((BlitPassData passData, UnsafeGraphContext context) =>
                {
                    if (_integratedDepthMapHandle == null || _integratedDepthMapHandle.rt == null)
                    {
                        return;
                    }

                    var rt = _integratedDepthMapHandle.rt;
                    context.cmd.RequestAsyncReadback(rt, 0, 0, screenWidth, 0, screenHeight, 0, 1, GraphicsFormat.R32_UInt, request =>
                    {
                        if (request.hasError)
                        {
                            Debug.LogError("[PCD IntegratedDepth Export] AsyncGPUReadback error.");
                            return;
                        }

                        int w = request.width;
                        int h = request.height;
                        var rawData = request.GetData<uint>();
                        uint[] depthData = new uint[w * h];
                        rawData.CopyTo(depthData);

                        string methodPrefix = GetMethodPrefix();

                        Debug.Log($"[PCD IntegratedDepth Export] AsyncGPUReadback success! w:{w}, h:{h}");
                        PCDIntegratedDepthMapExporter.ExportIntegratedDepthMapFromData(depthData, w, h, "Assets/HandTrackingData/DepthMaps/Integrated", methodPrefix);
                    });
                });
            }

            if (PCDRendererFeature.Instance != null && PCDRendererFeature.Instance.recordIntegratedDepthMap)
            {
                PCDRendererFeature.Instance.recordIntegratedDepthMap = false;
            }
        }

        // --- デバッグ用のNeighborhoodMapを非同期出力するパス ---
        if (_settings.recordNeighborhoodMap)
        {
            using (var builder = renderGraph.AddUnsafePass<BlitPassData>("PCD Extract Neighborhood Map", out var debugData))
            {
                builder.UseTexture(neighborhoodMapHandle_RG, AccessFlags.Read);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((BlitPassData passData, UnsafeGraphContext context) =>
                {
                    if (_neighborhoodMapHandle == null || _neighborhoodMapHandle.rt == null)
                    {
                        return;
                    }

                    var rt = _neighborhoodMapHandle.rt;
                    context.cmd.RequestAsyncReadback(rt, 0, 0, screenWidth, 0, screenHeight, 0, 1, GraphicsFormat.R32_SInt, request =>
                    {
                        if (request.hasError)
                        {
                            Debug.LogError("[PCD Neighborhood Map Export] AsyncGPUReadback error.");
                            return;
                        }

                        int w = request.width;
                        int h = request.height;
                        var rawData = request.GetData<int>();
                        int[] sizeData = new int[w * h];
                        rawData.CopyTo(sizeData);

                        string methodPrefix = GetMethodPrefix();

                        Debug.Log($"[PCD Neighborhood Map Export] AsyncGPUReadback success! w:{w}, h:{h}");
                        PCDOcclusionDebugExporter.ExportNeighborhoodMapFromData(sizeData, w, h, "Assets/HandTrackingData/NeighborhoodMaps", methodPrefix);
                    });
                });
            }

            if (PCDRendererFeature.Instance != null && PCDRendererFeature.Instance.recordNeighborhoodMap)
            {
                PCDRendererFeature.Instance.recordNeighborhoodMap = false;
            }
        }

        // --- デバッグ用のNeighborCountMapを非同期出力するパス ---
        if (_settings.recordNeighborCountMap)
        {
            using (var builder = renderGraph.AddUnsafePass<BlitPassData>("PCD Extract NeighborCount Map", out var debugData))
            {
                builder.UseTexture(neighborCountMapHandle_RG, AccessFlags.Read);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((BlitPassData passData, UnsafeGraphContext context) =>
                {
                    if (_neighborCountMapHandle == null || _neighborCountMapHandle.rt == null)
                    {
                        return;
                    }

                    var rt = _neighborCountMapHandle.rt;
                    context.cmd.RequestAsyncReadback(rt, 0, 0, screenWidth, 0, screenHeight, 0, 1, GraphicsFormat.R32_UInt, request =>
                    {
                        if (request.hasError)
                        {
                            Debug.LogError("[PCD NeighborCount Map Export] AsyncGPUReadback error.");
                            return;
                        }

                        int w = request.width;
                        int h = request.height;
                        var rawData = request.GetData<uint>();
                        int[] countData = new int[w * h];
                        for(int i=0; i<w*h; i++) countData[i] = (int)rawData[i];

                        string methodPrefix = GetMethodPrefix();

                        Debug.Log($"[PCD NeighborCount Map Export] AsyncGPUReadback success! w:{w}, h:{h}");
                        PCDOcclusionDebugExporter.ExportNeighborhoodMapFromData(countData, w, h, "Assets/HandTrackingData/NeighborCountMaps", "Count_" + methodPrefix, isNeighborCount: true);
                    });
                });
            }

            if (PCDRendererFeature.Instance != null && PCDRendererFeature.Instance.recordNeighborCountMap)
            {
                PCDRendererFeature.Instance.recordNeighborCountMap = false;
            }
        }
    }
}