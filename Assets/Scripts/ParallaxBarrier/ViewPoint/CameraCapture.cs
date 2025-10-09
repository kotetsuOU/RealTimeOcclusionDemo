using UnityEngine;
using System.IO;
using System.Collections;

public class CameraCapture : MonoBehaviour
{
    [Header("Common Settings")]
    public UnityEngine.Camera targetCamera;
    public int captureWidth = 2560;
    public int captureHeight = 1440;

    [Header("Single Capture Settings")]
    public string singleCaptureFolder = "HandTrakingData/RecordedViewPointPicture/Pictures";

    [Header("Video Recording Settings")]
    public int frameRate = 30;
    public string videoFramesFolder = "HandTrakingData/RecordedViewPointPicture/VideoFrames";

    [Header("Frame Control")]
    [Tooltip("録画を開始するフレーム番号 (カウントは0から)。")]
    public int startFrame = 0;

    [Tooltip("録画を終了するフレーム番号 (このフレームのキャプチャは実行されない)。")]
    public int endFrame = 300;

    [Header("Automation Options")]
    [Tooltip("ゲーム開始時に自動で録画を開始します。")]
    public bool autoStartRecordingOnPlay = false;

    [Tooltip("このキーを押すことで録画の開始/停止をトグルします。")]
    public UnityEngine.KeyCode toggleRecordingKey = UnityEngine.KeyCode.R;

    private bool isRecording = false;
    private string currentVideoFolderPath;
    private int frameCount = 0;

    void Start()
    {
        if (autoStartRecordingOnPlay)
        {
            StartRecording();
        }
    }

    void Update()
    {
        if (UnityEngine.Input.GetKeyDown(toggleRecordingKey))
        {
            if (isRecording)
            {
                StopRecording();
            }
            else
            {
                StartRecording();
            }
        }
    }

    public void Capture()
    {
        string directoryPath = System.IO.Path.Combine(UnityEngine.Application.dataPath, singleCaptureFolder);
        if (!System.IO.Directory.Exists(directoryPath))
        {
            System.IO.Directory.CreateDirectory(directoryPath);
        }

        string fileName = string.Format("capture_{0}.png", System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
        string filePath = System.IO.Path.Combine(directoryPath, fileName);

        SaveFrameToFile(filePath);
        UnityEngine.Debug.Log(string.Format("キャプチャを保存しました: {0}", filePath));
    }

    public void StartRecording()
    {
        if (isRecording)
        {
            UnityEngine.Debug.LogWarning("既に録画が開始されています。");
            return;
        }

        if (endFrame <= startFrame)
        {
            UnityEngine.Debug.LogError(string.Format("終了フレーム ({0}) は開始フレーム ({1}) より大きく設定してください。", endFrame, startFrame));
            return;
        }

        isRecording = true;
        frameCount = 0;

        string timeStamp = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        currentVideoFolderPath = System.IO.Path.Combine(UnityEngine.Application.dataPath, videoFramesFolder, timeStamp);

        if (!System.IO.Directory.Exists(currentVideoFolderPath))
        {
            System.IO.Directory.CreateDirectory(currentVideoFolderPath);
        }

        StartCoroutine(RecordFrames());
        UnityEngine.Debug.Log(string.Format("録画を開始しました。カウント開始: 0、キャプチャ範囲: {0}～{1} (フレーム{1}は除く)、保存先: {2}", startFrame, endFrame, currentVideoFolderPath));
    }

    public void StopRecording()
    {
        if (!isRecording)
        {
            return;
        }

        isRecording = false;
        UnityEngine.Debug.Log("録画を停止しました。合計フレーム数: " + frameCount);
    }

    private System.Collections.IEnumerator RecordFrames()
    {
        float frameDuration = 1f / frameRate;

        while (isRecording)
        {
            if (frameCount >= endFrame)
            {
                StopRecording();
                yield break;
            }

            if (frameCount >= startFrame && frameCount < endFrame)
            {
                string filePath = System.IO.Path.Combine(currentVideoFolderPath, $"frame_{frameCount:D5}.png");
                SaveFrameToFile(filePath);
            }

            frameCount++;

            yield return new UnityEngine.WaitForSeconds(frameDuration);
        }
    }

    private void SaveFrameToFile(string filePath)
    {
        UnityEngine.RenderTexture rt = new UnityEngine.RenderTexture(captureWidth, captureHeight, 24);
        targetCamera.targetTexture = rt;

        UnityEngine.Texture2D screenShot = new UnityEngine.Texture2D(captureWidth, captureHeight, UnityEngine.TextureFormat.RGB24, false);
        targetCamera.Render();

        UnityEngine.RenderTexture.active = rt;
        screenShot.ReadPixels(new UnityEngine.Rect(0, 0, captureWidth, captureHeight), 0, 0);

        targetCamera.targetTexture = null;
        UnityEngine.RenderTexture.active = null;
        UnityEngine.Object.Destroy(rt);

        byte[] bytes = screenShot.EncodeToPNG();
        System.IO.File.WriteAllBytes(filePath, bytes);

        UnityEngine.Object.Destroy(screenShot);
    }
}