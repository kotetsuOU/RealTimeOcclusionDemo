using System;
using System.Collections.Generic;
using Core.Logging;
using Intel.RealSense;
using UnityEngine;

namespace RealSense.DummyPointCloud
{
    /// <summary>
    /// Unityの3D Object (MeshFilter / SkinnedMeshRenderer) のリストから
    /// 物理密度や色を指定してダミーの実測点群を生成し、
    /// RsProcessingPipe / RsDummyProcessingPipe の Source (RsFrameProvider) として供給するコンポーネント。
    /// 内部の責務は ChangeTracker, MaterialColorPresenter, SamplingProcessor, StreamingBridge に分離委譲されています。
    /// </summary>
    [AppLoggable("DPC (Dummy Point Cloud)")]
    [DisallowMultipleComponent]
    public class RsDummyPointCloudProvider : RsFrameProvider, IAppLoggable
    {
        [Header("Target 3D Objects Settings")]
        [Tooltip("実測点群の生成元となる Unity 3D オブジェクトのリスト")]
        public List<GameObject> targetObjects = new List<GameObject>();

        [Tooltip("各オブジェクトの子要素にある全ての Mesh/SkinnedMeshRenderer を含めて点群化するかどうか")]
        public bool includeChildren = true;

        [Header("Physical Point Density & Color")]
        [Tooltip("点群の物理密度の指定単位")]
        public PointDensityUnit densityUnit = PointDensityUnit.PointsPerCm2;

        [Tooltip("密度の数値（1cm^2あたりの点数、または点間隔mmなど）")]
        [Range(0.001f, 1000f)]
        public float densityValue = 1.0f;

        [Tooltip("サンプリング点数の最大上限（過剰な重さを防止）")]
        [Range(1000, 500000)]
        public int maxPointLimit = 100000;

        [Tooltip("点群のカラー指定モード")]
        public PointColorMode colorMode = PointColorMode.SolidColor;

        [Tooltip("SolidColor モード時の点群およびマテリアルの色")]
        public Color solidColor = new Color(241f / 255f, 187f / 255f, 147f / 255f, 1f);

        [Tooltip("SolidColor 変更時にターゲットオブジェクトのマテリアルカラーおよび RsPointCloudRenderer の描画色も連動して変更するかどうか")]
        public bool applyColorToMaterialAndRenderer = true;

        [Header("Noise & Outliers Settings")]
        [Tooltip("法線方向ノイズおよび外れ値の設定")]
        public RsPointCloudNoiseSettings noiseSettings = new RsPointCloudNoiseSettings();

        [Header("Camera Perspective Settings")]
        [Tooltip("true: カメラ視点・画角・オクルージョンを適用 / false: カメラの向き問わず全方向の全点群を出力")]
        public bool useCameraPerspective = true;

        [Tooltip("ダミー視点となる仮想 RealSense カメラの位置・姿勢（指定しない場合は本オブジェクトの Transform）")]
        public Transform simulatedCameraTransform;

        [Tooltip("視覚的な仮解像度 (Width)")]
        public int depthWidth = 640;

        [Tooltip("視覚的な仮解像度 (Height)")]
        public int depthHeight = 480;

        [Tooltip("更新フレームレート (FPS)")]
        [Range(1, 60)]
        public int updateFPS = 30;

        public override event Action<PipelineProfile> OnStart;
        public override event Action OnStop;
        public override event Action<Frame> OnNewSample;

        // 分割された責務クラス群 (MonoBehaviour コンストラクタ実行時エラー防止のため遅延初期化)
        private RsDummyChangeTracker _changeTracker;
        private RsDummyMaterialColorPresenter _materialColorPresenter;
        private RsDummySamplingProcessor _samplingProcessor;
        private RsDummyStreamingBridge _streamingBridge;

        private RsDummyChangeTracker ChangeTracker => _changeTracker ?? (_changeTracker = new RsDummyChangeTracker());
        private RsDummyMaterialColorPresenter MaterialColorPresenter => _materialColorPresenter ?? (_materialColorPresenter = new RsDummyMaterialColorPresenter());
        private RsDummySamplingProcessor SamplingProcessor => _samplingProcessor ?? (_samplingProcessor = new RsDummySamplingProcessor());
        private RsDummyStreamingBridge StreamingBridge => _streamingBridge ?? (_streamingBridge = new RsDummyStreamingBridge());

