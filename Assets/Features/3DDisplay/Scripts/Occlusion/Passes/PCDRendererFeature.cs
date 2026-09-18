using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class PCDRendererFeature : ScriptableRendererFeature
{
    private static PCDRendererFeature _instance;
    public static PCDRendererFeature Instance
    {
        get
        {
            if (_instance == null || !_instance)
            {
                _instance = null;
                var features = Resources.FindObjectsOfTypeAll<PCDRendererFeature>();
                if (features != null && features.Length > 0)
                {
                    foreach (var f in features)
                    {
                        if (f != null && f)
                        {
                            _instance = f;
                            break;
                        }
                    }
                }
            }
            return _instance;
        }
        internal set
        {
            _instance = value;
        }
    }

    public enum PCD_OcclusionKernel
    {
        Bouchiba = 0,
        Exponential = 1,
        Linear = 2,
        Skip = 3,
        DepthOnly = 4
    }

    public enum PCD_OcclusionEvaluationMode
    {
        Average = 0,
        SectorThreshold = 1,
        SectorConsecutiveZeros = 2
    }

    public enum PCD_HoleFillingMethod
    {
        None = 0,
        JointBilateral = 1,
        PullPush = 2,
        Morphology_OC = 3, // Opening-Closing
        Morphology_CO = 4  // Closing-Opening
    }

    public enum PCD_GridSize
    {
        Grid8x8 = 8,
        Grid16x16 = 16,
        Grid32x32 = 32
    }

    public enum PCD_CameraTargetMode
    {
        AllValidCameras = 0,    // CullingMask != 0 の全有効カメラ
        VirtualCamerasOnly = 1, // 名前が "Virtual" を含むカメラのみ
        CustomFilter = 2        // 指定キーワード (cameraNameFilter) を含むカメラのみ
    }

    [System.Serializable]
    public struct PCDRenderSettings
    {
        public PCD_CameraTargetMode cameraTargetMode;
        public string cameraNameFilter;

        public PCD_OcclusionKernel kernelType;
        public PCD_OcclusionEvaluationMode evaluationMode;
        [Range(1, 8)] public int minOccludedSectors;
        [Range(0, 8)] public int maxConsecutiveEmptySectors;
        [Range(0, 6)] public int minSearchLevel;

        public float exponentAlpha;
        public float densityThreshold_e;
        public float neighborhoodParam_p_prime;
        public bool enableDensityBasedLOD;
        public bool enableGradientCorrection;
        public float gradientThreshold_g_th;
        [Range(0f, 1f)] public float occlusionThreshold;
        [Range(0f, 1f)] public float occlusionFadeWidth;
        public bool enableVirtualContactOcclusion;
        public float virtualContactRadius;
        public float virtualContactSpacing;
        public Color virtualContactColor;
        public bool enablePixelTagMap;
        public bool enableOcclusionMap;
        public bool enableBufferManagerLog;
        public bool recordOcclusionDebugMap;
        public bool recordPixelTagMap;
        public bool recordIntegratedDepthMap;
        public bool recordNeighborhoodMap;
        public bool recordNeighborCountMap;

        /// <summary>
        /// 256占有パターンの単独二値マスク表示 (-1: 通常描画, 0..255: 指定パターンの二値マスク表示)
        /// </summary>
        [Range(-1, 255)]
        public int debugPatternId;

        /// <summary>
        /// 8セクターの単独二値マスク表示 (-1: 通常描画, 0..7: 指定セクターの二値マスク表示)
        /// </summary>
        [Range(-1, 7)]
        public int debugSectorId;

        public bool enableVirtualDepthIntegration;

        public bool enableTagBasedOptimization;   // ① タグに基づく探索スキップ
        public bool enableTypeAwareDensity;       // ② 仮想物体を区別した密度計算
        public bool enableSoftOcclusionFade;      // ③ ソフトオクルージョン (FadeWidth)
        public PCD_HoleFillingMethod holeFillingMethod; // ④ エッジ保持型ホールフィリング手法

        public PCD_GridSize gridSize;             // グリッドサイズ (最適化・検証用)

        [Range(1, 25)] public int morphKernelHalfSize;
        [Range(0, 5)] public int morphErodeIterations;
        [Range(1, 5)] public int morphDilateIterations;

        [HideInInspector] public uint _dynamicMultiplierRuntimeValue;
    }

    [Header("Required Assets")]
    public ComputeShader pointCloudCompute;

    public PCDSettingsBridge settings { get; private set; }

    private PCDRenderPass _scriptablePass;
    internal PCDResourcePool CurrentResources => _scriptablePass != null ? _scriptablePass.Resources : null;

    private bool _useGlobalBufferMode = false;
    public bool IsGlobalBufferMode => _useGlobalBufferMode;

    public void SetUseGlobalBuffer(bool enable)
    {
        _useGlobalBufferMode = enable;
    }

    // Inspectorで設定されている値を構造体として取得する
    private PCDRenderSettings GetSettings()
    {
        if (settings == null)
        {
            settings = new PCDSettingsBridge();
        }
        return settings.GetSettings(_internalDynamicMultiplier);
    }

    [HideInInspector] public uint _internalDynamicMultiplier = 1;
    public uint LastFrameVirtualMeshPixelCount { get; set; } = 1;

    // レンダラー特徴の初期化時に呼ばれる
    public override void Create()
    {
        Instance = this;
        _useGlobalBufferMode = false;

        if (settings == null)
        {
            settings = new PCDSettingsBridge();
        }

        _scriptablePass?.Cleanup();

        // レンダリングパスのインスタンスを生成し、実行タイミングを設定
        _scriptablePass = new PCDRenderPass(this.pointCloudCompute, GetSettings());
        _scriptablePass.renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
    }

    // 動的オブジェクト用にデータ再構築をリクエストする
    public void MarkPointCloudDataDirty()
    {
        _scriptablePass?.MarkPointCloudDataDirty();
    }

    // RenderGraph パス追加
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        Instance = this;

        // 1. Sceneビューなど解像度の異なるカメラが混ざることで、RTHandleが毎フレーム破棄・再構築されるのを防ぐため、Game/VRのみ許可する
        if (renderingData.cameraData.cameraType != CameraType.Game && renderingData.cameraData.cameraType != CameraType.VR)
        {
            return;
        }

        var cam = renderingData.cameraData.camera;

        // 2. CullingMask チェック: 描画対象レイヤーが0のカメラ（SRD実カメラ等）は即座にスキップ
        if (cam == null || cam.cullingMask == 0)
        {
            return;
        }

        // 3. カメラターゲットモードに基づくフィルタリング
        var currentSettings = GetSettings();
        if (currentSettings.cameraTargetMode == PCD_CameraTargetMode.VirtualCamerasOnly)
        {
            if (string.IsNullOrEmpty(cam.name) || !cam.name.ToLowerInvariant().Contains("virtual"))
            {
                return;
            }
        }
        else if (currentSettings.cameraTargetMode == PCD_CameraTargetMode.CustomFilter)
        {
            if (!string.IsNullOrEmpty(currentSettings.cameraNameFilter) &&
                (string.IsNullOrEmpty(cam.name) || !cam.name.ToLowerInvariant().Contains(currentSettings.cameraNameFilter.ToLowerInvariant())))
            {
                return;
            }
        }

        if (pointCloudCompute == null)
        {
            return;
        }

        if (_scriptablePass != null)
        {
            // Inspectorでの変更をパスに反映
            _scriptablePass.UpdateSettings(currentSettings);
            if (settings == null)
            {
                settings = new PCDSettingsBridge();
            }
            _scriptablePass.SetDebugFlags(settings.enablePixelTagMap, settings.enableOcclusionMap);
        }

        renderer.EnqueuePass(_scriptablePass);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _scriptablePass?.Cleanup();
        _useGlobalBufferMode = false;

        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void SetPointCloudData(PCV_Data data)
    {
        if (_scriptablePass != null)
        {
            _scriptablePass.SetPointCloudData(data);
        }
    }

    public void SetExternalBuffer(ComputeBuffer buffer, int count)
    {
        if (_scriptablePass != null)
        {
            _scriptablePass.SetExternalBuffer(buffer, count);
        }
    }

    public Texture GetDebugDisplayMap() => _scriptablePass?.GetDebugDisplayMap();

    // ==========================================
    // インスペクターの値が変更された時に自動で呼ばれる検証関数
    // ==========================================
    private void OnValidate()
    {
        if (settings == null)
        {
            settings = new PCDSettingsBridge();
        }
        settings.OnValidate();
    }
}
