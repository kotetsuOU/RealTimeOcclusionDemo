using UnityEngine;
using Intel.RealSense;
using Mediapipe;
using Mediapipe.Unity;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Mediapipe.Unity.Sample;
using Mediapipe.Tasks.Core;
using Mediapipe.Tasks.Vision.Core;
using Mediapipe.Tasks.Vision.FaceDetector;

public class HeadTrackingManager : MonoBehaviour
{
    [Header("RealSense Settings")]
    public RsFrameProvider frameProvider;
    
    [Header("Camera to Control")]
    public Transform trackingCamera;
    
    [Header("MediaPipe Settings")]
    public string modelPath = "blaze_face_short_range.bytes"; // configAssetの代わりにモデル名に変更

    // カメラの追従速度
    public float positionalSmoothing = 15f; 
    private Vector3 targetHeadPosition;

    [Header("Optimization")]
    [Tooltip("Target interval to run MediaPipe Face Detection to save CPU time")]
    public float detectionIntervalSeconds = 0.1f; // 10Hz = 0.1s
    private float lastDetectionTime = 0f;
    private Vector2 lastEyePixelPos = new Vector2(-1, -1);

    // MediaPipe のグラフ管理
    private FaceDetector faceDetector;

    // RealSense の画像データを一時保存するバッファ
    private byte[] colorData;
    private int frameWidth;
    private int frameHeight;

    private void Start()
    {
        if (trackingCamera != null)
            targetHeadPosition = trackingCamera.position;

        // MediaPipe の初期化
        StartCoroutine(InitMediaPipe());
    }

    private IEnumerator InitMediaPipe()
    {
        // FaceDetector用モデル(.bytes等)の非同期読み込み（StreamingAssets等からのロード）
        yield return AssetLoader.PrepareAssetAsync(modelPath);

        // FaceDetectorの設定
        var options = new FaceDetectorOptions(
            new BaseOptions(BaseOptions.Delegate.CPU, modelAssetPath: modelPath),
            runningMode: Mediapipe.Tasks.Vision.Core.RunningMode.IMAGE,
            minDetectionConfidence: 0.5f,
            minSuppressionThreshold: 0.3f,
            numFaces: 1
        );

        // APIを利用してFaceDetectorを初期化（C#スクリプトからの生成アプローチB）
        faceDetector = FaceDetector.CreateFromOptions(options);

        // グラフの準備ができたら RealSense のイベント購読を開始
        if (frameProvider != null)
        {
            frameProvider.OnNewSample += OnNewSample;
        }
    }

    private void OnNewSample(Frame frame)
    {
        if (faceDetector == null) return;

        using (var frames = frame.As<FrameSet>())
        {
            var colorFrame = frames.ColorFrame;
            var depthFrame = frames.DepthFrame;

            if (colorFrame == null || depthFrame == null) return;

            frameWidth = colorFrame.Width;
            frameHeight = colorFrame.Height;
            int stride = colorFrame.Stride;

            // 1. 画像バッファの確保とコピー
            if (colorData == null || colorData.Length != stride * frameHeight)
            {
                colorData = new byte[stride * frameHeight];
            }
            colorFrame.CopyTo(colorData);

            // 2. フレームレート制限 (10Hz等)
            if (Time.time - lastDetectionTime >= detectionIntervalSeconds)
            {
                lastDetectionTime = Time.time;
                // MediaPipeで顔の2D座標を検出 (CPU推論 / Task API利用)
                lastEyePixelPos = DetectEyePositionWithMediaPipe();
            }

            Vector2 eyePixelPos = lastEyePixelPos;

            if (eyePixelPos.x < 0 || eyePixelPos.y < 0) return;

            // 3. 検出したピクセルの Depth (距離) を取得
            int x = Mathf.Clamp(Mathf.RoundToInt(eyePixelPos.x), 0, depthFrame.Width - 1);
            int y = Mathf.Clamp(Mathf.RoundToInt(eyePixelPos.y), 0, depthFrame.Height - 1);
            float distance = depthFrame.GetDistance(x, y);

            if (distance > 0)
            {
                // 4. 2DピクセルとDepthから RealSense の 3D 空間座標に変換
                var intrinsics = depthFrame.Profile.As<VideoStreamProfile>().GetIntrinsics();
                
                // RsMathが不足している環境に対応するため、自前でデプロジェクション計算を行う
                float[] point3D = new float[3];
                point3D[0] = distance * (x - intrinsics.ppx) / intrinsics.fx;
                point3D[1] = distance * (y - intrinsics.ppy) / intrinsics.fy;
                point3D[2] = distance;

                // Unityの座標系(左手系)に変換（Zは奥が正、Yは上が正に合わせて調整が必要な場合あり）
                Vector3 newHeadPos = new Vector3(point3D[0], -point3D[1], point3D[2]);

                targetHeadPosition = newHeadPos;
            }
        }
    }

    private Vector2 DetectEyePositionWithMediaPipe()
    {
        if (colorData == null || faceDetector == null) return new Vector2(-1, -1);

        try
        {
            // byte[] を NativeArray に変換（Allocator.Temp でメモリを一時確保）
            using (var colorDataNative = new NativeArray<byte>(colorData, Allocator.Temp))
            // RealSenseの色フォーマット（通常RGB8）に合わせてImageを作成
            using (var image = new Mediapipe.Image(ImageFormat.Types.Format.Srgb, frameWidth, frameHeight, frameWidth * 3, colorDataNative))
            {
                // Imageモードで顔検出を実行
                var detectionResult = faceDetector.Detect(image);

                if (detectionResult.detections != null && detectionResult.detections.Count > 0)
                {
                    // 最初の顔を取得
                    var detection = detectionResult.detections[0];
                    var bbox = detection.boundingBox;

                    // 顔の中心ピクセル（ピクセル座標系）を計算
                    float centerXPct = (bbox.left + bbox.right) / 2.0f;
                    float centerYPct = (bbox.top + bbox.bottom) / 2.0f;

                    // 目を狙うなら少し上にオフセットをかける等
                    return new Vector2(centerXPct, centerYPct);
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"MediaPipe Inference Error: {e.Message}");
        }

        return new Vector2(-1, -1);
    }

    private void Update()
    {
        if (trackingCamera != null)
        {
            // 滑らかにカメラを追従させる
            trackingCamera.position = Vector3.Lerp(trackingCamera.position, targetHeadPosition, Time.deltaTime * positionalSmoothing);
        }
    }

    private void OnDestroy()
    {
        if (frameProvider != null)
        {
            frameProvider.OnNewSample -= OnNewSample;
        }

        if (faceDetector != null)
        {
            faceDetector.Close();
        }
    }
}