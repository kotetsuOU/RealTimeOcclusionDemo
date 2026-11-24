#if UNITY_EDITOR
#nullable enable
using UnityEditor;
using UnityEngine;

namespace Features.Haptics.Editor
{
    /// <summary>
    /// <see cref="HAP_FoxBodyHapticsController"/> の Inspector 拡張において、
    /// 身体配置トグルグリッドおよび部位別接触判定しきい値セクションの描画を担当するヘルパークラス。
    /// </summary>
    public static class HAP_FoxBodyInspectorSections
    {
        /// <summary>
        /// 8 部位（頭・左右耳・前後4足・尻尾）の一括操作ボタンと身体配置トグルグリッドを描画します。
        /// </summary>
        public static void DrawBodyPartLayoutSection(
            SerializedProperty headProp,
            SerializedProperty leftEarProp,
            SerializedProperty rightEarProp,
            SerializedProperty frontLeftProp,
            SerializedProperty frontRightProp,
            SerializedProperty backLeftProp,
            SerializedProperty backRightProp,
            SerializedProperty tailProp)
        {
            EditorGUILayout.LabelField("Body Part Toggles (Body Layout)", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Select All (全有効)", EditorStyles.miniButtonLeft))
            {
                SetAll(true, headProp, leftEarProp, rightEarProp, frontLeftProp, frontRightProp, backLeftProp, backRightProp, tailProp);
            }
            if (GUILayout.Button("Deselect All (全無効)", EditorStyles.miniButtonMid))
            {
                SetAll(false, headProp, leftEarProp, rightEarProp, frontLeftProp, frontRightProp, backLeftProp, backRightProp, tailProp);
            }
            if (GUILayout.Button("Feet Only (足のみ)", EditorStyles.miniButtonRight))
            {
                headProp.boolValue = false;
                leftEarProp.boolValue = false;
                rightEarProp.boolValue = false;
                frontLeftProp.boolValue = true;
                frontRightProp.boolValue = true;
                backLeftProp.boolValue = true;
                backRightProp.boolValue = true;
                tailProp.boolValue = false;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(3);

            DrawCenteredToggle(headProp, "Head (頭)");
            DrawPairedToggles(leftEarProp, "Left Ear (左耳)", rightEarProp, "Right Ear (右耳)");
            DrawPairedToggles(frontLeftProp, "Front Left (前左足)", frontRightProp, "Front Right (前右足)");
            DrawPairedToggles(backLeftProp, "Back Left (後左足)", backRightProp, "Back Right (後右足)");
            DrawCenteredToggle(tailProp, "Tail (尻尾)");
        }

        /// <summary>
        /// 部位別の手近接接触しきい値スライダーおよび Copy/Reset 操作ボタンを描画します。
        /// </summary>
        public static void DrawIndividualThresholdsSection(
            SerializedProperty defaultThresholdProp,
            SerializedProperty headProp,
            SerializedProperty leftEarProp,
            SerializedProperty rightEarProp,
            SerializedProperty frontLeftProp,
            SerializedProperty frontRightProp,
            SerializedProperty backLeftProp,
            SerializedProperty backRightProp,
            SerializedProperty tailProp)
        {
            EditorGUILayout.HelpBox(
                "部位ごとの手の接触しきい値（メートル）を設定します。\n未設定 (<= 0) の部位は上記の共通しきい値が自動適用されます。",
                MessageType.None);

            DrawThresholdRow(headProp, "Head (頭)", defaultThresholdProp);
            DrawThresholdRow(leftEarProp, "Left Ear (左耳)", defaultThresholdProp);
            DrawThresholdRow(rightEarProp, "Right Ear (右耳)", defaultThresholdProp);
            DrawThresholdRow(frontLeftProp, "Front Left (前左足)", defaultThresholdProp);
            DrawThresholdRow(frontRightProp, "Front Right (前右足)", defaultThresholdProp);
            DrawThresholdRow(backLeftProp, "Back Left (後左足)", defaultThresholdProp);
            DrawThresholdRow(backRightProp, "Back Right (後右足)", defaultThresholdProp);
            DrawThresholdRow(tailProp, "Tail (尻尾)", defaultThresholdProp);

            EditorGUILayout.Space(2);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Copy Common to All", EditorStyles.miniButtonLeft))
            {
                float common = defaultThresholdProp.floatValue;
                SetAllFloats(common, headProp, leftEarProp, rightEarProp, frontLeftProp, frontRightProp, backLeftProp, backRightProp, tailProp);
            }
            if (GUILayout.Button("Reset All to Default", EditorStyles.miniButtonRight))
            {
                SetAllFloats(-1f, headProp, leftEarProp, rightEarProp, frontLeftProp, frontRightProp, backLeftProp, backRightProp, tailProp);
            }
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawCenteredToggle(SerializedProperty prop, string label)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            prop.boolValue = EditorGUILayout.ToggleLeft(label, prop.boolValue, GUILayout.Width(130));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawPairedToggles(SerializedProperty leftProp, string leftLabel, SerializedProperty rightProp, string rightLabel)
        {
            EditorGUILayout.BeginHorizontal();
            leftProp.boolValue = EditorGUILayout.ToggleLeft(leftLabel, leftProp.boolValue);
            rightProp.boolValue = EditorGUILayout.ToggleLeft(rightLabel, rightProp.boolValue);
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawThresholdRow(SerializedProperty prop, string label, SerializedProperty defaultThresholdProp)
        {
            EditorGUILayout.BeginHorizontal();
            float currentVal = prop.floatValue;
            bool isCustom = currentVal > 0f;

            float displayVal = isCustom ? currentVal : defaultThresholdProp.floatValue;
            EditorGUI.BeginChangeCheck();
            float newVal = EditorGUILayout.Slider(new GUIContent(label), displayVal, 0.005f, 0.30f);
            if (EditorGUI.EndChangeCheck())
            {
                prop.floatValue = newVal;
            }

            if (isCustom)
            {
                if (GUILayout.Button("Reset", EditorStyles.miniButton, GUILayout.Width(50)))
                {
                    prop.floatValue = -1f;
                }
            }
            else
            {
                Color prevColor = GUI.color;
                GUI.color = new Color(0.8f, 0.8f, 0.8f, 0.7f);
                GUILayout.Label("(Default)", EditorStyles.miniLabel, GUILayout.Width(50));
                GUI.color = prevColor;
            }
            EditorGUILayout.EndHorizontal();
        }

        private static void SetAll(bool value, params SerializedProperty[] props)
        {
            foreach (var p in props) p.boolValue = value;
        }

        private static void SetAllFloats(float value, params SerializedProperty[] props)
        {
            foreach (var p in props) p.floatValue = value;
        }
    }
}
#endif

