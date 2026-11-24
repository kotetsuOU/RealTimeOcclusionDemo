#nullable enable
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Features.Haptics.Debug;
using Features.Haptics.Hardware;

/// <summary>
/// AUTD デバイスのワイヤーフレーム可視化・マテリアル色分け・仮想オブジェクトへのグルーピング可視化を統合制御するユーティリティクラス。
/// シーン探索は <see cref="HAP_GizmoSceneTargetResolver"/>、表面 OBB 描画は <see cref="HAP_SurfaceMappingGizmoDrawer"/> へ委譲します。
/// </summary>
public static class HAP_GizmoVisualizer
{
    private static readonly MaterialPropertyBlock ReusablePropertyBlock = new MaterialPropertyBlock();

    /// <summary>
    /// AUTD デバイス群のグループ分け、枠線・ラベル・マテリアル色分け、および対象表面マッピングを描画します。
    /// </summary>
    public static void DrawDevicesAndGroupings(
        AUTD3Device[] devices,
        bool enableDirectionalGrouping,
        float directionalAngleThreshold,
        HCD_Pipeline? hcdPipeline,
        HAP_AUTDDebugDisabler? debugDisabler = null,
        HAP_HapticsIllusionCustomController? illusionController = null)
    {
        var sortedDevices = devices.OrderBy(d => d.ID).ToArray();
        var deviceGroups = GroupDevicesByOrientation(sortedDevices);

        for (int groupIndex = 0; groupIndex < deviceGroups.Count; groupIndex++)
        {
            var group = deviceGroups[groupIndex];
            Color groupColor = enableDirectionalGrouping && deviceGroups.Count > 0
                ? Color.HSVToRGB((float)groupIndex / deviceGroups.Count, 0.8f, 1f)
                : new Color(0.2f, 0.2f, 0.8f, 1f);
            groupColor.a = 0.5f;

            foreach (var device in group)
            {
                DrawSingleDeviceGizmo(device, groupColor, enableDirectionalGrouping, debugDisabler, illusionController);
            }
        }

        if (enableDirectionalGrouping)
        {
            GameObject? activeObj = HAP_GizmoSceneTargetResolver.ResolveActiveVirtualObject(hcdPipeline);
            if (activeObj != null)
            {
                HAP_SurfaceMappingGizmoDrawer.DrawVirtualObjectSurfaceMapping(
                    activeObj,
                    deviceGroups,
                    directionalAngleThreshold,
                    debugDisabler);
            }
        }
    }

    /// <summary>
    /// <see cref="HAP_AUTDHapticsController"/> からの Gizmo 描画要求を受け取り、デバイスおよびグルーピングの可視化を行います。
    /// </summary>
    public static void DrawControllerGizmos(HAP_AUTDHapticsController controller)
    {
        if (controller == null || !controller.visualizeDevices) return;

        var devices = HAP_GizmoSceneTargetResolver.GetCachedDevices();
        if (devices.Length == 0) return;

#if UNITY_EDITOR
        var disabler = controller.debugDisabler
                    ?? controller.GetComponent<HAP_AUTDDebugDisabler>()
                    ?? controller.GetComponentInChildren<HAP_AUTDDebugDisabler>();

        HAP_HapticsIllusionCustomController? illusionCtrl = null;
        if (controller.sourceMode == HapticsSourceMode.ObjectTarget && controller.objectHapticsControllers != null)
        {
            controller.objectHapticsControllers.RemoveAll(c => c == null);
            if (controller.objectHapticsControllers.Count > 0)
            {
                int validIdx = Mathf.Clamp(controller.activeObjectControllerIndex, 0, controller.objectHapticsControllers.Count - 1);
                illusionCtrl = controller.objectHapticsControllers[validIdx] as HAP_HapticsIllusionCustomController;
            }
        }

        DrawDevicesAndGroupings(
            devices,
            controller.enableDirectionalGrouping,
            controller.directionalAngleThreshold,
            controller.hcdPipeline,
            disabler,
            illusionCtrl);
#endif
    }

