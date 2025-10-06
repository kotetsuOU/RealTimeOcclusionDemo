using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

public class PCV_Processor
{
    private readonly PCV_Data data;
    private readonly VoxelGrid voxelGrid;

    public PCV_Processor(PCV_Data pointCloudData, float voxelSize)
    {
        this.data = pointCloudData;
        if (this.data != null && this.data.PointCount > 0)
        {
            this.voxelGrid = new VoxelGrid(this.data.Vertices, voxelSize);
        }
    }

    public bool FindClosestPoint(Ray ray, float maxDistance, out int closestIndex)
    {
        closestIndex = -1;
        if (data == null || data.PointCount == 0) return false;

        float minDistanceSq = float.MaxValue;
        float maxDistanceSq = maxDistance * maxDistance;

        for (int i = 0; i < data.PointCount; i++)
        {
            Vector3 point = data.Vertices[i];
            Vector3 originToPoint = point - ray.origin;
            float distanceSq = Vector3.Cross(ray.direction, originToPoint).sqrMagnitude;

            if (distanceSq < minDistanceSq)
            {
                minDistanceSq = distanceSq;
                closestIndex = i;
            }
        }

        return closestIndex != -1 && minDistanceSq < maxDistanceSq;
    }

    public List<int> FindNeighbors(int pointIndex, float searchRadius)
    {
        if (voxelGrid == null) return new List<int>();
        return voxelGrid.FindNeighbors(pointIndex, searchRadius);
    }

    public IEnumerator FilterNoiseCoroutine(float searchRadius, int threshold, Action<PCV_Data> onComplete)
    {
        if (data == null || data.PointCount == 0 || voxelGrid == null)
        {
            onComplete?.Invoke(new PCV_Data(new List<Vector3>(), new List<Color>()));
            yield break;
        }

        var filteredVertices = new List<Vector3>();
        var filteredColors = new List<Color>();
        int pointsPerFrame = 5000;

        for (int i = 0; i < data.PointCount; i++)
        {
            List<int> neighbors = voxelGrid.FindNeighbors(i, searchRadius);
            if (neighbors.Count >= threshold)
            {
                filteredVertices.Add(data.Vertices[i]);
                filteredColors.Add(data.Colors[i]);
            }

            if (i > 0 && (i + 1) % pointsPerFrame == 0)
            {
                yield return null;
            }
        }
        onComplete?.Invoke(new PCV_Data(filteredVertices, filteredColors));
    }

    private struct Point
    {
        public Vector3 position;
        public Color color;
    }

    public PCV_Data FilterNoiseGPU(ComputeShader computeShader, float searchRadius, int threshold, out long elapsedMilliseconds)
    {
        var stopwatch = Stopwatch.StartNew();

        if (data == null || data.PointCount == 0)
        {
            stopwatch.Stop();
            elapsedMilliseconds = stopwatch.ElapsedMilliseconds;
            return new PCV_Data(new List<Vector3>(), new List<Color>());
        }

        var pointData = new Point[data.PointCount];
        for (int i = 0; i < data.PointCount; i++)
        {
            pointData[i] = new Point { position = data.Vertices[i], color = data.Colors[i] };
        }

        int pointStructSize = sizeof(float) * 3 + sizeof(float) * 4;
        var pointsBuffer = new ComputeBuffer(data.PointCount, pointStructSize);
        pointsBuffer.SetData(pointData);

        var filteredPointsBuffer = new ComputeBuffer(data.PointCount, pointStructSize, ComputeBufferType.Append);
        filteredPointsBuffer.SetCounterValue(0);

        int kernel = computeShader.FindKernel("CSMain");
        computeShader.SetInt("_PointCount", data.PointCount);
        computeShader.SetFloat("_SearchRadius", searchRadius);
        computeShader.SetInt("_NeighborThreshold", threshold);
        computeShader.SetBuffer(kernel, "_Points", pointsBuffer);
        computeShader.SetBuffer(kernel, "_FilteredPoints", filteredPointsBuffer);

        int threadGroups = Mathf.CeilToInt(data.PointCount / 64.0f);
        computeShader.Dispatch(kernel, threadGroups, 1, 1);

        var countBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw);
        ComputeBuffer.CopyCount(filteredPointsBuffer, countBuffer, 0);
        int[] countArray = { 0 };
        countBuffer.GetData(countArray);
        int filteredPointCount = countArray[0];

        var filteredVertices = new List<Vector3>();
        var filteredColors = new List<Color>();

        if (filteredPointCount > 0)
        {
            var filteredPointData = new Point[filteredPointCount];
            filteredPointsBuffer.GetData(filteredPointData, 0, 0, filteredPointCount);

            for (int i = 0; i < filteredPointCount; i++)
            {
                filteredVertices.Add(filteredPointData[i].position);
                filteredColors.Add(filteredPointData[i].color);
            }
        }

        pointsBuffer.Release();
        filteredPointsBuffer.Release();
        countBuffer.Release();

        stopwatch.Stop();
        elapsedMilliseconds = stopwatch.ElapsedMilliseconds;

