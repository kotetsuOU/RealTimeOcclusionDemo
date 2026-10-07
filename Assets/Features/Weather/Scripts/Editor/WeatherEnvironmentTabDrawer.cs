#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Features.Weather.Editor
{
    /// <summary>
    /// WeatherManager の環境・入出力タブ（Lighting, Audio, Input）の
    /// Inspector GUI 描画を担当する Drawer クラス。
    /// </summary>
    public static class WeatherEnvironmentTabDrawer
    {
        public static void DrawLighting(SerializedProperty lightingProp)
        {
            if (lightingProp == null) return;

            EditorGUILayout.LabelField("Scene Lights & Flash Modulation", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(lightingProp.FindPropertyRelative("sunLight"), new GUIContent("Sun (Directional Light)"));
            EditorGUILayout.PropertyField(lightingProp.FindPropertyRelative("boltPointLight"), new GUIContent("Bolt (Point Light)"));

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Dimming (Rain)", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(lightingProp.FindPropertyRelative("lightDarkenRate"), new GUIContent("Sun Darken Rate"));
            EditorGUILayout.PropertyField(lightingProp.FindPropertyRelative("ambientDarkenRate"), new GUIContent("Ambient Darken Rate"));

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Flash Boost (Lightning)", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(lightingProp.FindPropertyRelative("boltLightIntensity"), new GUIContent("Bolt Light Intensity"));
            EditorGUILayout.PropertyField(lightingProp.FindPropertyRelative("boltLightRange"), new GUIContent("Bolt Light Range (m)"));
            EditorGUILayout.PropertyField(lightingProp.FindPropertyRelative("sunFlashBoost"), new GUIContent("Sun Flash Boost"));
            EditorGUILayout.PropertyField(lightingProp.FindPropertyRelative("ambientFlashBoost"), new GUIContent("Ambient Flash Boost"));

            EditorGUILayout.Space(4);
            EditorGUILayout.PropertyField(lightingProp.FindPropertyRelative("postProcessVolume"), new GUIContent("URP Volume (Optional)"));
        }

        public static void DrawAudio(SerializedProperty audioSettingsProp)
        {
            if (audioSettingsProp == null) return;

            EditorGUILayout.LabelField("Procedural DSP Sound Synthesis", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(audioSettingsProp.FindPropertyRelative("rainGain"), new GUIContent("Rain Max Gain"));
            EditorGUILayout.PropertyField(audioSettingsProp.FindPropertyRelative("rainCutoff"), new GUIContent("Rain LowPass Cutoff (Hz)"));

            EditorGUILayout.Space(4);
            EditorGUILayout.PropertyField(audioSettingsProp.FindPropertyRelative("thunderGain"), new GUIContent("Thunder Max Gain"));
            EditorGUILayout.PropertyField(audioSettingsProp.FindPropertyRelative("soundDistanceScale"), new GUIContent("Speed of Sound Scale"));
        }

        public static void DrawInput(SerializedProperty inputProp)
        {
            if (inputProp == null) return;

            EditorGUILayout.LabelField("Hand Tracking & Point Cloud Gesture", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(inputProp.FindPropertyRelative("fallbackHandTransform"), new GUIContent("Fallback Hand Target"));
            EditorGUILayout.PropertyField(inputProp.FindPropertyRelative("minClusterPoints"), new GUIContent("Min Cluster Points"));

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Debug Key Bindings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(inputProp.FindPropertyRelative("toggleRainKey"), new GUIContent("Toggle Rain Key"));
            EditorGUILayout.PropertyField(inputProp.FindPropertyRelative("strikeKey"), new GUIContent("Trigger Strike Key"));
            EditorGUILayout.PropertyField(inputProp.FindPropertyRelative("preset0Key"), new GUIContent("Preset 0% (Clear) Key"));
            EditorGUILayout.PropertyField(inputProp.FindPropertyRelative("preset30Key"), new GUIContent("Preset 30% (Light) Key"));
            EditorGUILayout.PropertyField(inputProp.FindPropertyRelative("preset70Key"), new GUIContent("Preset 70% (Heavy) Key"));
            EditorGUILayout.PropertyField(inputProp.FindPropertyRelative("preset100Key"), new GUIContent("Preset 100% (Storm) Key"));
        }
    }
}
#endif
