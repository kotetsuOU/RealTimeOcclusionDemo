using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(RsTransformController))]
public class RsTransformControllerEditor : Editor
{
    // SerializedProperties
    private SerializedProperty configFileNameProp;
    private SerializedProperty loadOnStartProp;
    private SerializedProperty showCalibrationGuideProp;
    private SerializedProperty guideFrameColorProp;
    private SerializedProperty cornerMarkerColorProp;
    private SerializedProperty calibrationOriginProp;
    private SerializedProperty calibrationBoxSizeProp;

    private void OnEnable()
    {
        // プロパティの紐づけ
        configFileNameProp = serializedObject.FindProperty("configFileName");
        loadOnStartProp = serializedObject.FindProperty("loadOnStart");
        showCalibrationGuideProp = serializedObject.FindProperty("showCalibrationGuide");
        guideFrameColorProp = serializedObject.FindProperty("guideFrameColor");
        cornerMarkerColorProp = serializedObject.FindProperty("cornerMarkerColor");
        calibrationOriginProp = serializedObject.FindProperty("calibrationOrigin");
        calibrationBoxSizeProp = serializedObject.FindProperty("calibrationBoxSize");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        RsTransformController controller = (RsTransformController)target;

        // 1. 基本設定の描画
        EditorGUILayout.LabelField("Configuration", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(configFileNameProp);
        EditorGUILayout.PropertyField(loadOnStartProp);

        EditorGUILayout.Space();

        // 2. キャリブレーション設定の描画（Play中は非表示）
        if (!UnityEngine.Application.isPlaying)
        {
            EditorGUILayout.LabelField("Calibration Guide", EditorStyles.boldLabel);

            // ガイド表示のトグル
            EditorGUILayout.PropertyField(showCalibrationGuideProp);

            // ガイドがONのときだけ詳細設定を表示（インデントを付けて階層構造を表現）
            if (showCalibrationGuideProp.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox("緑色の枠線と赤い球に合わせて、点群の位置を調整してください。", MessageType.Info);

                EditorGUILayout.PropertyField(calibrationOriginProp, new GUIContent("Origin (Start Point)"));
                EditorGUILayout.PropertyField(calibrationBoxSizeProp, new GUIContent("Box Size"));

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Visual Settings", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(guideFrameColorProp);
                EditorGUILayout.PropertyField(cornerMarkerColorProp);

                EditorGUI.indentLevel--;
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Transform I/O Operations", EditorStyles.boldLabel);

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        bool isValidName = !string.IsNullOrEmpty(controller.configFileName);
        if (!isValidName)
        {
            EditorGUILayout.HelpBox("Config File Name is required.", MessageType.Warning);
        }

        EditorGUI.BeginDisabledGroup(!isValidName);
        EditorGUILayout.BeginHorizontal();

        GUI.backgroundColor = new Color(0.7f, 0.8f, 1f);
        if (GUILayout.Button("Save"))
        {
            if (EditorUtility.DisplayDialog("Save Transforms",
                $"Save current transforms to '{controller.configFileName}'?", "Yes", "No"))
            {
                controller.SaveTransformConfig();
            }
        }

        GUI.backgroundColor = new Color(1f, 0.8f, 0.7f);
        if (GUILayout.Button("Load"))
        {
            if (EditorUtility.DisplayDialog("Load Transforms",
                $"Load transforms from '{controller.configFileName}'?\nCurrent positions will be overwritten.", "Yes", "No"))
            {
                controller.LoadTransformConfig();
            }
        }

        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.EndVertical();

        serializedObject.ApplyModifiedProperties();
    }
}