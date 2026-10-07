using UnityEngine;
using UnityEngine.Rendering;

namespace Features.Weather
{
    /// <summary>
    /// 雨粒および地面衝突時の飛沫 (Splash) を ParticleSystem により描画・制御するプロセッサ。
    /// 発生面は視点（カメラ）の水平位置に追従しますが、シミュレーション空間は World 空間に固定されるため、
    /// 視点が移動してもすでに降っている雨粒が不自然に頭についてくることはありません。
    /// </summary>
    public class WeatherRainProcessor : MonoBehaviour
    {
        private static readonly Color DropColor = new Color(0.75f, 0.8f, 0.95f, 0.45f);

        [Header("References")]
        [Tooltip("雨粒・飛沫用の描画マテリアル (URP Particles/Unlit 推奨)")]
        [SerializeField] private Material particleMaterial;

        [Header("Fall Speed & Simulation")]
        [Tooltip("雨粒の最小落下速度 (m/s)")]
        [SerializeField] private float fallSpeedMin = 7.0f;

        [Tooltip("雨粒の最大落下速度 (m/s)")]
        [SerializeField] private float fallSpeedMax = 9.0f;

        [Header("Simulation Bounds")]
        [Tooltip("雨の発生面の幅 (X) と奥行き (Z) (m)")]
        [SerializeField] private Vector2 rainAreaSize = new Vector2(6.0f, 6.0f);

        [Tooltip("雨の発生面の中心オフセット / 固定位置 (X, Z)")]
        [SerializeField] private Vector2 rainAreaCenter = Vector2.zero;

        [Tooltip("固定ワールド平面モード (True: カメラを追従せず固定平面に配置, False: カメラ追従)")]
        [SerializeField] private bool isFixedBounds = false;

        [Tooltip("雲の高さ (雨の発生面 Y座標)")]
        [SerializeField] private float cloudY = 2.4f;

        [Tooltip("地面の高さ (消滅・着地 Y座標)")]
        [SerializeField] private float groundY = 0.0f;

        [Header("Emission Settings")]
        [Tooltip("最大降雨時の毎秒放出パーティクル数")]
        [SerializeField] private float maxRate = 2500f;

        [Header("Drop Appearance")]
        [Tooltip("雨粒の基本サイズ・幅 (m) (卓上SRDisplay推奨: 0.001〜0.003)")]
        [SerializeField] private float dropSize = 0.002f;

        [Tooltip("雨粒の落下方向への引き伸ばし倍率 (Stretch Length Scale)")]
        [SerializeField] private float lengthScale = 1.8f;

        [Header("Collision Settings")]
        [Tooltip("雨粒の衝突消滅・飛沫生成モード")]
        [SerializeField] private WeatherRainCollisionMode collisionMode = WeatherRainCollisionMode.Plane;

        [Tooltip("World衝突判定時の対象レイヤー (床・机など。プレイヤーや手・Foxは除外推奨)")]
        [SerializeField] private LayerMask environmentMask = ~0;

        private ParticleSystem _rainPs;
        private ParticleSystem _splashPs;
        private Transform _planeTransform;
        private bool _isInitialized;

        public Material ParticleMaterial
        {
            get => particleMaterial;
            set
            {
                particleMaterial = value;
                if (_rainPs != null)
                {
                    var r = _rainPs.GetComponent<ParticleSystemRenderer>();
                    if (r != null) r.sharedMaterial = value;
                }
                if (_splashPs != null)
                {
                    var sr = _splashPs.GetComponent<ParticleSystemRenderer>();
                    if (sr != null) sr.sharedMaterial = value;
                }
            }
        }

        public float FallSpeedMin
        {
            get => fallSpeedMin;
            set
            {
                fallSpeedMin = value;
                UpdateSimulationParameters();
            }
        }

        public float FallSpeedMax
        {
            get => fallSpeedMax;
            set
            {
                fallSpeedMax = value;
                UpdateSimulationParameters();
            }
        }

        public float DropSize
        {
            get => dropSize;
            set
            {
                dropSize = value;
                UpdateSimulationParameters();
            }
        }

        public float LengthScale
        {
            get => lengthScale;
            set
            {
                lengthScale = value;
                UpdateSimulationParameters();
            }
        }

        public Vector2 RainAreaSize
        {
            get => rainAreaSize;
            set
            {
                rainAreaSize = value;
                UpdateSimulationParameters();
            }
        }

        public Vector2 RainAreaCenter
        {
            get => rainAreaCenter;
            set => rainAreaCenter = value;
        }

        public bool IsFixedBounds
        {
            get => isFixedBounds;
            set => isFixedBounds = value;
        }

        public float CloudY
        {
            get => cloudY;
            set
            {
                cloudY = value;
                UpdateSimulationParameters();
            }
        }

        public float GroundY
        {
            get => groundY;
            set
            {
                groundY = value;
                UpdateSimulationParameters();
            }
        }

        public WeatherRainCollisionMode CollisionMode
        {
            get => collisionMode;
            set
            {
                collisionMode = value;
                UpdateSimulationParameters();
            }
        }

