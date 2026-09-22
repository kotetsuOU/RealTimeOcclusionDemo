#pragma warning disable 0618, 0672

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Core.Logging;
#if UNITY_6000_0_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
#endif

namespace SRD.Core
{
    [AppLoggable("SRD Display (PCD/SRD)")]
    public class MirrorRendererFeature : ScriptableRendererFeature, IAppLoggable
    {
        public const string TagMirrorPassDebug = "SRD_MirrorPassDebug";

        [System.Serializable]
        public class MirrorSettings
        {
            [Tooltip("鏡像反転用マテリアル (Shader: Hidden/SRD/ScreenSpaceMirror)")]
            public Material mirrorMaterial;

            [Tooltip("RenderPassの実行イベントタイミング")]
            public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;

            [Tooltip("固定ターゲット座標を使用するか (falseの場合はカメラ前方0.5mを動的ターゲットにする)")]
            public bool useFixedTargetPos = false;

            [Tooltip("視点誤差検証用の固定ターゲットワールド座標")]
            public Vector3 debugTargetWorldPos = Vector3.zero;
        }

        public static bool RequestStageDebugCapture = false;
        public static string StageDebugCaptureDir = "";
        public static bool LosslessMirrorMode = false;

        public MirrorSettings settings = new MirrorSettings();
        private MirrorRenderPass _mirrorPass;

        public void RegisterLogTriggers(LogCategoryGroup group, HashSet<string> existingLabels)
        {
            AddSubTriggerIfNotExists(group, this, "[SRD_MirrorPassDebug] Mirror RenderPass Shader & 2D vs 3D Mirror Disparity Diagnostics", TagMirrorPassDebug, existingLabels);
        }

        private void AddSubTriggerIfNotExists(LogCategoryGroup group, Object targetObj, string label, string tag, HashSet<string> existingLabels)
        {
            if (!existingLabels.Contains(label))
            {
                group.entries.Add(new LogInstanceEntry
                {
                    label = label,
                    tag = tag,
                    target = targetObj,
                    enabled = true
                });
                existingLabels.Add(label);
            }
        }

        public override void Create()
        {
            if (settings.mirrorMaterial == null)
            {
                Shader shader = Shader.Find("Hidden/SRD/ScreenSpaceMirror");
                if (shader != null)
                {
                    settings.mirrorMaterial = CoreUtils.CreateEngineMaterial(shader);
                }
            }

            _mirrorPass = new MirrorRenderPass(this, settings);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (settings.mirrorMaterial == null) return;

            // Previewカメラ等での無駄な実行を除外
            if (renderingData.cameraData.cameraType == CameraType.Preview) return;

            renderer.EnqueuePass(_mirrorPass);
        }

        protected override void Dispose(bool disposing)
        {
            _mirrorPass?.Dispose();
        }
    }

    public class MirrorRenderPass : ScriptableRenderPass
    {
        private readonly MirrorRendererFeature _owner;
        private readonly MirrorRendererFeature.MirrorSettings _settings;
        private RTHandle _tempTextureHandle;

        public MirrorRenderPass(MirrorRendererFeature owner, MirrorRendererFeature.MirrorSettings settings)
        {
            _owner = owner;
            _settings = settings;
            renderPassEvent = settings.renderPassEvent;
        }

        private RTHandle _debugM0Handle;
        private RTHandle _debugM1Handle;
        private RTHandle _debugM2Handle;

#if UNITY_6000_0_OR_NEWER
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            if (_settings.mirrorMaterial == null || !resourceData.activeColorTexture.IsValid())
                return;

            _settings.mirrorMaterial.SetFloat("_LosslessMirrorMode", MirrorRendererFeature.LosslessMirrorMode ? 1.0f : 0.0f);

            TextureHandle activeColor = resourceData.activeColorTexture;

            TextureDesc desc = renderGraph.GetTextureDesc(activeColor);
            desc.name = "_MirrorTempTexture";
            desc.clearBuffer = false;
            desc.depthBufferBits = DepthBits.None;
            TextureHandle tempTex = renderGraph.CreateTexture(desc);

