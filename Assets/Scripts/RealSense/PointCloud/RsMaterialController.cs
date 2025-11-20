using UnityEngine;
using System.Collections.Generic;

public class RsMaterialController : MonoBehaviour
{
    [Tooltip("操作対象とするMeshRendererコンポーネントのリスト")]
    public List<MeshRenderer> targetRenderers;

    [Tooltip("切り替えに使用するマテリアルのリスト")]
    public List<Material> materials;

    [SerializeField, HideInInspector]
    private int _currentMaterialIndex = 0;

    void Start()
    {
        if (targetRenderers == null || targetRenderers.Count == 0)
        {
            UnityEngine.Debug.LogWarning("[RsMaterialController] 操作対象のMeshRendererが1つも設定されていません。", this);
            return;
        }

        ApplyCurrentMaterial();
    }

    public void ChangeMaterial(int index)
    {
        if (materials == null || materials.Count == 0)
        {
            UnityEngine.Debug.LogWarning("[RsMaterialController] マテリアルリストが設定されていません。", this);
            return;
        }

        if (index < 0 || index >= materials.Count)
        {
            UnityEngine.Debug.LogError($"[RsMaterialController] マテリアルのインデックスが範囲外です: {index}", this);
            return;
        }

        if (materials[index] == null)
        {
            UnityEngine.Debug.LogWarning($"[RsMaterialController] インデックス {index} のマテリアルがNULLです。", this);
            return;
        }

        _currentMaterialIndex = index;
        ApplyCurrentMaterial();
    }

    private void ApplyCurrentMaterial()
    {
        if (materials == null || materials.Count == 0 || targetRenderers == null || targetRenderers.Count == 0)
        {
            return;
        }

        if (_currentMaterialIndex >= materials.Count || materials[_currentMaterialIndex] == null)
        {
            return;
        }

        Material materialToApply = materials[_currentMaterialIndex];

        foreach (var renderer in targetRenderers)
        {
            if (renderer != null)
            {
                renderer.material = materialToApply;
            }
        }
    }

    public int GetCurrentMaterialIndex()
    {
        return _currentMaterialIndex;
    }
}