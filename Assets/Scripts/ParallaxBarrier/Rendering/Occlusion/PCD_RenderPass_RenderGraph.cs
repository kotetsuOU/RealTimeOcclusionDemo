using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Experimental.Rendering;

public partial class PCDRenderPass
{
    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        // 初期化が行われていない場合は初期化を実行
        if (!_isInitialized) Initialize();
        if (!_isInitialized) return;

        // 再生中のみ処理を実行（エディタの編集中はスキップ）
        if (!UnityEngine.Application.isPlaying) return;

        // グローバルバッファモードを使用するかどうかを判断
        bool shouldUseExternal = PCDRendererFeature.Instance.IsGlobalBufferMode;

        // 外部（グローバル）のポイントクラウドデータが存在する場合、バッファをセットする
        if (shouldUseExternal && RsGlobalPointCloudManager.Instance != null)
        {
            var globalBuffer = RsGlobalPointCloudManager.Instance.GetGlobalBuffer();
            var globalCount = RsGlobalPointCloudManager.Instance.CurrentTotalCount;
            _bufferManager.SetExternalBuffer(globalBuffer, globalCount);
        }
        else
        {
            _bufferManager.SetExternalBuffer(null, 0);
        }

        // バッファの更新処理を実行
        _bufferManager.Update();

        ComputeBuffer activeBuffer = null;
        int activeCount = 0;

        // 外部バッファが使用され、データが存在する場合の処理
        if (_bufferManager.UseExternalBuffer && _bufferManager.ExternalPointBuffer != null)
        {
            // Update: If count is 0, it should be treated as 0 instead of falling back to the whole buffer size (which may still hold old cached data on the GPU).
            int extCount = _bufferManager.ExternalPointCount >= 0 ? _bufferManager.ExternalPointCount : _bufferManager.ExternalPointBuffer.count;

            // 内部データも存在する場合は、両方を結合したバッファを使用する
            if (_bufferManager.PointCount > 0)
            {
                int totalCount = extCount + _bufferManager.PointCount;
                _bufferManager.EnsureCombinedBuffer(totalCount);
                activeBuffer = _bufferManager.CombinedBuffer;
                activeCount = totalCount;
            }
            else
            {
                // 外部データのみの場合はそのまま使用
                activeBuffer = _bufferManager.ExternalPointBuffer;
                activeCount = extCount;
            }
        }
        else
        {
            // 内部データのみを使用
            activeBuffer = _bufferManager.PointBuffer;
            activeCount = _bufferManager.PointCount;
        }

        // DepthMapメッシュやPointCloudメッシュ、点群データが存在するか確認
        bool hasDepthMapMeshes = _bufferManager.HasDepthMapMeshes();
        bool hasPointCloudMeshes = _bufferManager.HasPointCloudMeshes();
        bool pointCloudHasData = activeBuffer != null && activeCount > 0 && activeBuffer.IsValid();

        // デバッグ記録時のログ出力
        if (_settings.recordOcclusionDebugMap || _settings.recordPixelTagMap || _settings.recordIntegratedDepthMap)
        {
            UnityEngine.Debug.Log($"[PCDRenderPass] Record Debug. Occlusion={_settings.recordOcclusionDebugMap} PixelTag={_settings.recordPixelTagMap} IntegratedDepth={_settings.recordIntegratedDepthMap} DepthMap={hasDepthMapMeshes} PCMeshes={hasPointCloudMeshes} PointCloudData={pointCloudHasData} (Buffer={activeBuffer!=null}, Count={activeCount})");
        }

        // 点群データもメッシュも無い、背景深度の取得のみのモード
        bool depthMapOnlyMode = hasDepthMapMeshes && !hasPointCloudMeshes && !pointCloudHasData;

