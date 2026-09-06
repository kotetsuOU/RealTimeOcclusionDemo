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

        private void OnValidate()
        {
            UpdateMaterialAndRendererColors();
        }

        public void UpdateMaterialAndRendererColors()
        {
            if (!applyColorToMaterialAndRenderer || colorMode != PointColorMode.SolidColor) return;

            if (targetObjects != null)
            {
                if (_materialPropertyBlock == null) _materialPropertyBlock = new MaterialPropertyBlock();

                foreach (var obj in targetObjects)
                {
                    if (obj == null) continue;

                    var renderers = includeChildren
                        ? obj.GetComponentsInChildren<Renderer>()
                        : obj.GetComponents<Renderer>();

                    foreach (var r in renderers)
                    {
                        if (r == null) continue;

                        r.GetPropertyBlock(_materialPropertyBlock);
                        _materialPropertyBlock.SetColor("_Color", solidColor);
                        _materialPropertyBlock.SetColor("_BaseColor", solidColor);
                        r.SetPropertyBlock(_materialPropertyBlock);
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

            _streamingCoroutine = StartCoroutine(StreamingLoop());

            AppLogger.Log(DPC_LogTriggers.TagProvider, $"Streaming started successfully. (CameraPerspective: {useCameraPerspective}, FPS: {updateFPS})", this);
        }

        public void StopStreaming()
        {
            if (!Streaming) return;

            AppLogger.Log(DPC_LogTriggers.TagProvider, "Stopping dummy point cloud streaming...", this);

            if (_streamingCoroutine != null)
            {
                StopCoroutine(_streamingCoroutine);
                _streamingCoroutine = null;
            }

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

        private IEnumerator StreamingLoop()
        {
            int logCounter = 0;

            while (Streaming)
            {
                UpdateMaterialAndRendererColors();

                if (targetObjects != null && targetObjects.Count > 0)
                {
                    var prevData = LastSampledData;

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

                    // 3. 描画レンダラー(RsDummyPointCloudRenderer)にノイズ適用済みデータとして引き渡すため Positions を更新
                    sampledData.Positions = finalPositions;
                    LastSampledData = sampledData;

                    // 4. データ更新判定（Dynamicノイズ有効時またはサンプリングデータ変化時のみ DataVersion を進める）
                    if (isDynamicNoise || prevData.Positions != LastSampledData.Positions || prevData.PointCount != LastSampledData.PointCount)
                    {
                        DataVersion++;

                        logCounter++;
                        if (logCounter % 60 == 0 || prevData.PointCount != LastSampledData.PointCount)
                        {
                            if (isNoiseActive)
                            {
                                AppLogger.Log(DPC_LogTriggers.TagNoiseProcessor,
                                    $"Processed noise/outliers for {LastSampledData.PointCount} points. (Mode: {noiseSettings.updateMode}, Noise: {noiseSettings.enableNoise} [{noiseSettings.noiseAmountMm}mm {noiseSettings.noiseRatio * 100:F1}% {noiseSettings.noiseType}], Outliers: {noiseSettings.enableOutliers} [{noiseSettings.outlierRatio * 100:F1}% {noiseSettings.outlierDistanceMm}mm])", this);
                            }
                            else
                            {
                                AppLogger.Log(DPC_LogTriggers.TagProvider,
                                    $"Sampled & Updated DataVersion: {DataVersion} ({LastSampledData.PointCount} points).", this);
                            }
                        }
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

                float waitSec = 1.0f / Mathf.Max(1, updateFPS);
                yield return new WaitForSeconds(waitSec);
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