            bool isCapture = MirrorRendererFeature.RequestStageDebugCapture && !string.IsNullOrEmpty(MirrorRendererFeature.StageDebugCaptureDir);
            string captureDir = MirrorRendererFeature.StageDebugCaptureDir;

            if (isCapture)
            {
                RenderTextureDescriptor captureDesc = cameraData.cameraTargetDescriptor;
                captureDesc.depthBufferBits = 0;
                RenderingUtils.ReAllocateHandleIfNeeded(ref _debugM0Handle, captureDesc, name: "PCD_M0_Debug");
                RenderingUtils.ReAllocateHandleIfNeeded(ref _debugM1Handle, captureDesc, name: "PCD_M1_Debug");
                RenderingUtils.ReAllocateHandleIfNeeded(ref _debugM2Handle, captureDesc, name: "PCD_M2_Debug");

                TextureHandle m0Target = renderGraph.ImportTexture(_debugM0Handle);
                RenderGraphUtils.AddCopyPass(renderGraph, activeColor, m0Target, "MirrorPass_CaptureM0");
            }

            // 1. activeColor -> tempTex (Mirror Shader)
            var blitParams = new RenderGraphUtils.BlitMaterialParameters(activeColor, tempTex, _settings.mirrorMaterial, 0);
            RenderGraphUtils.AddBlitPass(renderGraph, blitParams, "MirrorPass_Shader");

            if (isCapture)
            {
                TextureHandle m1Target = renderGraph.ImportTexture(_debugM1Handle);
                RenderGraphUtils.AddCopyPass(renderGraph, tempTex, m1Target, "MirrorPass_CaptureM1");
            }

            // 2. tempTex -> activeColor (Copy Back)
            RenderGraphUtils.AddCopyPass(renderGraph, tempTex, activeColor, "MirrorPass_CopyBack");

            if (isCapture)
            {
                TextureHandle m2Target = renderGraph.ImportTexture(_debugM2Handle);
                RenderGraphUtils.AddCopyPass(renderGraph, activeColor, m2Target, "MirrorPass_CaptureM2");

                string camName = cameraData.camera != null ? cameraData.camera.name : "Camera";
                SaveDebugHandleToPNG(_debugM0Handle, System.IO.Path.Combine(captureDir, $"M0_{camName}.png"));
                SaveDebugHandleToPNG(_debugM1Handle, System.IO.Path.Combine(captureDir, $"M1_{camName}.png"));
                SaveDebugHandleToPNG(_debugM2Handle, System.IO.Path.Combine(captureDir, $"M2_{camName}.png"));
                MirrorRendererFeature.RequestStageDebugCapture = false;
            }

            if ((AppLogger.IsEnabled(_owner, MirrorRendererFeature.TagMirrorPassDebug) || AppLogger.IsEnabled(MirrorRendererFeature.TagMirrorPassDebug)) && Time.frameCount % 60 == 0)
            {
                LogPassStatus(cameraData.camera);
            }
        }
#endif

        private static void SaveDebugHandleToPNG(RTHandle handle, string path)
        {
            if (handle == null || handle.rt == null) return;
            RenderTexture rt = handle.rt;
            int width = rt.width;
            int height = rt.height;
            AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32, request =>
            {
                if (request.hasError) return;
                try
                {
                    Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                    tex.LoadRawTextureData(request.GetData<byte>());
                    tex.Apply();
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                    System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
                    UnityEngine.Object.Destroy(tex);
                    Debug.Log($"[MirrorStageDebug] 保存完了: {System.IO.Path.GetFileName(path)} ({width}x{height})");
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[MirrorStageDebug] 保存失敗 {path}: {e.Message}");
                }
            });
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RenderTextureDescriptor descriptor = renderingData.cameraData.cameraTargetDescriptor;
            descriptor.depthBufferBits = 0;

