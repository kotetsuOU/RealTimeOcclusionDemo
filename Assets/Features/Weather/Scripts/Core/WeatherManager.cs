using System;
using UnityEngine;
using Core.Logging;

namespace Features.Weather
{
    /// <summary>
    /// 天候演出システム（雨・雲・落雷・照明・環境音・入力）のファサードクラス。
    /// 詳細な処理は以下の 4 つの専任クラスに委譲し、システム全体の連携とライフサイクルを管理します:
    /// 1. WeatherHierarchyRegistrar: 階層構造の自動構築・サブプロセッサ配線・PCD レイヤー管理
    /// 2. WeatherConfigDispatcher: 共有空間バウンズの解決および各プロセッサへの設定同期
    /// 3. WeatherStrikeCoordinator: 落雷発生・自動落雷・閃光ライティング・立体雷鳴音響の調停
    /// 4. WeatherSimulationCoordinator: 雨フェード遷移・追従・強度伝達・ジェスチャー検知ループ
    /// </summary>
    [AppLoggable("Weather (WeatherEffect)")]
    public class WeatherManager : MonoBehaviour
    {
        public const string TagWeatherManager = "WeatherManager";
        public const string TagWeatherGesture = "WeatherGesture";

        // ── 空間・全般設定 ──
        [Header("General Space Configuration")]
        [Tooltip("視点 (カメラ)。未設定時は Camera.main を使用")]
        [SerializeField] private Transform viewer;

        [Tooltip("雲の高さ (雨の発生面・落雷始点の Y 座標)")]
        [SerializeField] private float cloudY = 2.4f;

        [Tooltip("地面の高さ (消滅・着地の Y 座標)")]
        [SerializeField] private float groundY = 0.0f;

        [Tooltip("雨の強さフェードにかかる時間 (s)")]
        [SerializeField] private float rainFadeDuration = 2.0f;

        [Header("Shared Space Bounds (X, Z 平面)")]
        [Tooltip("雨と落雷で共通の X, Z 平面範囲設定を使用するか")]
        [SerializeField] private bool useSharedBounds = true;

        [Tooltip("X, Z 平面の中心座標 (m)")]
        [SerializeField] private Vector2 boundsCenter = Vector2.zero;

        [Tooltip("X, Z 平面の幅 (X) と奥行き (Z) (m)")]
        [SerializeField] private Vector2 boundsSize = new Vector2(4.0f, 4.0f);

        [Tooltip("True: カメラの水平位置に追従 / False: 固定ワールド X, Z 平面に配置")]
        [SerializeField] private bool followViewer = true;

        // ── 設定データモデル ──
        [SerializeField] private WeatherRainSettings rain = new WeatherRainSettings();
        [SerializeField] private WeatherCloudSettings clouds = new WeatherCloudSettings();
        [SerializeField] private WeatherLightningSettings lightning = new WeatherLightningSettings();
        [SerializeField] private WeatherLightingSettings lighting = new WeatherLightingSettings();
        [SerializeField] private WeatherAudioSettings audioSettings = new WeatherAudioSettings();
        [SerializeField] private WeatherInputSettings input = new WeatherInputSettings();

        // ── サブプロセッサ参照 ──
        [SerializeField, HideInInspector] private WeatherRainProcessor rainProcessor;
        [SerializeField, HideInInspector] private WeatherCloudProcessor cloudProcessor;
        [SerializeField, HideInInspector] private WeatherLightningProcessor lightningProcessor;
        [SerializeField, HideInInspector] private WeatherLightingProcessor lightingProcessor;
        [SerializeField, HideInInspector] private WeatherAudioSynthesizer audioSynthesizer;
        [SerializeField, HideInInspector] private WeatherHcdInputBridge inputBridge;
        [SerializeField, HideInInspector] private WeatherKeyController keyController;

        // ── 専任コーディネーター (Pure C#) ──
        private readonly WeatherSimulationCoordinator _simulation = new WeatherSimulationCoordinator();
        private readonly WeatherStrikeCoordinator _strikeCoordinator = new WeatherStrikeCoordinator();

