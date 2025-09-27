using UnityEngine;
using System.IO;
using System.Collections;

public class CameraCapture : MonoBehaviour
{
    [Header("Common Settings")]
    public Camera targetCamera;
    public int captureWidth = 2560;
    public int captureHeight = 1440;

    [Header("Single Capture Settings")]
    public string singleCaptureFolder = "HandTrakingData/RecordedViewPointPicture/Pictures";

    [Header("Video Recording Settings")]
    public int frameRate = 30;
    public string videoFramesFolder = "HandTrakingData/RecordedViewPointPicture/VideoFrames";

    private bool isRecording = false;
    private string currentVideoFolderPath;
    private int frameCount = 0;

    public void Capture()
    {
        string directoryPath = Path.Combine(UnityEngine.Application.dataPath, singleCaptureFolder);
        if (!Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        string fileName = string.Format("capture_{0}.png", System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
        string filePath = Path.Combine(directoryPath, fileName);

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

        isRecording = true;
        frameCount = 0;

        string timeStamp = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        currentVideoFolderPath = Path.Combine(UnityEngine.Application.dataPath, videoFramesFolder, timeStamp);

        if (!Directory.Exists(currentVideoFolderPath))
        {
            Directory.CreateDirectory(currentVideoFolderPath);
        }

        StartCoroutine(RecordFrames());
        UnityEngine.Debug.Log("録画を開始しました。保存先: " + currentVideoFolderPath);
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

    private IEnumerator RecordFrames()
    {
        while (isRecording)
        {
            string filePath = Path.Combine(currentVideoFolderPath, $"frame_{frameCount:D5}.png");
            SaveFrameToFile(filePath);
            frameCount++;

            yield return new WaitForSeconds(1f / frameRate);
        }
    }

    private void SaveFrameToFile(string filePath)
    {
        RenderTexture rt = new RenderTexture(captureWidth, captureHeight, 24);
        targetCamera.targetTexture = rt;

        Texture2D screenShot = new Texture2D(captureWidth, captureHeight, TextureFormat.RGB24, false);
        targetCamera.Render();
        RenderTexture.active = rt;
        screenShot.ReadPixels(new Rect(0, 0, captureWidth, captureHeight), 0, 0);

        targetCamera.targetTexture = null;
        RenderTexture.active = null;
        Destroy(rt);

        byte[] bytes = screenShot.EncodeToPNG();
        File.WriteAllBytes(filePath, bytes);

        Destroy(screenShot);
    }
}