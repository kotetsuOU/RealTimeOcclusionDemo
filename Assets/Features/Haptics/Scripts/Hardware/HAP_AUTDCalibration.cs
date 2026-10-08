using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Core.Logging;
using Features.Haptics.Debug;

#nullable enable

/// <summary>
/// AUTD3 デバイスの物理配置と Unity 空間座標とのキャリブレーション（位置合わせ）を行うコンポーネント。
/// 単焦点または多焦点テスト信号を送信し、オフセットの計算やデバイス Transform へのベイクを支援します。
/// </summary>
public class HAP_AUTDCalibration : MonoBehaviour
{
    public HAP_AUTDHapticsController hapticsController = null!;
    public HAP_AUTDHardwareController hardwareController = null!;
    public HAP_AUTDTransformLoader transformLoader = null!;

    [Header("Calibration Mode")]
    [Tooltip("有効時、通常のハプティクス出力をバイパスし、下記のキャリブレーション焦点を照射します。")]
    public bool enableCalibration = false;

    [Header("Target Devices")]
    [Tooltip("各 AUTD デバイスの照射 ON/OFF トグルリスト。")]
    public List<bool> targetDevices = new List<bool>();

    [Header("Focus Settings")]
    public bool useMultiFocus = false;
    [Tooltip("Transform が割り当てられている場合、singleFocusPosition の代わりにその位置が使用されます。")]
    public Transform? singleFocusTarget;
    public Vector3 singleFocusPosition = Vector3.zero;
    public List<Vector3> multiFocusPositions = new List<Vector3> { Vector3.zero };

    [Tooltip("正解位置（リファレンス）。ApplyOffsetByDifference 実行時の基準点となります。")]
    public Transform? truePositionTarget;

    [Range(0f, 1f)]
    public float focusAmplitude = 1f;

    void Awake()
    {
        if (hapticsController == null) hapticsController = FindAnyObjectByType<HAP_AUTDHapticsController>();
        if (hardwareController == null) hardwareController = FindAnyObjectByType<HAP_AUTDHardwareController>();
        if (transformLoader == null)
        {
            if (hapticsController != null && hapticsController.transformLoader != null)
                transformLoader = hapticsController.transformLoader;
            else
                transformLoader = FindAnyObjectByType<HAP_AUTDTransformLoader>();
        }
    }

    void Update()
    {
        if (hapticsController == null || hardwareController == null) return;

        hapticsController.bypassHaptics = enableCalibration;

        if (enableCalibration)
        {
            EmitCalibrationFocus();
        }
    }

    private void EmitCalibrationFocus()
    {
        if (targetDevices == null || targetDevices.Count == 0 || hardwareController == null || hardwareController.Backend == null || !hardwareController.IsConnected)
            return;

        var connectedDevices = hardwareController.ConnectedDevices;
        Vector3 offset = transformLoader != null ? transformLoader.offset : Vector3.zero;
        float intensityPa = focusAmplitude * 10000f;

        var assignedIndices = new List<int>();
        for (int i = 0; i < targetDevices.Count && i < connectedDevices.Count; i++)
        {
            if (targetDevices[i])
            {
                assignedIndices.Add(connectedDevices[i].ID);
            }
        }
        if (assignedIndices.Count == 0) return;

        var dummyCluster = new TrackedCluster
        {
            Centroid = Vector3.zero,
            Normal = Vector3.up,
            Force = 1.0f,
            IsAlive = true
        };

        var fociData = new HAP_FociGenerator.ClusterFociData(dummyCluster);
        fociData.AssignedDeviceIndices = assignedIndices;
        fociData.UseSTM = false;

        if (useMultiFocus && multiFocusPositions.Count > 0)
        {
            foreach (var p in multiFocusPositions)
            {
                fociData.SequentialFoci.Add(new HAP_FocusPoint(p + offset, intensityPa));
            }
        }
        else
        {
            Vector3 pos = singleFocusTarget != null ? singleFocusTarget.position : singleFocusPosition;
            fociData.SequentialFoci.Add(new HAP_FocusPoint(pos + offset, intensityPa));
        }

        var fociList = new List<HAP_FociGenerator.ClusterFociData> { fociData };
        var debugDisabler = hapticsController != null ? hapticsController.debugDisabler : null;

        hardwareController.Backend.SendFoci(
            fociList,
            connectedDevices,
            useMultiFocus ? HoloAlgorithm.GSPAT : HoloAlgorithm.Naive,
            enableDirectionalGrouping: false,
            directionalAngleThreshold: 90f,
            focusIntensityPascal: intensityPa,
            synchronousSend: true,
            profiler: null,
            debugDisabler: debugDisabler
        );
    }

    /// <summary>
    /// 現在のこのオブジェクトのTransformをTransformLoaderのOffsetに適用し、位置をリセットします。
    /// </summary>
    public void ApplyOffset()
    {
        if (transformLoader == null) return;

        transformLoader.offset += this.transform.localPosition;
        this.transform.localPosition = Vector3.zero;
        this.transform.localRotation = Quaternion.identity;
    }

    /// <summary>
    /// 現在のFocusTargetと正解位置（truePositionTarget）の差分からオフセットを計算し適用します。
    /// </summary>
    public void ApplyOffsetByDifference()
    {
        if (transformLoader == null) return;

        Vector3 focusPos = singleFocusTarget != null ? singleFocusTarget.position : singleFocusPosition;

        if (truePositionTarget == null)
        {
            AppLogger.LogWarning(this, HAP_LogTriggers.TagCalibration, "truePositionTarget is not set. Cannot apply difference.");
            return;
        }

        Vector3 diff = focusPos - truePositionTarget.position;
        transformLoader.offset += diff;

        AppLogger.Log(this, HAP_LogTriggers.TagCalibration, $"Applied offset by difference: {diff}. New Offset: {transformLoader.offset}");
    }

    /// <summary>
    /// 現在のoffsetをTargetDevicesで選択されているAUTD3DeviceのTransformに永続的に反映（Bake）し、offsetをリセットします。
    /// </summary>
    public void BakeOffsetToDevices()
    {
        if (transformLoader == null) return;

        Vector3 currentOffset = transformLoader.offset;
        if (currentOffset == Vector3.zero)
        {
            AppLogger.Log(this, HAP_LogTriggers.TagCalibration, "Offset is already zero. Nothing to bake.");
            return;
        }

        var devices = FindObjectsByType<AUTD3Device>(FindObjectsSortMode.None).OrderBy(d => d.ID).ToArray();
        if (devices.Length == 0)
        {
            AppLogger.LogWarning(this, HAP_LogTriggers.TagCalibration, "No AUTD3Device found in the scene to bake to.");
            return;
        }

        int bakedCount = 0;
        for (int i = 0; i < devices.Length; i++)
        {
            if (i < targetDevices.Count && targetDevices[i])
            {
                devices[i].transform.position -= currentOffset;
                bakedCount++;
            }
        }

        transformLoader.offset = Vector3.zero;
        AppLogger.Log(this, HAP_LogTriggers.TagCalibration, $"Baked offset {currentOffset} to {bakedCount} selected devices. (Device positions moved by {-currentOffset}). Offset reset to zero.");
    }
}