        public SampledPointCloudData LastSampledData { get; private set; }
        
        /// <summary>
        /// データが実際にサンプリング更新された回数（レンダラー側が「動いたら更新」を判断するために使用）
        /// </summary>
        public int DataVersion { get; private set; } = 0;

        public void RegisterLogTriggers(LogCategoryGroup group, HashSet<string> existingLabels)
        {
            var triggers = GetComponent<DPC_LogTriggers>() ?? gameObject.AddComponent<DPC_LogTriggers>();
            triggers.RegisterLogTriggers(group, existingLabels);
        }

        private void Awake()
        {
            if (simulatedCameraTransform == null)
            {
                simulatedCameraTransform = transform;
            }
        }

        private void OnEnable()
        {
            StartStreaming();
            UpdateMaterialAndRendererColors();
        }

        private void OnDisable()
        {
            StopStreaming();
        }

        private void OnDestroy()
        {
            StopStreaming();
            _samplingProcessor?.Dispose();
            _streamingBridge?.Dispose();
            _samplingProcessor = null;
            _streamingBridge = null;
        }

        private void OnValidate()
        {
            SetDirty();
            UpdateMaterialAndRendererColors();
        }

        /// <summary>
        /// 外部からダーティフラグを立て、次フレームの Update で強制的に再計算を行わせます。
        /// </summary>
        public void SetDirty()
        {
            ChangeTracker.SetDirty();
            SamplingProcessor.InvalidateCache();
        }

        /// <summary>
        /// 単一のターゲットオブジェクトを設定し、必要に応じて即時サンプリング更新します。
        /// </summary>
        public void SetTargetObject(GameObject target, bool forceUpdateNow = true)
        {
            if (targetObjects == null) targetObjects = new List<GameObject>();
            targetObjects.Clear();
            if (target != null)
            {
                targetObjects.Add(target);
            }
            SetDirty();
            if (forceUpdateNow)
            {
                ForceUpdateSampling();
            }
        }

        /// <summary>
        /// 複数のターゲットオブジェクトを設定し、必要に応じて即時サンプリング更新します。
        /// </summary>
        public void SetTargetObjects(IEnumerable<GameObject> targets, bool forceUpdateNow = true)
        {
            if (targetObjects == null) targetObjects = new List<GameObject>();
            targetObjects.Clear();
            if (targets != null)
            {
                foreach (var t in targets)
                {
                    if (t != null) targetObjects.Add(t);
                }
            }
            SetDirty();
            if (forceUpdateNow)
            {
                ForceUpdateSampling();
            }
        }

        private void Update()
        {
            if (!Streaming) return;

            if (ChangeTracker.CheckShouldUpdate(this, noiseSettings, out bool isDynamicNoise, out bool requiresCacheInvalidation))
            {
                if (requiresCacheInvalidation)
                {
                    SamplingProcessor.InvalidateCache();
                }

                int prevVersion = DataVersion;
                ExecuteSampling(isDynamicNoise);
                ChangeTracker.UpdateSnapshots(this, noiseSettings);

                if (DataVersion != prevVersion)
                {
                    AppLogger.Log(DPC_LogTriggers.TagProvider,
                        $"[Update] 点群データを更新しました (DataVersion: {DataVersion}, {LastSampledData.PointCount} 点)", this);
                }
            }
        }

        public void StartStreaming()
        {
            if (Streaming) return;

            AppLogger.Log(DPC_LogTriggers.TagProvider, "Starting dummy point cloud streaming...", this);

            ActiveProfile = StreamingBridge.StartStreaming(depthWidth, depthHeight, updateFPS, HandleNewFrame);
            Streaming = true;

            OnStart?.Invoke(ActiveProfile);
            SetDirty();

            AppLogger.Log(DPC_LogTriggers.TagProvider, $"Streaming started successfully. (CameraPerspective: {useCameraPerspective}, FPS: {updateFPS})", this);
        }

