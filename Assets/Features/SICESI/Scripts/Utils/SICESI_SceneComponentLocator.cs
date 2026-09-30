using UnityEngine;
using RealSense.DummyPointCloud;

namespace SICESI
{
    /// <summary>
    /// シーン内の評価対象コンポーネント（左右眼カメラ、シーン俯瞰カメラ、点群レンダラー、仮想物体）を
    /// 階層や命名規則に基づいて自動検出するロケータークラス。
    /// </summary>
    public static class SICESI_SceneComponentLocator
    {
        public static void LocateCameras(ref Camera leftEyeCamera, ref Camera rightEyeCamera, ref Camera sceneCaptureCamera)
        {
#if UNITY_2023_1_OR_NEWER
            var cameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            var cameras = Object.FindObjectsOfType<Camera>(true);
#endif
            foreach (var cam in cameras)
            {
                if (cam == null) continue;
                string camName = cam.name.ToLower();
                if (camName.Contains("lefteye") || camName.EndsWith("_l") || camName.Contains("left"))
                {
                    leftEyeCamera = cam;
                }
                else if (camName.Contains("righteye") || camName.EndsWith("_r") || camName.Contains("right"))
                {
                    rightEyeCamera = cam;
                }
                else if (sceneCaptureCamera == null && (camName.Contains("scene") || camName.Contains("overview") || camName.Contains("capture")))
                {
                    sceneCaptureCamera = cam;
                }
            }

            if (leftEyeCamera != null || rightEyeCamera != null || sceneCaptureCamera != null)
            {
                Debug.Log($"[SICESI] カメラを自動検出しました: Left={leftEyeCamera?.name}, Right={rightEyeCamera?.name}, Scene={sceneCaptureCamera?.name}");
            }
        }

        public static void LocateDummyComponents(
            ref RsDummyPointCloudProvider dummyPointCloudProvider,
            ref GameObject pointCloudObject,
            ref PCDOcclusionPipelineController occlusionPipelineController,
            ref GameObject virtualObject)
        {
#if UNITY_2023_1_OR_NEWER
            dummyPointCloudProvider = Object.FindFirstObjectByType<RsDummyPointCloudProvider>();
            var renderer = Object.FindFirstObjectByType<RsDummyPointCloudRenderer>();
            if (occlusionPipelineController == null)
            {
                occlusionPipelineController = Object.FindFirstObjectByType<PCDOcclusionPipelineController>();
            }
#else
            dummyPointCloudProvider = Object.FindObjectOfType<RsDummyPointCloudProvider>();
            var renderer = Object.FindObjectOfType<RsDummyPointCloudRenderer>();
            if (occlusionPipelineController == null)
            {
                occlusionPipelineController = Object.FindObjectOfType<PCDOcclusionPipelineController>();
            }
#endif
            if (renderer != null && pointCloudObject == null)
            {
                pointCloudObject = renderer.gameObject;
            }

            if (virtualObject == null)
            {
                var vo = GameObject.Find("VirtualObjects");
                if (vo != null)
                {
                    virtualObject = vo;
                    Debug.Log($"[SICESI] 仮想オブジェクトを自動検出しました: {vo.name}");
                }
            }
        }
    }
}
