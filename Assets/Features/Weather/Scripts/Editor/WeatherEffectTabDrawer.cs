#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Features.Weather.Editor
{
    /// <summary>
    /// WeatherManager の気象エフェクトタブ（Rain, Cloud, Lightning）の
    /// Inspector GUI 描画を担当する Drawer クラス。
    /// </summary>
    public static class WeatherEffectTabDrawer
    {
        public static void DrawRain(SerializedProperty rainProp)
        {
            if (rainProp == null) return;

            EditorGUILayout.LabelField("Rain Fall Speed & Physics", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(rainProp.FindPropertyRelative("fallSpeedMin"), new GUIContent("Min Fall Speed (m/s)"));
            EditorGUILayout.PropertyField(rainProp.FindPropertyRelative("fallSpeedMax"), new GUIContent("Max Fall Speed (m/s)"));

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Rain Bounds & Area", EditorStyles.boldLabel);
            var useCustom = rainProp.FindPropertyRelative("useCustomBounds");
            EditorGUILayout.PropertyField(useCustom, new GUIContent("Use Custom Bounds (Override General)"));
            if (useCustom.boolValue)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.PropertyField(rainProp.FindPropertyRelative("areaCenter"), new GUIContent("Area Center (X, Z)"));
                    EditorGUILayout.PropertyField(rainProp.FindPropertyRelative("areaSize"), new GUIContent("Area Size (X, Z)"));
                }
            }
            else
            {
                EditorGUILayout.HelpBox("現在 General の Shared Space Bounds (X, Z) が適用されています。", MessageType.None);
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Drop Appearance (Size & Stretch)", EditorStyles.boldLabel);
            EditorGUILayout.Slider(rainProp.FindPropertyRelative("dropSize"), 0.0005f, 0.01f, new GUIContent("Drop Size / Width (m)"));
            EditorGUILayout.Slider(rainProp.FindPropertyRelative("lengthScale"), 0.5f, 5.0f, new GUIContent("Length Scale (Stretch)"));

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Particle & Collision Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(rainProp.FindPropertyRelative("material"), new GUIContent("Particle Material"));
            EditorGUILayout.PropertyField(rainProp.FindPropertyRelative("maxRate"), new GUIContent("Max Emission Rate (/s)"));
            EditorGUILayout.PropertyField(rainProp.FindPropertyRelative("collisionMode"), new GUIContent("Collision Mode"));
            EditorGUILayout.PropertyField(rainProp.FindPropertyRelative("environmentMask"), new GUIContent("Environment Mask"));
        }

        public static void DrawCloud(SerializedProperty cloudsProp)
        {
            if (cloudsProp == null) return;

            EditorGUILayout.LabelField("Cloud Rendering (PCD Layer / Opaque)", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(cloudsProp.FindPropertyRelative("enableClouds"), new GUIContent("Enable Clouds (True/False)"));
            EditorGUILayout.PropertyField(cloudsProp.FindPropertyRelative("material"), new GUIContent("Cloud Material (Opaque Lit)"));

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Appearance & Color Gradient (Intensity 0 → 1)", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(cloudsProp.FindPropertyRelative("lightCloudColor"), new GUIContent("Light Cloud Color (Clear/0%)"));
            EditorGUILayout.PropertyField(cloudsProp.FindPropertyRelative("stormCloudColor"), new GUIContent("Storm Cloud Color (Storm/100%)"));

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Volume & Coverage (Expansion)", EditorStyles.boldLabel);
            EditorGUILayout.Slider(cloudsProp.FindPropertyRelative("minScale"), 0.01f, 1.5f, new GUIContent("Min Scale (Intensity 0)"));
            EditorGUILayout.Slider(cloudsProp.FindPropertyRelative("maxScale"), 0.02f, 3.0f, new GUIContent("Max Scale (Intensity 1)"));
            EditorGUILayout.IntSlider(cloudsProp.FindPropertyRelative("clusterCount"), 3, 16, new GUIContent("Cluster Count"));
            EditorGUILayout.IntSlider(cloudsProp.FindPropertyRelative("puffsPerCluster"), 3, 10, new GUIContent("Puffs Per Cluster"));
            EditorGUILayout.Slider(cloudsProp.FindPropertyRelative("cloudThickness"), 0.01f, 0.5f, new GUIContent("Cloud Thickness (m)"));

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Drift Animation", EditorStyles.boldLabel);
            EditorGUILayout.Slider(cloudsProp.FindPropertyRelative("driftSpeed"), 0f, 0.2f, new GUIContent("Drift Speed"));
        }

        public static void DrawLightning(SerializedProperty lightningProp, SerializedProperty useSharedBoundsProp)
        {
            if (lightningProp == null) return;

            EditorGUILayout.LabelField("Lightning Bolt & Strike Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(lightningProp.FindPropertyRelative("material"), new GUIContent("Bolt Material"));

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Strike Area (Ground Picking)", EditorStyles.boldLabel);
            var useCustom = lightningProp.FindPropertyRelative("useCustomBounds");
            EditorGUILayout.PropertyField(useCustom, new GUIContent("Use Custom Bounds (Override General)"));
            if (useCustom.boolValue)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.PropertyField(lightningProp.FindPropertyRelative("strikeAreaCenter"), new GUIContent("Strike Center (X, Z)"));
                    EditorGUILayout.PropertyField(lightningProp.FindPropertyRelative("strikeAreaSize"), new GUIContent("Strike Size (X, Z)"));
                }
            }
            else if (useSharedBoundsProp != null && useSharedBoundsProp.boolValue)
            {
                EditorGUILayout.HelpBox("現在 General の Shared Space Bounds (X, Z) 矩形平面が落雷エリアとして適用されています（雨の発生範囲と完全一致）。", MessageType.Info);
            }
            else
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.PropertyField(lightningProp.FindPropertyRelative("minRadius"), new GUIContent("Min Strike Radius (m)"));
                    EditorGUILayout.PropertyField(lightningProp.FindPropertyRelative("maxRadius"), new GUIContent("Max Strike Radius (m)"));
                    EditorGUILayout.PropertyField(lightningProp.FindPropertyRelative("fov"), new GUIContent("Strike FOV (deg)"));
                }
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.PropertyField(lightningProp.FindPropertyRelative("requireLineOfSight"), new GUIContent("Require Line Of Sight"));
            EditorGUILayout.PropertyField(lightningProp.FindPropertyRelative("environmentMask"), new GUIContent("Environment Mask"));
            EditorGUILayout.PropertyField(lightningProp.FindPropertyRelative("minStrikeInterval"), new GUIContent("Min Interval (s)"));

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Bolt Timing & Appearance", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(lightningProp.FindPropertyRelative("coreColor"), new GUIContent("Core Color (Center)"));
            EditorGUILayout.PropertyField(lightningProp.FindPropertyRelative("glowColor"), new GUIContent("Glow Color (Outer)"));
            EditorGUILayout.Slider(lightningProp.FindPropertyRelative("flashDuration"), 0.05f, 1.2f, new GUIContent("Flash Duration (s)"));
            EditorGUILayout.Slider(lightningProp.FindPropertyRelative("fadeDuration"), 0.1f, 1.5f, new GUIContent("Fade Duration (s)"));
            EditorGUILayout.Slider(lightningProp.FindPropertyRelative("trunkWidth"), 0.005f, 0.08f, new GUIContent("Trunk Width (m)"));

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Auto Strike (Storm Mode)", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(lightningProp.FindPropertyRelative("autoThunder"), new GUIContent("Enable Auto Thunder"));
            EditorGUILayout.PropertyField(lightningProp.FindPropertyRelative("stormThreshold"), new GUIContent("Storm Threshold (0〜1)"));
            EditorGUILayout.PropertyField(lightningProp.FindPropertyRelative("meanStrikeInterval"), new GUIContent("Mean Interval (Poisson, s)"));
        }
    }
}
#endif
