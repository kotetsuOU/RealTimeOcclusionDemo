using UnityEngine;
using System.Collections.Generic;

#nullable enable

/// <summary>
/// 【触覚錯覚・実験用焦点設定】
/// デバイスインデックスごとに独立した焦点位置・STM設定を管理する構造体。
/// </summary>
[System.Serializable]
public class HapticsIllusionTargetConfig
{
    [Tooltip("焦点の識別名（デバッグ用）")]
    public string focusName = "Illusion Focus Target";

    [Tooltip("焦点照射の目標となる Transform（接点、または接点以外の任意位置）")]
    public Transform? targetTransform;

    [Tooltip("この焦点を担当する AUTD デバイスグループ")]
    public HAP_AUTDDeviceGroup assignedDeviceGroup = new HAP_AUTDDeviceGroup(new int[] { 0 });

    /// <summary>
    /// 下位互換用：担当 AUTD インデックス
    /// </summary>
    public int assignedDeviceIndex
    {
        get => (assignedDeviceGroup != null && assignedDeviceGroup.HasAnyDevice) ? assignedDeviceGroup.SelectedDeviceIDs[0] : 0;
        set
        {
            if (assignedDeviceGroup == null) assignedDeviceGroup = new HAP_AUTDDeviceGroup();
            assignedDeviceGroup.Clear();
            assignedDeviceGroup.SetDeviceSelected(value, true);
        }
    }

    [Tooltip("焦点位置のローカル/法線方向オフセット (メートル)。\n表面: 0, 内側(めり込み): マイナス, 外側: プラス")]
    public Vector3 offsetPosition = Vector3.zero;

    [Tooltip("焦点生成の有効/無効")]
    public bool isEnabled = true;

    [Header("Acoustic / STM Settings")]
    [Tooltip("時分割焦点回転 (STM) を使用するかどうか。false の場合は定点照射。")]
    public bool useSTM = true;

    [Tooltip("STM再生周波数 (Hz)。例: 80Hz")]
    public float stmFrequency = 80f;

    [Tooltip("STM回転軌跡の半径 (メートル)。例: 0.005 (5mm)")]
    public float stmRadius = 0.005f;

    [Tooltip("STM1周期あたりの分割点数。")]
    [Range(4, 64)]
    public int stmPoints = 16;

    [Tooltip("超音波音圧強度 (Pascal)。0 で全系の defaultIntensityPascal を使用。")]
    public float focusIntensityPascal = 0f;
}

