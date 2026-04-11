using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Experimental.Rendering;

public partial class PCDRenderPass : ScriptableRenderPass
{
    private const string PROFILER_TAG = "PCDRendering";

    // フレームごとの文字列ルックアップを避けるためにシェーダープロパティIDをキャッシュ
    private static class ShaderIDs
    {
        public static readonly int PointCount = Shader.PropertyToID("_PointCount");
        public static readonly int ScreenParams = Shader.PropertyToID("_ScreenParams");
        public static readonly int ViewMatrix = Shader.PropertyToID("_ViewMatrix");
        public static readonly int ProjectionMatrix = Shader.PropertyToID("_ProjectionMatrix");
        public static readonly int InverseProjectionMatrix = Shader.PropertyToID("_InverseProjectionMatrix");
        public static readonly int DensityThreshold_e = Shader.PropertyToID("_DensityThreshold_e");
        public static readonly int NeighborhoodParam_p_prime = Shader.PropertyToID("_NeighborhoodParam_p_prime");
        public static readonly int GradientThreshold_g_th = Shader.PropertyToID("_GradientThreshold_g_th");
        public static readonly int OcclusionThreshold = Shader.PropertyToID("_OcclusionThreshold");
        public static readonly int OcclusionFadeWidth = Shader.PropertyToID("_OcclusionFadeWidth");

        public static readonly int ColorMap = Shader.PropertyToID("_ColorMap");
        public static readonly int DepthMap = Shader.PropertyToID("_DepthMap");
        public static readonly int ColorMap_RW = Shader.PropertyToID("_ColorMap_RW");
        public static readonly int DepthMap_RW = Shader.PropertyToID("_DepthMap_RW");
        public static readonly int ViewPositionMap = Shader.PropertyToID("_ViewPositionMap");
        public static readonly int ViewPositionMap_RW = Shader.PropertyToID("_ViewPositionMap_RW");
        public static readonly int GridZMinMap = Shader.PropertyToID("_GridZMinMap");
        public static readonly int GridZMinMap_RW = Shader.PropertyToID("_GridZMinMap_RW");
        public static readonly int DensityMap = Shader.PropertyToID("_DensityMap");
        public static readonly int DensityMap_RW = Shader.PropertyToID("_DensityMap_RW");
        public static readonly int GridLevelMap = Shader.PropertyToID("_GridLevelMap");
        public static readonly int GridLevelMap_RW = Shader.PropertyToID("_GridLevelMap_RW");
        public static readonly int FilteredGridLevelMap = Shader.PropertyToID("_FilteredGridLevelMap");
        public static readonly int FilteredGridLevelMap_RW = Shader.PropertyToID("_FilteredGridLevelMap_RW");
        public static readonly int NeighborhoodSizeMap = Shader.PropertyToID("_NeighborhoodSizeMap");
        public static readonly int NeighborhoodSizeMap_RW = Shader.PropertyToID("_NeighborhoodSizeMap_RW");
        
        public static readonly int DepthPyramidL1 = Shader.PropertyToID("_DepthPyramidL1");
        public static readonly int DepthPyramidL1_RW = Shader.PropertyToID("_DepthPyramidL1_RW");
        public static readonly int DepthPyramidL2 = Shader.PropertyToID("_DepthPyramidL2");
        public static readonly int DepthPyramidL2_RW = Shader.PropertyToID("_DepthPyramidL2_RW");
        public static readonly int DepthPyramidL3 = Shader.PropertyToID("_DepthPyramidL3");
        public static readonly int DepthPyramidL3_RW = Shader.PropertyToID("_DepthPyramidL3_RW");
        public static readonly int DepthPyramidL4 = Shader.PropertyToID("_DepthPyramidL4");
        public static readonly int DepthPyramidL4_RW = Shader.PropertyToID("_DepthPyramidL4_RW");
        public static readonly int CorrectedNeighborhoodSizeMap_RW = Shader.PropertyToID("_CorrectedNeighborhoodSizeMap_RW");
        public static readonly int FinalNeighborhoodSizeMap = Shader.PropertyToID("_FinalNeighborhoodSizeMap");
        
