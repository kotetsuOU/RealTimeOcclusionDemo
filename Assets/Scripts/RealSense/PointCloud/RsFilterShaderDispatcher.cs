using UnityEngine;
using UnityEngine.Rendering;

public class RsFilterShaderDispatcher
{
    private const int THREAD_GROUP_SIZE = 256;

    private readonly ComputeShader _filterShader;
    private readonly ComputeShader _transformShader;

    private readonly int _filterKernel;
    private readonly int _transformKernel;

    public RsFilterShaderDispatcher(ComputeShader filterShader, ComputeShader transformShader)
    {
        _filterShader = filterShader;
        _transformShader = transformShader;

        _filterKernel = _filterShader.FindKernel("CSMain");
        _transformKernel = _transformShader.FindKernel("CSMain");
    }

    public void DispatchFilter(
        CommandBuffer cmd,
        ComputeBuffer rawVertices,
        ComputeBuffer filteredVertices,
        ComputeBuffer samplingBuffer,
        ComputeBuffer distanceDiscardBuffer,
        Matrix4x4 localToWorld,
        Vector3 globalThreshold1,
        Vector3 globalThreshold2,
        int vertexCount,
        float maxDistance,
        Vector3 linePoint,
        Vector3 lineDir,
        float samplingRate,
        int randomSeed)
    {
        if (cmd == null)
        {
            return;
        }

        cmd.SetComputeBufferParam(_filterShader, _filterKernel, "rawVertices", rawVertices);
        cmd.SetComputeBufferParam(_filterShader, _filterKernel, "filteredVertices", filteredVertices);
        cmd.SetComputeBufferParam(_filterShader, _filterKernel, "samplingBuffer", samplingBuffer);
        cmd.SetComputeBufferParam(_filterShader, _filterKernel, "distanceDiscardBuffer", distanceDiscardBuffer);

        cmd.SetComputeMatrixParam(_filterShader, "localToWorld", localToWorld);
        cmd.SetComputeVectorParam(_filterShader, "globalThreshold1", globalThreshold1);
        cmd.SetComputeVectorParam(_filterShader, "globalThreshold2", globalThreshold2);
        cmd.SetComputeIntParam(_filterShader, "vertexCount", vertexCount);
        cmd.SetComputeFloatParam(_filterShader, "maxDistance", maxDistance);
        cmd.SetComputeVectorParam(_filterShader, "linePoint", linePoint);
        cmd.SetComputeVectorParam(_filterShader, "lineDir", lineDir);
        cmd.SetComputeFloatParam(_filterShader, "samplingRate", samplingRate);
        cmd.SetComputeIntParam(_filterShader, "randomSeed", randomSeed);

        int threadGroups = Mathf.CeilToInt(vertexCount / (float)THREAD_GROUP_SIZE);
        cmd.DispatchCompute(_filterShader, _filterKernel, threadGroups, 1, 1);
    }

    public void DispatchTransform(
        CommandBuffer cmd,
        ComputeBuffer rawVertices,
        ComputeBuffer filteredVertices,
        Matrix4x4 localToWorld,
        Vector3 globalThreshold1,
        Vector3 globalThreshold2,
        int vertexCount)
    {
        if (cmd == null)
        {
            return;
        }

        cmd.SetComputeBufferParam(_transformShader, _transformKernel, "rawVertices", rawVertices);
        cmd.SetComputeBufferParam(_transformShader, _transformKernel, "filteredVertices", filteredVertices);
        cmd.SetComputeMatrixParam(_transformShader, "localToWorld", localToWorld);
        cmd.SetComputeVectorParam(_transformShader, "globalThreshold1", globalThreshold1);
        cmd.SetComputeVectorParam(_transformShader, "globalThreshold2", globalThreshold2);
        cmd.SetComputeIntParam(_transformShader, "vertexCount", vertexCount);

        int threadGroups = Mathf.CeilToInt(vertexCount / (float)THREAD_GROUP_SIZE);
        cmd.DispatchCompute(_transformShader, _transformKernel, threadGroups, 1, 1);
    }
}