            RenderingUtils.ReAllocateHandleIfNeeded(ref _tempTextureHandle, descriptor, name: "_MirrorTempTexture");
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (_settings.mirrorMaterial == null) return;

            _settings.mirrorMaterial.SetFloat("_LosslessMirrorMode", MirrorRendererFeature.LosslessMirrorMode ? 1.0f : 0.0f);

            CommandBuffer cmd = CommandBufferPool.Get("MirrorRendererFeature");
            RTHandle cameraColorTarget = renderingData.cameraData.renderer.cameraColorTargetHandle;

            bool isCapture = MirrorRendererFeature.RequestStageDebugCapture && !string.IsNullOrEmpty(MirrorRendererFeature.StageDebugCaptureDir);
            string captureDir = MirrorRendererFeature.StageDebugCaptureDir;

            if (isCapture)
            {
                RenderTextureDescriptor desc = cameraColorTarget.rt != null ? cameraColorTarget.rt.descriptor : renderingData.cameraData.cameraTargetDescriptor;
                desc.depthBufferBits = 0;
                RenderingUtils.ReAllocateHandleIfNeeded(ref _debugM0Handle, desc, name: "PCD_M0_Debug");
                RenderingUtils.ReAllocateHandleIfNeeded(ref _debugM1Handle, desc, name: "PCD_M1_Debug");
                RenderingUtils.ReAllocateHandleIfNeeded(ref _debugM2Handle, desc, name: "PCD_M2_Debug");

                cmd.Blit(cameraColorTarget, _debugM0Handle);
            }

            // 1. cameraColorTarget -> _tempTextureHandle (反転シェーダー適用)
            Blit(cmd, cameraColorTarget, _tempTextureHandle, _settings.mirrorMaterial, 0);

            if (isCapture)
            {
                cmd.Blit(_tempTextureHandle, _debugM1Handle);
            }

            // 2. _tempTextureHandle -> cameraColorTarget (画面へ書き戻し)
            Blit(cmd, _tempTextureHandle, cameraColorTarget);

            if (isCapture)
            {
                cmd.Blit(cameraColorTarget, _debugM2Handle);
                string camName = renderingData.cameraData.camera != null ? renderingData.cameraData.camera.name : "Camera";
                SaveDebugHandleToPNG(_debugM0Handle, System.IO.Path.Combine(captureDir, $"M0_{camName}.png"));
                SaveDebugHandleToPNG(_debugM1Handle, System.IO.Path.Combine(captureDir, $"M1_{camName}.png"));
                SaveDebugHandleToPNG(_debugM2Handle, System.IO.Path.Combine(captureDir, $"M2_{camName}.png"));
                MirrorRendererFeature.RequestStageDebugCapture = false;
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);

