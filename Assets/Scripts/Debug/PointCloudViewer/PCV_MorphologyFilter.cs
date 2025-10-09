using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

public static class PCV_MorphologyFilter
{
    private struct Point
    {
        public Vector3 position;
        public Color color;
    }

    public static PCV_Data ApplyGPU(PCV_Data data, ComputeShader computeShader, float voxelSize, int erosionIterations, int dilationIterations)
    {
        if (data == null || data.PointCount == 0 || computeShader == null)
        {
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
        ComputeBuffer countBuffer = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Default);

        bufferA.SetData(initialPointData);

        int morpologyKernel = computeShader.FindKernel("CSMorpology");
        int currentPointCount = maxPoints;
        int currentBufferIndex = 0;
        int totalIterations = erosionIterations + dilationIterations;

        for (int iter = 0; iter < totalIterations; iter++)
        {
            bool isErosion = iter < erosionIterations;
            ComputeBuffer pointsIn = (currentBufferIndex == 0) ? bufferA : bufferB;
            ComputeBuffer pointsOut = (currentBufferIndex == 0) ? bufferB : bufferA;

            countBuffer.SetData(new uint[] { 0 });
            computeShader.SetInt("_PointCountIn", currentPointCount);
            computeShader.SetFloat("_VoxelSize", voxelSize);
            computeShader.SetInt("_CurrentIterationMode", isErosion ? 0 : 1);
            computeShader.SetBuffer(morpologyKernel, "_PointsIn", pointsIn);
            computeShader.SetBuffer(morpologyKernel, "_PointsOut", pointsOut);
            computeShader.SetBuffer(morpologyKernel, "_PointCountOut", countBuffer);

            int threadGroups = Mathf.CeilToInt(currentPointCount / 64.0f);
            if (threadGroups > 0)
            {
                computeShader.Dispatch(morpologyKernel, threadGroups, 1, 1);
            }

            uint[] countArray = { 0 };
            countBuffer.GetData(countArray);
            currentPointCount = (int)countArray[0];

            if (currentPointCount == 0)
            {
                currentBufferIndex = (currentBufferIndex == 0) ? 1 : 0;
                break;
            }
            currentBufferIndex = (currentBufferIndex == 0) ? 1 : 0;
        }

        ComputeBuffer finalBuffer = (currentBufferIndex == 0) ? bufferA : bufferB;
        var filteredVertices = new List<Vector3>();
        var filteredColors = new List<Color>();

        if (currentPointCount > 0)
        {
            var filteredPointData = new Point[currentPointCount];
            finalBuffer.GetData(filteredPointData, 0, 0, currentPointCount);
            for (int i = 0; i < currentPointCount; i++)
            {
                filteredVertices.Add(filteredPointData[i].position);
                filteredColors.Add(filteredPointData[i].color);
            }
        }

        bufferA.Release();
        bufferB.Release();
        countBuffer.Release();

        return new PCV_Data(filteredVertices, filteredColors);
    }
}