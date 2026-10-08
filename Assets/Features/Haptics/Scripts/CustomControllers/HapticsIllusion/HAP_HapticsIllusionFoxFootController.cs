using UnityEngine;
using System.Collections.Generic;

#nullable enable

/// <summary>
/// 【触覚錯覚・実験用コントローラー】
/// HAP_FoxFootHapticsController のボーン検出・接触判定ロジックを100%継承しつつ、
/// 足の接点側(AUTD #0)と「反対側/裏側 (AUTD #1)」のそれぞれから独立した単焦点/STMを照射するためのモデル。
/// </summary>
public class HAP_HapticsIllusionFoxFootController : HAP_FoxFootHapticsController
{
    [Header("Device Allocation Settings")]
    [Tooltip("接点（足の表面）へ照射を担当する AUTD デバイスグループ")]
    public HAP_AUTDDeviceGroup contactDeviceGroup = new HAP_AUTDDeviceGroup(new int[] { 0 });

    [Tooltip("接点の反対側（裏側/回り込み検証位置）へ照射を担当する AUTD デバイスグループ")]
    public HAP_AUTDDeviceGroup oppositeDeviceGroup = new HAP_AUTDDeviceGroup(new int[] { 1 });

    /// <summary>
    /// 下位互換用：接点側 AUTD インデックス
    /// </summary>
    public int contactDeviceIndex
    {
        get => (contactDeviceGroup != null && contactDeviceGroup.HasAnyDevice) ? contactDeviceGroup.SelectedDeviceIDs[0] : 0;
        set
        {
            if (contactDeviceGroup == null) contactDeviceGroup = new HAP_AUTDDeviceGroup();
            contactDeviceGroup.Clear();
            contactDeviceGroup.SetDeviceSelected(value, true);
        }
    }

    /// <summary>
    /// 下位互換用：反対側 AUTD インデックス
    /// </summary>
    public int oppositeDeviceIndex
    {
        get => (oppositeDeviceGroup != null && oppositeDeviceGroup.HasAnyDevice) ? oppositeDeviceGroup.SelectedDeviceIDs[0] : 1;
        set
        {
            if (oppositeDeviceGroup == null) oppositeDeviceGroup = new HAP_AUTDDeviceGroup();
            oppositeDeviceGroup.Clear();
            oppositeDeviceGroup.SetDeviceSelected(value, true);
        }
    }

    [Tooltip("反対側（裏側）への焦点照射を有効にするかどうか")]
    public bool enableOppositeFocus = true;

    [Header("Illusion Offset Settings")]
    [Tooltip("接点側焦点の位置オフセット (ローカルベクトル)。")]
    public Vector3 contactOffset = Vector3.zero;

    [Tooltip("反対側（裏側）焦点の位置オフセット。\n footTargetTouchDirection (デフォルト down) の逆方向(上方向)への押し込み距離や、裏側回り込み検証位置の調整に使用します。")]
    public Vector3 oppositeOffset = new Vector3(0f, 0.03f, 0f); // デフォルトで上方/裏側へ3cmオフセット

    [Header("STM Settings (Illusion Focus)")]
    [Tooltip("STM時分割回転を使用するか。false の場合は定点照射。")]
    public bool useSTM = true;

    [Tooltip("STM回転の周波数 (Hz)。例: 80Hz")]
    public float stmFrequency = 80f;

    [Tooltip("STM回転の半径 (メートル)。例: 0.005 (5mm)")]
    public float stmRadius = 0.005f;

    [Tooltip("STMの1周期あたりのサンプル点数")]
    [Range(4, 64)]
    public int stmPoints = 16;

    /// <summary>
    /// FoxFootの足検知・接触判定を維持したまま、
    /// 接点側 (contactDeviceGroup) と 反対側 (oppositeDeviceGroup) に独立した焦点データを生成して返します。
    /// </summary>
    public override List<HAP_FociGenerator.ClusterFociData> GetHapticsTargets(float defaultIntensityPascal, Vector3 offset)
    {
        var result = new List<HAP_FociGenerator.ClusterFociData>();

        // アクティブなターゲット（足・尻尾など）を取得
        foreach (var info in TargetInfos)
        {
            if (info.Transform == null) continue;
            if (!IsTargetActive(info.Transform, info.IsEnabled, info.IsTail)) continue;

            Vector3 footPos = info.Transform.position + offset;
            Vector3 normal = info.TouchDirection.normalized; // 例: down

            // 1. 接点側の焦点データ生成 (contactDeviceGroup 用)
            Vector3 contactPos = footPos + contactOffset;
            var contactFociData = CreateFociDataForGroup(
                contactPos,
                normal,
                contactDeviceGroup.SelectedDeviceIDs,
                defaultIntensityPascal
            );
            result.Add(contactFociData);

            // 2. 反対側（裏側）の焦点データ生成 (oppositeDeviceGroup 用)
            if (enableOppositeFocus)
            {
                Vector3 oppositePos = footPos + oppositeOffset;
                var oppositeFociData = CreateFociDataForGroup(
                    oppositePos,
                    -normal, // 反対向きのベクトル
                    oppositeDeviceGroup.SelectedDeviceIDs,
                    defaultIntensityPascal
                );
                result.Add(oppositeFociData);
            }
        }

        return result;
    }

