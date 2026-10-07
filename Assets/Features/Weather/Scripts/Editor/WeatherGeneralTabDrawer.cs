#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Features.Weather.Editor
{
    /// <summary>
    /// WeatherManager の General タブ（空間・カメラ設定、共有空間バウンズ、自動セットアップボタン）の
    /// GUI 描画を担当する Drawer クラス。
    /// </summary>
    public static class WeatherGeneralTabDrawer
    {
        public static void Draw(
            WeatherManager manager,
            SerializedProperty viewerProp,
            SerializedProperty cloudYProp,
            SerializedProperty groundYProp,
            SerializedProperty rainFadeDurationProp,
            SerializedProperty useSharedBoundsProp,
            SerializedProperty boundsCenterProp,
            SerializedProperty boundsSizeProp,
            SerializedProperty followViewerProp)
        {
            EditorGUILayout.LabelField("Space & Camera Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(viewerProp, new GUIContent("Viewer (Camera)"));
            EditorGUILayout.PropertyField(cloudYProp, new GUIContent("Cloud Y (Spawn/Strike Sky)"));
            EditorGUILayout.PropertyField(groundYProp, new GUIContent("Ground Y (Floor)"));
            EditorGUILayout.PropertyField(rainFadeDurationProp, new GUIContent("Fade Duration (s)"));

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Shared Space Bounds (X, Z 平面)", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(useSharedBoundsProp, new GUIContent("Use Shared Bounds"));
            if (useSharedBoundsProp.boolValue)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.PropertyField(boundsCenterProp, new GUIContent("Bounds Center (X, Z)"));
                    EditorGUILayout.PropertyField(boundsSizeProp, new GUIContent("Bounds Size (X, Z)"));
                    EditorGUILayout.PropertyField(followViewerProp, new GUIContent("Follow Viewer (Camera XZ)"));
                }
            }

            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("Auto Setup & Integration", EditorStyles.boldLabel);
            if (GUILayout.Button("Setup & Serialize All Assets (Materials, Lights, Processors)", GUILayout.Height(30)))
            {
                WeatherSetupUtility.BuildWeatherHierarchy(manager.gameObject);
                EditorUtility.DisplayDialog("Weather Setup", "マテリアル・ライト・プロセッサの自動セットアップが完了しました。", "OK");
            }

            EditorGUILayout.Space(4);
            if (GUILayout.Button("Apply PCD Layer to Weather (for PCDRenderer Occlusion)", GUILayout.Height(28)))
            {
                WeatherSetupUtility.ApplyPcdLayerToWeather();
            }

            EditorGUILayout.Space(2);
            if (GUILayout.Button("Remove Weather from PR_VirtualObjectManager (Clean-up)", GUILayout.Height(24)))
            {
                WeatherSetupUtility.RemoveWeatherFromVirtualObjectManager();
            }
        }
    }
}
#endif
