using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Core.Logging;
using Features.Haptics.Core;
using Features.Haptics.Debug;

#nullable enable

/// <summary>
/// AUTD3デバイス群との物理接続、ハードウェア設定（Modulation, Silencer, Fan, Temperature）を管理するコントローラー。
/// 実際の通信・送信処理は登録されている <see cref="IHAP_AUTDBackend"/> に委譲します。
/// </summary>
public class HAP_AUTDHardwareController : MonoBehaviour
{
    [Header("Link Settings")]
    [Tooltip("AUTDデバイスとの接続方法を選択します")]
    public AUTDLinkType linkType = AUTDLinkType.TwinCAT;

    [Tooltip("SOEM使用時のネットワークアダプタ名（必要であれば指定）")]
    public string soemAdapterName = "";

    [Header("Hardware Environment")]
    [Tooltip("環境温度（摂氏）。音速計算に使用され、焦点の正確さに影響します。室温に合わせてください。")]
    public float temperature = 25f;

    [Tooltip("デバイス冷却ファンのON/OFF。高出力で長時間使用する場合は ON にしてください。")]
    public bool enableFan = false;

    [Header("Modulation Settings")]
    [Tooltip("変調モード。\nSine: 指定周波数で明滅（ブーンという感触）。\nStatic: 連続出力（押される感触）。")]
    public ModulationMode modulationMode = ModulationMode.Sine;

    [Tooltip("サイン波の変調周波数 (Hz)。一般的に人間の皮膚は 150〜200Hz で最も感度が高くなります。")]
    public float sineFrequency = 150f;

    [Tooltip("定常波(Static)の振幅 (0.0〜1.0)。通常は1.0を使用します。")]
    public float staticAmplitude = 1.0f;

    [Header("Silencer Settings")]
    [Tooltip("サイレンサーのモード。可聴ノイズ（ジージー音）を減らします。\nFixedUpdateRate: 強度と位相のステップで指定。\nFixedCompletionTime: 完了時間で指定。")]
    public SilencerMode silencerMode = SilencerMode.FixedUpdateRate;

    [Tooltip("位相の変化ステップ。小さいほど静かになりますが、応答が遅れます。")]
    public ushort silencerStepPhase = 500;

    [Tooltip("振幅の変化ステップ。小さいほど静かになりますが、応答が遅れます。")]
    public ushort silencerStepAmplitude = 65535;

    private IHAP_AUTDBackend? _backend;
    public IHAP_AUTDBackend? Backend => _backend;

    public List<AUTD3Device> ConnectedDevices { get; private set; } = new List<AUTD3Device>();
    public bool IsConnected => _backend != null && _backend.IsConnected;

    private async void Awake()
    {
        _backend = HAP_AUTDBackendRegistry.CreateBackend();
        if (_backend == null)
        {
            AppLogger.LogWarning(this, HAP_LogTriggers.TagLinkService, "No AUTD backend registered. Ultrasonic haptics will be disabled.");
            return;
        }

        ConnectedDevices = FindObjectsByType<AUTD3Device>(FindObjectsSortMode.None)
            .OrderBy(obj => obj.ID)
            .ToList();

        await _backend.OpenAsync(linkType, soemAdapterName, ConnectedDevices);

        if (_backend.IsConnected)
        {
            _backend.ApplyModulation(modulationMode, sineFrequency, staticAmplitude);
            _backend.ApplySilencer(silencerMode, silencerStepPhase, silencerStepAmplitude);
            _backend.ApplyFan(enableFan);
            _backend.ApplyTemperature(temperature);
        }
    }

    private void Update()
    {
        if (_backend == null || !_backend.IsConnected) return;

        _backend.ApplyModulation(modulationMode, sineFrequency, staticAmplitude);
        _backend.ApplySilencer(silencerMode, silencerStepPhase, silencerStepAmplitude);
        _backend.ApplyFan(enableFan);
        _backend.ApplyTemperature(temperature);
    }

    public void ApplyModulation() => _backend?.ApplyModulation(modulationMode, sineFrequency, staticAmplitude);
    public void ApplySilencer() => _backend?.ApplySilencer(silencerMode, silencerStepPhase, silencerStepAmplitude);
    public void ApplyFan() => _backend?.ApplyFan(enableFan);
    public void ApplyTemperature() => _backend?.ApplyTemperature(temperature);

    public void SetFan(bool on)
    {
        enableFan = on;
        ApplyFan();
    }

    public void SetNull()
    {
        _backend?.SendNull();
    }

    private async void OnDestroy()
    {
        if (_backend != null)
        {
            await _backend.CloseAsync();
            _backend.Dispose();
            _backend = null;
        }
    }
}
