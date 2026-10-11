using System;
using Intel.RealSense;
using UnityEngine;

namespace RealSense.DummyPointCloud
{
    /// <summary>
    /// Intel RealSense の SoftwareDevice との接続・フレーム発行・ライフサイクルを管理するブリッジクラス。
    /// </summary>
    public class RsDummyStreamingBridge : IDisposable
    {
        private RsDummySoftwareDevice _softwareDevice;

        public PipelineProfile ActiveProfile => _softwareDevice?.ActiveProfile;
        public bool IsStreaming => _softwareDevice != null;

        /// <summary>
        /// 仮想 RealSense ソフトウェアデバイスを初期化し、ストリーミングを開始します。
        /// </summary>
        public PipelineProfile StartStreaming(int width, int height, int fps, Action<Frame> onFrameAvailable)
        {
            if (IsStreaming) return ActiveProfile;

            _softwareDevice = new RsDummySoftwareDevice();
            _softwareDevice.Initialize(width, height, fps);
            if (onFrameAvailable != null)
            {
                _softwareDevice.OnFrameAvailable += onFrameAvailable;
            }

            return _softwareDevice.ActiveProfile;
        }

        /// <summary>
        /// ソフトウェアデバイスを停止・破棄します。
        /// </summary>
        public void StopStreaming(Action<Frame> onFrameAvailable)
        {
            if (_softwareDevice != null)
            {
                if (onFrameAvailable != null)
                {
                    _softwareDevice.OnFrameAvailable -= onFrameAvailable;
                }
                _softwareDevice.Dispose();
                _softwareDevice = null;
            }
        }

        /// <summary>
        /// サンプリングされた点群座標配列を仮想 RealSense DepthFrame として発行します。
        /// </summary>
        public void PublishPointCloudAsDepthFrame(Vector3[] positions, Transform cameraTransform, bool useCameraPerspective)
        {
            if (_softwareDevice != null && positions != null && positions.Length > 0)
            {
                _softwareDevice.PublishPointCloudAsDepthFrame(positions, cameraTransform, useCameraPerspective);
            }
        }

        public void Dispose()
        {
            StopStreaming(null);
        }
    }
}