        public static readonly int OcclusionResultMap = Shader.PropertyToID("_OcclusionResultMap");
        public static readonly int OcclusionResultMap_RW = Shader.PropertyToID("_OcclusionResultMap_RW");
        public static readonly int FinalImage_RW = Shader.PropertyToID("_FinalImage_RW");
        
        public static readonly int OriginTypeMap = Shader.PropertyToID("_OriginTypeMap");
        public static readonly int OriginTypeMap_RW = Shader.PropertyToID("_OriginTypeMap_RW");
        public static readonly int OriginMap_RW = Shader.PropertyToID("_OriginMap_RW");

        public static readonly int OcclusionValueMap_RW = Shader.PropertyToID("_OcclusionValueMap_RW");
        public static readonly int RecordOcclusionDebug = Shader.PropertyToID("_RecordOcclusionDebug");

        public static readonly int MergeSrcBuffer = Shader.PropertyToID("_MergeSrcBuffer");
        public static readonly int MergeDstBuffer = Shader.PropertyToID("_MergeDstBuffer");
        public static readonly int MergeSrcOffset = Shader.PropertyToID("_MergeSrcOffset");
        public static readonly int MergeDstOffset = Shader.PropertyToID("_MergeDstOffset");
        public static readonly int MergeCopyCount = Shader.PropertyToID("_MergeCopyCount");
        public static readonly int PointBuffer = Shader.PropertyToID("_PointBuffer");
        public static readonly int StaticMeshCounter_RW = Shader.PropertyToID("_StaticMeshCounter_RW");

        public static readonly int UseVirtualDepth = Shader.PropertyToID("_UseVirtualDepth");
        public static readonly int VirtualDepthMap = Shader.PropertyToID("_VirtualDepthMap");
        public static readonly int CameraColorTexture = Shader.PropertyToID("_CameraColorTexture");
    }

    private ComputeShader pointCloudCompute; // オクルージョンパイプラインを定義するコアコンピュートシェーダー
    private Material m_BlendMaterial;        // 結果として得られた画像を画面上でブレンドするために使用されるマテリアル
    private bool _enableAlphaBlend;          // 最終的な点群の結果をアルファブレンドするかどうか
    private PCDRendererFeature.PCDRenderSettings _settings; // 機能インスペクターの値に対応する現在の設定

    // 個々のコンピュートシェーダー関数に対応するカーネルID
    private int _kernelClear, _kernelClearCounter, _kernelProject, _kernelCalcGridZMin, _kernelCalcDensity,
                _kernelCalcGridLevel, _kernelGridMedianFilter,
                _kernelCalcNeighborhoodSize,
                _kernelBuildDepthPyramidL1, _kernelBuildDepthPyramidL2,
                _kernelBuildDepthPyramidL3, _kernelBuildDepthPyramidL4,
                _kernelApplyGradient,
                _kernelComputeOcclusion, _kernelFillHoles, _kernelInterpolate,
                _kernelMerge, _kernelInitFromCamera;

    // 出力およびデバッグマップ
    private RTHandle _originDebugMapHandle;
    private RTHandle _occlusionValueMapHandle;
    private bool _isInitialized = false;
    private const int STRIDE = 28; // 1つのポイントデータのサイズを表す: sizeof(float)*3 + sizeof(float)*3 + sizeof(uint)

    // --- バッファ マネージャー ---
    private PCDPointBufferManager _bufferManager;

    private ComputeBuffer _staticMeshCounterBuffer;

