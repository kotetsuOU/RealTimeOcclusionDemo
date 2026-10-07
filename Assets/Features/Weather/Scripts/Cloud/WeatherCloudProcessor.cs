using System.Collections.Generic;
using UnityEngine;
using Core.Logging;

namespace Features.Weather
{
    /// <summary>
    /// 雲（Cloud）の配置、ライフサイクル、雨の激しさに応じたグラデーションおよび不透明 PCD レイヤー描画を統括するプロセッサ。
    /// 実際の描画・メッシュ管理は WeatherCloudRenderer、位置追従・浮遊計算は WeatherCloudMotion に委譲します。
    /// </summary>
    [AppLoggable("Weather (WeatherEffect)")]
    public class WeatherCloudProcessor : MonoBehaviour, IAppLoggable
    {
        public const string TagCloud = "WeatherCloud";

        public void RegisterLogTriggers(LogCategoryGroup group, HashSet<string> existingLabels)
        {
            const string label = "[WeatherCloud] Cloud State";
            if (!existingLabels.Contains(label))
            {
                group.entries.Add(new LogInstanceEntry
                {
                    label = label,
                    tag = TagCloud,
                    target = this,
                    enabled = true
                });
                existingLabels.Add(label);
            }
        }

        [Header("Cloud Configuration")]
        [Tooltip("雲の表示を有効にするか")]
        [SerializeField] private bool enableClouds = true;

        [Tooltip("雲用描画マテリアル (URP Opaque Lit 推奨)")]
        [SerializeField] private Material cloudMaterial;

        [Tooltip("雲の配置高さ (Y座標)")]
        [SerializeField] private float cloudY = 2.4f;

        [Tooltip("雲の配置中心 (X, Z)")]
        [SerializeField] private Vector2 cloudAreaCenter = Vector2.zero;

        [Tooltip("雲の配置サイズ (X, Z)")]
        [SerializeField] private Vector2 cloudAreaSize = new Vector2(4.0f, 4.0f);

        [Tooltip("カメラの水平位置に追従するか")]
        [SerializeField] private bool followViewer = true;

        [Header("Appearance & Colors")]
        [Tooltip("穏やか/低降雨時の雲の色")]
        [SerializeField] private Color lightCloudColor = new Color(0.92f, 0.94f, 0.98f, 1.0f);

        [Tooltip("激しい嵐・豪雨時の雲の色")]
        [SerializeField] private Color stormCloudColor = new Color(0.18f, 0.20f, 0.24f, 1.0f);

        [Tooltip("雨強度 0 でのスケール倍率 (卓上SRDisplay等の0.1〜0.2m極小設定にも対応)")]
        [Range(0.01f, 1.5f)]
        [SerializeField] private float minScale = 0.2f;

        [Tooltip("雨強度 1 でのスケール倍率")]
        [Range(0.02f, 3.0f)]
        [SerializeField] private float maxScale = 0.6f;

        [Tooltip("雲クラスタ数")]
        [SerializeField] private int clusterCount = 7;

        [Tooltip("クラスタあたりのパフ数")]
        [SerializeField] private int puffsPerCluster = 6;

        [Tooltip("雲の厚み (m)")]
        [SerializeField] private float cloudThickness = 0.15f;

        [Tooltip("雲の微小浮遊・揺らぎ速度")]
        [SerializeField] private float driftSpeed = 0.05f;

        private readonly WeatherCloudRenderer _renderer = new WeatherCloudRenderer();
        private readonly WeatherCloudMotion _motion = new WeatherCloudMotion();
        private float _currentIntensity = 0f;

        public bool EnableClouds
        {
            get => enableClouds;
            set => SetEnabled(value);
        }

        public Material CloudMaterial
        {
            get => cloudMaterial;
            set
            {
                cloudMaterial = value;
                _renderer.SetMaterial(value);
            }
        }

        public float CloudY
        {
            get => cloudY;
            set
            {
                cloudY = value;
                _motion.CloudY = value;
                _motion.ResetToFixedPosition();
                _renderer.SetPosition(_motion.BasePosition);
            }
        }

        public float MinScale
        {
            get => minScale;
            set { minScale = value; UpdateVisuals(); }
        }

