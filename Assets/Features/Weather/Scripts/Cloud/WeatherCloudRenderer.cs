using UnityEngine;

namespace Features.Weather
{
    /// <summary>
    /// 雲（Cloud）の 3D 階層（GameObject/MeshFilter/MeshRenderer）、PCD レイヤー適用、
    /// プロシージャルメッシュ再構築、および MaterialPropertyBlock による Zero-GC 色・スケール描画を担当するレンダラークラス。
    /// </summary>
    public class WeatherCloudRenderer
    {
        private GameObject _cloudRoot;
        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private Mesh _generatedMesh;
        private MaterialPropertyBlock _propBlock;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        public GameObject CloudRoot => _cloudRoot;
        public bool IsActive => _cloudRoot != null && _cloudRoot.activeSelf;

        /// <summary>
        /// 雲の 3D 階層とマテリアルを初期化・保証します。
        /// </summary>
        public void EnsureHierarchy(Transform parent, Material cloudMaterial)
        {
            if (_cloudRoot == null)
            {
                var existing = parent.Find("CloudCluster");
                if (existing != null)
                {
                    _cloudRoot = existing.gameObject;
                }
                else
                {
                    _cloudRoot = new GameObject("CloudCluster");
                    _cloudRoot.transform.SetParent(parent, false);
                }
            }

            if (_meshFilter == null)
            {
                _meshFilter = _cloudRoot.GetComponent<MeshFilter>();
                if (_meshFilter == null) _meshFilter = _cloudRoot.AddComponent<MeshFilter>();
            }

            if (_meshRenderer == null)
            {
                _meshRenderer = _cloudRoot.GetComponent<MeshRenderer>();
                if (_meshRenderer == null) _meshRenderer = _cloudRoot.AddComponent<MeshRenderer>();
            }

            if (_meshRenderer.sharedMaterial == null)
            {
                var mat = cloudMaterial != null ? cloudMaterial : WeatherCloudBuilder.CreateFallbackMaterial();
                _meshRenderer.sharedMaterial = mat;
            }

            _meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _meshRenderer.receiveShadows = true;

            if (_propBlock == null)
            {
                _propBlock = new MaterialPropertyBlock();
            }
        }

        /// <summary>
        /// 雲メッシュの再構築を行います。
        /// </summary>
        public void RebuildMesh(Vector2 areaSize, float thickness, int clusterCount, int puffsPerCluster)
        {
            if (_meshFilter == null) return;

            if (_generatedMesh != null)
            {
                if (Application.isPlaying) Object.Destroy(_generatedMesh);
                else Object.DestroyImmediate(_generatedMesh);
            }

            _generatedMesh = WeatherCloudBuilder.BuildCloudMesh(
                areaSize,
                thickness,
                clusterCount,
                puffsPerCluster);

            _meshFilter.sharedMesh = _generatedMesh;
        }

        /// <summary>
        /// マテリアルを更新します。
        /// </summary>
        public void SetMaterial(Material material)
        {
            if (_meshRenderer != null && material != null)
            {
                _meshRenderer.sharedMaterial = material;
            }
        }

        /// <summary>
        /// 雲のワールド座標を設定します。
        /// </summary>
        public void SetPosition(Vector3 position)
        {
            if (_cloudRoot != null)
            {
                _cloudRoot.transform.position = position;
            }
        }

        /// <summary>
        /// 雲のローカルスケールを設定します。
        /// </summary>
        public void SetScale(float scaleMul)
        {
            if (_cloudRoot != null)
            {
                _cloudRoot.transform.localScale = Vector3.one * scaleMul;
            }
        }

        /// <summary>
        /// MaterialPropertyBlock を用いて完全 Zero-GC で色を反映します。
        /// </summary>
        public void SetColor(Color color)
        {
            if (_meshRenderer == null) return;

            if (_propBlock == null) _propBlock = new MaterialPropertyBlock();
            _meshRenderer.GetPropertyBlock(_propBlock);
            _propBlock.SetColor(BaseColorId, color);
            _propBlock.SetColor(ColorId, color);
            _meshRenderer.SetPropertyBlock(_propBlock);
        }

        /// <summary>
        /// 雲オブジェクトのアクティブ状態を切り替えます。
        /// </summary>
        public void SetActive(bool active)
        {
            if (_cloudRoot != null && _cloudRoot.activeSelf != active)
            {
                _cloudRoot.SetActive(active);
            }
        }

        /// <summary>
        /// 点群オクルージョン用 PCD レイヤー (Layer 3) を雲オブジェクトに適用します。
        /// </summary>
        public void ApplyPcdLayer(GameObject rootObject)
        {
            int pcdLayer = LayerMask.NameToLayer("PCD");
            if (pcdLayer < 0) return;

            if (rootObject != null) rootObject.layer = pcdLayer;
            if (_cloudRoot != null) _cloudRoot.layer = pcdLayer;
        }

        /// <summary>
        /// 生成したプロシージャルメッシュのメモリを解放します。
        /// </summary>
        public void Dispose()
        {
            if (_generatedMesh != null)
            {
                if (Application.isPlaying) Object.Destroy(_generatedMesh);
                else Object.DestroyImmediate(_generatedMesh);
                _generatedMesh = null;
            }
        }
    }
}
