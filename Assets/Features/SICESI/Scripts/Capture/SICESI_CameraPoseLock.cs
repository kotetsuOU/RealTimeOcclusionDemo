using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace SICESI
{
    /// <summary>
    /// ステレオカメラ（左右眼）の Transform 位置・回転・ProjectionMatrix をスナップショット保存し、
    /// スイープ撮影中の揺らぎや外部変更を防ぐために beginCameraRendering で姿勢を固定するヘルパークラス。
    /// IDisposable を実装しているため、using スコープで安全に解除できます。
    /// </summary>
    public class SICESI_CameraPoseLock : IDisposable
    {
        private struct CameraPoseSnapshot
        {
            public Vector3 position;
            public Quaternion rotation;
            public Matrix4x4 projectionMatrix;
            public bool hasSnapshot;

            public static CameraPoseSnapshot Capture(Camera cam)
            {
                if (cam == null) return default;
                return new CameraPoseSnapshot
                {
                    position = cam.transform.position,
                    rotation = cam.transform.rotation,
                    projectionMatrix = cam.projectionMatrix,
                    hasSnapshot = true
                };
            }

            public void Apply(Camera cam)
            {
                if (!hasSnapshot || cam == null) return;
                cam.transform.position = position;
                cam.transform.rotation = rotation;
                cam.projectionMatrix = projectionMatrix;
            }
        }

        private readonly Camera _leftCam;
        private readonly Camera _rightCam;
        private CameraPoseSnapshot _leftSnapshot;
        private CameraPoseSnapshot _rightSnapshot;
        private bool _isLocked;

        public SICESI_CameraPoseLock(Camera leftCamera, Camera rightCamera)
        {
            _leftCam = leftCamera;
            _rightCam = rightCamera;
            Lock();
        }

        public void Lock()
        {
            if (_isLocked) return;
            _leftSnapshot = CameraPoseSnapshot.Capture(_leftCam);
            _rightSnapshot = CameraPoseSnapshot.Capture(_rightCam);
            _isLocked = true;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        public void Unlock()
        {
            if (!_isLocked) return;
            _isLocked = false;
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        }

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera cam)
        {
            if (!_isLocked) return;
            if (_leftCam != null && cam == _leftCam)
            {
                _leftSnapshot.Apply(cam);
            }
            else if (_rightCam != null && cam == _rightCam)
            {
                _rightSnapshot.Apply(cam);
            }
        }

        public void Dispose()
        {
            Unlock();
        }
    }
}
