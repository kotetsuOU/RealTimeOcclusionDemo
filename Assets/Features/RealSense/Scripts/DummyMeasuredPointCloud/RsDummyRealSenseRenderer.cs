using System;
using System.Collections.Generic;
using Core.Logging;
using UnityEngine;

namespace RealSense.DummyPointCloud
{
    /// <summary>
    /// ダミー点群の生成（<see cref="RsDummyPointCloudProvider"/> 相当）と
    /// Dirty-based GPU バッファ描画（<see cref="RsDummyPointCloudRenderer"/> 相当）を 1 つに統合したコンポーネント。
    /// </summary>
    [AppLoggable("DPC (Dummy Point Cloud)")]
    [RequireComponent(typeof(MeshRenderer))]
    public class RsDummyRealSenseRenderer : RsPointCloudRenderer, IRsDummyPointCloudSource, IAppLoggable
    {
        [Header("Target 3D Objects Settings")]
        [Tooltip("実測点群の生成元となる Unity 3D オブジェクトのリスト")]
        public List<GameObject> targetObjects = new List<GameObject>();
        public bool includeChildren = true;

        [Header("Physical Point Density & Color")]
        public PointDensityUnit densityUnit = PointDensityUnit.PointsPerCm2;
        [Range(0.001f, 1000f)] public float densityValue = 1.0f;
        [Range(1000, 500000)] public int maxPointLimit = 100000;
        public PointColorMode colorMode = PointColorMode.SolidColor;
        public Color solidColor = new Color(241f / 255f, 187f / 255f, 147f / 255f, 1f);
        public bool applyColorToMaterialAndRenderer = true;

        [Header("Noise & Outliers Settings")]
        public RsPointCloudNoiseSettings noiseSettings = new RsPointCloudNoiseSettings();

        [Header("Camera Perspective Settings")]
        public bool useCameraPerspective = true;
        public Transform simulatedCameraTransform;
        public int depthWidth = 640;
        public int depthHeight = 480;
        [Range(1, 60)] public int updateFPS = 30;

        [Header("Debug & Gizmos Settings")]
        public bool showGizmos = false;

        private RsDummyChangeTracker _changeTracker;
        private RsDummyMaterialColorPresenter _materialColorPresenter;
        private RsDummySamplingProcessor _samplingProcessor;
        private RsDummyGpuBuffer _gpuBuffer;

        private int _dataVersion = 0;
        private int _appliedDataVersion = -1;
        private bool _isRegisteredToGlobalManager = false;

        private RsDummyChangeTracker ChangeTracker => _changeTracker ?? (_changeTracker = new RsDummyChangeTracker());
        private RsDummyMaterialColorPresenter MaterialColorPresenter => _materialColorPresenter ?? (_materialColorPresenter = new RsDummyMaterialColorPresenter());
        private RsDummySamplingProcessor SamplingProcessor => _samplingProcessor ?? (_samplingProcessor = new RsDummySamplingProcessor());

        public SampledPointCloudData LastSampledData { get; private set; }
        public int DataVersion => _dataVersion;

        List<GameObject> IRsDummyPointCloudSource.TargetObjects => targetObjects;
        bool IRsDummyPointCloudSource.IncludeChildren => includeChildren;
        PointDensityUnit IRsDummyPointCloudSource.DensityUnit => densityUnit;
        float IRsDummyPointCloudSource.DensityValue => densityValue;
        int IRsDummyPointCloudSource.MaxPointLimit => maxPointLimit;
        PointColorMode IRsDummyPointCloudSource.ColorMode => colorMode;
        Color IRsDummyPointCloudSource.SolidColor => solidColor;
        bool IRsDummyPointCloudSource.UseCameraPerspective => useCameraPerspective;
        Transform IRsDummyPointCloudSource.SimulatedCameraTransform => simulatedCameraTransform;
        int IRsDummyPointCloudSource.UpdateFPS => updateFPS;
        Transform IRsDummyPointCloudSource.OwnerTransform => transform;

        public void RegisterLogTriggers(LogCategoryGroup group, HashSet<string> existingLabels)
        {
            var triggers = GetComponent<DPC_LogTriggers>() ?? gameObject.AddComponent<DPC_LogTriggers>();
            triggers.RegisterLogTriggers(group, existingLabels);
        }

        private void Awake()
        {
            if (simulatedCameraTransform == null)
                simulatedCameraTransform = transform;
        }

        private void OnEnable()
        {
            RegisterToGlobalManager();
            ChangeTracker.SetDirty();
            UpdateMaterialAndRendererColors();
        }

