using UnityEngine;
using System.IO;

public class CameraCapture : MonoBehaviour
{
    public Camera targetCamera;

    public int captureWidth = 2560;
    public int captureHeight = 1440;

    public string folderPath = "HandTrakingData/RecordedViewPointPicture";

    public void Capture()
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
        string directoryPath = Path.Combine(UnityEngine.Application.dataPath, folderPath);

        if (!Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        string fileName = string.Format("{0}/capture_{1}.png", directoryPath, System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
        File.WriteAllBytes(fileName, bytes);

        UnityEngine.Debug.Log(string.Format("ƒLƒƒƒvƒ`ƒƒ‚ð•Û‘¶‚µ‚Ü‚µ‚½: {0}", fileName));
    }
}