        return new PCV_Data(filteredVertices, filteredColors);
    }

    public PCV_Data FilterMorpologyGPU(ComputeShader computeShader, float voxelSize, int erosionIterations, int dilationIterations, out long elapsedMilliseconds)
    {
        var stopwatch = Stopwatch.StartNew();

        if (data == null || data.PointCount == 0 || computeShader == null)
        {
            stopwatch.Stop();
            elapsedMilliseconds = stopwatch.ElapsedMilliseconds;
            return new PCV_Data(new List<Vector3>(), new List<Color>());
        }

        int pointStructSize = sizeof(float) * 3 + sizeof(float) * 4;
        int maxPoints = data.PointCount;

        var initialPointData = new Point[maxPoints];
        for (int i = 0; i < maxPoints; i++)
        {
            initialPointData[i] = new Point { position = data.Vertices[i], color = data.Colors[i] };
        }

        ComputeBuffer bufferA = new ComputeBuffer(maxPoints, pointStructSize, ComputeBufferType.Default);
        ComputeBuffer bufferB = new ComputeBuffer(maxPoints, pointStructSize, ComputeBufferType.Default);

        // カウンターバッファ (RW_BUFFER_U_INT _PointCountOut に対応)
        // 1要素のuintを保持
        ComputeBuffer countBuffer = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Default);

        // 初期データ設定 (bufferAを最初の入力とする)
        bufferA.SetData(initialPointData);

        int morpologyKernel = computeShader.FindKernel("CSMorpology");

        // 現在の有効点数
        int currentPointCount = maxPoints;

        // 現在のバッファのインデックス (0: bufferAがIN, bufferBがOUT)
        int currentBufferIndex = 0;

        // -------------------------------------------------
        // 2. 複数回処理の実行 (GPUで完結)
        // -------------------------------------------------

        // 侵食 (Erosion) -> 膨張 (Dilation) の順に処理
        int totalIterations = erosionIterations + dilationIterations;

        for (int iter = 0; iter < totalIterations; iter++)
        {
            bool isErosion = iter < erosionIterations;

            // バッファの決定 (Ping-Pong)
            ComputeBuffer pointsIn = (currentBufferIndex == 0) ? bufferA : bufferB;
            ComputeBuffer pointsOut = (currentBufferIndex == 0) ? bufferB : bufferA;

            // 💡 カウンターをリセット（次の出力点数を0にする）
            countBuffer.SetData(new uint[] { 0 });

            // 1. Morpology カーネルのパラメーター設定
            computeShader.SetInt("_PointCountIn", currentPointCount);
            computeShader.SetFloat("_VoxelSize", voxelSize);
            computeShader.SetInt("_CurrentIterationMode", isErosion ? 0 : 1); // 0:侵食, 1:膨張

            // バッファのバインド
            computeShader.SetBuffer(morpologyKernel, "_PointsIn", pointsIn);
            computeShader.SetBuffer(morpologyKernel, "_PointsOut", pointsOut);
            computeShader.SetBuffer(morpologyKernel, "_PointCountOut", countBuffer);

            // 2. ディスパッチ実行
            int threadGroups = Mathf.CeilToInt(currentPointCount / 64.0f);
            if (threadGroups > 0)
            {
                computeShader.Dispatch(morpologyKernel, threadGroups, 1, 1);
            }

            // 3. 結果の点数を取得 (GPU -> CPU通信: ここだけは必須)
            uint[] countArray = { 0 };
            countBuffer.GetData(countArray);
            int nextPointCount = (int)countArray[0];

            // 4. 次のイテレーションの準備
            currentPointCount = nextPointCount;

            if (currentPointCount == 0)
            {
                // 点がなくなったら、以降の処理はスキップ
                currentBufferIndex = (currentBufferIndex == 0) ? 1 : 0; // 最終結果が空でないバッファに入るように調整
                break;
            }

            // バッファを反転
            currentBufferIndex = (currentBufferIndex == 0) ? 1 : 0;
        }

        // ------------------------------------
        // 3. 最終結果の取得
        // ------------------------------------

        // 最終的なデータが格納されているバッファ
        ComputeBuffer finalBuffer = (currentBufferIndex == 0) ? bufferA : bufferB;

        var filteredVertices = new List<Vector3>();
        var filteredColors = new List<Color>();

        if (currentPointCount > 0)
        {
            var filteredPointData = new Point[currentPointCount];
            finalBuffer.GetData(filteredPointData, 0, 0, currentPointCount); // 最終結果のみCPUに転送

            for (int i = 0; i < currentPointCount; i++)
            {
                filteredVertices.Add(filteredPointData[i].position);
                filteredColors.Add(filteredPointData[i].color);
            }
        }

        // 4. バッファの解放
        bufferA.Release();
        bufferB.Release();
        countBuffer.Release();

        stopwatch.Stop();
        elapsedMilliseconds = stopwatch.ElapsedMilliseconds;

        return new PCV_Data(filteredVertices, filteredColors);
    }
}