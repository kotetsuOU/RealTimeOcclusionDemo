#if AUTD3_SDK_CURRENT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AUTD3;
using Core.Logging;
using Features.Haptics.Debug;
using Features.Haptics.Hardware;

#nullable enable

namespace Features.Haptics.Backends.Current
{
    /// <summary>
    /// Current SDK (autd3-sdk v0.14.1) の <see cref="Client"/> / <see cref="Geometry"/> 接続ライフサイクル、
    /// タイムアウトトークン生成、および安全切断・リソース解放を担当するセッション管理クラス。
    /// </summary>
    public sealed class HAP_CurrentConnectionSession
    {
        private static readonly TimeSpan OpenCommandTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan ConfigCommandTimeout = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan ShutdownCommandTimeout = TimeSpan.FromMilliseconds(500);

        private readonly SemaphoreSlim _sendLock;
        private readonly HAP_CurrentHardwareConfigurator _hardwareConfigurator;

        private Client? _client;
        private Geometry? _geometry;
        private CancellationTokenSource? _lifecycleCts;

        public Client? Client => _client;
        public Geometry? Geometry => _geometry;
        public bool IsConnected => _client != null && _geometry != null;

        public HAP_CurrentConnectionSession(
            SemaphoreSlim sendLock,
            HAP_CurrentHardwareConfigurator hardwareConfigurator)
        {
            _sendLock = sendLock;
            _hardwareConfigurator = hardwareConfigurator;
        }

        /// <summary>
        /// セッション寿命に連動したタイムアウト付き <see cref="CancellationTokenSource"/> を生成します。
        /// </summary>
        public CancellationTokenSource CreateTimeoutTokenSource(TimeSpan timeout)
        {
            var lifecycleCts = _lifecycleCts;
            if (lifecycleCts == null || lifecycleCts.IsCancellationRequested)
            {
                var cts = new CancellationTokenSource();
                cts.Cancel();
                return cts;
            }

            var linked = CancellationTokenSource.CreateLinkedTokenSource(lifecycleCts.Token);
            linked.CancelAfter(timeout);
            return linked;
        }

        /// <summary>
        /// 指定されたリンク種別・インタフェースで AUTD3 デバイス群へ非同期接続し、初期化コマンドを送信します。
        /// </summary>
        public async Task OpenAsync(AUTDLinkType linkType, string linkTarget, IReadOnlyList<AUTD3Device> devices)
        {
            if (devices == null || devices.Count == 0)
            {
                AppLogger.LogWarning(null, HAP_LogTriggers.TagLinkService, "[CurrentBackend] No AUTD3Device found in scene. Skipping connection.");
                return;
            }

            var orderedDevices = devices.OrderBy(obj => obj.ID).ToList();
            var sdkDevices = orderedDevices.Select(obj => new Autd3(
                obj.transform.position,
                HAP_DeviceOrientationHelper.GetCurrentSdkRotation(obj)
            )).ToList();

            _lifecycleCts?.Dispose();
            _lifecycleCts = new CancellationTokenSource();
            _geometry = new Geometry(sdkDevices);

            Interface iface = BuildInterface(linkType, linkTarget);
            string targetDesc = iface.AddrValue ?? iface.NameValue ?? "Auto";
            AppLogger.Log(null, HAP_LogTriggers.TagLinkService,
                $"[CurrentBackend] Connecting to AUTD3 via {linkType} (iface={targetDesc}). Found {sdkDevices.Count} devices (using Unity local -Z emission alignment).");

            for (int i = 0; i < _geometry.NumDevices; i++)
            {
                var dev = _geometry[i];
                AppLogger.Log(null, HAP_LogTriggers.TagLinkService,
                    $"[CurrentBackend] Geometry[{i}] (ID={orderedDevices[i].ID}): Center={dev.Center:F4}, Axial={dev.AxialDirection:F3}");
            }

            try
            {
                var option = new TransportOption
                {
                    Iface = iface,
                    OpenTimeout = OpenCommandTimeout,
                    SyncOnOpen = true,
                };

                using (var openCts = CreateTimeoutTokenSource(OpenCommandTimeout))
                {
                    _client = await Client.OpenAsync(_geometry, option, openCts.Token).ConfigureAwait(false);
                }

                using (var initCts = CreateTimeoutTokenSource(OpenCommandTimeout))
                {
                    await _client.SendAsync(new Clear(), initCts.Token).ConfigureAwait(false);
                    await _client.SendAsync(new Synchronize(), initCts.Token).ConfigureAwait(false);
                    await _hardwareConfigurator.SendNullAsync(_client, _geometry, initCts.Token).ConfigureAwait(false);
                }

                await LogFirmwareVersionsAsync(_client).ConfigureAwait(false);

                if (IsConnected)
                {
                    AppLogger.Log(null, HAP_LogTriggers.TagLinkService, "[CurrentBackend] Connected and initialized successfully.");
                }
            }
            catch (OperationCanceledException)
            {
                bool isShutdown = _lifecycleCts == null || _lifecycleCts.IsCancellationRequested;
                CleanupFailedOpen();
                if (isShutdown)
                {
                    AppLogger.LogWarning(null, HAP_LogTriggers.TagLinkService, "[CurrentBackend] Connection cancelled during shutdown.");
                }
                else
                {
                    AppLogger.LogError(null, HAP_LogTriggers.TagLinkService,
                        $"[CurrentBackend] Connection timed out after {OpenCommandTimeout.TotalSeconds:F0}s (iface={targetDesc}, devices={sdkDevices.Count}). " +
                        "デバイスの電源・LAN配線、autd3-console との同時接続、または Windows ファイアウォールの受信規則（Unity Editor のパブリックネットワーク Block 設定等）を確認してください。");
                }
            }
            catch (Exception ex)
            {
                CleanupFailedOpen();
                AppLogger.LogError(null, HAP_LogTriggers.TagLinkService, $"[CurrentBackend] Failed to connect: {ex.Message}");
            }
        }