        /// <summary>
        /// WeatherManager の一元管理設定を適用します。
        /// </summary>
        public void ApplySettings(
            WeatherRainSettings settings,
            float cloudYPos,
            float groundYPos,
            Vector2? sharedCenter = null,
            Vector2? sharedSize = null,
            bool? sharedFollowViewer = null)
        {
            if (settings == null) return;

            cloudY = cloudYPos;
            groundY = groundYPos;
            fallSpeedMin = Mathf.Max(0.1f, settings.fallSpeedMin);
            fallSpeedMax = Mathf.Max(fallSpeedMin, settings.fallSpeedMax);

            if (settings.useCustomBounds)
            {
                rainAreaSize = settings.areaSize;
                rainAreaCenter = settings.areaCenter;
                isFixedBounds = true;
            }
            else
            {
                rainAreaSize = sharedSize ?? settings.areaSize;
                rainAreaCenter = sharedCenter ?? settings.areaCenter;
                isFixedBounds = !(sharedFollowViewer ?? true);
            }

            maxRate = settings.maxRate;
            dropSize = settings.dropSize;
            lengthScale = settings.lengthScale;
            collisionMode = settings.collisionMode;
            environmentMask = settings.environmentMask;

            if (settings.material != null)
            {
                ParticleMaterial = settings.material;
            }

            if (isFixedBounds)
            {
                transform.position = new Vector3(rainAreaCenter.x, cloudY, rainAreaCenter.y);
            }

            if (_isInitialized)
            {
                UpdateSimulationParameters();
            }
        }

        public void Initialize(Material customMaterial = null)
        {
            if (customMaterial != null)
            {
                particleMaterial = customMaterial;
            }
            if (particleMaterial == null)
            {
                particleMaterial = WeatherRainBuilder.CreateFallbackMaterial();
            }

            SetupRainParticleSystem();
            _isInitialized = true;
            SetIntensity(0f);
        }

        private void Awake()
        {
            if (!_isInitialized)
            {
                Initialize();
            }
        }

        /// <summary>
        /// 視点（カメラ）の水平位置に雨の発生面を追従させます。
        /// 固定平面モード (isFixedBounds == true) の場合はカメラを追従せず、指定された中心に配置されます。
        /// </summary>
        public void FollowViewer(Vector3 viewerPosition)
        {
            if (isFixedBounds)
            {
                transform.position = new Vector3(rainAreaCenter.x, cloudY, rainAreaCenter.y);
            }
            else
            {
                transform.position = new Vector3(viewerPosition.x + rainAreaCenter.x, cloudY, viewerPosition.z + rainAreaCenter.y);
            }
        }

        /// <summary>
        /// 雨の強度 (0.0: 止んでいる 〜 1.0: 豪雨) を適用します。
        /// </summary>
        public void SetIntensity(float intensity)
        {
            if (_rainPs == null) return;

            float clamped = Mathf.Clamp01(intensity);
            var emission = _rainPs.emission;
            emission.rateOverTime = maxRate * clamped;

            var main = _rainPs.main;
            Color c = DropColor;
            c.a *= (0.4f + 0.6f * clamped);
            main.startColor = c;
        }

        private void SetupRainParticleSystem()
        {
            int pcdLayer = LayerMask.NameToLayer("PCD");
            int targetLayer = pcdLayer >= 0 ? pcdLayer : gameObject.layer;
            gameObject.layer = targetLayer;

            if (_rainPs == null)
            {
                var rainChild = new GameObject("RainParticles");
                rainChild.transform.SetParent(transform, false);
                rainChild.layer = targetLayer;
                _rainPs = rainChild.AddComponent<ParticleSystem>();
            }
            else
            {
                _rainPs.gameObject.layer = targetLayer;
            }

            // 雨粒メインシステムの構築を WeatherRainBuilder に委譲
            WeatherRainBuilder.BuildRainParticles(
                _rainPs,
                particleMaterial,
                cloudY,
                groundY,
                fallSpeedMin,
                fallSpeedMax,
                rainAreaSize,
                maxRate,
                collisionMode,
                dropSize,
                lengthScale);

            // 衝突モジュールの設定を WeatherRainBuilder に委譲
            _planeTransform = WeatherRainBuilder.BuildCollisionModule(
                _rainPs,
                collisionMode,
                groundY,
                environmentMask,
                _planeTransform,
                transform);

            // 飛沫サブエミッターのセットアップを WeatherRainBuilder に委譲
            if (collisionMode != WeatherRainCollisionMode.None)
            {
                if (_splashPs == null)
                {
                    var splashChild = new GameObject("SplashParticles");
                    splashChild.transform.SetParent(_rainPs.transform, false);
                    splashChild.layer = targetLayer;
                    _splashPs = splashChild.AddComponent<ParticleSystem>();
                }
                else
                {
                    _splashPs.gameObject.layer = targetLayer;
                }
                WeatherRainBuilder.BuildSplashSubEmitter(_splashPs, _rainPs, particleMaterial, maxRate);
            }

            _rainPs.Play();
        }

        private void UpdateSimulationParameters()
        {
            if (_rainPs == null) return;
            SetupRainParticleSystem();
        }

        private void OnDestroy()
        {
            if (_planeTransform != null)
            {
                Destroy(_planeTransform.gameObject);
            }
        }
    }
}
