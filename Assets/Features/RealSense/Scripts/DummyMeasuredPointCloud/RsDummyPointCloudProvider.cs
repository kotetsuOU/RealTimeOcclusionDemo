using System;
using System.Collections;
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

        private RsMeshPointCloudSampler _sampler;
        private RsPointCloudNoiseProcessor _noiseProcessor;
        private RsDummySoftwareDevice _softwareDevice;
        private Coroutine _streamingCoroutine;
        private MaterialPropertyBlock _materialPropertyBlock;

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
            _sampler = new RsMeshPointCloudSampler();
            _noiseProcessor = new RsPointCloudNoiseProcessor();
            _materialPropertyBlock = new MaterialPropertyBlock();
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
            _sampler = null;
            _noiseProcessor = null;
        }

        private struct TransformSnapshot
        {
            public Transform transform;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
        }

        private List<TransformSnapshot> _trackedTransforms = new List<TransformSnapshot>();

        // パラメータ変更検知用スナップショット
        private PointDensityUnit _cachedDensityUnit;
        private float _cachedDensityValue = -1f;
        private int _cachedMaxPointLimit = -1;
        private PointColorMode _cachedColorMode;
        private Color _cachedSolidColor;
        private bool _cachedIncludeChildren;
        private bool _cachedUseCameraPerspective;
        private Transform _cachedSimulatedCameraTransform;
        private int _cachedTargetObjectsCount = -1;

        // ノイズ設定変更検知用スナップショット
        private bool _cachedNoiseEnabled;
        private float _cachedNoiseAmountMm = -1f;
        private float _cachedNoiseRatio = -1f;
        private NoiseDistributionType _cachedNoiseType;
        private bool _cachedOutliersEnabled;
        private float _cachedOutlierRatio = -1f;
        private float _cachedOutlierDistanceMm = -1f;
        private NoiseUpdateMode _cachedNoiseUpdateMode;

        private bool _isDirty = true;
        private float _lastDynamicUpdateTime = 0f;

        /// <summary>
        /// 外部からダーティフラグを立て、次フレームの Update で強制的に再計算を行わせます。
        /// </summary>
        public void SetDirty()
        {
            _isDirty = true;
            if (_sampler != null) _sampler.InvalidateCache();
        }

        private void OnValidate()
        {
            SetDirty();
            UpdateMaterialAndRendererColors();
        }

        private void Update()
        {
            if (!Streaming) return;

            bool isDynamicNoise = noiseSettings != null &&
                                  (noiseSettings.enableNoise || noiseSettings.enableOutliers) &&
                                  noiseSettings.updateMode == NoiseUpdateMode.Dynamic;

            bool shouldUpdate = false;

            if (_isDirty)
            {
                shouldUpdate = true;
            }
            else if (HasSettingsChanged())
            {
                shouldUpdate = true;
                if (_sampler != null) _sampler.InvalidateCache();
            }
            else if (HasTransformsChanged())
            {
                shouldUpdate = true;
            }
            else if (isDynamicNoise)
            {
                float interval = 1.0f / Mathf.Max(1, updateFPS);
                if (Time.time - _lastDynamicUpdateTime >= interval)
                {
                    shouldUpdate = true;
                    _lastDynamicUpdateTime = Time.time;
                }
            }

            if (shouldUpdate)
            {
                int prevVersion = DataVersion;
                ExecuteSampling(isDynamicNoise || _isDirty);
                UpdateSnapshots();
                _isDirty = false;

                if (DataVersion != prevVersion)
                {
                    AppLogger.Log(DPC_LogTriggers.TagProvider,
                        $"[Update] 点群データを更新しました (DataVersion: {DataVersion}, {LastSampledData.PointCount} 点)", this);
                }
            }
        }

        private bool HasSettingsChanged()
        {
            if (_cachedDensityUnit != densityUnit) return true;
            if (!Mathf.Approximately(_cachedDensityValue, densityValue)) return true;
            if (_cachedMaxPointLimit != maxPointLimit) return true;
            if (_cachedColorMode != colorMode) return true;
            if (_cachedSolidColor != solidColor) return true;
            if (_cachedIncludeChildren != includeChildren) return true;
            if (_cachedUseCameraPerspective != useCameraPerspective) return true;
            if (_cachedSimulatedCameraTransform != simulatedCameraTransform) return true;

            int targetCount = targetObjects != null ? targetObjects.Count : 0;
            if (_cachedTargetObjectsCount != targetCount) return true;

            if (noiseSettings != null)
            {
                if (_cachedNoiseEnabled != noiseSettings.enableNoise) return true;
                if (!Mathf.Approximately(_cachedNoiseAmountMm, noiseSettings.noiseAmountMm)) return true;
                if (!Mathf.Approximately(_cachedNoiseRatio, noiseSettings.noiseRatio)) return true;
                if (_cachedNoiseType != noiseSettings.noiseType) return true;
                if (_cachedOutliersEnabled != noiseSettings.enableOutliers) return true;
                if (!Mathf.Approximately(_cachedOutlierRatio, noiseSettings.outlierRatio)) return true;
                if (!Mathf.Approximately(_cachedOutlierDistanceMm, noiseSettings.outlierDistanceMm)) return true;
                if (_cachedNoiseUpdateMode != noiseSettings.updateMode) return true;
            }

            return false;
        }

        private bool HasTransformsChanged()
        {
            Transform camXform = simulatedCameraTransform != null ? simulatedCameraTransform : transform;
            if (camXform != null && camXform.hasChanged)
            {
                camXform.hasChanged = false;
                return true;
            }

            if (_trackedTransforms.Count == 0 && targetObjects != null && targetObjects.Count > 0)
            {
                return true;
            }

            for (int i = 0; i < _trackedTransforms.Count; i++)
            {
                var snap = _trackedTransforms[i];
                if (snap.transform == null) return true;
                if (snap.transform.hasChanged ||
                    snap.transform.position != snap.position ||
                    snap.transform.rotation != snap.rotation ||
                    snap.transform.lossyScale != snap.scale)
                {
                    snap.transform.hasChanged = false;
                    return true;
                }
            }

            return false;
        }

        private void UpdateSnapshots()
        {
            _cachedDensityUnit = densityUnit;
            _cachedDensityValue = densityValue;
            _cachedMaxPointLimit = maxPointLimit;
            _cachedColorMode = colorMode;
            _cachedSolidColor = solidColor;
            _cachedIncludeChildren = includeChildren;
            _cachedUseCameraPerspective = useCameraPerspective;
            _cachedSimulatedCameraTransform = simulatedCameraTransform;
            _cachedTargetObjectsCount = targetObjects != null ? targetObjects.Count : 0;

            if (noiseSettings != null)
            {
                _cachedNoiseEnabled = noiseSettings.enableNoise;
                _cachedNoiseAmountMm = noiseSettings.noiseAmountMm;
                _cachedNoiseRatio = noiseSettings.noiseRatio;
                _cachedNoiseType = noiseSettings.noiseType;
                _cachedOutliersEnabled = noiseSettings.enableOutliers;
                _cachedOutlierRatio = noiseSettings.outlierRatio;
                _cachedOutlierDistanceMm = noiseSettings.outlierDistanceMm;
                _cachedNoiseUpdateMode = noiseSettings.updateMode;
            }

            _trackedTransforms.Clear();
            if (targetObjects != null)
            {
                foreach (var obj in targetObjects)
                {
                    if (obj == null || !obj.activeInHierarchy) continue;
                    var renderers = includeChildren ? obj.GetComponentsInChildren<Renderer>() : obj.GetComponents<Renderer>();
                    foreach (var r in renderers)
                    {
                        if (r == null || !RsMeshPointCloudSampler.IsRendererActiveForSampling(r, obj)) continue;
                        r.transform.hasChanged = false;
                        _trackedTransforms.Add(new TransformSnapshot
                        {
                            transform = r.transform,
                            position = r.transform.position,
                            rotation = r.transform.rotation,
                            scale = r.transform.lossyScale
                        });
                    }
                }
            }
        }

        public void StartStreaming()
        {
            if (Streaming) return;

            AppLogger.Log(DPC_LogTriggers.TagProvider, "Starting dummy point cloud streaming...", this);

            _softwareDevice = new RsDummySoftwareDevice();
            _softwareDevice.Initialize(depthWidth, depthHeight, updateFPS);
            _softwareDevice.OnFrameAvailable += HandleNewFrame;

            ActiveProfile = _softwareDevice.ActiveProfile;
            Streaming = true;

            OnStart?.Invoke(ActiveProfile);

            SetDirty();

            AppLogger.Log(DPC_LogTriggers.TagProvider, $"Streaming started successfully. (CameraPerspective: {useCameraPerspective}, FPS: {updateFPS})", this);
        }

        public void StopStreaming()
        {
            if (!Streaming) return;

            AppLogger.Log(DPC_LogTriggers.TagProvider, "Stopping dummy point cloud streaming...", this);

            if (_softwareDevice != null)
            {
                _softwareDevice.OnFrameAvailable -= HandleNewFrame;
                _softwareDevice.Dispose();
                _softwareDevice = null;
            }

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
            _isDirty = true;
            if (_sampler == null) _sampler = new RsMeshPointCloudSampler();
            _sampler.InvalidateCache();

            ExecuteSampling(true);
            UpdateSnapshots();
            _isDirty = false;

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

        public void UpdateMaterialAndRendererColors()
        {
            if (!applyColorToMaterialAndRenderer || colorMode != PointColorMode.SolidColor) return;

            if (targetObjects != null)
            {
                if (_materialPropertyBlock == null) _materialPropertyBlock = new MaterialPropertyBlock();

                foreach (var obj in targetObjects)
                {
                    if (obj == null || !obj.activeInHierarchy) continue;

                    var renderers = includeChildren
                        ? obj.GetComponentsInChildren<Renderer>()
                        : obj.GetComponents<Renderer>();

                    foreach (var r in renderers)
                    {
                        if (r == null || !RsMeshPointCloudSampler.IsRendererActiveForSampling(r, obj)) continue;

                        r.GetPropertyBlock(_materialPropertyBlock);
                        _materialPropertyBlock.SetColor("_Color", solidColor);
                        _materialPropertyBlock.SetColor("_BaseColor", solidColor);
                        r.SetPropertyBlock(_materialPropertyBlock);
                    }
                }
            }
        }

        private void ExecuteSampling(bool forceDataVersionAdvance)
        {
            UpdateMaterialAndRendererColors();

            if (targetObjects != null && targetObjects.Count > 0)
            {
                var prevData = LastSampledData;

                if (_sampler == null) _sampler = new RsMeshPointCloudSampler();
                if (_noiseProcessor == null) _noiseProcessor = new RsPointCloudNoiseProcessor();

                // 1. Mesh / SkinnedMesh リストから物理密度・色に応じた点群をサンプリング
                var sampledData = _sampler.SamplePointCloud(
                    targetObjects,
                    includeChildren,
                    densityUnit,
                    densityValue,
                    colorMode,
                    solidColor,
                    maxPointLimit);

                bool isNoiseActive = _noiseProcessor != null && noiseSettings != null && (noiseSettings.enableNoise || noiseSettings.enableOutliers);
                bool isDynamicNoise = isNoiseActive && noiseSettings.updateMode == NoiseUpdateMode.Dynamic;

                // 2. ノイズ・外れ値の適用
                Vector3[] finalPositions = sampledData.Positions;
                if (isNoiseActive && sampledData.PointCount > 0)
                {
                    finalPositions = _noiseProcessor.ProcessPointCloud(
                        sampledData.Positions,
                        sampledData.Normals,
                        sampledData.PointCount,
                        noiseSettings);
                }

                // 3. Positions を更新
                sampledData.Positions = finalPositions;
                LastSampledData = sampledData;

                // 4. データ更新判定
                if (forceDataVersionAdvance || isDynamicNoise || prevData.Positions != LastSampledData.Positions || prevData.PointCount != LastSampledData.PointCount)
                {
                    DataVersion++;
                }

                // 5. SoftwareDevice 経由で RealSense DepthFrame / FrameSet として発行
                if (_softwareDevice != null && LastSampledData.PointCount > 0)
                {
                    Transform camXform = simulatedCameraTransform != null ? simulatedCameraTransform : transform;
                    _softwareDevice.PublishPointCloudAsDepthFrame(
                        finalPositions,
                        camXform,
                        useCameraPerspective);
                }
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
