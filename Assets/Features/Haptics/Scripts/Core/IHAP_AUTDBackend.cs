using System;
using System.Collections.Generic;
using System.Threading.Tasks;

#nullable enable

namespace Features.Haptics.Core
{
    /// <summary>
    /// AUTD3 ハードウェアとの通信・設定・焦点送信を担当するバックエンドの抽象インターフェース。
    /// 旧 SDK (AUTD3Sharp v3.x) および 新 SDK (autd3-sdk 0.9.x) の具象実装がこれを実装します。
    /// </summary>
    public interface IHAP_AUTDBackend : IDisposable
    {
        /// <summary>バックエンドの識別名（例: "Legacy (AUTD3Sharp)", "Current (autd3-sdk 0.9)"）。</summary>
        string BackendName { get; }

        /// <summary>現在ハードウェアまたはシミュレータに接続中かどうか。</summary>
        bool IsConnected { get; }

        /// <summary>
        /// AUTD3 デバイスへの接続を開始します。
        /// </summary>
        Task OpenAsync(AUTDLinkType linkType, string soemAdapterName, IReadOnlyList<AUTD3Device> devices);

        /// <summary>
        /// デバイス接続を破棄・クローズします。
        /// </summary>
        Task CloseAsync();

        /// <summary>出力停止（Null）を送信します。</summary>
        void SendNull();

        /// <summary>変調（Modulation）設定を送信します。</summary>
        void ApplyModulation(ModulationMode mode, float sineFreq, float staticAmp);

        /// <summary>サイレンサー（Silencer）設定を送信します。</summary>
        void ApplySilencer(SilencerMode mode, ushort stepPhase, ushort stepAmp);

        /// <summary>冷却ファンの ON/OFF を適用します。</summary>
        void ApplyFan(bool enableFan);

        /// <summary>環境温度（音速計算用）を適用します。</summary>
        void ApplyTemperature(float temperature);

        /// <summary>
        /// 計算済みの焦点データ群を各デバイスへ送信します。
        /// </summary>
        void SendFoci(
            IReadOnlyList<HAP_FociGenerator.ClusterFociData> clusterData,
            IReadOnlyList<AUTD3Device> connectedDevices,
            HoloAlgorithm holoAlgorithm,
            bool enableDirectionalGrouping,
            float directionalAngleThreshold,
            float focusIntensityPascal,
            bool synchronousSend,
            HAP_AUTDPerformanceProfiler? profiler,
            HAP_AUTDDebugDisabler? debugDisabler = null,
            uint gspatRepeat = 20);
    }
}