            // 構造化デバッグログの出力
            if ((AppLogger.IsEnabled(_owner, MirrorRendererFeature.TagMirrorPassDebug) || AppLogger.IsEnabled(MirrorRendererFeature.TagMirrorPassDebug)) && Time.frameCount % 60 == 0)
            {
                LogPassStatus(renderingData.cameraData.camera);
            }
        }

        private void LogPassStatus(Camera camera)
        {
            Vector3 camPos = camera.transform.position;
            Vector3 camForward = camera.transform.forward;
            Vector3 camRight = camera.transform.right;
            Vector3 camUp = camera.transform.up;

            // 正しい 鏡像 CameraToWorld 行列 (mirrorCameraToWorld, det = -1.0)
            Matrix4x4 mirrorCameraToWorld = Matrix4x4.identity;
            mirrorCameraToWorld.SetColumn(0, new Vector4(-camRight.x, camRight.y, camRight.z, 0));
            mirrorCameraToWorld.SetColumn(1, new Vector4(camUp.x, camUp.y, camUp.z, 0));
            mirrorCameraToWorld.SetColumn(2, new Vector4(-camForward.x, camForward.y, camForward.z, 0));
            mirrorCameraToWorld.SetColumn(3, new Vector4(-camPos.x, camPos.y, camPos.z, 1));

            Matrix4x4 mirrorView = mirrorCameraToWorld.inverse;
            Matrix4x4 mirrorVP = camera.projectionMatrix * mirrorView;

            // 画面中央近傍の動的ターゲット (カメラ前方0.5m)
            Vector3 targetWorld = _settings.useFixedTargetPos
                ? _settings.debugTargetWorldPos
                : camPos + camForward * 0.5f;

            // 鏡像カメラ空間でのターゲット座標
            Vector3 targetCamSpace = mirrorView.MultiplyPoint(targetWorld);

            // Clip / NDC 座標
            Vector4 clip = mirrorVP * new Vector4(targetWorld.x, targetWorld.y, targetWorld.z, 1.0f);
            Vector3 ndc = Mathf.Approximately(clip.w, 0f) ? Vector3.zero : new Vector3(clip.x / clip.w, clip.y / clip.w, clip.z / clip.w);

            // 1. 実カメラでのそのままの投影スクリーン座標
            Vector3 realScreen = camera.WorldToScreenPoint(targetWorld);

            // 2. 2D UV Flip (画面空間ミラー) による実際の表示ピクセル座標 (水平反転)
            Vector3 uvFlipScreen = new Vector3(camera.pixelWidth - realScreen.x, realScreen.y, realScreen.z);

            // 3. 真の 3D 鏡像 CameraToWorld (det=-1) から作られた View 行列での投影スクリーン座標
            Vector3 virtualMirrorScreen = ProjectWorldToScreen(targetWorld, mirrorVP, camera.pixelWidth, camera.pixelHeight);

            float pixelError = Mathf.Abs(uvFlipScreen.x - virtualMirrorScreen.x);

            string logMessage =
                $"[MirrorRenderFeature Debug - True Mirror Test]\n" +
                $"Camera: {camera.name}\n\n" +
                $"=== True Mirror Matrix Diagnostics ===\n" +
                $"CameraToWorld Det: {mirrorCameraToWorld.determinant:F2} (expected -1.0)\n" +
                $"Mirror View Det:    {mirrorView.determinant:F2}\n" +
                $"Target in Mirror Cam Space: {targetCamSpace:F2}\n" +
                $"Target Clip: {clip}, NDC: {ndc}\n\n" +
                $"=== Target World Point: {targetWorld:F2} ===\n" +
                $"Real Camera Screen:           ({realScreen.x:F1}, {realScreen.y:F1})\n" +
                $"True 3D Mirror Screen:        ({virtualMirrorScreen.x:F1}, {virtualMirrorScreen.y:F1})\n" +
                $"UV Flip Screen Result (2D):   ({uvFlipScreen.x:F1}, {uvFlipScreen.y:F1})\n\n" +
                $"Pixel Disparity Error (UV Flip vs True 3D Mirror): {pixelError:F1} px";

            AppLogger.Log(_owner, logMessage, MirrorRendererFeature.TagMirrorPassDebug);
        }

        private Vector3 ProjectWorldToScreen(Vector3 worldPoint, Matrix4x4 vpMatrix, float width, float height)
        {
            Vector4 clip = vpMatrix * new Vector4(worldPoint.x, worldPoint.y, worldPoint.z, 1.0f);
            if (Mathf.Approximately(clip.w, 0f)) return Vector3.zero;

            Vector3 ndc = new Vector3(clip.x / clip.w, clip.y / clip.w, clip.z / clip.w);
            float screenX = (ndc.x + 1.0f) * 0.5f * width;
            float screenY = (ndc.y + 1.0f) * 0.5f * height;

            return new Vector3(screenX, screenY, clip.w);
        }

        public void Dispose()
        {
            _tempTextureHandle?.Release();
            _debugM0Handle?.Release();
            _debugM1Handle?.Release();
            _debugM2Handle?.Release();
        }
    }
}

#pragma warning restore 0618, 0672
