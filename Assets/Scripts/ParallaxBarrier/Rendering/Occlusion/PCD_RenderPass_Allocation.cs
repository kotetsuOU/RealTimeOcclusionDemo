using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Experimental.Rendering;

public partial class PCDRenderPass
{
    private void AllocateDebugMapHandles(int screenWidth, int screenHeight)
    {
        // デバッグマップ(PixelTag または Occlusion)のテクスチャハンドル生成
        // 画面解像度が変わった場合などは再割り当てを行う
        if (_settings.enablePixelTagMap || _settings.enableOcclusionMap)
        {
            if (_debugDisplayMapHandle == null || _debugDisplayMapHandle.rt == null || _debugDisplayMapHandle.rt.width != screenWidth || _debugDisplayMapHandle.rt.height != screenHeight)
            {
                _debugDisplayMapHandle?.Release();
                var desc = new RenderTextureDescriptor(screenWidth, screenHeight, GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.ARGBFloat, false), 0);
                desc.enableRandomWrite = true;
                _debugDisplayMapHandle = RTHandles.Alloc(desc, name: "_DebugDisplayMap");
            }
        }

        // オクルージョンデバッグマップのテクスチャハンドル生成
        if (_settings.recordOcclusionDebugMap || _settings.recordPixelTagMap)
        {
            if (_occlusionValueMapHandle == null || _occlusionValueMapHandle.rt == null || _occlusionValueMapHandle.rt.width != screenWidth || _occlusionValueMapHandle.rt.height != screenHeight)
            {
                _occlusionValueMapHandle?.Release();
                var desc = new RenderTextureDescriptor(screenWidth, screenHeight, GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.RGFloat, false), 0);
                desc.enableRandomWrite = true;
                _occlusionValueMapHandle = RTHandles.Alloc(desc, name: "_OcclusionValueMap");
            }
        }

        // 統合DepthMap記録用のテクスチャハンドル生成
        if (_settings.recordIntegratedDepthMap)
        {
            if (_integratedDepthMapHandle == null || _integratedDepthMapHandle.rt == null || _integratedDepthMapHandle.rt.width != screenWidth || _integratedDepthMapHandle.rt.height != screenHeight)
            {
                _integratedDepthMapHandle?.Release();
                var desc = new RenderTextureDescriptor(screenWidth, screenHeight, GraphicsFormat.R32_UInt, 0);
                desc.enableRandomWrite = true;
                _integratedDepthMapHandle = RTHandles.Alloc(desc, name: "_IntegratedDepthMap");
            }
        }

        // NeighborhoodMap記録用のテクスチャハンドル生成
        if (_settings.recordNeighborhoodMap)
        {
            if (_neighborhoodMapHandle == null || _neighborhoodMapHandle.rt == null || _neighborhoodMapHandle.rt.width != screenWidth || _neighborhoodMapHandle.rt.height != screenHeight)
            {
                _neighborhoodMapHandle?.Release();
                var desc = new RenderTextureDescriptor(screenWidth, screenHeight, GraphicsFormat.R32_SInt, 0);
                desc.enableRandomWrite = true;
                _neighborhoodMapHandle = RTHandles.Alloc(desc, name: "_NeighborhoodMapDebug");
            }
        }

        // NeighborCountMap記録用（シェーダー側でバインディングに必須なため常にアロケートしておく）
        if (_neighborCountMapHandle == null || _neighborCountMapHandle.rt == null || _neighborCountMapHandle.rt.width != screenWidth || _neighborCountMapHandle.rt.height != screenHeight)
        {
            _neighborCountMapHandle?.Release();
            var desc = new RenderTextureDescriptor(screenWidth, screenHeight, GraphicsFormat.R32_UInt, 0);
            desc.enableRandomWrite = true;
            _neighborCountMapHandle = RTHandles.Alloc(desc, name: "_NeighborCountMapDebug");
        }
    }
}