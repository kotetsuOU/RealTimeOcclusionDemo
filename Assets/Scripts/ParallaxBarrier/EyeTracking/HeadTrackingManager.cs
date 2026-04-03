using UnityEngine;
using Intel.RealSense;
using Mediapipe;
using Mediapipe.Unity;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;

public class HeadTrackingManager : MonoBehaviour
{
    [Header("RealSense Settings")]
    public RsFrameProvider frameProvider;
    
    [Header("Camera to Control")]
    public Transform trackingCamera;
    
    [Header("MediaPipe Settings")]
    public TextAsset configAsset; // FaceDetectionのテキスト形式のグラフファイルをアサイン（.pbtxt 等）

    // カメラの追従速度
    public float positionalSmoothing = 15f; 
    private Vector3 targetHeadPosition;

    // MediaPipe のグラフ管理
    private CalculatorGraph graph;
    private const string inputStreamName = "input_video";
    private const string outputStreamName = "output_detections"; // グラフファイルに依存します
    private OutputStreamPoller<List<Detection>> outputPoller;

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
        // MediaPipe Unity Plugin v0.11+ では直接ResourceManagerのInitializeは不要なケースが多いためスキップ
        yield return null;

        // グラフの構築
        if (configAsset != null)
        {
            graph = new CalculatorGraph(configAsset.text);
            
            // 出力ストリームの監視を設定（新しいバージョンではPollerが直接返る）
            outputPoller = graph.AddOutputStreamPoller<List<Detection>>(outputStreamName);
            
            graph.StartRun();
        }

        // グラフの準備ができたら RealSense のイベント購読を開始
        if (frameProvider != null)
        {
            frameProvider.OnNewSample += OnNewSample;
        }
    }

    private void OnNewSample(Frame frame)
    {
        if (graph == null || outputPoller == null) return;

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

            // 2. MediaPipeで顔の2D座標を検出 (CPU推論)
            Vector2 eyePixelPos = DetectEyePositionWithMediaPipe();

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
        if (colorData == null) return new Vector2(-1, -1);

        try
        {
            // byte[] を NativeArray に変換（Allocator.Temp でメモリを一時確保）
            using (var colorDataNative = new NativeArray<byte>(colorData, Allocator.Temp))
            // RealSenseの色フォーマット（通常RGB8）に合わせてImageFrameを作成
            using (var imageFrame = new ImageFrame(ImageFormat.Types.Format.Srgb, frameWidth, frameHeight, frameWidth * 3, colorDataNative))
            {
                long timestampMicrosec = System.DateTime.Now.Ticks / 10;
                
                // ImageFramePacket は Packet.CreateImageFrameAt に置き換わる
                using (var packet = Packet.CreateImageFrameAt(imageFrame, timestampMicrosec))
                {
                    // グラフに画像を流し込む
                    graph.AddPacketToInputStream(inputStreamName, packet);
                }

                // DetectionVectorPacket は Packet<List<Detection>> に置き換わる
                using (var outputPacket = new Packet<List<Detection>>())
                {
                    if (outputPoller.Next(outputPacket))
                    {
                        var detections = outputPacket.Get(Detection.Parser);
                        if (detections != null && detections.Count > 0)
                        {
                            // 最初の顔を取得
                            var detection = detections[0];
                            var bbox = detection.LocationData.RelativeBoundingBox;

                            // 顔の中心ピクセル（大まかな鼻〜目の間）を計算
                            float centerXPct = bbox.Xmin + bbox.Width / 2.0f;
                            float centerYPct = bbox.Ymin + bbox.Height / 2.0f;

                            float pixelX = centerXPct * frameWidth;
                            float pixelY = centerYPct * frameHeight;

                            return new Vector2(pixelX, pixelY);
                        }
                    }
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

        if (graph != null)
        {
            graph.CloseInputStream(inputStreamName);
            graph.WaitUntilDone();
            graph.Dispose();
        }
    }
}