using System.Collections.Generic;
using UnityEngine;

namespace Features.Weather
{
    /// <summary>
    /// 稲妻ジオメトリ (主幹 Trunk、枝 Branches、着地点放電スパーク ImpactSparks) の事前プール管理クラス。
    /// 親 Transform のオフセットを排除したワールド原点独立階層と、カメラ CullingMask (Layer 3: PCD) への完全整合を保証します。
    /// 実際のメッシュ構築は WeatherLightningMeshBuilder、パス幾何ディスパッチは WeatherBoltGeometryDispatcher、
    /// HDR 発光・アルファ制御は WeatherBoltEmissionController に委譲し、完全 Zero-GC で動作します。
    /// </summary>
    public class WeatherLightningBoltPool : MonoBehaviour
    {
        private const int MaxTrunkPoints = 129; // 2^6 + 1
        private const int MaxBranchPoints = 65; // 2^5 + 1
        private const int MaxBranches = 3;

        private GameObject _boltRoot;
        private WeatherBoltRibbon _trunk;
        private readonly List<WeatherBoltRibbon> _branches = new List<WeatherBoltRibbon>();
        private WeatherBoltRibbon _impactSparks;

        private readonly Vector3[] _trunkPoints = new Vector3[MaxTrunkPoints];
        private readonly WeatherBoltEmissionController _emissionController = new WeatherBoltEmissionController();
        private float _currentTrunkWidth = 0.03f;
        private bool _isInitialized;

        public void SetColors(Color core, Color glow)
        {
            _emissionController.SetColors(core, glow);
        }

        private void Awake()
        {
            if (!_isInitialized)
            {
                Initialize(transform, null, _currentTrunkWidth);
            }
        }

        /// <summary>
        /// 独立したワールド原点階層と各十字リボンメッシュを事前確保・初期化します。
        /// </summary>
        public void Initialize(Transform parent, Material material, float trunkWidth = 0.03f)
        {
            if (_isInitialized)
            {
                if (material != null) SetMaterial(material);
                return;
            }

            _currentTrunkWidth = Mathf.Max(0.005f, trunkWidth);

            int pcdLayer = LayerMask.NameToLayer("PCD");
            int targetLayer = pcdLayer >= 0 ? pcdLayer : 3;
            gameObject.layer = targetLayer;

            // 親 Transform の移動・回転・スケールによるメッシュの座標ズレを根絶するため、
            // ワールド原点独立ルートとして生成 (SetParent(null))
            _boltRoot = new GameObject("LightningBolt_Pooled");
            _boltRoot.transform.SetParent(null, false);
            _boltRoot.transform.position = Vector3.zero;
            _boltRoot.transform.rotation = Quaternion.identity;
            _boltRoot.transform.localScale = Vector3.one;

            if (material == null)
            {
                material = WeatherLightningMeshBuilder.CreateFallbackMaterial();
            }

            // 1. 主幹 (Trunk) 十字リボンメッシュ生成
            _trunk = WeatherLightningMeshBuilder.CreateRibbon("Trunk", _boltRoot.transform, material, 1.0f, MaxTrunkPoints, targetLayer);

            // 2. 枝 (Branches) 十字リボンメッシュ事前生成
            for (int i = 0; i < MaxBranches; i++)
            {
                var branch = WeatherLightningMeshBuilder.CreateRibbon($"Branch_{i}", _boltRoot.transform, material, 0.6f, MaxBranchPoints, targetLayer);
                _branches.Add(branch);
            }

            // 3. 着地点放電スパーク (Ground Impact Sparks) メッシュ生成
            int sparkTotalPoints = WeatherLightningMeshBuilder.ImpactRayCount * WeatherLightningMeshBuilder.ImpactPointsPerRay;
            _impactSparks = WeatherLightningMeshBuilder.CreateRibbon("ImpactSparks", _boltRoot.transform, material, 1.0f, sparkTotalPoints, targetLayer);

            SetLayerRecursively(_boltRoot, targetLayer);
            _boltRoot.SetActive(false);
            _isInitialized = true;
        }

        public void SetMaterial(Material material)
        {
            if (material == null) return;
            ApplyMaterialToRibbon(_trunk, material);
            for (int i = 0; i < _branches.Count; i++)
            {
                ApplyMaterialToRibbon(_branches[i], material);
            }
            ApplyMaterialToRibbon(_impactSparks, material);
        }

        private static void ApplyMaterialToRibbon(WeatherBoltRibbon ribbon, Material mat)
        {
            if (ribbon == null) return;
            if (ribbon.CoreRenderer != null) ribbon.CoreRenderer.sharedMaterial = mat;
            if (ribbon.GlowRenderer != null) ribbon.GlowRenderer.sharedMaterial = mat;
        }

        public void SetWidth(float trunkWidth)
        {
            _currentTrunkWidth = Mathf.Max(0.005f, trunkWidth);
        }

        /// <summary>
        /// 指定した空座標・着地座標に基づき、プール内の光の筋（十字リボン）および着地点放電スパークを表示します。
        /// </summary>
        public void BuildAndShow(Vector3 sky, Vector3 ground, WeatherLightningGeometry geometry)
        {
            if (!_isInitialized)
            {
                Initialize(transform, null, _currentTrunkWidth);
            }

            if (geometry == null || _boltRoot == null) return;

            // PCD レイヤー (Layer 3) を確実に全パーツに保証
            int pcdLayer = LayerMask.NameToLayer("PCD");
            SetLayerRecursively(_boltRoot, pcdLayer >= 0 ? pcdLayer : 3);

            // パス幾何計算および各リボンへのメッシュ更新ディスパッチ
            WeatherBoltGeometryDispatcher.DispatchGeometry(
                sky,
                ground,
                geometry,
                _trunk,
                _branches,
                _impactSparks,
                _trunkPoints,
                _currentTrunkWidth,
                MaxBranches);

            _boltRoot.SetActive(true);
            SetLevel(1.0f);
        }

        /// <summary>
        /// 発光レベル (0.0 〜 1.0) に応じてマテリアルカラーとアルファを即時反映します。
        /// </summary>
        public void SetLevel(float level)
        {
            if (!_isInitialized || !_boltRoot.activeSelf) return;
            _emissionController.ApplyLevel(level, _trunk, _branches, _impactSparks);
        }

        public void Hide()
        {
            if (_boltRoot != null)
            {
                _boltRoot.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            _trunk?.Dispose();
            for (int i = 0; i < _branches.Count; i++)
            {
                _branches[i]?.Dispose();
            }
            _impactSparks?.Dispose();

            if (_boltRoot != null)
            {
                Destroy(_boltRoot);
            }
        }

        private static void SetLayerRecursively(GameObject obj, int layer)
        {
            if (obj == null) return;
            obj.layer = layer;
            foreach (Transform child in obj.transform)
            {
                if (child != null) SetLayerRecursively(child.gameObject, layer);
            }
        }
    }
}
