#if AUTD3_SDK_CURRENT
using System;
using System.Collections.Generic;
using System.Linq;
using Features.Haptics.Core;
using Features.Haptics.Debug;
using Features.Haptics.Processors;
using AUTD3;
using static AUTD3.Units;

#nullable enable

namespace Features.Haptics.Backends.Current
{
    /// <summary>
    /// autd3-sdk (v0.14.1 / Current SDK) におけるホログラフィ (Holo) および時空間変調 (STM)
    /// のコマンド構築とネイティブバッファ寿命管理を統括するビルダー。
    /// 実際の構築処理は <see cref="HAP_CurrentHoloBuilder"/> および <see cref="HAP_CurrentSTMBuilder"/> に委譲します。
    /// </summary>
    public class HAP_CurrentPatternBuilder
    {
        /// <summary>
        /// 送信対象の <see cref="ICommand"/> と、それが参照するネイティブバッファ群（<see cref="PhaseBuffer"/> / <see cref="IntensityBuffer"/>）
        /// の寿命をまとめて管理するコンテナ。送信完了後に確実に <see cref="Dispose"/> してください。
        /// </summary>
        public sealed class BuiltCommand : IDisposable
        {
            private readonly IDisposable[] _ownedDisposables;
            private bool _disposed;

            public ICommand Command { get; }
            public bool UseStreaming { get; }

            public BuiltCommand(ICommand command, bool useStreaming, params IDisposable[] ownedDisposables)
            {
                Command = command;
                UseStreaming = useStreaming;
                _ownedDisposables = ownedDisposables ?? Array.Empty<IDisposable>();
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;

                for (int i = 0; i < _ownedDisposables.Length; i++)
                {
                    try
                    {
                        _ownedDisposables[i]?.Dispose();
                    }
                    catch
                    {
                        // 破棄時の二次例外は握りつぶして残りのバッファも確実に解放する
                    }
                }
            }
        }

        private readonly HAP_CurrentHoloBuilder _holoBuilder = new HAP_CurrentHoloBuilder();
        private readonly HAP_CurrentSTMBuilder _stmBuilder = new HAP_CurrentSTMBuilder();

        /// <summary>
        /// 焦点クラスタデータ群からホログラフィパターンまたは STM コマンドを計算して返します。
        /// 返却された <see cref="BuiltCommand"/> は送信完了後に呼び出し側で <see cref="BuiltCommand.Dispose"/> してください。
        /// </summary>
        public BuiltCommand BuildPatterns(
            Geometry geometry,
            float soundSpeed,
            IReadOnlyList<HAP_FociGenerator.ClusterFociData> clusterData,
            IReadOnlyList<AUTD3Device> connectedDevices,
            HoloAlgorithm holoAlgorithm,
            float focusIntensityPascal,
            HAP_AUTDDebugDisabler? debugDisabler = null,
            uint gspatRepeat = 20)
        {
            var clusterList = clusterData.ToList();
            Length wavelength = Pattern.Wavelength(soundSpeed * m / s);
            TransducerMask mask = BuildTransducerMask(geometry, connectedDevices, debugDisabler);

            bool useSTM = clusterList.Any(c => c.UseSTM && c.STMFrames != null && c.STMFrames.Count > 1);
            if (useSTM)
            {
                return _stmBuilder.Build(geometry, soundSpeed, wavelength, clusterList, holoAlgorithm, focusIntensityPascal, gspatRepeat, mask);
            }

            return _holoBuilder.Build(geometry, wavelength, clusterList, holoAlgorithm, focusIntensityPascal, gspatRepeat, mask);
        }

        /// <summary>
        /// <see cref="HAP_AUTDDebugDisabler"/> の設定に基づき、無効化されたデバイスの全振動子を停止するマスクを構築します。
        /// 全デバイス有効時はアロケーションを行わず <see cref="TransducerMask.AllEnabled"/> を返します。
        /// </summary>
        private static TransducerMask BuildTransducerMask(
            Geometry geometry,
            IReadOnlyList<AUTD3Device> connectedDevices,
            HAP_AUTDDebugDisabler? debugDisabler)
        {
            if (debugDisabler == null || connectedDevices == null || connectedDevices.Count == 0)
            {
                return TransducerMask.AllEnabled;
            }

            var orderedDevices = connectedDevices.OrderBy(d => d.ID).ToList();
            int deviceCount = Math.Min(geometry.NumDevices, orderedDevices.Count);
            bool hasDisabled = false;
            for (int i = 0; i < deviceCount; i++)
            {
                if (debugDisabler.IsDisabled(orderedDevices[i].ID))
                {
                    hasDisabled = true;
                    break;
                }
            }

            if (!hasDisabled)
            {
                return TransducerMask.AllEnabled;
            }

            bool[][] mask = geometry.OutputMaskBuffer();
            for (int i = 0; i < deviceCount; i++)
            {
                if (debugDisabler.IsDisabled(orderedDevices[i].ID))
                {
                    Array.Fill(mask[i], false);
                }
            }

            return TransducerMask.Masked(mask);
        }
    }
}
#endif