    /// <summary>
    /// 指定された位置・デバイスグループ向けに ClusterFociData を構築するヘルパー
    /// </summary>
    private HAP_FociGenerator.ClusterFociData CreateFociDataForGroup(
        Vector3 position,
        Vector3 normal,
        List<int> deviceIDs,
        float intensityPascal)
    {
        TrackedCluster dummyCluster = new TrackedCluster
        {
            Centroid = position,
            Normal = normal,
            Force = 1.0f,
            IsAlive = true
        };

        var fociData = new HAP_FociGenerator.ClusterFociData(dummyCluster);
        fociData.AssignedDeviceIndices = new List<int>(deviceIDs);
        if (deviceIDs.Count > 0)
        {
            fociData.AssignedDeviceIndex = deviceIDs[0];
        }
        fociData.UseSTM = useSTM;
        fociData.STMFrequency = stmFrequency;

        if (useSTM && stmRadius > 0f && stmPoints >= 4)
        {
            Vector3 right = Vector3.Cross(normal, Vector3.up);
            if (right.sqrMagnitude < 0.001f)
            {
                right = Vector3.Cross(normal, Vector3.right);
            }
            right.Normalize();
            Vector3 up = Vector3.Cross(right, normal).normalized;

            for (int i = 0; i < stmPoints; i++)
            {
                float angle = (2.0f * Mathf.PI * i) / stmPoints;
                Vector3 p = position + (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * stmRadius;
                fociData.STMFrames.Add(new List<Vector3> { p });
            }

            fociData.SequentialFoci.Add(new HAP_FocusPoint(position, intensityPascal));
        }
        else
        {
            fociData.SequentialFoci.Add(new HAP_FocusPoint(position, intensityPascal));
        }

        return fociData;
    }

    protected override void OnDrawGizmos()
    {
        if (!ShouldDrawGizmos()) return;

        foreach (var info in TargetInfos)
        {
            if (info.Transform == null) continue;

            bool isActive = IsTargetActive(info.Transform, info.IsEnabled, info.IsTail);
            Vector3 footPos = info.Transform.position;

            // 接点側 Gizmo (緑 or 赤)
            Gizmos.color = isActive ? activeColor : inactiveColor;
            Vector3 contactPos = footPos + contactOffset;
            Gizmos.DrawWireSphere(contactPos, 0.005f);

            if (isActive && useSTM && stmRadius > 0f)
            {
                DrawSTMCircle(contactPos, footTargetTouchDirection.normalized, stmRadius, Color.cyan);
            }

            // 反対側 Gizmo (マゼンタ)
            if (enableOppositeFocus)
            {
                Vector3 oppositePos = footPos + oppositeOffset;
                Gizmos.color = isActive ? Color.magenta : Color.gray;
                Gizmos.DrawWireSphere(oppositePos, 0.005f);
                Gizmos.DrawLine(contactPos, oppositePos);

                if (isActive && useSTM && stmRadius > 0f)
                {
                    DrawSTMCircle(oppositePos, -footTargetTouchDirection.normalized, stmRadius, Color.magenta);
                }
            }

        }
    }

    private void DrawSTMCircle(Vector3 center, Vector3 normal, float radius, Color color)
    {
        Gizmos.color = color;
        Vector3 right = Vector3.Cross(normal, Vector3.up);
        if (right.sqrMagnitude < 0.001f)
        {
            right = Vector3.Cross(normal, Vector3.right);
        }
        right.Normalize();
        Vector3 up = Vector3.Cross(right, normal).normalized;

        int segments = 20;
        Vector3 prev = center + right * radius;
        for (int i = 1; i <= segments; i++)
        {
            float angle = (2.0f * Mathf.PI * i) / segments;
            Vector3 next = center + (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * radius;
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }
}