        if (depthMapOnlyMode)
        {
            // DepthMap取得のみであればフルレンダリングはスキップ
            if (_settings.recordOcclusionDebugMap || _settings.recordPixelTagMap || _settings.recordIntegratedDepthMap)
            {
                UnityEngine.Debug.LogWarning("[PCDRenderPass] Skipped rendering because depthMapOnlyMode is true.");
                if (PCDRendererFeature.Instance != null)
                {
                    PCDRendererFeature.Instance.recordOcclusionDebugMap = false;
                    PCDRendererFeature.Instance.recordPixelTagMap = false;
                    PCDRendererFeature.Instance.recordIntegratedDepthMap = false;
                }
            }
            return;
        }

        // 描画すべきデータが全く無ければスキップ
        if (!pointCloudHasData && !hasDepthMapMeshes)
        {
            if (_settings.recordOcclusionDebugMap || _settings.recordPixelTagMap || _settings.recordIntegratedDepthMap)
            {
                UnityEngine.Debug.LogWarning("[PCDRenderPass] Check box pressed but ignored. No point cloud and no depth map data.");
                if (PCDRendererFeature.Instance != null)
                {
                    PCDRendererFeature.Instance.recordOcclusionDebugMap = false;
                    PCDRendererFeature.Instance.recordPixelTagMap = false;
                    PCDRendererFeature.Instance.recordIntegratedDepthMap = false;
                }
            }
            return;
        }

        // カメラやリソース情報の取得
        var cameraData = frameData.Get<UniversalCameraData>();
        var resourceData = frameData.Get<UniversalResourceData>();
        Camera camera = cameraData.camera;
        int screenWidth = cameraData.cameraTargetDescriptor.width;
        int screenHeight = cameraData.cameraTargetDescriptor.height;

        // 16x16で分割されたグリッドマップの解像度を計算
        int gridWidth = Mathf.CeilToInt(screenWidth / 16.0f);
        int gridHeight = Mathf.CeilToInt(screenHeight / 16.0f);
        int l1_Width = 1, l1_Height = 1, l2_Width = 1, l2_Height = 1, l3_Width = 1, l3_Height = 1, l4_Width = 1, l4_Height = 1;

        // 勾配に応じた近傍補正を有効にしている場合、階層マップ(L1〜L4)の解像度を計算
        if (_settings.enableGradientCorrection)
        {
            l1_Width = Mathf.Max(1, Mathf.CeilToInt(screenWidth / 2.0f));
            l1_Height = Mathf.Max(1, Mathf.CeilToInt(screenHeight / 2.0f));
            l2_Width = Mathf.Max(1, Mathf.CeilToInt(l1_Width / 2.0f));
            l2_Height = Mathf.Max(1, Mathf.CeilToInt(l1_Height / 2.0f));
            l3_Width = Mathf.Max(1, Mathf.CeilToInt(l2_Width / 2.0f));
            l3_Height = Mathf.Max(1, Mathf.CeilToInt(l2_Height / 2.0f));
            l4_Width = Mathf.Max(1, Mathf.CeilToInt(l3_Width / 2.0f));
            l4_Height = Mathf.Max(1, Mathf.CeilToInt(l3_Height / 2.0f));
        }

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

        TextureHandle finalImageHandle;
        TextureHandle debugDisplayMapHandle_RG = default;
        TextureHandle occlusionValueMapHandle_RG = default;
        TextureHandle integratedDepthMapHandle_RG = default;
        TextureHandle neighborhoodMapHandle_RG = default;

        // コンピュートシェーダーを実行するパスをRenderGraphに追加
        using (var builder = renderGraph.AddComputePass<ComputePassData>(PROFILER_TAG, out var data))
        {
            // パスへ渡すパラメータ（シェーダーや各種データ）を登録
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

            // 仮想深度（バックグラウンドの深度）を使用する場合、カメラの深度テクスチャを登録
            if (data.hasVirtualDepth || depthMapOnlyMode)
            {
                data.virtualDepthTexture = resourceData.cameraDepthTexture;
            }
            else
            {
                // 使用しない場合のフォールバックテクスチャとしてのダミーを作成
                var virtualDepthFallbackDesc = new TextureDesc(1, 1)
                {
                    colorFormat = GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.RFloat, false)
                };
                data.virtualDepthTexture = renderGraph.CreateTexture(virtualDepthFallbackDesc);
            }
            builder.UseTexture(data.virtualDepthTexture, AccessFlags.Read);

