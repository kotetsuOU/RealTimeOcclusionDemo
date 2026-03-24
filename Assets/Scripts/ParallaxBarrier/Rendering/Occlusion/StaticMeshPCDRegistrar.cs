using UnityEngine;
using System.Collections;

[RequireComponent(typeof(MeshFilter))]
public class StaticMeshPCDRegistrar : MonoBehaviour
{
    [Tooltip("PointCloud: メッシュの頂点を点群として扱う / DepthMap: URPの深度情報として扱う")]
    public PCDProcessingMode mode = PCDProcessingMode.DepthMap;

    private MeshFilter _meshFilter;
    private MeshRenderer _meshRenderer;
    private bool _isRegistered = false;

    // コンポーネントが有効になった際に、レンダラーFeatureへメッシュを登録する
    private void OnEnable()
    {
        _meshFilter = GetComponent<MeshFilter>();
        _meshRenderer = GetComponent<MeshRenderer>();

        if (_meshFilter == null || _meshFilter.mesh == null)
        {
            Debug.LogError("[StaticMeshPCDRegistrar] MeshFilter or Mesh not found.", this.gameObject);
            return;
        }

        // DepthMapモードの場合、MeshRendererが正しく設定・有効化されている必要がある
        if (mode == PCDProcessingMode.DepthMap)
        {
            if (_meshRenderer == null)
            {
                Debug.LogError("[StaticMeshPCDRegistrar] DepthMap mode requires MeshRenderer. Add MeshRenderer to: " + gameObject.name, this.gameObject);
                return;
            }
            if (!_meshRenderer.enabled)
            {
                Debug.LogWarning("[StaticMeshPCDRegistrar] MeshRenderer is disabled. DepthMap mode requires it enabled: " + gameObject.name, this.gameObject);
            }
            if (_meshRenderer.sharedMaterial == null)
            {
                Debug.LogWarning("[StaticMeshPCDRegistrar] No Material assigned. DepthMap mode requires Material: " + gameObject.name, this.gameObject);
            }
        }

        if (_isRegistered) return;

        // PCDRendererFeatureが既に初期化されていればすぐに登録
        if (PCDRendererFeature.Instance != null)
        {
            PCDRendererFeature.Instance.AddStaticMesh(_meshFilter.sharedMesh, transform, mode);
            _isRegistered = true;
            Debug.Log("[StaticMeshPCDRegistrar] Mesh registered: " + _meshFilter.mesh.name + " Mode: " + mode);
        }
        else
        {
            // まだ初期化されていない場合は、コルーチンで待機する
            Debug.LogWarning("[StaticMeshPCDRegistrar] Waiting for PCDRendererFeature: " + _meshFilter.mesh.name);
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

        if (!_isRegistered && _meshFilter != null && _meshFilter.mesh != null)
        {
            Debug.Log("[StaticMeshPCDRegistrar] PCDRendererFeature found. Registering: " + _meshFilter.mesh.name + " Mode: " + mode);
            PCDRendererFeature.Instance.AddStaticMesh(_meshFilter.sharedMesh, transform, mode);
            _isRegistered = true;
        }
    }

    // コンポーネントが無効になる、または破棄される際に登録を解除する
    private void OnDisable()
    {
        if (_isRegistered && _meshFilter != null && _meshFilter.mesh != null)
        {
            if (PCDRendererFeature.Instance != null)
            {
                PCDRendererFeature.Instance.RemoveStaticMesh(_meshFilter.mesh, transform);
                Debug.Log("[StaticMeshPCDRegistrar] Mesh unregistered: " + _meshFilter.mesh.name);
            }
        }
        _isRegistered = false;
    }
}

// 登録したメッシュの処理モード
public enum PCDProcessingMode
{
    PointCloud,  // 頂点配列を元にオクルージョン計算に巻き込む
    DepthMap     // 背景として扱い、奥行きのみを提供する
}