        // ── プロパティ ──
        public Transform Viewer { get => viewer; set => viewer = value; }
        public float CloudY { get => cloudY; set { cloudY = value; ApplyAllSettings(); } }
        public float GroundY { get => groundY; set { groundY = value; ApplyAllSettings(); } }
        public float RainFadeDuration { get => rainFadeDuration; set { rainFadeDuration = value; _simulation.FadeDuration = value; } }
        public bool UseSharedBounds { get => useSharedBounds; set { useSharedBounds = value; ApplyAllSettings(); } }
        public Vector2 BoundsCenter { get => boundsCenter; set { boundsCenter = value; ApplyAllSettings(); } }
        public Vector2 BoundsSize { get => boundsSize; set { boundsSize = value; ApplyAllSettings(); } }
        public bool FollowViewerMode { get => followViewer; set { followViewer = value; ApplyAllSettings(); } }

        public WeatherRainSettings Rain => rain;
        public WeatherCloudSettings Clouds => clouds;
        public WeatherLightningSettings Lightning => lightning;
        public WeatherLightingSettings Lighting => lighting;
        public WeatherAudioSettings AudioSettings => audioSettings;
        public WeatherInputSettings InputSettings => input;

        public WeatherRainProcessor RainProcessor { get => rainProcessor; set => rainProcessor = value; }
        public WeatherCloudProcessor CloudProcessor { get => cloudProcessor; set => cloudProcessor = value; }
        public WeatherLightningProcessor LightningProcessor { get => lightningProcessor; set => lightningProcessor = value; }
        public WeatherLightingProcessor LightingProcessor { get => lightingProcessor; set => lightingProcessor = value; }
        public WeatherAudioSynthesizer AudioSynthesizer { get => audioSynthesizer; set => audioSynthesizer = value; }
        public WeatherHcdInputBridge InputBridge { get => inputBridge; set => inputBridge = value; }
        public WeatherKeyController KeyController { get => keyController; set => keyController = value; }

        public bool EnableClouds
        {
            get => clouds.enableClouds;
            set
            {
                clouds.enableClouds = value;
                if (cloudProcessor != null) cloudProcessor.SetEnabled(value);
            }
        }

        public float RainIntensity => _simulation.CurrentIntensity;
        public bool IsRaining => _simulation.IsRaining;

        // ── イベント ──
        public event Action<Vector3> OnLightningStrike;
        public event Action<float> OnRainIntensityChanged;

        private void Awake()
        {
            EnsureViewer();

            _simulation.Initialize(rainFadeDuration);
            _simulation.OnIntensityChanged += intensity => OnRainIntensityChanged?.Invoke(intensity);

            _strikeCoordinator.OnLightningStrike += pos => OnLightningStrike?.Invoke(pos);

            EnsureProcessors();
            ApplyAllSettings();
            SetupGestureCallbacks();
            ApplyPcdLayer();
        }

        public void EnsureViewer()
        {
            WeatherHierarchyRegistrar.EnsureViewer(transform, ref viewer);
        }

        private void OnValidate()
        {
            if (Application.isPlaying) ApplyAllSettings();
        }

        private void OnEnable() => ApplyAllSettings();

        private void OnDisable()
        {
            _simulation.Reset();
            _strikeCoordinator.Reset();
            lightingProcessor?.RestoreBaseLighting();
            rainProcessor?.SetIntensity(0f);
            cloudProcessor?.SetIntensity(0f);
            lightningProcessor?.Stop();
        }