        protected override void TryAutoAssignDependencies() { }

        protected override void OnRendererStart()
        {
            _gpuBuffer = new RsDummyGpuBuffer(GetComponent<MeshRenderer>());
            RegisterToGlobalManager();
            ForceUpdateSampling();
        }

        private void OnValidate()
        {
            SetDirty();
            UpdateMaterialAndRendererColors();
        }

        private void Update()
        {
            if (ChangeTracker.CheckShouldUpdate(this, noiseSettings, out bool isDynamicNoise, out bool requiresCacheInvalidation))
            {
                if (requiresCacheInvalidation)
                    SamplingProcessor.InvalidateCache();

                ExecuteSampling(isDynamicNoise);
                ChangeTracker.UpdateSnapshots(this, noiseSettings);
            }
        }

        private void LateUpdate()
        {
            RegisterToGlobalManager();
            EnsureBufferUpdated();
            _gpuBuffer?.Draw(pointCloudColor, gameObject.layer);
        }

        public void SetDirty()
        {
            ChangeTracker.SetDirty();
            SamplingProcessor.InvalidateCache();
        }

        public void ForceUpdateSampling()
        {
            SamplingProcessor.InvalidateCache();
            ExecuteSampling(true);
            ChangeTracker.UpdateSnapshots(this, noiseSettings);
            EnsureBufferUpdated();
        }

        public void UpdateMaterialAndRendererColors()
        {
            if (!applyColorToMaterialAndRenderer || colorMode != PointColorMode.SolidColor)
                return;

            MaterialColorPresenter.ApplySolidColor(targetObjects, includeChildren, solidColor);
            pointCloudColor = solidColor;
        }

        public void EnsureBufferUpdated()
        {
            if (_gpuBuffer == null)
                _gpuBuffer = new RsDummyGpuBuffer(GetComponent<MeshRenderer>());

            if (_dataVersion != _appliedDataVersion)
            {
                _gpuBuffer.Upload(LastSampledData);
                _appliedDataVersion = _dataVersion;
            }
        }

        private void ExecuteSampling(bool forceDataVersionAdvance)
        {
            UpdateMaterialAndRendererColors();

            var prevData = LastSampledData;
            SampledPointCloudData sampledData;

            if (targetObjects != null && targetObjects.Count > 0)
            {
                sampledData = SamplingProcessor.ProcessSampling(
                    targetObjects, includeChildren, densityUnit, densityValue,
                    colorMode, solidColor, maxPointLimit, noiseSettings, out _);
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
            if (forceDataVersionAdvance || isDynamicNoise || prevData.Positions != LastSampledData.Positions || prevData.PointCount != LastSampledData.PointCount)
            {
                _dataVersion++;
            }
        }

        private void RegisterToGlobalManager()
        {
            if (_isRegisteredToGlobalManager) return;

            var manager = RsGlobalPointCloudManager.Instance;
            if (manager != null && manager.renderers != null)
            {
                if (!manager.renderers.Contains(this))
                    manager.renderers.Add(this);
                _isRegisteredToGlobalManager = true;
            }
        }

        private void OnDrawGizmos()
        {
            if (!showGizmos || LastSampledData.Positions == null || LastSampledData.PointCount <= 0)
                return;

            Gizmos.color = pointCloudColor;
            int count = LastSampledData.PointCount;
            int step = Mathf.Max(1, count / 1000);
            for (int i = 0; i < count; i += step)
            {
                Gizmos.DrawSphere(LastSampledData.Positions[i], 0.003f);
            }
        }

        public override ComputeBuffer GetFilteredVerticesBuffer() { EnsureBufferUpdated(); return _gpuBuffer?.VerticesBuffer; }
        public override int GetLastFilteredCount() { EnsureBufferUpdated(); return _gpuBuffer?.ValidPointCount ?? 0; }
        public override ComputeBuffer GetPCDSourceBuffer() => GetFilteredVerticesBuffer();
        public override int GetPCDSourceCount() => GetLastFilteredCount();
        public override ComputeBuffer GetOcclusionSourceBuffer() => GetFilteredVerticesBuffer();
        public override int GetOcclusionSourceCount() => GetLastFilteredCount();

        protected override void OnRendererDispose()
        {
            var manager = RsGlobalPointCloudManager.Instance;
            if (manager != null && manager.renderers != null)
                manager.renderers.Remove(this);

            _gpuBuffer?.Dispose();
            _gpuBuffer = null;
            _samplingProcessor?.Dispose();
            _samplingProcessor = null;
        }
    }
}

