// =============================================================================
// PCDRenderPass.cs — 点群オクルージョンパイプラインのオーケストレーター
// =============================================================================
//
// 【アーキテクチャ概要】
//
// このクラスは Unity の ScriptableRenderPass を継承し、点群（PointCloud）と仮想
// オブジェクトの間でリアルタイムオクルージョン判定を行うレンダリングパスです。
//
// 全てのロジックは独立したコンポーネントクラスに委譲されており、
// このクラスはオーケストレーション（RenderGraph への登録と制御フロー）のみを担当します。
//
// コンポーネント構成:
//   - PCDShaderConstants       … シェーダープロパティIDの定数（static）
//   - PCDKernelRegistry        … カーネルIDの初期化と保持
//   - PCDResourcePool          … RTHandle の一元管理（Alloc/Release）
//   - PCDPointBufferManager    … GPU バッファ管理（ComputeBuffer）
//   - PCDPipelineContext       … ステージ間データ共有（Blackboard）
//   - IPCDPipelineStage        … 各処理ステージのインターフェース
//   - PCDDebugReadbackManager  … デバッグ用 AsyncReadback パス
//
// パイプライン処理フロー:
//   1. RecordRenderGraph()     — RenderGraph にコンピュートパスと Blit パスを登録
//   2. ExecuteComputePass()    — GPU 上でオクルージョンパイプラインを実行
//   3. ExecuteBlitPass()       — 結果画像をカメラターゲットに転送
//
// =============================================================================
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Experimental.Rendering;
using Core.Logging;

public class PCDRenderPass : ScriptableRenderPass
{
    private const string PROFILER_TAG = "PCDRendering";

    // =========================================================================
    // コンピュートシェーダーと設定
    // =========================================================================
    private readonly ComputeShader _computeShader;
    private PCDRendererFeature.PCDRenderSettings _settings;

    // =========================================================================
    // コンポーネント
    // =========================================================================
    private readonly PCDKernelRegistry _kernels = new PCDKernelRegistry();
    private readonly PCDResourcePool _resources = new PCDResourcePool();
    internal PCDResourcePool Resources => _resources;
    private readonly PCDPointBufferManager _bufferManager = new PCDPointBufferManager();
    private readonly PCDDebugReadbackManager _debugManager = new PCDDebugReadbackManager();

    // =========================================================================
    // パイプラインステージ
    // =========================================================================
    private readonly IPCDPipelineStage[] _stages;

    // =========================================================================
    // 出力およびデバッグ用 RTHandle
    // =========================================================================
    private RTHandle _directGpuImageMapHandle;
    private RTHandle _directGpuImageLeftHandle;
    private RTHandle _directGpuImageRightHandle;

    // =========================================================================
    // 状態管理
    // =========================================================================
    private ComputeBuffer _staticMeshCounterBuffer;
    private PCDPointBufferManager.Point[] _virtualContactPointsArray;

    // =========================================================================
    // SRD Manager キャッシュ
    // =========================================================================
    private SRD.Core.SRDManager _cachedSrdManager;
    private float _lastSrdManagerSearchTime = -1000f;

    private SRD.Core.SRDManager GetSRDManager()
    {
        if (_cachedSrdManager != null)
            return _cachedSrdManager;

        if (Time.realtimeSinceStartup - _lastSrdManagerSearchTime > 2.0f)
        {
            _cachedSrdManager = UnityEngine.Object.FindAnyObjectByType<SRD.Core.SRDManager>();
            _lastSrdManagerSearchTime = Time.realtimeSinceStartup;
        }

        return _cachedSrdManager;
    }

    // =========================================================================
    // ビルダー（責務分割）
    // =========================================================================
    private readonly PCDContextBuilder _contextBuilder = new PCDContextBuilder();
    private readonly PCDComputePassBuilder _computePassBuilder = new PCDComputePassBuilder();
    private readonly PCDBlitPassBuilder _blitPassBuilder = new PCDBlitPassBuilder();

    // =========================================================================
    // コンストラクタ
    // =========================================================================

    public PCDRenderPass(ComputeShader computeShader, PCDRendererFeature.PCDRenderSettings settings)
    {
        _computeShader = computeShader;
        _settings = settings;

        _staticMeshCounterBuffer = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Default);
        _staticMeshCounterBuffer.SetData(new uint[] { 0 });

