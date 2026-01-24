using UnityEngine;

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
        _filterShader.SetBuffer(_filterKernel, "rawVertices", rawVertices);
        _filterShader.SetBuffer(_filterKernel, "filteredVertices", filteredVertices);
        _filterShader.SetBuffer(_filterKernel, "samplingBuffer", samplingBuffer);
        _filterShader.SetBuffer(_filterKernel, "distanceDiscardBuffer", distanceDiscardBuffer);

        _filterShader.SetMatrix("localToWorld", localToWorld);
        _filterShader.SetVector("globalThreshold1", globalThreshold1);
        _filterShader.SetVector("globalThreshold2", globalThreshold2);
        _filterShader.SetInt("vertexCount", vertexCount);
        _filterShader.SetFloat("maxDistance", maxDistance);
        _filterShader.SetVector("linePoint", linePoint);
        _filterShader.SetVector("lineDir", lineDir);
        _filterShader.SetFloat("samplingRate", samplingRate);
        _filterShader.SetInt("randomSeed", randomSeed);

        int threadGroups = Mathf.CeilToInt(vertexCount / (float)THREAD_GROUP_SIZE);
        _filterShader.Dispatch(_filterKernel, threadGroups, 1, 1);
    }

    public void DispatchTransform(
        ComputeBuffer rawVertices,
        ComputeBuffer filteredVertices,
        Matrix4x4 localToWorld,
        Vector3 globalThreshold1,
        Vector3 globalThreshold2,
        int vertexCount)
    {
        _transformShader.SetBuffer(_transformKernel, "rawVertices", rawVertices);
        _transformShader.SetBuffer(_transformKernel, "filteredVertices", filteredVertices);
        _transformShader.SetMatrix("localToWorld", localToWorld);
        _transformShader.SetVector("globalThreshold1", globalThreshold1);
        _transformShader.SetVector("globalThreshold2", globalThreshold2);
        _transformShader.SetInt("vertexCount", vertexCount);

        int threadGroups = Mathf.CeilToInt(vertexCount / (float)THREAD_GROUP_SIZE);
        _transformShader.Dispatch(_transformKernel, threadGroups, 1, 1);
    }
}