    public PCDRenderPass(ComputeShader computeShader, PCDRendererFeature.PCDRenderSettings settings, Material blendMaterial, bool enableAlphaBlend)
    {
        this.pointCloudCompute = computeShader;
        this._settings = settings;

        this.m_BlendMaterial = blendMaterial;
        this._enableAlphaBlend = enableAlphaBlend;

        _bufferManager = new PCDPointBufferManager(); // 静的メッシュや点群のためのデータマネージャーを初期化します

        _staticMeshCounterBuffer = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Default);
        _staticMeshCounterBuffer.SetData(new uint[] { 0 });
    }

    /// <summary> 外部（スクリプトやインスペクターの変更など）からレンダラーの設定を更新します。 </summary>
    public void UpdateSettings(PCDRendererFeature.PCDRenderSettings settings)
    {
        this._settings = settings;
    }

    /// <summary> オリジンデバッグマップのレンダリングを切り替えます。 </summary>
    public void SetDebugFlag(bool enableDebugMap)
    {
        this._settings.enableOriginDebugMap = enableDebugMap;
    }

    /// <summary> 外部のコンピュートバッファを直接注入できるようにします。 </summary>
    public void SetExternalBuffer(ComputeBuffer buffer, int count)
    {
        _bufferManager.SetExternalBuffer(buffer, count);
    }

    /// <summary> 内部のPCV_Dataオブジェクトから点群データを設定します。 </summary>
    public void SetPointCloudData(PCV_Data data)
    {
        _bufferManager.SetPointCloudData(data);
    }

    /// <summary> 点群のオクルージョンと相互作用するように静的なUnityメッシュを登録します。 </summary>
    public void AddStaticMesh(Mesh mesh, Transform transform, PCDProcessingMode mode)
    {
        _bufferManager.AddStaticMesh(mesh, transform, mode);
    }

    /// <summary> バッファの更新を強制するために、点群データをダーティとしてマークします。 </summary>
    public void MarkPointCloudDataDirty()
    {
        _bufferManager.SetDataDirty();
    }

    /// <summary> トラックされている静的なUnityメッシュの登録を解除します。 </summary>
    public void RemoveStaticMesh(Mesh mesh, Transform transform)
    {
        _bufferManager.RemoveStaticMesh(mesh, transform);
    }

    /// <summary> コンピュートシェーダーの設定からカーネルのインデックスIDを取得します。 </summary>
    private void Initialize()
    {
        if (pointCloudCompute == null)
        {
            UnityEngine.Debug.LogError("Compute Shader is null. Initialization failed.");
            _isInitialized = false;
            return;
        }

        _kernelClear = pointCloudCompute.FindKernel("ClearMaps");
        _kernelClearCounter = pointCloudCompute.FindKernel("ClearCounter");
        _kernelProject = pointCloudCompute.FindKernel("ProjectPoints");
        _kernelCalcGridZMin = pointCloudCompute.FindKernel("CalculateGridZMin");
        _kernelCalcDensity = pointCloudCompute.FindKernel("CalculateDensity");
        _kernelCalcGridLevel = pointCloudCompute.FindKernel("CalculateGridLevel");
        _kernelGridMedianFilter = pointCloudCompute.FindKernel("GridMedianFilter");
        _kernelCalcNeighborhoodSize = pointCloudCompute.FindKernel("CalculateNeighborhoodSize");

        if (_settings.enableGradientCorrection)
        {
            _kernelBuildDepthPyramidL1 = pointCloudCompute.FindKernel("BuildDepthPyramidL1");
            _kernelBuildDepthPyramidL2 = pointCloudCompute.FindKernel("BuildDepthPyramidL2");
            _kernelBuildDepthPyramidL3 = pointCloudCompute.FindKernel("BuildDepthPyramidL3");
            _kernelBuildDepthPyramidL4 = pointCloudCompute.FindKernel("BuildDepthPyramidL4");
            _kernelApplyGradient = pointCloudCompute.FindKernel("ApplyAdaptiveGradientCorrection");
        }

        _kernelComputeOcclusion = pointCloudCompute.FindKernel("ComputeOcclusion");
        _kernelFillHoles = pointCloudCompute.FindKernel("FillHoles");
        _kernelInterpolate = pointCloudCompute.FindKernel("Interpolate");
        _kernelMerge = pointCloudCompute.FindKernel("MergeBuffer");
        _kernelInitFromCamera = pointCloudCompute.FindKernel("InitFromCamera");

        _isInitialized = true;
    }

    private class ComputePassData
    {
        internal ComputeShader computeShader;
        internal int pointCount;
        internal Vector4 screenParams;
        internal Matrix4x4 viewMatrix;
        internal Matrix4x4 projectionMatrix;
        
        internal PCDRendererFeature.PCDRenderSettings settings;

        internal int kernelClear, kernelClearCounter, kernelProject, kernelCalcGridZMin, kernelCalcDensity,
                     kernelCalcGridLevel, kernelGridMedianFilter,
                     kernelCalcNeighborhoodSize,
                     kernelBuildDepthPyramidL1, kernelBuildDepthPyramidL2,
                     kernelBuildDepthPyramidL3, kernelBuildDepthPyramidL4,
                     kernelApplyGradient,
                     kernelComputeOcclusion, kernelFillHoles, kernelInterpolate,
                     kernelMerge, kernelInitFromCamera;

        // コピー用バッファ
        internal bool useExternal;
        internal ComputeBuffer externalBuffer;
        internal ComputeBuffer internalBuffer;
        internal int externalCount;
        internal int internalCount;
        internal ComputeBuffer combinedBuffer; // ターゲットバッファ
        internal ComputeBuffer pointBuffer;
        internal ComputeBuffer staticMeshCounterBuffer;

        internal TextureHandle colorMap;
        internal TextureHandle depthMap;
        internal TextureHandle virtualDepthTexture;
        internal TextureHandle cameraColorTexture;
        internal bool hasVirtualDepth;
        internal bool depthMapOnlyMode;
        internal Matrix4x4 inverseProjectionMatrix;
        internal TextureHandle viewPositionMap;
        internal TextureHandle gridZMinMap;
        internal TextureHandle densityMap;
        internal TextureHandle gridLevelMap;
        internal TextureHandle filteredGridLevelMap;
        internal TextureHandle neighborhoodSizeMap;
        internal TextureHandle depthPyramidL1;
        internal TextureHandle depthPyramidL2;
        internal TextureHandle depthPyramidL3;
        internal TextureHandle depthPyramidL4;
        internal TextureHandle correctedNeighborhoodSizeMap;
        internal TextureHandle occlusionResultMap;
        internal TextureHandle occlusionValueMap;
        internal TextureHandle finalImage;
        internal TextureHandle originTypeMap;
        internal TextureHandle originDebugMap;
    }

    private class BlitPassData
    {
        internal Material blendMaterial;
        internal TextureHandle sourceImage;
        internal TextureHandle cameraTarget;
        internal bool enableAlphaBlend;
        internal bool enableOriginDebugMap;
    }

    /// <summary> オリジンデバッグマップが生成されている場合はそれを返し、そうでない場合はnullを返します。 </summary>
    public Texture GetOriginDebugMap()
    {
        if (_settings.enableOriginDebugMap && _originDebugMapHandle != null)
        {
            return _originDebugMapHandle;
        }
        return null;
    }

    /// <summary> このフレームでオクルージョンパスのパイプラインをスキップするかどうかを決定します。 </summary>
    public bool ShouldSkipRendering()
    {
        // 外部バッファの確認
        bool hasExternalData = _bufferManager.UseExternalBuffer && _bufferManager.ExternalPointBuffer != null && _bufferManager.ExternalPointBuffer.IsValid() && _bufferManager.ExternalPointCount > 0;

        // 内部バッファの確認
        bool hasInternalData = _bufferManager.PointBuffer != null && _bufferManager.PointBuffer.IsValid() && _bufferManager.PointCount > 0;

        // DepthMapモードのメッシュがあるか確認
        bool hasDepthMapMeshes = _bufferManager.HasDepthMapMeshes();

        // PointCloudモードのメッシュがあるか確認
        bool hasPointCloudMeshes = _bufferManager.HasPointCloudMeshes();

        // 点群データがなく、注入するメッシュもない場合（または背景の深度のみを生成する場合）、レンダリングをスキップします。
        bool noPointCloudData = !hasExternalData && !hasInternalData && !hasPointCloudMeshes;
        bool depthMapOnlyMode = hasDepthMapMeshes && noPointCloudData;

        return depthMapOnlyMode;
    }

    /// <summary> メモリリークを防ぐために、リソースと参照を適切に解放します。 </summary>
    public void Cleanup()
    {
        _bufferManager.Cleanup();

        _originDebugMapHandle?.Release();
        _originDebugMapHandle = null;

        _occlusionValueMapHandle?.Release();
        _occlusionValueMapHandle = null;

        _staticMeshCounterBuffer?.Release();
        _staticMeshCounterBuffer = null;

        _isInitialized = false;
    }
}