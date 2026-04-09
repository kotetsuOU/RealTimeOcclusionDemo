using UnityEngine;
using System.Collections;

public class StaticMeshPCDRegistrar : MonoBehaviour
{
    [Tooltip("PointCloud: メッシュの頂点を点群として扱う / DepthMap: URPの深度情報として扱う")]
    public PCDProcessingMode mode = PCDProcessingMode.DepthMap;

    [Tooltip("有効にすると、毎フレームTransformの更新を検知して点群データを再構築します")]
    public bool isDynamic = false;

    private MeshFilter _meshFilter;
    private SkinnedMeshRenderer _skinnedMeshRenderer;
    private Renderer _renderer;
    private Mesh _targetMesh;
    private Mesh _bakedMesh; // アニメーション付きメッシュ焼き込み用
    private bool _isRegistered = false;

    private Vector3 _lastPosition;
    private Quaternion _lastRotation;
    private Vector3 _lastScale;

    // コンポーネントが有効になった際に、レンダラーFeatureへメッシュを登録する
    private void OnEnable()
    {
        _meshFilter = GetComponent<MeshFilter>();
        _skinnedMeshRenderer = GetComponent<SkinnedMeshRenderer>();
        _renderer = GetComponent<Renderer>();

        if (_meshFilter != null)
        {
            _targetMesh = _meshFilter.sharedMesh;
        }
        else if (_skinnedMeshRenderer != null)
        {
            if (mode == PCDProcessingMode.PointCloud)
            {
                // PointCloudモードの場合はアニメーションを反映するために専用のメッシュを用意
                _bakedMesh = new Mesh();
                _bakedMesh.name = _skinnedMeshRenderer.gameObject.name + "_BakedPCD";
                _skinnedMeshRenderer.BakeMesh(_bakedMesh);
                _targetMesh = _bakedMesh;
            }
            else
            {
                // DepthMapモードはURP描画に任せるためsharedMeshでOK
                _targetMesh = _skinnedMeshRenderer.sharedMesh;
            }
        }

        if (_targetMesh == null)
        {
            Debug.LogError("[StaticMeshPCDRegistrar] MeshFilter or SkinnedMeshRenderer (with a valid Mesh) not found.", this.gameObject);
            return;
        }

        // DepthMapモードの場合、Rendererが正しく設定・有効化されている必要がある
        if (mode == PCDProcessingMode.DepthMap)
        {
            if (_renderer == null)
            {
                Debug.LogError("[StaticMeshPCDRegistrar] DepthMap mode requires Renderer. Add MeshRenderer or SkinnedMeshRenderer to: " + gameObject.name, this.gameObject);
                return;
            }
            if (!_renderer.enabled)
            {
                Debug.LogWarning("[StaticMeshPCDRegistrar] Renderer is disabled. DepthMap mode requires it enabled: " + gameObject.name, this.gameObject);
            }
            if (_renderer.sharedMaterial == null)
            {
                Debug.LogWarning("[StaticMeshPCDRegistrar] No Material assigned. DepthMap mode requires Material: " + gameObject.name, this.gameObject);
            }
        }

        if (_isRegistered) return;

        // PCDRendererFeatureが既に初期化されていればすぐに登録
        if (PCDRendererFeature.Instance != null)
        {
            PCDRendererFeature.Instance.AddStaticMesh(_targetMesh, transform, mode);
            _isRegistered = true;
            SaveTransformState();
            Debug.Log("[StaticMeshPCDRegistrar] Mesh registered: " + _targetMesh.name + " Mode: " + mode);
        }
        else
        {
            // まだ初期化されていない場合は、コルーチンで待機する
            Debug.LogWarning("[StaticMeshPCDRegistrar] Waiting for PCDRendererFeature: " + _targetMesh.name);
            StartCoroutine(RegisterWhenReady());
        }
    }

    // PCDRendererFeatureの初期化完了を待ってからメッシュを登録するコルーチン
    private IEnumerator RegisterWhenReady()
    {
        while (PCDRendererFeature.Instance == null)
        {
            yield return null;
        }

        if (!_isRegistered && _targetMesh != null)
        {
            Debug.Log("[StaticMeshPCDRegistrar] PCDRendererFeature found. Registering: " + _targetMesh.name + " Mode: " + mode);
            PCDRendererFeature.Instance.AddStaticMesh(_targetMesh, transform, mode);
            _isRegistered = true;
            SaveTransformState();
        }
    }

    // コンポーネントが無効になる、または破棄される際に登録を解除する
    private void OnDisable()
    {
        if (_isRegistered && _targetMesh != null)
        {
            if (PCDRendererFeature.Instance != null)
            {
                PCDRendererFeature.Instance.RemoveStaticMesh(_targetMesh, transform);
                Debug.Log("[StaticMeshPCDRegistrar] Mesh unregistered: " + _targetMesh.name);
            }
        }
        _isRegistered = false;

        if (_bakedMesh != null)
        {
            Destroy(_bakedMesh);
            _bakedMesh = null;
        }
    }

    private void Update()
    {
        // 登録済みかつ動的オブジェクトで、 PointCloudモードの場合
        // （DepthMap モードは URP 側で自動的に描画されるため点群バッファの再構築は不要）
        if (_isRegistered && isDynamic && mode == PCDProcessingMode.PointCloud)
        {
            bool isDirty = false;

            // 1. Transform の変更検知
            if (transform.position != _lastPosition || transform.rotation != _lastRotation || transform.localScale != _lastScale)
            {
                isDirty = true;
                SaveTransformState();
            }

            // 2. SkinnedMeshRenderer (アニメーション付き) の場合は現在のボーンのポーズをメッシュにベイク
            if (_skinnedMeshRenderer != null && _bakedMesh != null)
            {
                _skinnedMeshRenderer.BakeMesh(_bakedMesh);
                isDirty = true; // アニメーションがある場合は常に頂点が動くため更新通知を出す
            }

            // 更新があれば通知
            if (isDirty && PCDRendererFeature.Instance != null)
            {
                PCDRendererFeature.Instance.MarkPointCloudDataDirty();
            }
        }
    }

    private void SaveTransformState()
    {
        _lastPosition = transform.position;
        _lastRotation = transform.rotation;
        _lastScale = transform.localScale;
    }
}

// 登録したメッシュの処理モード
public enum PCDProcessingMode
{
    PointCloud,  // 頂点配列を元にオクルージョン計算に巻き込む
    DepthMap     // 背景として扱い、奥行きのみを提供する
}
