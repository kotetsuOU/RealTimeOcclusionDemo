using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 手の点群から「手のひら側（上面）の中央」の高さを求め、触覚焦点の Transform を更新する。
/// 旧実験（OuterIllusion3 ブランチの PointCloudDepthSampler / MultiAUTD3Controller / DownTargetFollower）と同じ考え方：
///   ・X,Z は固定の目印（palmReference）の位置
///   ・Y は、その X,Z 付近（±sampleRange）の点群の Y の中央値
///   ・手のひら側の焦点 = (X, Y, Z)、手の甲側の焦点 = (X, Y - handThickness, Z)
/// 点群は RsIntegratedPointCloud の GPU バッファを非同期に読み戻して使う（座標はワールド座標の前提。要確認）。
/// </summary>
public class AmbiguityPalmTracker : MonoBehaviour
{
    [Header("点群")]
    [Tooltip("手を撮っているカメラの RsProcessingPipe（上側カメラ）")]
    [SerializeField] private RsProcessingPipe processingPipe;

    [Header("目印（手の中央の真上／真下に置く）")]
    [Tooltip("X,Z をここから取る。Y は無視（点群から求める）")]
    [SerializeField] private Transform palmReference;

    [Header("出力する焦点の Transform（AmbiguityHAPHaptics に渡す）")]
    [SerializeField] private Transform upperFocus;
    [SerializeField] private Transform downFocus;

    [Header("サンプリング")]
    [Tooltip("中心から何 m 以内の点を集めるか")]
    [SerializeField] private float sampleRange = 0.015f;
    [Tooltip("これ未満の点数のフレームは無視（前回値を保持）")]
    [SerializeField] private int minPoints = 5;
    [Tooltip("この Y より低い点は無視（手の下の机などを拾わない）。使わないなら十分小さい値")]
    [SerializeField] private float minValidY = -1000f;
    [Tooltip("フレームごとの中央値を、直近何フレーム分まとめて中央値にするか（揺れ対策）")]
    [SerializeField, Range(1, 30)] private int smoothingFrames = 10;
    [Tooltip("この距離以上動いたときだけ焦点を更新する（点群の揺れ対策）")]
    [SerializeField] private float moveThreshold = 0.002f;

    [Header("手の厚みと微調整")]
    [Tooltip("手のひら面の Y からこの分だけ下げた位置を、手の甲面の焦点にする")]
    [SerializeField] private float handThickness = 0.03f;
    [Tooltip("手のひら側焦点の微調整（ワールド座標, m）")]
    [SerializeField] private Vector3 upperOffset = Vector3.zero;
    [Tooltip("手の甲側焦点の微調整（ワールド座標, m）")]
    [SerializeField] private Vector3 downOffset = Vector3.zero;

    /// <summary>有効な手のひら高さが得られているか</summary>
    public bool HasPalm { get; private set; }
    /// <summary>現在の手のひら面（上面）の中央位置</summary>
    public Vector3 PalmPoint { get; private set; }
    public string Status { get; private set; } = "未接続";

    private RsIntegratedPointCloud _cloud;
    private bool _subscribed;
    private volatile bool _needsReadback;
    private bool _pending;
    private Vector3[] _points;
    private int _count;
    private readonly List<float> _frameMedians = new List<float>();
    private readonly List<float> _scratch = new List<float>();
    private Vector3? _lastApplied;

    private void OnDisable()
    {
        if (_subscribed && _cloud != null) _cloud.OnPointCloudUpdated -= OnCloudUpdated;
        _subscribed = false;
        _cloud = null;
    }

    // ストリーミング開始後でないとブロックが見つからないので、見つかるまで毎フレーム試す
    private void TryConnect()
    {
        if (_subscribed || processingPipe == null) return;
        var cloud = processingPipe.GetFirstBlock<RsIntegratedPointCloud>();
        if (cloud == null) return;
        _cloud = cloud;
        _cloud.OnPointCloudUpdated += OnCloudUpdated;
        _subscribed = true;
        Status = "接続済み（点群待ち）";
    }

    // RealSense のスレッドから呼ばれる。フラグを立てるだけ
    private void OnCloudUpdated() => _needsReadback = true;

    private void Update()
    {
        TryConnect();
        if (_needsReadback)
        {
            _needsReadback = false;
            RequestReadback();
        }
    }

    private void RequestReadback()
    {
        if (_pending || _cloud == null) return;
        var buf = _cloud.PointCloudBuffer;
        int count = _cloud.LastPointCount;
        if (buf == null || count <= 0) return;
        _pending = true;
        AsyncGPUReadback.Request(buf, count * 12, 0, OnReadback); // Vector3 = 12 byte
    }

    private void OnReadback(AsyncGPUReadbackRequest req)
    {
        _pending = false;
        if (req.hasError) return;

        var data = req.GetData<Vector3>();
        if (_points == null || _points.Length < data.Length) _points = new Vector3[data.Length];
        for (int i = 0; i < data.Length; i++) _points[i] = data[i];
        _count = data.Length;

        if (palmReference == null) return;
        if (!TryFrameMedianY(palmReference.position.x, palmReference.position.z, out float y))
        {
            Status = $"点が足りない（±{sampleRange} m 内に {minPoints} 点未満）";
            return;
        }

        _frameMedians.Add(y);
        while (_frameMedians.Count > smoothingFrames) _frameMedians.RemoveAt(0);

        _scratch.Clear();
        _scratch.AddRange(_frameMedians);
        _scratch.Sort();
        float palmY = _scratch[_scratch.Count / 2];

        var p = palmReference.position;
        PalmPoint = new Vector3(p.x, palmY, p.z);
        HasPalm = true;
        Status = $"OK palmY={palmY:F4}（{_frameMedians.Count} フレーム）";
        ApplyFocus();
    }

    private bool TryFrameMedianY(float cx, float cz, out float y)
    {
        y = 0f;
        if (_count == 0) return false;
        _scratch.Clear();
        for (int i = 0; i < _count; i++)
        {
            var p = _points[i];
            if (Mathf.Abs(p.x - cx) < sampleRange && Mathf.Abs(p.z - cz) < sampleRange && p.y >= minValidY)
                _scratch.Add(p.y);
        }
        if (_scratch.Count < minPoints) return false;
        _scratch.Sort();
        y = _scratch[_scratch.Count / 2];
        return true;
    }

    private void ApplyFocus()
    {
        if (_lastApplied.HasValue && Vector3.Distance(_lastApplied.Value, PalmPoint) <= moveThreshold) return;
        _lastApplied = PalmPoint;
        if (upperFocus != null) upperFocus.position = PalmPoint + upperOffset;
        if (downFocus != null) downFocus.position = PalmPoint + Vector3.down * handThickness + downOffset;
    }

    [ContextMenu("Log Status")]
    private void LogStatus() =>
        Debug.Log($"[PalmTracker] {Status} / 接続={_subscribed} / 点数={_count} / palm={(HasPalm ? PalmPoint.ToString("F4") : "なし")}");
}
