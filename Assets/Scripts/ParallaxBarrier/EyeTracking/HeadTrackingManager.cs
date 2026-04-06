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
using FaceDetectionResult = Mediapipe.Tasks.Components.Containers.DetectionResult;

public class HeadTrackingManager : MonoBehaviour
{
    [Header("RealSense Settings")]
    public RsFrameProvider frameProvider;
    
    [Header("Camera to Control")]
    public Transform trackingCamera;
    
    [Header("Debug Objects")]
    public Transform rightEyeDebug; // デバッグ用：右目のTransform（Sphere等）を指定
    public Transform leftEyeDebug;  // デバッグ用：左目のTransform（Sphere等）を指定
    
    [Header("MediaPipe Settings")]
    public string modelPath = "blaze_face_short_range.bytes"; // configAssetの代わりにモデル名に変更

    // カメラの追従速度
    public float positionalSmoothing = 15f; 
    private Vector3 targetHeadPositionLocal;
    private Vector3 targetRightEyePositionLocal;
    private Vector3 targetLeftEyePositionLocal;

    [Header("Optimization")]
    [Tooltip("Target interval to run MediaPipe Face Detection to save CPU time")]
    public float detectionIntervalSeconds = 0.1f; // 10Hz = 0.1s
    private float lastDetectionTime = 0f;
    private Vector2 lastEyePixelPos = new Vector2(-1, -1);
    private Vector2 lastRightEyePixelPos = new Vector2(-1, -1);
    private Vector2 lastLeftEyePixelPos = new Vector2(-1, -1);

    // MediaPipe のグラフ管理
    private FaceDetector faceDetector;

    // RealSense の画像データを一時保存するバッファ
    private byte[] colorData;
    private int frameWidth;
    private int frameHeight;

    // ゴミ（GC Allocation）を出さないように使い回すためのバッファ
    private FaceDetectionResult detectionResult;

    private void Start()
    {
        if (trackingCamera != null)
            targetHeadPositionLocal = transform.InverseTransformPoint(trackingCamera.position);

        if (rightEyeDebug != null) rightEyeDebug.gameObject.SetActive(false);
        if (leftEyeDebug != null) leftEyeDebug.gameObject.SetActive(false);

        // ResourceManager のセットアップ (ここで AssetsLoader に提供する)
#if UNITY_EDITOR
        AssetLoader.Provide(new LocalResourceManager());
#else
        AssetLoader.Provide(new StreamingAssetsResourceManager());
#endif
        // エラー回避のためnullチェックで分岐しないが提供する
        if (AssetLoader.PrepareAssetAsync("test") == null) 
        { } // dummy

        // MediaPipe の初期化
        StartCoroutine(InitMediaPipe());
    }

