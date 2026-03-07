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