        public void StopStreaming()
        {
            if (!Streaming) return;

            AppLogger.Log(DPC_LogTriggers.TagProvider, "Stopping dummy point cloud streaming...", this);

            _streamingBridge?.StopStreaming(HandleNewFrame);
            Streaming = false;
            OnStop?.Invoke();

            AppLogger.Log(DPC_LogTriggers.TagProvider, "Streaming stopped.", this);
        }

        /// <summary>
        /// 外部（評価スクリプトやエディタ等）から呼び出し、次フレームを待たずに
        /// 即座に現在の密度・ノイズ設定で再サンプリングを行い、描画レンダラーのGPUバッファまで強制同期更新します。
        /// </summary>
        public void ForceUpdateSampling()
        {
            SamplingProcessor.InvalidateCache();
            ExecuteSampling(true);
            ChangeTracker.UpdateSnapshots(this, noiseSettings);

            // 描画レンダラーのGPUバッファを即時反映
#if UNITY_2023_1_OR_NEWER
            var renderers = FindObjectsByType<RsDummyPointCloudRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            var renderers = FindObjectsOfType<RsDummyPointCloudRenderer>(true);
#endif
            foreach (var r in renderers)
            {
                if (r != null) r.EnsureBufferUpdated();
            }

            AppLogger.Log(DPC_LogTriggers.TagProvider,
                $"[ForceUpdateSampling] 強制再サンプリング完了: {LastSampledData.PointCount} 点 (密度: {densityValue} {densityUnit}, DataVersion: {DataVersion})", this);
        }

        /// <summary>
        /// ターゲットオブジェクトのマテリアルカラーを同期・更新します。
        /// </summary>
        public void UpdateMaterialAndRendererColors()
        {
            if (!applyColorToMaterialAndRenderer || colorMode != PointColorMode.SolidColor) return;
            MaterialColorPresenter.ApplySolidColor(targetObjects, includeChildren, solidColor);
        }

        /// <summary>
        /// 点群サンプリングパイプラインを実行し、結果を更新します。
        /// </summary>
        private void ExecuteSampling(bool forceDataVersionAdvance)
        {
            UpdateMaterialAndRendererColors();

            var prevData = LastSampledData;
            SampledPointCloudData sampledData;

            if (targetObjects != null && targetObjects.Count > 0)
            {
                sampledData = SamplingProcessor.ProcessSampling(
                    targetObjects,
                    includeChildren,
                    densityUnit,
                    densityValue,
                    colorMode,
                    solidColor,
                    maxPointLimit,
                    noiseSettings,
                    out _);
            }
            else
            {
                sampledData = new SampledPointCloudData
                {
                    Positions = Array.Empty<Vector3>(),
                    Colors = Array.Empty<Color>(),
                    Normals = Array.Empty<Vector3>(),
                    PointCount = 0
                };
            }

            bool isNoiseActive = noiseSettings != null && (noiseSettings.enableNoise || noiseSettings.enableOutliers);
            bool isDynamicNoise = isNoiseActive && noiseSettings.updateMode == NoiseUpdateMode.Dynamic;

            LastSampledData = sampledData;

            // データ更新判定 (DataVersion インクリメント)
            if (forceDataVersionAdvance || isDynamicNoise || prevData.Positions != LastSampledData.Positions || prevData.PointCount != LastSampledData.PointCount)
            {
                DataVersion++;
            }

            // ソフトウェアデバイス経由で RealSense DepthFrame / FrameSet を発行
            if (StreamingBridge.IsStreaming && LastSampledData.PointCount > 0)
            {
                Transform camXform = simulatedCameraTransform != null ? simulatedCameraTransform : transform;
                StreamingBridge.PublishPointCloudAsDepthFrame(LastSampledData.Positions, camXform, useCameraPerspective);
            }
        }

        private void HandleNewFrame(Frame frame)
        {
            if (Streaming && frame != null)
            {
                OnNewSample?.Invoke(frame);
            }
        }
    }
}
