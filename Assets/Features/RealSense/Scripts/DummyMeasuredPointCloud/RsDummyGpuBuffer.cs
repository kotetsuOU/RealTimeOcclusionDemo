using System;
using UnityEngine;

namespace RealSense.DummyPointCloud
{
    /// <summary>
    /// ダミー点群の GPU バッファ（頂点バッファ・間接描画引数バッファ）の確保・転送・描画を担う Pure C# ヘルパー。
    /// </summary>
    public sealed class RsDummyGpuBuffer : IDisposable
    {
        private readonly MeshRenderer _meshRenderer;
        private readonly RsPointCloudVisualization _visualization;
        private readonly uint[] _argsData = new uint[4] { 0, 1, 0, 0 };

        private ComputeBuffer _verticesBuffer;
        private ComputeBuffer _argsBuffer;
        private int _cachedValidPointCount = 0;

        public ComputeBuffer VerticesBuffer => _verticesBuffer;
        public int ValidPointCount => _cachedValidPointCount;

        public RsDummyGpuBuffer(MeshRenderer meshRenderer)
        {
            _meshRenderer = meshRenderer;
            EnsureMaterial();
            _visualization = new RsPointCloudVisualization(_meshRenderer);
            _argsBuffer = new ComputeBuffer(1, sizeof(uint) * 4, ComputeBufferType.IndirectArguments);
        }

        public void EnsureMaterial()
        {
            if (_meshRenderer == null || _meshRenderer.sharedMaterial != null)
                return;

            Shader pcdShader = Shader.Find("Custom/PointCloudSprite") ??
                               Shader.Find("HandTracking/PointCloudSprite") ??
                               Shader.Find("PointCloudViewer/PointCloudViewer") ??
                               Shader.Find("Unlit/Color");

            Material defaultMat = pcdShader != null
                ? new Material(pcdShader) { name = "Generated_PointCloud_Material" }
                : new Material(Shader.Find("Hidden/InternalErrorShader"));

            _meshRenderer.sharedMaterial = defaultMat;
        }

        public void Upload(SampledPointCloudData sampledData)
        {
            EnsureMaterial();

            int count = sampledData.Positions != null ? sampledData.PointCount : 0;
            _cachedValidPointCount = count;

            if (count > 0)
            {
                if (_verticesBuffer == null || _verticesBuffer.count < count)
                {
                    _verticesBuffer?.Release();
                    int newSize = Mathf.NextPowerOfTwo(Mathf.Max(count, 1024));
                    _verticesBuffer = new ComputeBuffer(newSize, sizeof(float) * 3);
                }

                _verticesBuffer.SetData(sampledData.Positions, 0, 0, count);

                _argsData[0] = (uint)count;
                if (_argsBuffer == null || !_argsBuffer.IsValid())
                {
                    _argsBuffer = new ComputeBuffer(1, sizeof(uint) * 4, ComputeBufferType.IndirectArguments);
                }
                _argsBuffer.SetData(_argsData);
            }
            else if (_argsBuffer != null && _argsBuffer.IsValid())
            {
                _argsData[0] = 0;
                _argsBuffer.SetData(_argsData);
            }
        }

        public void Draw(Color color, int layer)
        {
            if (_verticesBuffer != null && _cachedValidPointCount > 0 && _visualization != null)
            {
                _visualization.Draw(_verticesBuffer, _argsBuffer, color, layer);
            }
        }

        public void Dispose()
        {
            _verticesBuffer?.Release();
            _verticesBuffer = null;

            _argsBuffer?.Release();
            _argsBuffer = null;
        }
    }
}