            // カメラのカラーテクスチャを登録
            if (data.hasVirtualDepth && resourceData.activeColorTexture.IsValid())
            {
                data.cameraColorTexture = resourceData.activeColorTexture;
            }
            else
            {
                var cameraColorFallbackDesc = new TextureDesc(1, 1)
                {
                    colorFormat = GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.ARGBFloat, false)
                };
                data.cameraColorTexture = renderGraph.CreateTexture(cameraColorFallbackDesc);
            }
            builder.UseTexture(data.cameraColorTexture, AccessFlags.Read);

            // 中間処理で使用する各種バッファを生成（カラー、深度、座標情報など）
            var desc = new TextureDesc(screenWidth, screenHeight) { enableRandomWrite = true };
            desc.colorFormat = GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.ARGBFloat, false);
            data.colorMap = renderGraph.CreateTexture(desc);
            data.viewPositionMap = renderGraph.CreateTexture(desc);

            // 深度情報はRInt（整数型）として格納
            desc.colorFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R32_UInt;
            if (data.settings.recordIntegratedDepthMap)
            {
                integratedDepthMapHandle_RG = renderGraph.ImportTexture(_integratedDepthMapHandle);
                data.depthMap = integratedDepthMapHandle_RG;
            }
            else
            {
                data.depthMap = renderGraph.CreateTexture(desc);
            }
            desc.colorFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R32_SInt;
            if (data.settings.recordNeighborhoodMap)
            {
                neighborhoodMapHandle_RG = renderGraph.ImportTexture(_neighborhoodMapHandle);
                if (data.settings.enableGradientCorrection)
                {
                    data.correctedNeighborhoodSizeMap = neighborhoodMapHandle_RG;
                    data.neighborhoodSizeMap = renderGraph.CreateTexture(desc);
                }
                else
                {
                    data.neighborhoodSizeMap = neighborhoodMapHandle_RG;
                    data.correctedNeighborhoodSizeMap = renderGraph.CreateTexture(desc);
                }
            }
            else
            {
                data.neighborhoodSizeMap = renderGraph.CreateTexture(desc);
                data.correctedNeighborhoodSizeMap = renderGraph.CreateTexture(desc);
            }

            desc.colorFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R32_UInt;
            data.originTypeMap = renderGraph.CreateTexture(desc);

            // 密度とグリッドレベル用の縮小バッファを生成
            var gridDesc = new TextureDesc(gridWidth, gridHeight) { enableRandomWrite = true };
            gridDesc.colorFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R32_UInt;
            data.gridZMinMap = renderGraph.CreateTexture(gridDesc);
            gridDesc.colorFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R32_SInt;
            data.gridLevelMap = renderGraph.CreateTexture(gridDesc);
            data.filteredGridLevelMap = renderGraph.CreateTexture(gridDesc);
            gridDesc.colorFormat = GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.RFloat, false);
            data.densityMap = renderGraph.CreateTexture(gridDesc);

            if (data.settings.enableGradientCorrection)
            {
                var descL1 = new TextureDesc(l1_Width, l1_Height) { enableRandomWrite = true, colorFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R32_UInt };
                data.depthPyramidL1 = renderGraph.CreateTexture(descL1);
                var descL2 = new TextureDesc(l2_Width, l2_Height) { enableRandomWrite = true, colorFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R32_UInt };
                data.depthPyramidL2 = renderGraph.CreateTexture(descL2);
                var descL3 = new TextureDesc(l3_Width, l3_Height) { enableRandomWrite = true, colorFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R32_UInt };
                data.depthPyramidL3 = renderGraph.CreateTexture(descL3);
                var descL4 = new TextureDesc(l4_Width, l4_Height) { enableRandomWrite = true, colorFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R32_UInt };
                data.depthPyramidL4 = renderGraph.CreateTexture(descL4);
            }

            desc.colorFormat = GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.ARGBFloat, false);
            data.occlusionResultMap = renderGraph.CreateTexture(desc);
            
            if (data.settings.recordOcclusionDebugMap || data.settings.recordPixelTagMap)
            {
                occlusionValueMapHandle_RG = renderGraph.ImportTexture(_occlusionValueMapHandle);
                data.occlusionValueMap = occlusionValueMapHandle_RG;
            }
            else
            {
                desc.colorFormat = GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.RGFloat, false);
                data.occlusionValueMap = renderGraph.CreateTexture(desc);
            }
            
            desc.colorFormat = GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.ARGBFloat, false);
            data.finalImage = renderGraph.CreateTexture(desc);

            if (data.settings.enablePixelTagMap || data.settings.enableOcclusionMap)
            {
                debugDisplayMapHandle_RG = renderGraph.ImportTexture(_debugDisplayMapHandle);
                data.debugDisplayMap = debugDisplayMapHandle_RG;
            }
            else
            {
                data.debugDisplayMap = renderGraph.CreateTexture(desc);
            }

            // --- 変換および演算で読み書き(ReadWrite)するテクスチャを一括登録 ---
            builder.UseTexture(data.colorMap, AccessFlags.ReadWrite);
            builder.UseTexture(data.depthMap, AccessFlags.ReadWrite);
            builder.UseTexture(data.viewPositionMap, AccessFlags.ReadWrite);
            builder.UseTexture(data.gridZMinMap, AccessFlags.ReadWrite);
            builder.UseTexture(data.densityMap, AccessFlags.ReadWrite);
            builder.UseTexture(data.gridLevelMap, AccessFlags.ReadWrite);
            builder.UseTexture(data.filteredGridLevelMap, AccessFlags.ReadWrite);
            builder.UseTexture(data.neighborhoodSizeMap, AccessFlags.ReadWrite);
            if (data.settings.enableGradientCorrection)
            {
                builder.UseTexture(data.depthPyramidL1, AccessFlags.ReadWrite);
                builder.UseTexture(data.depthPyramidL2, AccessFlags.ReadWrite);
                builder.UseTexture(data.depthPyramidL3, AccessFlags.ReadWrite);
                builder.UseTexture(data.depthPyramidL4, AccessFlags.ReadWrite);
            }
            builder.UseTexture(data.correctedNeighborhoodSizeMap, AccessFlags.ReadWrite);
            builder.UseTexture(data.occlusionResultMap, AccessFlags.ReadWrite);
            builder.UseTexture(data.occlusionValueMap, AccessFlags.ReadWrite);
            builder.UseTexture(data.finalImage, AccessFlags.ReadWrite);
            builder.UseTexture(data.originTypeMap, AccessFlags.ReadWrite);
            builder.UseTexture(data.debugDisplayMap, AccessFlags.ReadWrite);

            finalImageHandle = data.finalImage;

            // アロケーションが終わったら、実際のComputeShader実行関数を登録
            builder.SetRenderFunc((ComputePassData passData, ComputeGraphContext context) =>
            {
                ExecuteComputePass(passData, context);
            });

            // デバッグデータを非同期読込する場合はカリングを無効化
            if (data.settings.recordOcclusionDebugMap || data.settings.recordPixelTagMap || data.settings.recordIntegratedDepthMap || data.settings.recordNeighborhoodMap)
            {
                builder.AllowPassCulling(false);
            }
        }

        // --- デバッグ用のオクルージョンマップを非同期出力するパス ---
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

                        string methodPrefix = "";
                        if (PCDRendererFeature.Instance != null)
                        {
                            bool isTag = PCDRendererFeature.Instance.enableTagBasedOptimization;
                            bool isDensity = PCDRendererFeature.Instance.enableTypeAwareDensity;
                            bool isFade = PCDRendererFeature.Instance.enableSoftOcclusionFade;
                            bool isHoleFill = PCDRendererFeature.Instance.enableJointBilateralHoleFilling;

                            if (isTag && isDensity && isFade && isHoleFill) methodPrefix = "Proposal";
                            else if (!isTag && !isDensity && !isFade && !isHoleFill) methodPrefix = "Traditional";
                            else methodPrefix = $"Ablation_T{(isTag?"1":"0")}_D{(isDensity?"1":"0")}_F{(isFade?"1":"0")}_H{(isHoleFill?"1":"0")}";
                        }

                        if (shouldExportPixelTagMap)
                        {
                            // PixelTagMap: 閾値判定後のアルファ値 (0か1かなど) で色付け＆CSV出力
                            PCDOcclusionDebugExporter.ExportOcclusionMap16PaletteFromData(fData, fData, w, h, "Assets/HandTrackingData/PixelTagMaps", "PixelTag_" + methodPrefix);
                        }

                        if (shouldExportOcclusionMap)
                        {
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

                        string methodPrefix = "";
                        if (PCDRendererFeature.Instance != null)
                        {
                            bool isTag = PCDRendererFeature.Instance.enableTagBasedOptimization;
                            bool isDensity = PCDRendererFeature.Instance.enableTypeAwareDensity;
                            bool isFade = PCDRendererFeature.Instance.enableSoftOcclusionFade;
                            bool isHoleFill = PCDRendererFeature.Instance.enableJointBilateralHoleFilling;

                            if (isTag && isDensity && isFade && isHoleFill) methodPrefix = "Proposal";
                            else if (!isTag && !isDensity && !isFade && !isHoleFill) methodPrefix = "Traditional";
                            else methodPrefix = $"Ablation_T{(isTag?"1":"0")}_D{(isDensity?"1":"0")}_F{(isFade?"1":"0")}_H{(isHoleFill?"1":"0")}";
                        }

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

                        string methodPrefix = "";
                        if (PCDRendererFeature.Instance != null)
                        {
                            bool isTag = PCDRendererFeature.Instance.enableTagBasedOptimization;
                            bool isDensity = PCDRendererFeature.Instance.enableTypeAwareDensity;
                            bool isFade = PCDRendererFeature.Instance.enableSoftOcclusionFade;
                            bool isHoleFill = PCDRendererFeature.Instance.enableJointBilateralHoleFilling;

                            if (isTag && isDensity && isFade && isHoleFill) methodPrefix = "Proposal";
                            else if (!isTag && !isDensity && !isFade && !isHoleFill) methodPrefix = "Traditional";
                            else methodPrefix = $"Ablation_T{(isTag?"1":"0")}_D{(isDensity?"1":"0")}_F{(isFade?"1":"0")}_H{(isHoleFill?"1":"0")}";
                        }

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

        // --- 生成された点群（またはデバッグマップ）を最終画面に描画する(Blit)パス ---
        using (var builder = renderGraph.AddRasterRenderPass<BlitPassData>("PCD Blit Pass", out var data))
        {
            data.blendMaterial = m_BlendMaterial;
            data.enableAlphaBlend = _enableAlphaBlend;
            data.cameraTarget = resourceData.activeColorTexture; // 出力先は現在のカラーテクスチャ
            data.enablePixelTagMap = _settings.enablePixelTagMap;
            data.enableOcclusionMap = _settings.enableOcclusionMap;

            // オリジンデバッグが有効ならそちらを描画元とし、無効なら最終画像をソースとする
            if (data.enablePixelTagMap || data.enableOcclusionMap)
            {
                data.sourceImage = debugDisplayMapHandle_RG;
                builder.UseTexture(data.sourceImage, AccessFlags.Read);
            }
            else
            {
                data.sourceImage = finalImageHandle;
                builder.UseTexture(data.sourceImage, AccessFlags.Read);
            }

            builder.SetRenderAttachment(data.cameraTarget, 0, AccessFlags.ReadWrite);
            // Blit処理関数を登録
            builder.SetRenderFunc((BlitPassData passData, RasterGraphContext context) =>
            {
                ExecuteBlitPass(passData, context);
            });
        }
    }
}
