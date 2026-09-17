// =============================================================================
// PCDDebugReadbackManager.cs
// -----------------------------------------------------------------------------
// デバッグ用のGPUデータ読み戻し（AsyncReadback）パスを管理する独立クラス。
//
// 各デバッグ記録フラグが有効な場合にのみ、対応する RenderGraph パスを追加し、
// GPU テクスチャの内容を CPU 側に非同期で読み戻して画像/CSV として保存する。
// =============================================================================
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Experimental.Rendering;
using Core.Logging;

/// <summary>
/// デバッグ用 AsyncReadback パスを RenderGraph に登録するマネージャー。
/// </summary>
internal class PCDDebugReadbackManager
{
    /// <summary> Blit パス用の軽量データコンテナ（デバッグ読み戻し用に流用） </summary>
    private class DebugReadbackPassData
    {
        internal TextureHandle sourceTexture;
    }

    /// <summary>
    /// 各デバッグ記録フラグが有効な場合に、対応する AsyncReadback パスを RenderGraph に追加する。
    /// </summary>
    public void EnqueueReadbackPasses(
        RenderGraph renderGraph,
        PCDRendererFeature.PCDRenderSettings settings,
        PCDResourcePool resources,
        int screenWidth, int screenHeight,
        TextureHandle occlusionValueMapHandle_RG,
        TextureHandle integratedDepthMapHandle_RG,
        TextureHandle neighborhoodMapHandle_RG,
        TextureHandle neighborCountMapHandle_RG,
        string methodPrefix)
    {
        // --- OcclusionMap / PixelTagMap ---
        if (settings.recordOcclusionDebugMap || settings.recordPixelTagMap)
        {
            bool shouldExportOcclusionMap = settings.recordOcclusionDebugMap;
            bool shouldExportPixelTagMap = settings.recordPixelTagMap;

            using (var builder = renderGraph.AddUnsafePass<DebugReadbackPassData>("PCD Extract Occlusion Debug", out var debugData))
            {
                builder.UseTexture(occlusionValueMapHandle_RG, AccessFlags.Read);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((DebugReadbackPassData passData, UnsafeGraphContext context) =>
                {
                    if (resources.OcclusionValueMap == null || resources.OcclusionValueMap.rt == null)
                        return;

                    var rt = resources.OcclusionValueMap.rt;
                    context.cmd.RequestAsyncReadback(rt, 0, 0, screenWidth, 0, screenHeight, 0, 1, GraphicsFormat.R32G32_SFloat, request =>
                    {
                        if (request.hasError)
                        {
                            AppLogger.LogError(PCD_LogTriggers.TagRecordDebug, "AsyncGPUReadback error.");
                            return;
                        }

                        int w = request.width;
                        int h = request.height;
                        var rawData = request.GetData<float>();
                        float[] fData = new float[w * h];
                        float[] rawValues = new float[w * h];
                        for (int i = 0; i < w * h; i++)
                        {
                            fData[i] = rawData[i * 2];
                            rawValues[i] = rawData[i * 2 + 1];
                        }

                        if (shouldExportPixelTagMap)
                        {
                            AppLogger.Log(PCD_LogTriggers.TagRecordDebug, $"AsyncGPUReadback success! PixelTagMap w:{w}, h:{h}");
                            PCDOcclusionDebugExporter.ExportOcclusionMap16PaletteFromData(fData, fData, w, h, "Assets/HandTrackingData/PixelTagMaps", "PixelTag_" + methodPrefix);
                        }
                        if (shouldExportOcclusionMap)
                        {
                            AppLogger.Log(PCD_LogTriggers.TagRecordDebug, $"AsyncGPUReadback success! OcclusionMap w:{w}, h:{h}");
                            PCDOcclusionDebugExporter.ExportOcclusionMap16PaletteFromData(fData, rawValues, w, h, "Assets/HandTrackingData/OcclusionMaps", "Occlusion_" + methodPrefix, preferRawValuesInCsv: true);
                        }
                    });
                });
            }

            if (PCDRendererFeature.Instance != null && PCDRendererFeature.Instance.settings != null)
            {
                PCDRendererFeature.Instance.settings.recordOcclusionDebugMap = false;
                PCDRendererFeature.Instance.settings.recordPixelTagMap = false;
            }
        }

        // --- IntegratedDepthMap ---
        if (settings.recordIntegratedDepthMap)
        {
            using (var builder = renderGraph.AddUnsafePass<DebugReadbackPassData>("PCD Extract Integrated Depth", out var debugDepthData))
            {
                builder.UseTexture(integratedDepthMapHandle_RG, AccessFlags.Read);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((DebugReadbackPassData passData, UnsafeGraphContext context) =>
                {
                    if (resources.IntegratedDepthMap == null || resources.IntegratedDepthMap.rt == null)
                        return;

                    var rt = resources.IntegratedDepthMap.rt;
                    context.cmd.RequestAsyncReadback(rt, 0, 0, screenWidth, 0, screenHeight, 0, 1, GraphicsFormat.R32_UInt, request =>
                    {
                        if (request.hasError)
                        {
                            AppLogger.LogError(PCD_LogTriggers.TagRecordDebug, "AsyncGPUReadback error for IntegratedDepth.");
                            return;
                        }

                        int w = request.width;
                        int h = request.height;
                        var rawData = request.GetData<uint>();
                        uint[] depthData = new uint[w * h];
                        rawData.CopyTo(depthData);

                        AppLogger.Log(PCD_LogTriggers.TagRecordDebug, $"AsyncGPUReadback success! IntegratedDepthMap w:{w}, h:{h}");
                        PCDIntegratedDepthMapExporter.ExportIntegratedDepthMapFromData(depthData, w, h, "Assets/HandTrackingData/DepthMaps/Integrated", methodPrefix);
                    });
                });
            }

            if (PCDRendererFeature.Instance != null && PCDRendererFeature.Instance.settings != null && PCDRendererFeature.Instance.settings.recordIntegratedDepthMap)
            {
                PCDRendererFeature.Instance.settings.recordIntegratedDepthMap = false;
            }
        }

        // --- NeighborhoodMap ---
        if (settings.recordNeighborhoodMap)
        {
            using (var builder = renderGraph.AddUnsafePass<DebugReadbackPassData>("PCD Extract Neighborhood Map", out var debugData))
            {
                builder.UseTexture(neighborhoodMapHandle_RG, AccessFlags.Read);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((DebugReadbackPassData passData, UnsafeGraphContext context) =>
                {
                    if (resources.NeighborhoodMap == null || resources.NeighborhoodMap.rt == null)
                        return;

                    var rt = resources.NeighborhoodMap.rt;
                    context.cmd.RequestAsyncReadback(rt, 0, 0, screenWidth, 0, screenHeight, 0, 1, GraphicsFormat.R32_SInt, request =>
                    {
                        if (request.hasError)
                        {
                            AppLogger.LogError(PCD_LogTriggers.TagRecordDebug, "AsyncGPUReadback error for NeighborhoodMap.");
                            return;
                        }

                        int w = request.width;
                        int h = request.height;
                        var rawData = request.GetData<int>();
                        int[] sizeData = new int[w * h];
                        rawData.CopyTo(sizeData);

                        AppLogger.Log(PCD_LogTriggers.TagRecordDebug, $"AsyncGPUReadback success! NeighborhoodMap w:{w}, h:{h}");
                        PCDOcclusionDebugExporter.ExportNeighborhoodMapFromData(sizeData, w, h, "Assets/HandTrackingData/NeighborhoodMaps", methodPrefix);
                    });
                });
            }

            if (PCDRendererFeature.Instance != null && PCDRendererFeature.Instance.settings != null && PCDRendererFeature.Instance.settings.recordNeighborhoodMap)
            {
                PCDRendererFeature.Instance.settings.recordNeighborhoodMap = false;
            }
        }

        // --- NeighborCountMap (単発デバッグ出力用。SICESIスイープ中は自前リードバックを行うためスキップ) ---
        if (settings.recordNeighborCountMap && !SICESI.SICESI_SectorMaskCollector.IsCollectingActive)
        {
            using (var builder = renderGraph.AddUnsafePass<DebugReadbackPassData>("PCD Extract NeighborCount Map", out var debugData))
            {
                builder.UseTexture(neighborCountMapHandle_RG, AccessFlags.Read);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((DebugReadbackPassData passData, UnsafeGraphContext context) =>
                {
                    if (resources.NeighborCountMap == null || resources.NeighborCountMap.rt == null)
                        return;

                    var rt = resources.NeighborCountMap.rt;
                    context.cmd.RequestAsyncReadback(rt, 0, 0, screenWidth, 0, screenHeight, 0, 1, GraphicsFormat.R32_UInt, request =>
                    {
                        if (request.hasError)
                        {
                            AppLogger.LogError(PCD_LogTriggers.TagRecordDebug, "AsyncGPUReadback error for NeighborCountMap.");
                            return;
                        }

                        int w = request.width;
                        int h = request.height;
                        var rawData = request.GetData<uint>();
                        uint[] uintRaw = rawData.ToArray();

                        AppLogger.Log(PCD_LogTriggers.TagRecordDebug, $"AsyncGPUReadback success! NeighborCountMap w:{w}, h:{h}");

                        // 1. 実測8ビット占有マスク詳細データ (RAWバイナリ, サマリーCSV, グレースケールPNG) を保存
                        PCDOcclusionDebugExporter.ExportSectorMaskData(uintRaw, w, h, "Assets/HandTrackingData/SectorMasks", "SectorMask_" + methodPrefix);

                        // 2. 従来の近傍カウント可視化PNGも同時に保存 (bit 8..11 の validSectorCount を抽出)
                        int[] countData = new int[w * h];
                        for (int i = 0; i < w * h; i++) countData[i] = (int)((uintRaw[i] >> 8) & 0x0Fu);
                        PCDOcclusionDebugExporter.ExportNeighborhoodMapFromData(countData, w, h, "Assets/HandTrackingData/NeighborCountMaps", "Count_" + methodPrefix, isNeighborCount: true);
                    });
                });
            }

            if (PCDRendererFeature.Instance != null && PCDRendererFeature.Instance.settings != null && PCDRendererFeature.Instance.settings.recordNeighborCountMap)
            {
                PCDRendererFeature.Instance.settings.recordNeighborCountMap = false;
            }
        }
    }
}