        public float MaxScale
        {
            get => maxScale;
            set { maxScale = value; UpdateVisuals(); }
        }

        public float CurrentIntensity => _currentIntensity;

        private void Awake()
        {
            SyncMotionSettings();
            _renderer.EnsureHierarchy(transform, cloudMaterial);
            _renderer.RebuildMesh(cloudAreaSize, cloudThickness, clusterCount, puffsPerCluster);
            _renderer.ApplyPcdLayer(gameObject);
            _motion.ResetToFixedPosition();
            UpdateVisuals();
        }

        private void Update()
        {
            if (!enableClouds || !_renderer.IsActive) return;

            // 1. スケールに応じた浮遊ワールド座標の計算と適用
            float scaleMul = WeatherCloudColorGrading.EvaluateScale(minScale, maxScale, _currentIntensity);
            Vector3 targetPos = _motion.EvaluatePosition(Time.time, scaleMul);
            _renderer.SetPosition(targetPos);

            // 2. 定期ログ (120フレーム毎)
            if (AppLogger.IsEnabled(this, TagCloud) && Time.frameCount % 120 == 0)
            {
                AppLogger.Log(this, TagCloud,
                    $"[WeatherCloud] Enabled={enableClouds}, Intensity={_currentIntensity:F2}, Pos={targetPos:F2}");
            }
        }

        public void ApplySettings(
            WeatherCloudSettings settings,
            float yLevel,
            Vector2? sharedCenter = null,
            Vector2? sharedSize = null,
            bool? sharedFollow = null)
        {
            if (settings == null) return;

            enableClouds = settings.enableClouds;
            if (settings.material != null) cloudMaterial = settings.material;

            cloudY = yLevel;
            cloudAreaCenter = sharedCenter ?? cloudAreaCenter;
            cloudAreaSize = sharedSize ?? cloudAreaSize;
            followViewer = sharedFollow ?? followViewer;

            lightCloudColor = settings.lightCloudColor;
            stormCloudColor = settings.stormCloudColor;
            minScale = settings.minScale;
            maxScale = settings.maxScale;
            clusterCount = settings.clusterCount;
            puffsPerCluster = settings.puffsPerCluster;
            cloudThickness = settings.cloudThickness;
            driftSpeed = settings.driftSpeed;

            SyncMotionSettings();
            _renderer.EnsureHierarchy(transform, cloudMaterial);
            _renderer.RebuildMesh(cloudAreaSize, cloudThickness, clusterCount, puffsPerCluster);
            _renderer.ApplyPcdLayer(gameObject);
            _motion.ResetToFixedPosition();
            UpdateVisuals();
        }

        private void SyncMotionSettings()
        {
            _motion.CloudY = cloudY;
            _motion.AreaCenter = cloudAreaCenter;
            _motion.FollowViewer = followViewer;
            _motion.DriftSpeed = driftSpeed;
        }

        public void SetIntensity(float intensity)
        {
            _currentIntensity = Mathf.Clamp01(intensity);
            UpdateVisuals();
        }

        public void SetEnabled(bool enabled)
        {
            enableClouds = enabled;
            _renderer.SetActive(enabled);
            if (enabled) UpdateVisuals();
        }

        public void FollowViewer(Vector3 viewerPos)
        {
            if (!followViewer) return;
            _motion.UpdateBasePosition(viewerPos);
            if (driftSpeed <= 0f)
            {
                _renderer.SetPosition(_motion.BasePosition);
            }
        }

        public void ApplyPcdLayer()
        {
            _renderer.ApplyPcdLayer(gameObject);
        }

        private void UpdateVisuals()
        {
            _renderer.SetActive(enableClouds);
            if (!enableClouds) return;

            float scaleMul = WeatherCloudColorGrading.EvaluateScale(minScale, maxScale, _currentIntensity);
            _renderer.SetScale(scaleMul);

            Color currentColor = WeatherCloudColorGrading.EvaluateColor(lightCloudColor, stormCloudColor, _currentIntensity);
            _renderer.SetColor(currentColor);
        }

        private void OnDestroy()
        {
            _renderer.Dispose();
        }
    }
}
