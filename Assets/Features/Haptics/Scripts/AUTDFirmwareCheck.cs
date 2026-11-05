#if USE_AUTD3_LEGACY
using System;
using System.Linq;
using AUTD3Sharp;
using UnityEngine;

/// <summary>
/// AUTD の接続とファームウェアのバージョン確認用の、一時的なデバッグスクリプト。
/// 再生すると 1 回だけ接続を試し、成功したら各デバイスのファームウェアのバージョンを Console に出力する。
///
/// 使い方:
///  1. このスクリプトを、シーン内の空の GameObject に付ける
///  2. MultiAUTD3Controller と HAP_AUTDHardwareController を、両方とも無効にする
///  3. AUTD Server など、AUTD を使う他のアプリを閉じる
///  4. 再生して、Console の [AUTDFirmwareCheck] を見る
/// 確認が済んだら、このスクリプトは削除してよい。
/// </summary>
public class AUTDFirmwareCheck : MonoBehaviour
{
    [Tooltip("ON: 接続のタイムアウトを 5000ms にして接続する(HAP_AUTDLinkService と同じ)。\nOFF: SDK 既定のタイムアウトで接続する(MultiAUTD3Controller と同じ)。")]
    public bool useLongTimeout = true;

    [Tooltip("シーンに AUTD3Device がないときに使う、ダミーのデバイス台数。実機の台数に合わせる(位置は、ファームウェアの確認には影響しない)")]
    public int dummyDeviceCount = 8;

    private Controller _autd;

    private void Start()
    {
        var devices = FindObjectsByType<AUTD3Device>(FindObjectsSortMode.None)
            .OrderBy(obj => obj.ID)
            .Select(obj => new AUTD3(pos: obj.transform.position, rot: obj.transform.rotation))
            .ToList();

        Debug.Log($"[AUTDFirmwareCheck] AUTD3Device コンポーネント: {devices.Count} 台 / タイムアウト: {(useLongTimeout ? "5000ms" : "SDK既定")}");

        if (devices.Count == 0)
        {
            // 台数が 0 だと接続処理が配列の範囲外エラーになるため、ダミーで台数だけ合わせる
            Debug.LogWarning($"[AUTDFirmwareCheck] AUTD3Device がないため、ダミーの {dummyDeviceCount} 台で接続します。");
            for (int i = 0; i < dummyDeviceCount; i++)
                devices.Add(new AUTD3(pos: Vector3.zero, rot: Quaternion.identity));
        }

        try
        {
            if (useLongTimeout)
            {
                var option = new SenderOption { Timeout = AUTD3Sharp.Duration.FromMillis(5000) };
                _autd = Controller.OpenWithOption(devices, new AUTD3Sharp.Link.TwinCAT(), option);
            }
            else
            {
                _autd = Controller.Open(devices, new AUTD3Sharp.Link.TwinCAT());
            }

            Debug.Log("[AUTDFirmwareCheck] 接続に成功しました。");

            var versions = _autd.FirmwareVersion();
            for (int i = 0; i < versions.Length; i++)
                Debug.Log($"[AUTDFirmwareCheck] device[{i}] firmware: {versions[i].Info}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[AUTDFirmwareCheck] 失敗しました:\n{ex}");
        }
    }

    private void OnDestroy()
    {
        _autd?.Dispose();
        _autd = null;
    }
}
#endif
