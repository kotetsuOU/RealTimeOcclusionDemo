using System.Collections.Generic;
using UnityEngine;
using Features.Haptics.Debug;
using Features.Haptics.Processors;

#nullable enable

/// <summary>
/// 触覚錯覚・実験用カスタムコントローラー。
/// 複数台の AUTD デバイスそれぞれに独立した単焦点（Focus / FociSTM）を割り当てます。
/// 焦点計算は <see cref="HAP_IllusionFociBuilder"/>、可視化は <see cref="HAP_IllusionGizmoDrawer"/> へ委譲します。
/// </summary>
public class HAP_HapticsIllusionCustomController : HAP_BaseObjectHapticsController
{
    [Header("Illusion Focus Configurations")]
    [Tooltip("各AUTDデバイスに割り当てる独立焦点のリスト設定")]
    public List<HapticsIllusionTargetConfig> focusConfigs = new List<HapticsIllusionTargetConfig>
    {
        new HapticsIllusionTargetConfig
        {
            focusName = "Contact Point Focus (AUTD #0)",
            assignedDeviceGroup = new HAP_AUTDDeviceGroup(new int[] { 0 }),
            useSTM = true,
            stmFrequency = 80f,
            stmRadius = 0.005f
        },
        new HapticsIllusionTargetConfig
        {
            focusName = "Non-Contact / Opposite Focus (AUTD #1)",
            assignedDeviceGroup = new HAP_AUTDDeviceGroup(new int[] { 1 }),
            useSTM = true,
            stmFrequency = 80f,
            stmRadius = 0.005f
        }
    };

    public override List<HapticsTargetInfo> TargetInfos
    {
        get
        {
            var list = new List<HapticsTargetInfo>(focusConfigs.Count);
            foreach (var cfg in focusConfigs)
            {
                if (cfg.targetTransform != null)
                {
                    list.Add(new HapticsTargetInfo
                    {
                        Name = cfg.focusName,
                        Transform = cfg.targetTransform,
                        IsEnabled = cfg.isEnabled,
                        IsTail = true,
                        TouchDirection = cfg.targetTransform.forward
                    });
                }
            }
            return list;
        }
    }

    public override bool HasActiveTargets()
    {
        foreach (var cfg in focusConfigs)
        {
            if (cfg.isEnabled && cfg.targetTransform != null && cfg.targetTransform.gameObject.activeInHierarchy)
            {
                return true;
            }
        }
        return false;
    }

    public override List<HAP_FociGenerator.ClusterFociData> GetHapticsTargets(float defaultIntensityPascal, Vector3 offset)
    {
        var result = new List<HAP_FociGenerator.ClusterFociData>(focusConfigs.Count);
        foreach (var cfg in focusConfigs)
        {
            if (!cfg.isEnabled || cfg.targetTransform == null || !cfg.targetTransform.gameObject.activeInHierarchy)
            {
                continue;
            }
            result.Add(HAP_IllusionFociBuilder.BuildFromTargetConfig(cfg, defaultIntensityPascal, offset));
        }
        return result;
    }

    protected override void OnDrawGizmos()
    {
        if (!ShouldDrawGizmos()) return;
        HAP_IllusionGizmoDrawer.DrawCustomIllusionGizmos(this);
    }
}
