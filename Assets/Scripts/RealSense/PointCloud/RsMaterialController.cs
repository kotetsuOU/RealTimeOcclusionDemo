using UnityEngine;
using System.Collections.Generic;

public enum PointCloudColorMode
{
    Skin,
    Black,
    Custom
}

public class RsMaterialController : MonoBehaviour
{
    [Header("Material Settings")]
    [Tooltip("切り替えに使用するマテリアルのリスト")]
    public List<Material> materials;

    [Header("Target Settings")]
    [Tooltip("操作対象とするRsPointCloudRendererのリスト。MeshRendererはここから自動取得されます。")]
    public List<RsPointCloudRenderer> targetPointCloudRenderers;

    [Header("Color Settings")]
    [Tooltip("点群の色のモード選択")]
    [HideInInspector]
    public PointCloudColorMode colorMode = PointCloudColorMode.Skin;

    [SerializeField, HideInInspector]
    private int _currentMaterialIndex = 0;

    private List<MeshRenderer> _cachedMeshRenderers = new List<MeshRenderer>();

    private Dictionary<RsPointCloudRenderer, Color> _initialColors = new Dictionary<RsPointCloudRenderer, Color>();

    private readonly Color _skinColor = new Color(241f / 255f, 187f / 255f, 147f / 255f, 1f);
    private readonly Color _blackColor = Color.black;

    void Start()
    {
        InitializeRenderers();
        ApplyCurrentMaterial();
        ApplyColorMode();
    }

    private void InitializeRenderers()
    {
        _cachedMeshRenderers.Clear();
        _initialColors.Clear();

        if (targetPointCloudRenderers == null) return;

        foreach (var pcRenderer in targetPointCloudRenderers)
        {
            if (pcRenderer != null)
            {
                var meshRenderer = pcRenderer.GetComponent<MeshRenderer>();

                if (meshRenderer != null)
                {
                    _cachedMeshRenderers.Add(meshRenderer);
                }
                else
                {
                    Debug.LogWarning($"[RsMaterialController] {pcRenderer.name} に MeshRenderer が見つかりません。", pcRenderer);
                }

                if (!_initialColors.ContainsKey(pcRenderer))
                {
                    _initialColors.Add(pcRenderer, pcRenderer.pointCloudColor);
                }
            }
        }
    }

    public void ChangeMaterial(int index)
    {
        if (materials == null || materials.Count == 0)
        {
            Debug.LogWarning("[RsMaterialController] マテリアルリストが設定されていません。", this);
            return;
        }

        if (index < 0 || index >= materials.Count)
        {
            Debug.LogError($"[RsMaterialController] マテリアルのインデックスが範囲外です: {index}", this);
            return;
        }

        if (materials[index] == null)
        {
            Debug.LogWarning($"[RsMaterialController] インデックス {index} のマテリアルがNULLです。", this);
            return;
        }

        _currentMaterialIndex = index;
        ApplyCurrentMaterial();
    }

    private void ApplyCurrentMaterial()
    {
        if (materials == null || materials.Count == 0 || _cachedMeshRenderers.Count == 0)
        {
            return;
        }

        if (_currentMaterialIndex >= materials.Count || materials[_currentMaterialIndex] == null)
        {
            return;
        }

        Material materialToApply = materials[_currentMaterialIndex];

        foreach (var renderer in _cachedMeshRenderers)
        {
            if (renderer != null)
            {
                renderer.material = materialToApply;
            }
        }
    }

    public void ChangeColorMode(PointCloudColorMode mode)
    {
        this.colorMode = mode;
        ApplyColorMode();
    }

    public void ApplyColorMode()
    {
        if (targetPointCloudRenderers == null || targetPointCloudRenderers.Count == 0)
        {
            return;
        }

        foreach (var pRenderer in targetPointCloudRenderers)
        {
            if (pRenderer == null) continue;

            Color targetColor = Color.white;
            bool applyColor = true;

            switch (colorMode)
            {
                case PointCloudColorMode.Skin:
                    targetColor = _skinColor;
                    break;
                case PointCloudColorMode.Black:
                    targetColor = _blackColor;
                    break;
                case PointCloudColorMode.Custom:
                    if (_initialColors.TryGetValue(pRenderer, out Color originalColor))
                    {
                        targetColor = originalColor;
                    }
                    else
                    {
                        applyColor = false;
                    }
                    break;
                default:
                    applyColor = false;
                    break;
            }

            if (applyColor)
            {
                pRenderer.pointCloudColor = targetColor;
            }
        }
    }

    public int GetCurrentMaterialIndex()
    {
        return _currentMaterialIndex;
    }
}