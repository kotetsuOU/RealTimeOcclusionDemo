using System.Collections.Generic;
using UnityEngine;
using Core.Logging;

public enum PointCloudColorMode
{
    Skin,
    Black,
    Blue,
    Custom,
    Original
}

[AppLoggable("RealSense (Pipeline)")]
public class RsMaterialController : MonoBehaviour
{
    [Header("Material Settings")]
    [Tooltip("適用するマテリアル")]
    public Material material;

    [Header("Color Settings")]
    [Tooltip("点群の色のモード選択")]
    public PointCloudColorMode colorMode = PointCloudColorMode.Skin;

    [Tooltip("Custom モード選択時に適用されるカラー")]
    public Color customColor = Color.white;

    private List<MeshRenderer> _cachedMeshRenderers = new List<MeshRenderer>();
    private Dictionary<RsPointCloudRenderer, Color> _initialColors = new Dictionary<RsPointCloudRenderer, Color>();

    private readonly Color _skinColor = new Color(241f / 255f, 187f / 255f, 147f / 255f, 1f);
    private readonly Color _blackColor = Color.black;
    private readonly Color _blueColor = Color.blue;

    private RsGlobalPointCloudManager _globalManager;

    private RsGlobalPointCloudManager GlobalManager
    {
        get
        {
            if (_globalManager == null)
            {
                _globalManager = RsGlobalPointCloudManager.Instance;
                if (_globalManager == null)
                {
                    _globalManager = GetComponent<RsGlobalPointCloudManager>();
                }
            }
            return _globalManager;
        }
    }

    private void Start()
    {
        InitializeRenderers();
        ApplyMaterial();
        ApplyColorMode();
    }

    private void OnValidate()
    {
        if (Application.isPlaying)
        {
            ApplyMaterial();
            ApplyColorMode();
        }
    }

    public void InitializeRenderers()
    {
        _cachedMeshRenderers.Clear();
        _initialColors.Clear();

        IEnumerable<RsPointCloudRenderer> renderers = null;
        if (GlobalManager != null)
        {
            renderers = GlobalManager.GetChildRenderers();
        }
        else
        {
#if UNITY_2023_1_OR_NEWER
            renderers = FindObjectsByType<RsPointCloudRenderer>(FindObjectsSortMode.None);
#else
            renderers = FindObjectsOfType<RsPointCloudRenderer>();
#endif
        }

        foreach (var pcRenderer in renderers)
        {
            if (pcRenderer != null)
            {
                var meshRenderer = pcRenderer.GetComponent<MeshRenderer>();
                if (meshRenderer != null)
                {
                    if (!_cachedMeshRenderers.Contains(meshRenderer))
                    {
                        _cachedMeshRenderers.Add(meshRenderer);
                    }
                }
                else
                {
                    AppLogger.LogWarning(this, $"{pcRenderer.name} に MeshRenderer が見つかりません。");
                }

                if (!_initialColors.ContainsKey(pcRenderer))
                {
                    _initialColors.Add(pcRenderer, pcRenderer.pointCloudColor);
                }
            }
        }
    }

    public void ApplyMaterial()
    {
        if (_cachedMeshRenderers.Count == 0)
        {
            InitializeRenderers();
        }

        if (_cachedMeshRenderers.Count == 0) return;

        foreach (var renderer in _cachedMeshRenderers)
        {
            if (renderer != null && material != null)
            {
                renderer.material = material;
            }
        }

        AppLogger.Log(this, $"Applied material '{material?.name}' to {_cachedMeshRenderers.Count} renderers.");
    }

    public void ChangeColorMode(PointCloudColorMode mode)
    {
        this.colorMode = mode;
        ApplyColorMode();
    }

    public void SetColor(Color color)
    {
        this.customColor = color;
        this.colorMode = PointCloudColorMode.Custom;
        ApplyColorMode();
    }

    public void ApplyColorMode()
    {
        IEnumerable<RsPointCloudRenderer> renderers = null;
        if (GlobalManager != null)
        {
            renderers = GlobalManager.GetChildRenderers();
        }
        else
        {
#if UNITY_2023_1_OR_NEWER
            renderers = FindObjectsByType<RsPointCloudRenderer>(FindObjectsSortMode.None);
#else
            renderers = FindObjectsOfType<RsPointCloudRenderer>();
#endif
        }

        int updatedCount = 0;
        foreach (var pRenderer in renderers)
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
                case PointCloudColorMode.Blue:
                    targetColor = _blueColor;
                    break;
                case PointCloudColorMode.Custom:
                    targetColor = customColor;
                    break;
                case PointCloudColorMode.Original:
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
                updatedCount++;
            }
        }

        AppLogger.Log(this, $"Applied color mode '{colorMode}' to {updatedCount} renderers.");
    }
}
