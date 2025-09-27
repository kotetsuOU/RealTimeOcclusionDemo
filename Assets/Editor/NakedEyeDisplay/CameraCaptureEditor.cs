using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(CameraCapture))]
public class CameraCaptureEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        EditorGUILayout.Space(20);

        CameraCapture captureScript = (CameraCapture)target;

        EditorGUILayout.LabelField("Still Image Capture", EditorStyles.boldLabel);
        if (GUILayout.Button("Capture Single Image"))
        {
            captureScript.Capture();
        }

        EditorGUILayout.Space(10);

        EditorGUILayout.LabelField("Video (Image Sequence) Recording", EditorStyles.boldLabel);
        if (EditorApplication.isPlaying)
        {
            if (GUILayout.Button("Start Recording"))
            {
                captureScript.StartRecording();
            }

            if (GUILayout.Button("Stop Recording"))
            {
                captureScript.StopRecording();
            }
        }
        else
        {
            EditorGUILayout.HelpBox("Recording is only available in Play Mode.", MessageType.Info);
        }
    }
}