using System.Collections.Generic;
using UnityEngine;
using Features.Haptics.Debug;
using Features.Haptics.Processors;

#nullable enable

/// <summary>
/// 触覚錯覚・実験用 Fox 足部コントローラー。
/// <see cref="HAP_FoxFootHapticsController"/> のボーン検出・接触判定を維持しつつ、
/// 接点側と反対側（裏側）へ独立した単焦点／STM を生成します。
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
    public Vector3 oppositeOffset = new Vector3(0f, 0.03f, 0f);

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
    /// 接点側および反対側のデバイスグループ向けに独立した焦点データを生成して返します。
    /// </summary>
    public override List<HAP_FociGenerator.ClusterFociData> GetHapticsTargets(float defaultIntensityPascal, Vector3 offset)
    {
        var result = new List<HAP_FociGenerator.ClusterFociData>();

        foreach (var info in TargetInfos)
        {
            if (info.Transform == null) continue;
            if (!IsTargetActive(info.Transform, info.IsEnabled, info.IsTail)) continue;

            Vector3 footPos = info.Transform.position + offset;
            Vector3 normal = info.TouchDirection.normalized;

            result.Add(HAP_IllusionFociBuilder.BuildForDirectionalGroup(
                footPos + contactOffset,
                normal,
                contactDeviceGroup.SelectedDeviceIDs,
                defaultIntensityPascal,
                useSTM,
                stmFrequency,
                stmRadius,
                stmPoints));

            if (enableOppositeFocus)
            {
                result.Add(HAP_IllusionFociBuilder.BuildForDirectionalGroup(
                    footPos + oppositeOffset,
                    -normal,
                    oppositeDeviceGroup.SelectedDeviceIDs,
                    defaultIntensityPascal,
                    useSTM,
                    stmFrequency,
                    stmRadius,
                    stmPoints));
            }
        }

        return result;
    }

    protected override void OnDrawGizmos()
    {
        if (!ShouldDrawGizmos()) return;
        HAP_IllusionGizmoDrawer.DrawFoxFootIllusionGizmos(this);
    }
}
