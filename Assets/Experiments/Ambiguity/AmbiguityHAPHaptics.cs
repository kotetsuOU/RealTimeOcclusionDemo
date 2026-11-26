using UnityEngine;

/// <summary>
/// このプロジェクトの触覚系（HAP_AUTDHardwareController / IHAP_AUTDBackend、autd3-sdk 0.14.x）に
/// 接続する、AmbiguityTask 用の出力アダプタ。
/// 上グループ・下グループの片方だけを使って、手の面に 1 点の静止焦点を出す。
/// 強度 0〜255 は、既存の FociStm と同じ線形対応（255 = maxPascal Pa）で音圧に換算する。
/// </summary>
public class AmbiguityHAPHaptics : MonoBehaviour, IAmbiguityHaptics
{
    [Header("接続")]
    [SerializeField] private HAP_AUTDHardwareController hardware;

    [Header("焦点位置（手の中央付近）")]
    [Tooltip("手のひら面（手のひら側＝上）の焦点位置")]
    [SerializeField] private Transform upperFocus;
    [Tooltip("手の甲面（手の甲側＝下）の焦点位置")]
    [SerializeField] private Transform downFocus;

    [Header("デバイスグループ（AUTD3Device.ID）")]
    [Tooltip("上側（手のひら側へ照射）。MultiAUTD3Controller の既定は 0,1,6,7")]
    [SerializeField] private int[] upperDeviceIds = { 0, 1, 6, 7 };
    [Tooltip("下側（手の甲側へ照射）。MultiAUTD3Controller の既定は 2,3,4,5")]
    [SerializeField] private int[] downDeviceIds = { 2, 3, 4, 5 };

    [Header("出力")]
    [Tooltip("強度 255 に対応する音圧（Pa）。既存の FociStm の換算（Pa/10000*255）に合わせた値")]
    [SerializeField] private float maxPascal = 10000f;
    [Tooltip("AM 変調の周波数（Hz）")]
    [SerializeField] private float modulationFreq = 250f;

    [Header("競合回避")]
    [Tooltip("実験中は止めたいコンポーネント（例: HAP_AUTDHapticsController、HAP_FoxBody/FootHapticsController など）。"
           + "これらが毎フレーム焦点を送ると、実験の出力が上書きされる")]
    [SerializeField] private MonoBehaviour[] disableWhileRunning;

    private bool[] _wasEnabled;

    private void Reset() => hardware = FindFirstObjectByType<HAP_AUTDHardwareController>();

    private void OnEnable()
    {
        if (disableWhileRunning != null)
        {
            _wasEnabled = new bool[disableWhileRunning.Length];
            for (int i = 0; i < disableWhileRunning.Length; i++)
            {
                if (disableWhileRunning[i] == null) continue;
                _wasEnabled[i] = disableWhileRunning[i].enabled;
                disableWhileRunning[i].enabled = false;
            }
        }
        if (hardware != null) hardware.SetModulationOverride(ModulationMode.Sine, modulationFreq);
    }

    private void OnDisable()
    {
        Stop();
        if (hardware != null) hardware.ClearModulationOverride();
        if (disableWhileRunning != null && _wasEnabled != null)
            for (int i = 0; i < disableWhileRunning.Length; i++)
                if (disableWhileRunning[i] != null) disableWhileRunning[i].enabled = _wasEnabled[i];
    }

    public void Present(AmbiguitySide side, int intensity)
    {
        intensity = Mathf.Clamp(intensity, 0, 255);

        // 強度 0 は、焦点を 0 Pa で出すのではなく、出力そのものを止める（無音）
        if (intensity == 0) { Stop(); return; }

        var backend = hardware != null ? hardware.Backend : null;
        if (backend == null || !backend.IsConnected)
        {
            Debug.LogWarning("[AmbiguityHAP] AUTD に未接続のため出力しません。");
            return;
        }

        Transform focus = side == AmbiguitySide.Upper ? upperFocus : downFocus;
        if (focus == null)
        {
            Debug.LogWarning($"[AmbiguityHAP] {side} の焦点位置（Transform）が未設定です。");
            return;
        }

        int[] ids = side == AmbiguitySide.Upper ? upperDeviceIds : downDeviceIds;
        float pascal = intensity / 255f * maxPascal;
        backend.SendGroupedSingleFocus(focus.position, pascal, ids, hardware.ConnectedDevices);
    }

    public void Stop()
    {
        if (hardware == null) return;
        var backend = hardware.Backend;
        if (backend != null && backend.IsConnected) backend.SendNull();
    }
}
