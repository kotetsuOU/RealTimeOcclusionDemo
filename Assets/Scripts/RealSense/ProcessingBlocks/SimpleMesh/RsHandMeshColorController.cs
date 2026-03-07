using System.Collections.Generic;
using UnityEngine;

public enum RsHandMeshDisplayColorMode
{
    RealSense,
    Skin,
    Black
}

public class RsHandMeshColorController : MonoBehaviour
{
    [Header("Target Settings")]
    [Tooltip("Assign RsHandMeshMeshRenderer components.")]
    public List<MonoBehaviour> targetHandMeshRenderers;

    [Header("Color Settings")]
    [HideInInspector]
    public RsHandMeshDisplayColorMode colorMode = RsHandMeshDisplayColorMode.Skin;

    void Start()
    {
        ApplyColorMode();
    }

    public void ChangeColorMode(RsHandMeshDisplayColorMode mode)
    {
        colorMode = mode;
        ApplyColorMode();
    }

    public void ApplyColorMode()
    {
        if (targetHandMeshRenderers == null || targetHandMeshRenderers.Count == 0)
            return;

        foreach (var renderer in targetHandMeshRenderers)
        {
            if (renderer == null) continue;

            var method = renderer.GetType().GetMethod("ChangeColorMode");
            if (method != null)
            {
                var parameters = method.GetParameters();
                if (parameters.Length == 1 && parameters[0].ParameterType.IsEnum)
                {
                    object mappedValue = System.Enum.Parse(parameters[0].ParameterType, colorMode.ToString() == "RealSense" ? "Custom" : colorMode.ToString());
                    method.Invoke(renderer, new[] { mappedValue });
                }
            }
        }
    }
}
