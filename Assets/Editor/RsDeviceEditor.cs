using Intel.RealSense;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(RsDevice))]
public class RsDeviceEditor : Editor
{
    private SerializedProperty config;
    private SerializedProperty mode;

    /// <summary>
    /// This function is called when the object becomes enabled and active.
    /// </summary>
    void OnEnable()
    {
        config = serializedObject.FindProperty("DeviceConfiguration");
        mode = config.FindPropertyRelative("mode");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        var device = target as RsDevice;
        bool isStreaming = device.isActiveAndEnabled && device.ActiveProfile != null;

        EditorGUI.BeginChangeCheck();

        EditorGUILayout.Space();

        EditorGUI.BeginDisabledGroup(isStreaming);
        mode.enumValueIndex = GUILayout.Toolbar(mode.enumValueIndex, mode.enumDisplayNames);
        EditorGUILayout.Space();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("processMode"));
        EditorGUILayout.Space();
        EditorGUI.EndDisabledGroup();

        switch ((RsConfiguration.Mode)mode.enumValueIndex)
        {
            case RsConfiguration.Mode.Live:
                {
                    EditorGUI.BeginDisabledGroup(isStreaming);
                    EditorGUILayout.PropertyField(config.FindPropertyRelative("RequestedSerialNumber"));
                    EditorGUILayout.Space();
                    EditorGUILayout.PropertyField(config.FindPropertyRelative("Profiles"), true);
                    EditorGUILayout.Space();
                    EditorGUI.EndDisabledGroup();
                    break;
                }
            case RsConfiguration.Mode.Playback:
                {
                    EditorGUI.BeginDisabledGroup(isStreaming);
                    EditorGUILayout.PropertyField(config.FindPropertyRelative("RequestedSerialNumber"));
                    EditorGUILayout.BeginHorizontal();
                    var prop = config.FindPropertyRelative("PlaybackFile");
                    EditorGUILayout.PropertyField(prop);
                    if (GUILayout.Button("Open", EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                    {
                        var path = EditorUtility.OpenFilePanel("Recorded sequence", "", "bag");
                        if (path.Length != 0)
                        {
                            prop.stringValue = path;
                        }
                        GUIUtility.ExitGUI();
                    }
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.Space();
                    EditorGUI.EndDisabledGroup();

                    if (isStreaming)
                    {
                        using (var playback = PlaybackDevice.FromDevice(device.ActiveProfile.Device))
                        {
                            bool isPlaying = playback.Status == PlaybackStatus.Playing;
                            var playBtnStyle = EditorGUIUtility.IconContent("PlayButton", "|Play");
                            var pauseBtnStyle = EditorGUIUtility.IconContent("PauseButton", "|Pause");
                            var rewindBtnStyle = EditorGUIUtility.IconContent("animation.firstkey.png");
                            EditorGUILayout.BeginHorizontal();
                            if (GUILayout.Button(rewindBtnStyle, "CommandLeft"))
                                playback.Position = 0;
                            if (GUILayout.Button(isPlaying ? pauseBtnStyle : playBtnStyle, "CommandRight"))
                            {
                                if (isPlaying)
                                    playback.Pause();
                                else
                                    playback.Resume();
                            }
                            EditorGUILayout.EndHorizontal();
                            if (!isPlaying)
                            {
                                playback.Position = (ulong)EditorGUILayout.Slider(playback.Position, 0, playback.Duration);
                            }
                            EditorGUI.BeginDisabledGroup(true);
                            EditorGUILayout.Space();
                            EditorGUILayout.PropertyField(config.FindPropertyRelative("Profiles"), true);
                            EditorGUI.EndDisabledGroup();
                        }
                    }
                    break;
                }
            case RsConfiguration.Mode.Record:
                {
                    EditorGUI.BeginDisabledGroup(isStreaming);
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("recordDurationInFrames"));
                    EditorGUILayout.PropertyField(config.FindPropertyRelative("RequestedSerialNumber"));
                    EditorGUILayout.BeginHorizontal();
                    var prop = config.FindPropertyRelative("RecordPath");
                    EditorGUILayout.PropertyField(prop);
                    if (GUILayout.Button("Choose", EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                    {
                        var path = EditorUtility.SaveFilePanel("Recorded sequence", "", System.DateTime.Now.ToString("yyyyMMdd_hhmmss"), "bag");
                        if (path.Length != 0)
                        {
                            prop.stringValue = path;
                        }
                        GUIUtility.ExitGUI();
                    }
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.Space();
                    EditorGUILayout.PropertyField(config.FindPropertyRelative("Profiles"), true);
                    EditorGUILayout.Space();
                    EditorGUI.EndDisabledGroup();
                    break;
                }
        }

        serializedObject.ApplyModifiedProperties();
        EditorGUI.EndChangeCheck();
    }
}