    private IEnumerator InitMediaPipe()
    {
        // Glogの初期化なども必要な場合があるため追加
        Glog.Initialize("HeadTrackingManager");

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
            // Time.timeはUnityのメインスレッド以外から呼ぶとエラーになるため、Stopwatchなどを使うかメインスレッドで管理する
            // 簡単な回避策として、Environment.TickCountを使う
            float currentTime = System.Environment.TickCount / 1000f;
            if (currentTime - lastDetectionTime >= detectionIntervalSeconds)
            {
                lastDetectionTime = currentTime;
                // MediaPipeで顔の2D座標を検出 (CPU推論 / Task API利用)
                var positions = DetectEyePositionWithMediaPipe();
                lastEyePixelPos = positions.center;
                lastRightEyePixelPos = positions.rightEye;
                lastLeftEyePixelPos = positions.leftEye;
            }

            Vector2 centerPixel = lastEyePixelPos;

            if (centerPixel.x < 0 || centerPixel.y < 0) return;

            // 3. 検出したピクセルの Depth (距離) を取得し、3D座標へ変換する処理
            var profile = depthFrame.Profile.As<VideoStreamProfile>();

            var centerLocal = GetLocalPositionFromPixel(centerPixel, depthFrame, profile);
            if (centerLocal.HasValue) targetHeadPositionLocal = centerLocal.Value;

            var rightLocal = GetLocalPositionFromPixel(lastRightEyePixelPos, depthFrame, profile);
            if (rightLocal.HasValue) targetRightEyePositionLocal = rightLocal.Value;

            var leftLocal = GetLocalPositionFromPixel(lastLeftEyePixelPos, depthFrame, profile);
            if (leftLocal.HasValue) targetLeftEyePositionLocal = leftLocal.Value;
        }
    }

    // 2DピクセルとDepthから RealSense の 3D 空間座標（ローカル）に変換するヘルパー
    private Vector3? GetLocalPositionFromPixel(Vector2 pixelPos, DepthFrame depthFrame, VideoStreamProfile profile)
    {
        if (pixelPos.x < 0 || pixelPos.y < 0) return null;

        int x = Mathf.Clamp(Mathf.RoundToInt(pixelPos.x), 0, depthFrame.Width - 1);
        int y = Mathf.Clamp(Mathf.RoundToInt(pixelPos.y), 0, depthFrame.Height - 1);
        float distance = depthFrame.GetDistance(x, y);

        if (distance > 0)
        {
            var intrinsics = profile.GetIntrinsics();
            float[] point3D = new float[3];
            point3D[0] = distance * (x - intrinsics.ppx) / intrinsics.fx;
            point3D[1] = distance * (y - intrinsics.ppy) / intrinsics.fy;
            point3D[2] = distance;

            // Unityの座標系(左手系)に変換（Zは奥が正、Yは上が正に合わせて調整が必要な場合あり）
            return new Vector3(point3D[0], -point3D[1], point3D[2]);
        }

        return null;
    }

    private (Vector2 center, Vector2 rightEye, Vector2 leftEye) DetectEyePositionWithMediaPipe()
    {
        var invalidPos = new Vector2(-1, -1);
        if (colorData == null || faceDetector == null) return (invalidPos, invalidPos, invalidPos);

        try
        {
            // byte[] を NativeArray に変換
            // 別スレッド＆10Hz処理による「4フレーム寿命」の警告 (Internal: deleting an allocation...) 
            // を回避するため、TempJobではなく Persistent を使用して using で即時破棄する
            using (var colorDataNative = new NativeArray<byte>(colorData, Allocator.Persistent))
            // RealSenseの色フォーマット（通常RGB8）に合わせてImageを作成
            using (var image = new Mediapipe.Image(ImageFormat.Types.Format.Srgb, frameWidth, frameHeight, frameWidth * 3, colorDataNative))
            {
                // Imageモードで顔検出を実行 (TryDetectを使ってガベージ発生を抑える)
                if (faceDetector.TryDetect(image, null, ref detectionResult))
                {
                    if (detectionResult.detections != null && detectionResult.detections.Count > 0)
                    {
                        // 最初の顔を取得
                        var detection = detectionResult.detections[0];
                        var bbox = detection.boundingBox;

                        // 顔の中心ピクセル（ピクセル座標系）を計算
                        float centerXPct = (bbox.left + bbox.right) / 2.0f;
                        float centerYPct = (bbox.top + bbox.bottom) / 2.0f;
                        Vector2 center = new Vector2(centerXPct, centerYPct);

                        Vector2 rightEye = invalidPos;
                        Vector2 leftEye = invalidPos;

                        // BlazeFaceのキーポイント: 0=RightEye, 1=LeftEye, 2=NoseTip, 3=Mouth, 4=RightEar, 5=LeftEar (MediaPipe仕様に準じる)
                        if (detection.keypoints != null && detection.keypoints.Count >= 2)
                        {
                            var re = detection.keypoints[0]; // 右目
                            var le = detection.keypoints[1]; // 左目
                            rightEye = new Vector2(re.x * frameWidth, re.y * frameHeight);
                            leftEye  = new Vector2(le.x * frameWidth, le.y * frameHeight);
                        }

                        // 目を狙うなら少し上にオフセットをかける等
                        return (center, rightEye, leftEye);
                    }
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"MediaPipe Inference Error: {e.Message}");
        }

        return (invalidPos, invalidPos, invalidPos);
    }

    private void Update()
    {
        if (trackingCamera != null)
        {
            // メインスレッドでローカル座標をグローバル（World）座標に変換する
            Vector3 newHeadPosWorld = transform.TransformPoint(targetHeadPositionLocal);

            // 滑らかにカメラを追従させる
            trackingCamera.position = Vector3.Lerp(trackingCamera.position, newHeadPosWorld, Time.deltaTime * positionalSmoothing);
        }

        if (rightEyeDebug != null)
        {
            if (lastRightEyePixelPos.x >= 0 && !rightEyeDebug.gameObject.activeSelf) 
                rightEyeDebug.gameObject.SetActive(true);

            if (rightEyeDebug.gameObject.activeSelf)
            {
                Vector3 worldPos = transform.TransformPoint(targetRightEyePositionLocal);
                rightEyeDebug.position = Vector3.Lerp(rightEyeDebug.position, worldPos, Time.deltaTime * positionalSmoothing);
            }
        }

        if (leftEyeDebug != null)
        {
            if (lastLeftEyePixelPos.x >= 0 && !leftEyeDebug.gameObject.activeSelf) 
                leftEyeDebug.gameObject.SetActive(true);

            if (leftEyeDebug.gameObject.activeSelf)
            {
                Vector3 worldPos = transform.TransformPoint(targetLeftEyePositionLocal);
                leftEyeDebug.position = Vector3.Lerp(leftEyeDebug.position, worldPos, Time.deltaTime * positionalSmoothing);
            }
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

        Glog.Shutdown();
    }
}