        private void Update()
        {
            // 1. 雨・雲・音量・手ジェスチャーの毎フレームシミュレーション
            _simulation.UpdateSimulation(
                Time.deltaTime,
                Time.time,
                viewer,
                rainProcessor,
                cloudProcessor,
                audioSynthesizer,
                inputBridge);

            // 2. 嵐時の自動落雷判定
            _strikeCoordinator.EvaluateAutoStrike(
                _simulation.CurrentIntensity,
                lightning,
                Time.time,
                lightningProcessor,
                viewer,
                cloudY,
                groundY);

            // 3. 定期デバッグログ (120フレーム毎)
            if (AppLogger.IsEnabled(this, TagWeatherManager) && Time.frameCount % 120 == 0)
            {
                AppLogger.Log(this, TagWeatherManager,
                    $"[WeatherManager] Rain={_simulation.CurrentIntensity:F2} (Target={_simulation.TargetIntensity:F2}), Busy={lightningProcessor?.IsBusy ?? false}");
            }
        }

        private void LateUpdate()
        {
            _strikeCoordinator.ApplyLighting(lightingProcessor, lightningProcessor, _simulation.CurrentIntensity);
        }

        public void SetRainIntensity(float intensity)
        {
            _simulation.SetTargetIntensity(intensity);
            AppLogger.Log(this, TagWeatherManager, $"[WeatherManager] 雨強度目標設定: {_simulation.TargetIntensity:F2}");
        }

        public void ToggleRain() => _simulation.ToggleRain();

        public bool TriggerStrike(Vector3? at = null)
        {
            EnsureViewer();
            return _strikeCoordinator.TriggerStrike(lightningProcessor, viewer, cloudY, groundY, at);
        }

        public void ApplyAllSettings()
        {
            WeatherConfigDispatcher.DispatchAllSettings(
                useSharedBounds,
                boundsCenter,
                boundsSize,
                followViewer,
                cloudY,
                groundY,
                rain,
                clouds,
                lightning,
                lighting,
                audioSettings,
                input,
                rainProcessor,
                cloudProcessor,
                lightningProcessor,
                lightingProcessor,
                audioSynthesizer,
                inputBridge,
                keyController);
        }

        public void EnsureProcessors()
        {
            WeatherHierarchyRegistrar.EnsureProcessors(
                transform,
                ref rainProcessor,
                ref cloudProcessor,
                ref lightningProcessor,
                ref lightingProcessor,
                ref audioSynthesizer,
                ref inputBridge,
                ref keyController);

            if (lightningProcessor != null)
            {
                _strikeCoordinator.BindEvents(lightningProcessor, OnLightningStartedInternal, OnLightningEndedInternal);
            }
        }

        private void SetupGestureCallbacks()
        {
            _simulation.OnRainToggleRequested += () =>
            {
                ToggleRain();
                AppLogger.Log(this, TagWeatherGesture, "[WeatherManager] ジェスチャー検知: 雨トグル");
            };

            _simulation.OnStrikeRequested += (targetPos) =>
            {
                TriggerStrike(targetPos);
                AppLogger.Log(this, TagWeatherGesture, $"[WeatherManager] ジェスチャー検知: 振り下ろし落雷 -> {targetPos:F2}");
            };
        }

        private void OnLightningStartedInternal(Vector3 groundPos)
        {
            _strikeCoordinator.HandleStrikeStarted(
                groundPos,
                viewer,
                lightingProcessor,
                audioSynthesizer,
                lightning,
                this);
        }

        private void OnLightningEndedInternal()
        {
            _strikeCoordinator.HandleStrikeEnded(lightingProcessor);
        }

        private void OnDrawGizmosSelected()
        {
            WeatherGizmoDrawer.DrawSpaceGizmos(viewer, cloudY, groundY, followViewer, boundsCenter, boundsSize);
        }

        /// <summary>
        /// 天候システム階層全体に PCD レイヤー (仮想オブジェクト用 Layer 3) を適用します。
        /// </summary>
        public void ApplyPcdLayer()
        {
            WeatherHierarchyRegistrar.ApplyPcdLayer(gameObject);
        }

        public static void SetLayerRecursively(GameObject obj, int newLayer)
        {
            WeatherHierarchyRegistrar.SetLayerRecursively(obj, newLayer);
        }
    }
}
