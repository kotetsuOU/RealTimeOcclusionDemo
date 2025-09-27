using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(CameraCapture))]
public class CameraCaptureEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        EditorGUILayout.Space();

        CameraCapture captureScript = (CameraCapture)target;

        if (GUILayout.Button("Capture Image"))
        {
            captureScript.Capture();
        }
    }
}