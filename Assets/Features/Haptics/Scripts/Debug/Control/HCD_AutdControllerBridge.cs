#if !USE_AUTD3_LEGACY
using UnityEngine;
using System.Collections.Generic;
using Core.Logging;
using Features.Haptics.Debug;

/// <summary>
/// HCD_Pipeline の結果を受け取り、配布パッケージ版の AutdController へ直接データを渡すためのブリッジスクリプト。
/// HAP_AUTDController (独自実装) との性能比較などに使用できます。
/// </summary>
public class HCD_AutdControllerBridge : MonoBehaviour
{
    [Header("Dependencies")]
    [Tooltip("接触判定を行う HCD_Pipeline の参照")]
    public HCD_Pipeline hcdPipeline;

    [Header("Acoustic Settings")]
    [Tooltip("AUTDController に渡す出力強度 (Pascal)")]
    public float focusAmplitudePa = 10000f;

    [Tooltip("ホログラフィアルゴリズム")]
    public AutdController.Algorithm algorithm = AutdController.Algorithm.GSPAT;

    [Tooltip("振幅のクランプ値")]
    public float clamp = 1.0f;

    private List<Vector3> _positions = new List<Vector3>();
    private List<float> _amplitudes = new List<float>();

    private bool _isCurrentlyOff = true;

    void Update()
    {
        // パイプラインの取得
        if (hcdPipeline == null)
        {
            hcdPipeline = HCD_Pipeline.Instance;
        }

        // パイプラインまたはパッケージ版 AutdController が存在しない場合は何もしない
        if (hcdPipeline == null || AutdController.instance == null || !AutdController.instance.isOpen)
        {
            return;
        }

        // トラッカーから安定化・追跡済みのクラスタリストを取得
        var trackedClusters = hcdPipeline.GetTrackedClusters();
        
        _positions.Clear();
        _amplitudes.Clear();

        // 有効なクラスタの重心を抽出
        foreach (var cluster in trackedClusters)
        {
            if (cluster.IsAlive && cluster.Force > 0.01f)
            {
                _positions.Add(cluster.Centroid);
                _amplitudes.Add(focusAmplitudePa);
            }
        }

        if (_positions.Count > 0)
        {
            // 複数焦点の場合は SetHolo を使用して焦点座標と振幅を渡す
            // (AutdController 側で自動的に GSPAT などのソルバが走ります)
            AutdController.instance.SetHolo(_positions, _amplitudes, algorithm, clamp);
            AutdController.instance.Send();
            _isCurrentlyOff = false;
        }
        else
        {
            // 接触がなくなった場合は出力を Null にして停止
            if (!_isCurrentlyOff)
            {
                AutdController.instance.SetNull();
                AutdController.instance.Send();
                _isCurrentlyOff = true;
            }
        }
    }
}

#else
using UnityEngine;
using System.Collections.Generic;
using Core.Logging;
using Features.Haptics.Debug;

/// <summary>
/// HCD_Pipeline の結果を受け取り、配布パッケージ版の AutdController へ直接データを渡すためのブリッジスクリプト。
/// ※ このブリッジは新SDK (autd3-sdk) の AutdController を使用するため、
///   旧SDK (AUTD3Sharp) 環境ではスタブとして動作します。
/// </summary>
public class HCD_AutdControllerBridge : MonoBehaviour
{
    [Header("Dependencies")]
    [Tooltip("接触判定を行う HCD_Pipeline の参照")]
    public HCD_Pipeline hcdPipeline;

    [Header("Acoustic Settings")]
    [Tooltip("AUTDController に渡す出力強度 (Pascal)")]
    public float focusAmplitudePa = 10000f;

    [Tooltip("振幅のクランプ値")]
    public float clamp = 1.0f;

    void Start()
    {
        AppLogger.LogWarning(this, HAP_LogTriggers.TagController, "This bridge requires the new SDK (autd3-sdk). It is disabled under the legacy SDK (AUTD3Sharp).");
    }
}

#endif
