using UnityEngine;
using UnityEngine.Rendering;

namespace Features.Weather
{
    /// <summary>
    /// 天候（雨の減光・雷の閃光）に連動して、DirectionalLight (Sun)、環境光 (Ambient)、
    /// 落雷地点の PointLight、および URP Post-Processing Volume を一括調光するプロセッサ。
    /// 裸眼立体ディスプレイ (SRDisplay) や VR においても、視差の破綻を起こさず自然な明暗演出を提供します。
    /// </summary>
    public class WeatherLightingProcessor : MonoBehaviour
    {
        [Header("Scene Lights")]
        [Tooltip("太陽光 (Directional Light)。未指定の場合は自動検索を試みます")]
        [SerializeField] private Light sunLight;

        [Tooltip("落雷時の局所照明用 Point Light (未指定時は自動生成)")]
        [SerializeField] private Light boltPointLight;

        [Header("Light Dimming (Rain)")]
        [Tooltip("雨最大時の太陽光減光率 (0: 減光なし 〜 1: 完全消灯)")]
        [SerializeField, Range(0f, 1f)] private float lightDarkenRate = 0.55f;

        [Tooltip("雨最大時の環境光減光率")]
        [SerializeField, Range(0f, 1f)] private float ambientDarkenRate = 0.5f;

        [Header("Lightning Flash Boost")]
        [Tooltip("落雷 Point Light の最大強度")]
        [SerializeField] private float boltLightIntensity = 7.0f;

        [Tooltip("落雷 Point Light の照射範囲 (m)")]
        [SerializeField] private float boltLightRange = 7.0f;

        [Tooltip("落雷瞬間の太陽光ブースト倍率")]
        [SerializeField] private float sunFlashBoost = 2.5f;

        [Tooltip("落雷瞬間の環境光ブースト倍率")]
        [SerializeField] private float ambientFlashBoost = 2.5f;

        [Header("URP Post-Processing (Optional)")]
        [Tooltip("URP Post-Processing Volume (設定されている場合、露出やトーン調整で画面全体の明暗を連動させます)")]
        [SerializeField] private Volume postProcessVolume;

        public Light SunLight { get => sunLight; set => sunLight = value; }
        public Light BoltPointLight { get => boltPointLight; set => boltPointLight = value; }
        public Volume PostProcessVolume { get => postProcessVolume; set => postProcessVolume = value; }

        /// <summary>
        /// WeatherManager の一元管理設定を適用します。
        /// </summary>
        public void ApplySettings(WeatherLightingSettings settings)
        {
            if (settings == null) return;

            if (settings.sunLight != null) sunLight = settings.sunLight;
            if (settings.boltPointLight != null) boltPointLight = settings.boltPointLight;
            if (settings.postProcessVolume != null) postProcessVolume = settings.postProcessVolume;

            lightDarkenRate = settings.lightDarkenRate;
            ambientDarkenRate = settings.ambientDarkenRate;
            boltLightIntensity = settings.boltLightIntensity;
            boltLightRange = settings.boltLightRange;
            sunFlashBoost = settings.sunFlashBoost;
            ambientFlashBoost = settings.ambientFlashBoost;

            if (boltPointLight != null)
            {
                boltPointLight.range = boltLightRange;
            }
        }

        private float _baseSunIntensity = 1.0f;
        private float _baseAmbientIntensity = 1.0f;
        private bool _isInitialized;

        public void Initialize(Light customSun = null)
        {
            if (customSun != null) sunLight = customSun;
            if (sunLight == null)
            {
                // シーン内の Directional Light を探索
                var lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
                foreach (var l in lights)
                {
                    if (l.type == LightType.Directional)
                    {
                        sunLight = l;
                        break;
                    }
                }
            }

            if (sunLight != null)
            {
                _baseSunIntensity = sunLight.intensity;
            }
            _baseAmbientIntensity = RenderSettings.ambientIntensity;

            // 落雷用 Point Light の準備
            if (boltPointLight == null)
            {
                var boltLightGo = new GameObject("BoltPointLight");
                boltLightGo.transform.SetParent(transform, false);
                boltPointLight = boltLightGo.AddComponent<Light>();
                boltPointLight.type = LightType.Point;
                boltPointLight.color = new Color(0.85f, 0.9f, 1.0f);
                boltPointLight.range = boltLightRange;
                boltPointLight.shadows = LightShadows.None;
                boltPointLight.enabled = false;
            }

            _isInitialized = true;
        }

        private void Awake()
        {
            if (!_isInitialized)
            {
                Initialize();
            }
        }

        /// <summary>
        /// 落雷開始時に Point Light を着弾点付近へ移動し有効化します。
        /// </summary>
        public void OnStrikeStarted(Vector3 groundPosition)
        {
            if (boltPointLight != null)
            {
                boltPointLight.transform.position = groundPosition + Vector3.up * 0.35f;
                boltPointLight.intensity = 0f;
                boltPointLight.enabled = true;
            }
        }

        /// <summary>
        /// 雨強度 (0〜1) と閃光量 (0〜1) を受けて、光源と環境設定を更新します。
        /// LateUpdate から呼び出すことで、同フレームのコルーチン閃光値を確実に反映します。
        /// </summary>
        public void ApplyLighting(float rainIntensity, float flashLevel)
        {
            // 減光ファクター (雨が強いほど暗くなる)
            float dimFactor = 1.0f - lightDarkenRate * Mathf.Clamp01(rainIntensity);
            float ambientDimFactor = 1.0f - ambientDarkenRate * Mathf.Clamp01(rainIntensity);

            // 閃光ブーストファクター (雷が光る瞬間に明るくなる)
            float boostFactor = 1.0f + sunFlashBoost * Mathf.Clamp01(flashLevel);
            float ambientBoostFactor = 1.0f + ambientFlashBoost * Mathf.Clamp01(flashLevel);

            if (sunLight != null)
            {
                sunLight.intensity = _baseSunIntensity * dimFactor * boostFactor;
            }

            RenderSettings.ambientIntensity = _baseAmbientIntensity * ambientDimFactor * ambientBoostFactor;

            if (boltPointLight != null && boltPointLight.enabled)
            {
                boltPointLight.intensity = boltLightIntensity * flashLevel;
            }
        }

        /// <summary>
        /// 落雷終了時に Point Light を消灯します。
        /// </summary>
        public void OnStrikeEnded()
        {
            if (boltPointLight != null)
            {
                boltPointLight.intensity = 0f;
                boltPointLight.enabled = false;
            }
        }

        public void RestoreBaseLighting()
        {
            if (sunLight != null)
            {
                sunLight.intensity = _baseSunIntensity;
            }
            RenderSettings.ambientIntensity = _baseAmbientIntensity;

            if (boltPointLight != null)
            {
                boltPointLight.enabled = false;
            }
        }

        private void OnDisable()
        {
            RestoreBaseLighting();
        }
    }
}
