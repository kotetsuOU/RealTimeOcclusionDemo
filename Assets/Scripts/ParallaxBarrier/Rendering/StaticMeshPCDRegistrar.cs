using UnityEngine;
using System.Collections;

[RequireComponent(typeof(MeshFilter))]
public class StaticMeshPCDRegistrar : MonoBehaviour
{
    private MeshFilter _meshFilter;
    private bool _isRegistered = false;

    private void OnEnable()
    {
        _meshFilter = GetComponent<MeshFilter>();

        if (_meshFilter == null || _meshFilter.mesh == null)
        {
            UnityEngine.Debug.LogError($"[StaticMeshPCDRegistrar] MeshFilter または Mesh が見つかりません。", this.gameObject);
            return;
        }

        if (_isRegistered) return;

        if (PCDRendererFeature.Instance != null)
        {
            PCDRendererFeature.Instance.AddStaticMesh(_meshFilter.mesh, transform);
            _isRegistered = true;
            UnityEngine.Debug.Log($"[StaticMeshPCDRegistrar] メッシュ '{_meshFilter.mesh.name}' を即時登録しました (Transform: '{transform.name}')。");
        }
        else
        {
            UnityEngine.Debug.LogWarning($"[StaticMeshPCDRegistrar] PCDRendererFeature のインスタンス待機中: '{_meshFilter.mesh.name}'");
            StartCoroutine(RegisterWhenReady());
        }
    }

    private IEnumerator RegisterWhenReady()
    {
        while (PCDRendererFeature.Instance == null)
        {
            yield return null;
        }

        if (!_isRegistered && _meshFilter != null && _meshFilter.mesh != null)
        {
            UnityEngine.Debug.Log($"[StaticMeshPCDRegistrar] PCDRendererFeature インスタンスを発見。メッシュ '{_meshFilter.mesh.name}' を登録します (Transform: '{transform.name}')。");
            PCDRendererFeature.Instance.AddStaticMesh(_meshFilter.mesh, transform);
            _isRegistered = true;
        }
    }

    private void OnDisable()
    {
        if (_isRegistered && _meshFilter != null && _meshFilter.mesh != null)
        {
            if (PCDRendererFeature.Instance != null)
            {
                PCDRendererFeature.Instance.RemoveStaticMesh(_meshFilter.mesh, transform);
                UnityEngine.Debug.Log($"[StaticMeshPCDRegistrar] メッシュ '{_meshFilter.mesh.name}' を解除しました (Transform: '{transform.name}')。");
            }
        }
        _isRegistered = false;
    }
}