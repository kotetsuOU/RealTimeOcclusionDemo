using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

public class PointCloudDepthSampler : MonoBehaviour
{
    [Header("Point Cloud Source")]
    [SerializeField] private RsIntegratedPointCloud pointCloud;

    [Header("Sampling")]
    [Tooltip("中心から何m以内の点を集めるか")]
    [SerializeField] private float sampleRange = 0.015f;

    [Tooltip("これ未満の点数なら前回値を使う")]
    [SerializeField] private int minPoints = 5;

    [Header("Debug")]
    [Tooltip("座標系の確認用。UpperTarget などをアサイン")]
    [SerializeField] private Transform debugCompareTarget;

    private float lastValidY;
    private bool hasValidY = false;
    private bool pending = false;
    private Vector3[] latestPoints;   // 読み戻した点のコピー
    private int latestCount = 0;

    void OnEnable()
    {
        if (pointCloud != null) pointCloud.OnPointCloudUpdated += RequestReadback;
    }

    void OnDisable()
    {
        if (pointCloud != null) pointCloud.OnPointCloudUpdated -= RequestReadback;
    }

    private void RequestReadback()
    {
        if (pending) return;
        if (pointCloud == null) return;

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

        data.CopyTo(latestPoints);
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

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
    if (latestCount > 0 && Input.GetKeyDown(KeyCode.P))
    {
        Debug.Log($"点数:{latestCount} 先頭:{latestPoints[0]}");
        if (debugCompareTarget != null)
            Debug.Log($"比較対象: {debugCompareTarget.position}");
    }

    }
}
