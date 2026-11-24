using UnityEngine;

#nullable enable

/// <summary>
/// 触覚錯覚・実験用カスタム焦点設定。
/// デバイスグループごとに独立した焦点位置・STM 設定を管理します。
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