        _stages = new IPCDPipelineStage[]
        {
            new PCDPreProcessStage(),
            new PCDDepthPyramidStage(),
            new PCDOcclusionStage(),
            new PCDHoleFillStage(),
            new PCDPostProcessStage(),
        };
    }

    // =========================================================================
    // パブリック API
    // =========================================================================

    public void UpdateSettings(PCDRendererFeature.PCDRenderSettings settings)
    {
        _settings = settings;
    }

    public void SetDebugFlags(bool enablePixelTagMap, bool enableOcclusionMap)
    {
        _settings.enablePixelTagMap = enablePixelTagMap;
        _settings.enableOcclusionMap = enableOcclusionMap;
    }

    public void SetExternalBuffer(ComputeBuffer buffer, int count) => _bufferManager.SetExternalBuffer(buffer, count);
    public void SetPointCloudData(PCV_Data data) => _bufferManager.SetPointCloudData(data);
    public void MarkPointCloudDataDirty() => _bufferManager.SetDataDirty();

    public Texture GetDebugDisplayMap()
    {
        bool isDebug = _settings.enablePixelTagMap || _settings.enableOcclusionMap
            || (_settings.debugPatternId >= 0 && _settings.debugPatternId < 256)
            || (_settings.debugSectorId >= 0 && _settings.debugSectorId <= 9);
        if (isDebug && _resources.DebugDisplayMap != null)
            return _resources.DebugDisplayMap;
        return null;
    }

    public bool ShouldSkipRendering()
    {
        bool hasExternalData = _bufferManager.UseExternalBuffer && _bufferManager.ExternalPointBuffer != null && _bufferManager.ExternalPointBuffer.IsValid() && _bufferManager.ExternalPointCount > 0;
        bool hasInternalData = _bufferManager.PointBuffer != null && _bufferManager.PointBuffer.IsValid() && _bufferManager.PointCount > 0;
        return !hasExternalData && !hasInternalData;
    }

    // =========================================================================
    // RenderGraph 登録 (オーケストレーター)
    // =========================================================================

    private string GetMethodPrefix()
    {
        if (PCDRendererFeature.Instance == null) return "";
        bool isTag = PCDRendererFeature.Instance.settings.enableTagBasedOptimization;
        bool isDensity = PCDRendererFeature.Instance.settings.enableTypeAwareDensity;
        bool isFade = PCDRendererFeature.Instance.settings.enableSoftOcclusionFade;
        bool isHoleFill = PCDRendererFeature.Instance.settings.holeFillingMethod != PCDRendererFeature.PCD_HoleFillingMethod.None;

        if (isTag && isDensity && isFade && isHoleFill) return "Proposal";
        if (!isTag && !isDensity && !isFade && !isHoleFill) return "Traditional";
        return $"Ablation_T{(isTag ? "1" : "0")}_D{(isDensity ? "1" : "0")}_F{(isFade ? "1" : "0")}_H{(isHoleFill ? "1" : "0")}";
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        // 1. カーネルの初期化
        if (!_kernels.IsInitialized) _kernels.Initialize(_computeShader);
        if (!_kernels.IsInitialized) return;

        // 2. コンテキスト（描画前の行列・状態）の構築とスキップ判定
        var preData = _contextBuilder.BuildPreComputeData(frameData, _settings, _bufferManager);
        if (preData.ShouldSkip)
        {
            if (_settings.recordOcclusionDebugMap || _settings.recordPixelTagMap || _settings.recordIntegratedDepthMap)
            {
                AppLogger.LogWarning(PCD_LogTriggers.TagPipeline, "Skipped rendering due to no point cloud data or depth-only mode.");
                if (PCDRendererFeature.Instance != null && PCDRendererFeature.Instance.settings != null)
                {
                    PCDRendererFeature.Instance.settings.recordOcclusionDebugMap = false;
                    PCDRendererFeature.Instance.settings.recordPixelTagMap = false;
                    PCDRendererFeature.Instance.settings.recordIntegratedDepthMap = false;
                }
            }
            return;
        }

        // リソースのアロケーション
        _resources.EnsureAllocated(preData.ScreenWidth, preData.ScreenHeight, _settings.gridSize);

        // 3. コンピュートパスの登録
        var outHandles = _computePassBuilder.EnqueueComputePass(
            renderGraph, preData, _settings, _resources, _bufferManager, 
            _staticMeshCounterBuffer, _kernels, _computeShader, _stages);

        // 4. デバッグ読み戻しパスの登録
        _debugManager.EnqueueReadbackPasses(
            renderGraph, _settings, _resources,
            preData.ScreenWidth, preData.ScreenHeight,
            outHandles.occlusionValueMap,
            outHandles.integratedDepthMap,
            outHandles.neighborhoodMap,
            outHandles.neighborCountMap,
            GetMethodPrefix());

        // 5. 最終画像をカメラに描画する Blit パスの登録
        _blitPassBuilder.EnqueueBlitPass(renderGraph, preData.ResourceData, _settings, outHandles);
    }

    // =========================================================================
    // リソース解放
    // =========================================================================

    public void Cleanup()
    {
        _resources.Dispose();
        _bufferManager.Cleanup();

        _directGpuImageMapHandle?.Release();
        _directGpuImageMapHandle = null;
        _directGpuImageLeftHandle?.Release();
        _directGpuImageLeftHandle = null;
        _directGpuImageRightHandle?.Release();
        _directGpuImageRightHandle = null;
        _staticMeshCounterBuffer?.Release();
        _staticMeshCounterBuffer = null;
    }
}