/// <summary>
/// 【触覚錯覚・実験用カスタムコントローラー】
/// GSPAT等の多焦点干渉計算を行わず、複数台のAUTDデバイスそれぞれに独立した単焦点（Focus / FocusSTM）を割り当てるための汎用コントローラー。
/// HAP_AUTDHapticsController の objectHapticsControllers (objects) リストに追加してシリアライズ使用できます。
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
            var list = new List<HapticsTargetInfo>();
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
        var result = new List<HAP_FociGenerator.ClusterFociData>();

        foreach (var cfg in focusConfigs)
        {
            if (!cfg.isEnabled || cfg.targetTransform == null || !cfg.targetTransform.gameObject.activeInHierarchy)
            {
                continue;
            }

            float intensity = cfg.focusIntensityPascal > 0 ? cfg.focusIntensityPascal : defaultIntensityPascal;
            Vector3 centerPos = cfg.targetTransform.position + cfg.targetTransform.TransformDirection(cfg.offsetPosition) + offset;

            TrackedCluster dummyCluster = new TrackedCluster
            {
                Centroid = centerPos,
                Normal = cfg.targetTransform.forward,
                Force = 1.0f,
                IsAlive = true
            };

            var fociData = new HAP_FociGenerator.ClusterFociData(dummyCluster);
            fociData.AssignedDeviceIndices = new List<int>(cfg.assignedDeviceGroup.SelectedDeviceIDs);
            if (cfg.assignedDeviceGroup.HasAnyDevice)
            {
                fociData.AssignedDeviceIndex = cfg.assignedDeviceGroup.SelectedDeviceIDs[0];
            }
            fociData.UseSTM = cfg.useSTM;
            fociData.STMFrequency = cfg.stmFrequency;

            if (cfg.useSTM && cfg.stmRadius > 0f && cfg.stmPoints >= 4)
            {
                Vector3 normal = cfg.targetTransform.forward;
                Vector3 right = cfg.targetTransform.right;
                Vector3 up = cfg.targetTransform.up;

                for (int i = 0; i < cfg.stmPoints; i++)
                {
                    float angle = (2.0f * Mathf.PI * i) / cfg.stmPoints;
                    Vector3 p = centerPos + (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * cfg.stmRadius;
                    fociData.STMFrames.Add(new List<Vector3> { p });
                }

                fociData.SequentialFoci.Add(new HAP_FocusPoint(centerPos, intensity));
            }
            else
            {
                fociData.SequentialFoci.Add(new HAP_FocusPoint(centerPos, intensity));
            }

            result.Add(fociData);
        }

        return result;
    }

    protected override void OnDrawGizmos()
    {
        if (!ShouldDrawGizmos()) return;

        // autdController から Disabler / DirectionalGrouping の情報を取得
        HAP_AUTDHapticsController? hapticsCtrl = autdController;
#if UNITY_EDITOR
        if (hapticsCtrl == null)
        {
            hapticsCtrl = UnityEngine.Object.FindAnyObjectByType<HAP_AUTDHapticsController>();
        }
#endif
        HAP_AUTDDebugDisabler? disabler = hapticsCtrl != null
            ? hapticsCtrl.GetComponent<HAP_AUTDDebugDisabler>()
            : null;
        bool useDirectional = hapticsCtrl != null && hapticsCtrl.enableDirectionalGrouping;
        float angleThreshold = hapticsCtrl != null ? hapticsCtrl.directionalAngleThreshold : 45f;

        Color[] activeColors = new Color[] { Color.cyan, Color.magenta, Color.yellow, Color.green };
        int idx = 0;

        foreach (var cfg in focusConfigs)
        {
            if (cfg.targetTransform == null) continue;

            Vector3 centerPos = cfg.targetTransform.position + cfg.targetTransform.TransformDirection(cfg.offsetPosition);
            var deviceIds = cfg.assignedDeviceGroup != null ? cfg.assignedDeviceGroup.SelectedDeviceIDs : new System.Collections.Generic.List<int>();

            // --- 優先度に沿った照射可否判定 ---
            // 優先度1: cfg.isEnabled が false → 無効（グレー）
            Color gizmoColor;
            string statusLabel;

            if (!cfg.isEnabled)
            {
                gizmoColor = Color.gray;
                statusLabel = "[disabled]";
            }
            else
            {
                // 優先度2: Disabler で割当デバイスが全滅しているか確認
                bool allDisabledByDisabler = disabler != null
                    && deviceIds.Count > 0
                    && deviceIds.TrueForAll(id => disabler.IsDisabled(id));

                if (allDisabledByDisabler)
                {
                    // 全担当デバイスがDisablerで無効 → 赤（照射不可）
                    gizmoColor = new Color(1f, 0.2f, 0.2f, 1f);
                    statusLabel = "[all devices disabled]";
                }
                else if (useDirectional && deviceIds.Count > 0)
                {
                    // 優先度3: DirectionalGrouping 有効時、角度閾値内の候補があるか確認
                    // Disablerで有効なデバイスのうち、角度条件を満たすものが1つでもあればOK
                    var sceneDevices = UnityEngine.Object.FindObjectsByType<AUTD3Device>(FindObjectsSortMode.None);
                    bool anyInRange = false;
                    foreach (var dev in sceneDevices)
                    {
                        if (disabler != null && disabler.IsDisabled(dev.ID)) continue;
                        if (!deviceIds.Contains(dev.ID)) continue;
                        float angle = Vector3.Angle(dev.transform.forward, -cfg.targetTransform.forward);
                        if (angle <= angleThreshold)
                        {
                            anyInRange = true;
                            break;
                        }
                    }

                    if (!anyInRange)
                    {
                        // 全担当デバイスが角度NG → 橙（DirectionalGroupingで照射不可）
                        gizmoColor = new Color(1f, 0.6f, 0f, 1f);
                        statusLabel = "[angle NG]";
                    }
                    else
                    {
                        gizmoColor = activeColors[idx % activeColors.Length];
                        statusLabel = "";
                    }
                }
                else
                {
                    gizmoColor = activeColors[idx % activeColors.Length];
                    statusLabel = "";
                }
            }

            // --- Gizmo描画 ---
            Gizmos.color = gizmoColor;
            Gizmos.DrawWireSphere(centerPos, 0.003f);

            if (cfg.useSTM && cfg.stmRadius > 0f)
            {
                Vector3 right = cfg.targetTransform.right;
                Vector3 up = cfg.targetTransform.up;
                int segments = 24;
                Vector3 prevPoint = centerPos + right * cfg.stmRadius;

                for (int i = 1; i <= segments; i++)
                {
                    float angle = (2.0f * Mathf.PI * i) / segments;
                    Vector3 nextPoint = centerPos + (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * cfg.stmRadius;
                    Gizmos.DrawLine(prevPoint, nextPoint);
                    prevPoint = nextPoint;
                }
            }

#if UNITY_EDITOR
            // 担当デバイスIDとステータスをラベル表示
            string deviceLabel = deviceIds.Count > 0
                ? $"AUTD [{string.Join(",", deviceIds)}]"
                : "AUTD [none]";
            if (!string.IsNullOrEmpty(statusLabel)) deviceLabel += " " + statusLabel;
            UnityEditor.Handles.color = gizmoColor;
            UnityEditor.Handles.Label(centerPos + Vector3.up * 0.015f, deviceLabel);
#endif

            idx++;
        }
    }
}
