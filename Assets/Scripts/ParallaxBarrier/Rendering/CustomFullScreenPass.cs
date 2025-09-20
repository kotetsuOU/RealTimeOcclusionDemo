using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using static System.Net.Mime.MediaTypeNames;

public class CustomFullScreenPass : ScriptableRenderPass
{
    Material material;
    RTHandle cameraColorTarget;

    public Color color1;
    public Color color2;

    public CustomFullScreenPass(Material material)
    {
        this.material = material;
        renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        profilingSampler = new ProfilingSampler("CustomFullScreenPass");
    }

    public void SetTarget(RTHandle cameraColorTarget)
    {
        this.cameraColorTarget = cameraColorTarget;
    }

    public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
    {
        ConfigureTarget(cameraColorTarget);
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        var cameraData = renderingData.cameraData;
        if (cameraData.cameraType != CameraType.Game)
        {
            return;
        }

        if (material == null)
        {
            return;
        }

        CommandBuffer cmd = CommandBufferPool.Get();
        using (new ProfilingScope(cmd, profilingSampler))
        {
            if (UnityEngine.Application.isPlaying)
            {
                if (Time.frameCount % 2 == 0)
                {
                    material.SetColor("_Color", color1);
                }
                else
                {
                    material.SetColor("_Color", color2);
                }
            }
            else
            {
                material.SetColor("_Color", color1);
            }
            /*if (UnityEngine.Application.isPlaying)
            {
                if (Time.frameCount % 2 == 0)
                {
                    material.SetInt("_FrameCount", 0);
                }
                else
                {
                    material.SetInt("_FrameCount",1);
                }
            }
            else
            {
                material.SetInt("_FrameCount", 0);
            }*/

            cmd.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Quads, 4, 1);
        }
        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();

        CommandBufferPool.Release(cmd);
    }
}
