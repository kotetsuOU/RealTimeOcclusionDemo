using UnityEngine;
using Intel.RealSense;
// using Mediapipe.Unity; // MediaPipeプラグインの想定

public class HeadTrackingManager : MonoBehaviour
{
    [Header("RealSense Settings")]
    public RsFrameProvider frameProvider;
    
    [Header("Camera to Control")]
    public Transform trackingCamera;

    private void Start()
    {
        // RealSenseのフレーム更新イベントに登録
        frameProvider.OnNewSample += OnNewSample;
    }

    private void OnNewSample(Frame frame)
    {
        using (var frames = frame.As<FrameSet>())
        {
            var colorFrame = frames.ColorFrame;
            var depthFrame = frames.DepthFrame;

            if (colorFrame == null || depthFrame == null) return;

            // 1. MediaPipeにColorFrameを渡して顔/目の2D座標を検出
            Vector2 eyePixelPos = DetectEyePositionWithMediaPipe(colorFrame);

            // 顔が検出されなかった場合はスキップ
            if (eyePixelPos.x < 0 || eyePixelPos.y < 0) return;

            // 2. そのピクセルのDepth（距離）を取得
            int x = Mathf.RoundToInt(eyePixelPos.x);
            int y = Mathf.RoundToInt(eyePixelPos.y);
            float distance = depthFrame.GetDistance(x, y);

            if (distance > 0)
            {
                // 3. 2DピクセルとDepthから、RealSenseの3D空間座標にデプロジェクション(逆投影)
                var intrinsics = depthFrame.Profile.As<VideoStreamProfile>().GetIntrinsics();
                float[] point3D = new float[3];
                float[] pixel = new float[2] { x, y };
                
                RsMath.DeprojectPixelToPoint(out point3D, intrinsics, pixel, distance);

                // point3D[0]=X(右), point3D[1]=Y(下), point3D[2]=Z(奥)
                // Unityの座標系(左手系)に変換
                Vector3 headPosition = new Vector3(point3D[0], -point3D[1], point3D[2]);

                // メインスレッドでカメラを動かすために変数を保持
                UpdateCameraPosition(headPosition);
            }
        }
    }

    private Vector2 DetectEyePositionWithMediaPipe(VideoFrame colorFrame)
    {
        // ==========================================
        // ここにMediaPipe (Face Detection / Face Mesh) の
        // 推論コードを記述し、目の中心ピクセル座標を返す
        // ==========================================
        
        // 仮の戻り値
        return new Vector2(colorFrame.Width / 2, colorFrame.Height / 2);
    }

    private void UpdateCameraPosition(Vector3 newPos)
    {
        // RealSenseのコールバックは別スレッドで呼ばれることがあるため、
        // Update()などでメインスレッドでのみTransformを操作するように工夫が必要です。
        // Unityの座標系スケールに合わせた調整（キャリブレーション）もここで行います。
    }

    private void OnDestroy()
    {
        if (frameProvider != null)
        {
            frameProvider.OnNewSample -= OnNewSample;
        }
    }
}