        /// <summary>
        /// 進行中コマンドを中断し、Null・サイレンサー無効化・ファン停止を送信したうえで安全に切断します。
        /// </summary>
        public async Task CloseAsync()
        {
            try { _lifecycleCts?.Cancel(); } catch { }

            await _sendLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_client != null)
                {
                    try
                    {
                        using var shutdownCts = new CancellationTokenSource(ShutdownCommandTimeout);
                        await _hardwareConfigurator.SendNullAsync(_client, _geometry, shutdownCts.Token).ConfigureAwait(false);
                        await _hardwareConfigurator.SendSilencerDisableAsync(_client, _geometry, shutdownCts.Token).ConfigureAwait(false);
                        await _hardwareConfigurator.SendFanStopAsync(_client, _geometry, shutdownCts.Token).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        AppLogger.LogWarning(null, HAP_LogTriggers.TagLinkService, $"[CurrentBackend] Error during close: {ex.Message}");
                    }
                    finally
                    {
                        try { _client.Dispose(); } catch { }
                        _client = null;
                        _hardwareConfigurator.ResetCachedStates();
                    }
                }

                DisposeGeometryAndLifecycle();
                AppLogger.Log(null, HAP_LogTriggers.TagLinkService, "[CurrentBackend] AUTD3 connection closed.");
            }
            finally
            {
                _sendLock.Release();
            }
        }

        /// <summary>
        /// 破棄処理時に同期的に接続終了を待機し、未解放リソースを確実に破棄します。
        /// </summary>
        public void Dispose()
        {
            if (_client != null)
            {
                try
                {
                    Task.Run(async () => await CloseAsync()).Wait(TimeSpan.FromSeconds(3));
                }
                catch (Exception ex)
                {
                    AppLogger.LogWarning(null, HAP_LogTriggers.TagLinkService, $"[CurrentBackend] Error during dispose: {ex.Message}");
                }
                finally
                {
                    if (_client != null)
                    {
                        try { _client.Dispose(); } catch { }
                        _client = null;
                    }
                    _hardwareConfigurator.ResetCachedStates();
                }
            }

            DisposeGeometryAndLifecycle();
        }

        private static Interface BuildInterface(AUTDLinkType linkType, string linkTarget)
        {
            string trimmed = string.IsNullOrWhiteSpace(linkTarget) ? string.Empty : linkTarget.Trim();

            switch (linkType)
            {
                case AUTDLinkType.LAN:
                    return trimmed.Length > 0 ? Interface.Name(trimmed) : Interface.Auto;

                case AUTDLinkType.Simulator:
                    string addr = trimmed.Length > 0 ? trimmed : "[::1]:44336";
                    return Interface.Addr(addr);

                default:
                    AppLogger.LogWarning(null, HAP_LogTriggers.TagLinkService,
                        $"[CurrentBackend] Legacy link type {linkType} is not used in autd3-sdk v0.14.1. Falling back to LAN (UDP/IPv6).");
                    return trimmed.Length > 0 ? Interface.Name(trimmed) : Interface.Auto;
            }
        }

        private async Task LogFirmwareVersionsAsync(Client client)
        {
            try
            {
                using var fwCts = CreateTimeoutTokenSource(ConfigCommandTimeout);
                var versions = await client.ReadFirmwareVersionAsync(fwCts.Token).ConfigureAwait(false);
                for (int i = 0; i < versions.Count; i++)
                {
                    AppLogger.Log(null, HAP_LogTriggers.TagLinkService,
                        $"[CurrentBackend] Device[{i}] Firmware: {versions[i]} (Supported={versions[i].IsSupported})");
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning(null, HAP_LogTriggers.TagLinkService,
                    $"[CurrentBackend] Could not read firmware version: {ex.Message}");
            }
        }

        private void CleanupFailedOpen()
        {
            try { _lifecycleCts?.Cancel(); } catch { }
            if (_client != null)
            {
                try { _client.Dispose(); } catch { }
                _client = null;
            }
            DisposeGeometryAndLifecycle();
            _hardwareConfigurator.ResetCachedStates();
        }

        private void DisposeGeometryAndLifecycle()
        {
            if (_geometry != null)
            {
                try { _geometry.Dispose(); } catch { }
                _geometry = null;
            }

            try { _lifecycleCts?.Dispose(); } catch { }
            _lifecycleCts = null;
        }
    }
}
#endif