    private static List<List<AUTD3Device>> GroupDevicesByOrientation(AUTD3Device[] sortedDevices)
    {
        var deviceGroups = new List<List<AUTD3Device>>();
        foreach (var device in sortedDevices)
        {
            bool added = false;
            Vector3 dForward = HAP_DeviceOrientationHelper.GetAdjustedForward(device);
            foreach (var group in deviceGroups)
            {
                Vector3 gForward = HAP_DeviceOrientationHelper.GetAdjustedForward(group[0]);
                if (Vector3.Angle(gForward, dForward) < 1.0f)
                {
                    group.Add(device);
                    added = true;
                    break;
                }
            }

            if (!added)
            {
                deviceGroups.Add(new List<AUTD3Device> { device });
            }
        }
        return deviceGroups;
    }

    private static void DrawSingleDeviceGizmo(
        AUTD3Device device,
        Color groupColor,
        bool enableDirectionalGrouping,
        HAP_AUTDDebugDisabler? debugDisabler,
        HAP_HapticsIllusionCustomController? illusionController)
    {
        bool isDisabled = debugDisabler != null && debugDisabler.IsDisabled(device.ID);

        bool isInIllusionGroup = false;
        List<string>? illusionGroupNames = null;
        if (illusionController != null)
        {
            foreach (var cfg in illusionController.focusConfigs)
            {
                if (cfg.assignedDeviceGroup != null && cfg.assignedDeviceGroup.SelectedDeviceIDs.Contains(device.ID))
                {
                    isInIllusionGroup = true;
                    illusionGroupNames ??= new List<string>();
                    illusionGroupNames.Add(cfg.focusName);
                }
            }
        }

        Gizmos.matrix = Matrix4x4.TRS(device.transform.position, device.transform.rotation, Vector3.one);
        Gizmos.color = isDisabled ? new Color(0.3f, 0.3f, 0.3f, 0.8f) : groupColor;
        Gizmos.DrawWireCube(new Vector3(0.096f, 0.075f, 0), new Vector3(0.192f, 0.151f, 0.01f));

        if (isInIllusionGroup && !isDisabled)
        {
            Gizmos.color = new Color(1f, 1f, 1f, 0.9f);
            Gizmos.DrawWireCube(new Vector3(0.096f, 0.075f, 0), new Vector3(0.182f, 0.141f, 0.012f));
        }

#if UNITY_EDITOR
        if (isInIllusionGroup && illusionGroupNames != null)
        {
            Gizmos.matrix = Matrix4x4.identity;
            Vector3 worldPos = device.transform.TransformPoint(new Vector3(0.096f, 0.151f + 0.02f, 0));
            UnityEditor.Handles.color = isDisabled ? Color.gray : Color.white;
            string label = $"AUTD#{device.ID}: {string.Join(", ", illusionGroupNames)}";
            if (isDisabled) label += " [DISABLED]";
            UnityEditor.Handles.Label(worldPos, label);
            Gizmos.matrix = Matrix4x4.TRS(device.transform.position, device.transform.rotation, Vector3.one);
        }
#endif

        ApplyDeviceMaterialTint(device, groupColor, enableDirectionalGrouping, isDisabled);
    }

    private static void ApplyDeviceMaterialTint(
        AUTD3Device device,
        Color groupColor,
        bool enableDirectionalGrouping,
        bool isDisabled)
    {
        var renderers = device.GetComponentsInChildren<Renderer>();
        foreach (var renderer in renderers)
        {
            renderer.GetPropertyBlock(ReusablePropertyBlock);

            if (isDisabled)
            {
                Color disabledColor = new Color(0.2f, 0.2f, 0.2f, 0.5f);
                ReusablePropertyBlock.SetColor("_Color", disabledColor);
                ReusablePropertyBlock.SetColor("_BaseColor", disabledColor);
            }
            else if (enableDirectionalGrouping)
            {
                Color solidColor = new Color(groupColor.r, groupColor.g, groupColor.b, 1f);
                ReusablePropertyBlock.SetColor("_Color", solidColor);
                ReusablePropertyBlock.SetColor("_BaseColor", solidColor);
            }
            else
            {
                renderer.SetPropertyBlock(null);
                continue;
            }

            renderer.SetPropertyBlock(ReusablePropertyBlock);
        }
    }
}
