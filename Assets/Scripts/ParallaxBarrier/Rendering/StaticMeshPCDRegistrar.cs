using UnityEngine;
using System.Collections;

[RequireComponent(typeof(MeshFilter))]
public class StaticMeshPCDRegistrar : MonoBehaviour
{
    [Tooltip("PointCloud: vertices / DepthMap: URP depth")]
    public PCDProcessingMode mode = PCDProcessingMode.DepthMap;

    private MeshFilter _meshFilter;
    private MeshRenderer _meshRenderer;
    private bool _isRegistered = false;

    private void OnEnable()
    {
        _meshFilter = GetComponent<MeshFilter>();
        _meshRenderer = GetComponent<MeshRenderer>();

        if (_meshFilter == null || _meshFilter.mesh == null)
        {
            Debug.LogError("[StaticMeshPCDRegistrar] MeshFilter or Mesh not found.", this.gameObject);
            return;
        }

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

        if (PCDRendererFeature.Instance != null)
        {
            PCDRendererFeature.Instance.AddStaticMesh(_meshFilter.sharedMesh, transform, mode);
            _isRegistered = true;
            Debug.Log("[StaticMeshPCDRegistrar] Mesh registered: " + _meshFilter.mesh.name + " Mode: " + mode);
        }
        else
        {
            Debug.LogWarning("[StaticMeshPCDRegistrar] Waiting for PCDRendererFeature: " + _meshFilter.mesh.name);
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
            Debug.Log("[StaticMeshPCDRegistrar] PCDRendererFeature found. Registering: " + _meshFilter.mesh.name + " Mode: " + mode);
            PCDRendererFeature.Instance.AddStaticMesh(_meshFilter.sharedMesh, transform, mode);
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
                Debug.Log("[StaticMeshPCDRegistrar] Mesh unregistered: " + _meshFilter.mesh.name);
            }
        }
        _isRegistered = false;
    }
}

public enum PCDProcessingMode
{
    PointCloud,
    DepthMap
}
