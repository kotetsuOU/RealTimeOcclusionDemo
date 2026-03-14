using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class RsHandMeshRenderFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
        public Material handMeshMaterial;
    }

    public Settings settings = new Settings();
    private RsHandMeshRenderPass _renderPass;

    public override void Create()
    {
        _renderPass = new RsHandMeshRenderPass(settings);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (settings.handMeshMaterial == null) return;
        if (RsHandMeshRenderBridge.Instance == null) return;
        if (!RsHandMeshRenderBridge.Instance.HasAnyData) return;
        
        renderer.EnqueuePass(_renderPass);
    }

    class RsHandMeshRenderPass : ScriptableRenderPass
    {
        private readonly Settings _settings;

        public RsHandMeshRenderPass(Settings settings)
        {
            _settings = settings;
            renderPassEvent = settings.renderPassEvent;
        }

        private class PassData
        {
            public Settings settings;
        }

        public override void RecordRenderGraph(UnityEngine.Rendering.RenderGraphModule.RenderGraph renderGraph, ContextContainer frameData)
        {
            var bridge = RsHandMeshRenderBridge.Instance;
            if (bridge == null || !bridge.HasAnyData) return;

            using (var builder = renderGraph.AddUnsafePass<PassData>("RsHandMeshRenderPass", out var passData))
            {
                passData.settings = _settings;

                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData data, UnityEngine.Rendering.RenderGraphModule.UnsafeGraphContext context) =>
                {
                    var cmd = context.cmd;
                    
                    var localBridge = RsHandMeshRenderBridge.Instance;
                    if (localBridge == null) return;

                    foreach (var kvp in localBridge.HandMeshes)
                    {
                        var handData = kvp.Value;
                        if (!handData.HasData) continue;

                        cmd.DrawProceduralIndirect(
                            handData.LocalToWorld,
                            data.settings.handMeshMaterial,
                            0,
                            MeshTopology.Triangles,
                            handData.ArgsBuffer,
                            0,
                            handData.PropertyBlock
                        );
                    }
                });
            }
        }

        [System.Obsolete]
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var bridge = RsHandMeshRenderBridge.Instance;
            if (bridge == null) return;

            var cmd = CommandBufferPool.Get("RsHandMeshRender");

            foreach (var kvp in bridge.HandMeshes)
            {
                var data = kvp.Value;
                if (!data.HasData) continue;

                cmd.DrawProceduralIndirect(
                    data.LocalToWorld,
                    _settings.handMeshMaterial,
                    0,
                    MeshTopology.Triangles,
                    data.ArgsBuffer,
                    0,
                    data.PropertyBlock
                );
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}