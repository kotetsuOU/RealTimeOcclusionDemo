using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

public class PointCloudDepthSampler : MonoBehaviour
{
    [Header("Point Cloud Source")]
    [Tooltip("対象カメラの RsProcessingPipe をアサイン")]
    [SerializeField] private RsProcessingPipe processingPipe;


    [Header("Sampling")]
    [Tooltip("中心から何m以内の点を集めるか")]
    [SerializeField] private float sampleRange = 0.015f;

    [Tooltip("これ未満の点数なら前回値を使う")]
    [SerializeField] private int minPoints = 5;

    [Header("Debug")]
    [SerializeField] private Transform debugCompareTarget;

    private RsIntegratedPointCloud pointCloud;
    private bool subscribed = false;

    private float lastValidY;
    private bool hasValidY = false;
    private bool pending = false;
    private Vector3[] latestPoints;
    private int latestCount = 0;

    private volatile bool needsReadback = false;

    // ストリーミング開始後でないと見つからないので、見つかるまで毎フレーム試す
    private void TryConnect()
    {
        if (subscribed) return;
        if (processingPipe == null || processingPipe.profile == null) return;

        foreach (var block in processingPipe.profile._processingBlocks)
        {
            if (block is RsIntegratedPointCloud integrated)
            {
                pointCloud = integrated;
                pointCloud.OnPointCloudUpdated += OnCloudUpdated;
                subscribed = true;
                Debug.Log($"[Sampler] 接続成功: {pointCloud.name}");
                return;
            }
        }
    }

    void OnDisable()
    {
        if (subscribed && pointCloud != null)
        {
            pointCloud.OnPointCloudUpdated -= OnCloudUpdated;
            subscribed = false;
        }
    }

    // RealSenseのパイプラインスレッドから呼ばれる。フラグを立てるだけ
    private void OnCloudUpdated()
    {
        needsReadback = true;
    }

    private void RequestReadback()
    {
        if (pending || pointCloud == null) return;

        var buf = pointCloud.PointCloudBuffer;
        int count = pointCloud.LastPointCount;
        if (buf == null || count <= 0) return;

        pending = true;
        AsyncGPUReadback.Request(buf, count * 12, 0, OnReadback);
    }

    private void OnReadback(AsyncGPUReadbackRequest req)
    {
        pending = false;
        if (req.hasError) return;

        var data = req.GetData<Vector3>();

        if (latestPoints == null || latestPoints.Length < data.Length)
            latestPoints = new Vector3[data.Length];

        for (int i = 0; i < data.Length; i++)
            latestPoints[i] = data[i];

        latestCount = data.Length;
    }

    public bool TryGetMedianY(float centerX, float centerZ, out float y)
    {
        y = lastValidY;
        if (latestCount == 0) return hasValidY;

        var ys = new List<float>();
        for (int i = 0; i < latestCount; i++)
        {
            Vector3 p = latestPoints[i];
            if (Mathf.Abs(p.x - centerX) < sampleRange &&
                Mathf.Abs(p.z - centerZ) < sampleRange)
                ys.Add(p.y);
        }

        if (ys.Count < minPoints) return hasValidY;

        ys.Sort();
        lastValidY = ys[ys.Count / 2];
        hasValidY = true;
        y = lastValidY;
        return true;
    }

    void Update()
    {
        TryConnect();

        if (needsReadback)
        {
            needsReadback = false;
            RequestReadback();
        }

        if (Input.GetKeyDown(KeyCode.P))
        {
            Debug.Log($"接続: OK / 点数: {latestCount}");

            if (debugCompareTarget != null)
            {
                Debug.Log($"Buffer:{(pointCloud?.PointCloudBuffer == null ? "null" : "あり")} " +
              $"LastPointCount:{pointCloud?.LastPointCount}");
                Vector3 t = debugCompareTarget.position;
                if (TryGetMedianY(t.x, t.z, out float y))
                    Debug.Log($"中央値Y: {y:F4} / Targetの Y: {t.y:F4}");
                else
                    Debug.Log($"点が足りない（範囲{sampleRange}m 内に{minPoints}点未満）");
            }
        }
    }
}