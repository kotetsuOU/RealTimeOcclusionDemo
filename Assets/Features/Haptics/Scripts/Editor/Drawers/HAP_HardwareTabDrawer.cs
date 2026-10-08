#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Features.Haptics.Editor
{
    /// <summary>
    /// HAP_AUTDHapticsController の「Hardware」タブ描画クラス。
    /// AUTD3物理通信接続、変調（Modulation）、サイレンサー（Silencer）、温度・ファン等のハードウェア設定を一元管理します。
    /// </summary>
    public static class HAP_HardwareTabDrawer
    {
        public static void Draw(HAP_EditorContext ctx)
        {
            EditorGUILayout.LabelField("📡 AUTD3 ハードウェア & 通信設定", EditorStyles.boldLabel);

            if (ctx.Controller.hardwareController == null)
            {
                EditorGUILayout.HelpBox("HardwareController が設定されていません。以下のボタンからシーン内のコントローラーを取得または自動アタッチしてください。", MessageType.Warning);
                if (GUILayout.Button("HardwareController を自動取得 / アタッチ", GUILayout.Height(28)))
                {
                    AutoAssignHardwareController(ctx);
                }
                return;
            }

            // 参照フィールド（変更可能）
            EditorGUILayout.PropertyField(ctx.HardwareControllerProp, new GUIContent("Hardware Controller"));

            if (ctx.HardwareSO == null)
            {
                ctx.RefreshLinkedObjects();
                if (ctx.HardwareSO == null) return;
            }

            var hw = ctx.Controller.hardwareController;

            // 実行時接続ステータス
            if (Application.isPlaying)
            {
                EditorGUILayout.Space(4);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    string connText = hw.IsConnected ? $"● 接続中 ({hw.linkType}) - デバイス数: {hw.ConnectedDevices.Count}" : "○ 未接続 (Disconnected)";
                    EditorGUILayout.LabelField("ハードウェア状態:", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(connText, EditorStyles.miniLabel);
                }
            }

            EditorGUILayout.Space(8);

            // ─── 1. 通信リンク設定 ───
            EditorGUILayout.LabelField("物理通信リンク (Link Settings)", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (ctx.HwLinkTypeProp != null) EditorGUILayout.PropertyField(ctx.HwLinkTypeProp, new GUIContent("接続リンク種別"));

                if (ctx.HwLinkTypeProp != null && ctx.HwLinkTypeProp.enumValueIndex == (int)AUTDLinkType.SOEM)
                {
                    if (ctx.HwSoemAdapterProp != null) EditorGUILayout.PropertyField(ctx.HwSoemAdapterProp, new GUIContent("SOEM アダプタ名"));
                }
            }

            EditorGUILayout.Space(8);

            // ─── 2. 環境設定 (温度 & ファン) ───
            EditorGUILayout.LabelField("環境設定 (Environment)", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (ctx.HwTemperatureProp != null) EditorGUILayout.PropertyField(ctx.HwTemperatureProp, new GUIContent("環境温度 (°C)"));
                if (ctx.HwEnableFanProp != null) EditorGUILayout.PropertyField(ctx.HwEnableFanProp, new GUIContent("冷却ファン有効化"));
            }

            EditorGUILayout.Space(8);

            // ─── 3. 変調設定 (Modulation) ───
            EditorGUILayout.LabelField("変調設定 (Modulation)", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (ctx.HwModulationModeProp != null) EditorGUILayout.PropertyField(ctx.HwModulationModeProp, new GUIContent("変調モード"));

                if (ctx.HwModulationModeProp != null)
                {
                    var mode = (ModulationMode)ctx.HwModulationModeProp.enumValueIndex;
                    if (mode == ModulationMode.Sine)
                    {
                        if (ctx.HwSineFrequencyProp != null) EditorGUILayout.PropertyField(ctx.HwSineFrequencyProp, new GUIContent("サイン波周波数 (Hz)"));
                    }
                    else if (mode == ModulationMode.Static)
                    {
                        if (ctx.HwStaticAmplitudeProp != null) EditorGUILayout.PropertyField(ctx.HwStaticAmplitudeProp, new GUIContent("静止波振幅 (0.0-1.0)"));
                    }
                }
            }

            EditorGUILayout.Space(8);

            // ─── 4. サイレンサー設定 (Silencer) ───
            EditorGUILayout.LabelField("サイレンサー設定 (Silencer - 可聴ノイズ低減)", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (ctx.HwSilencerModeProp != null) EditorGUILayout.PropertyField(ctx.HwSilencerModeProp, new GUIContent("サイレンサーモード"));
                if (ctx.HwSilencerStepPhaseProp != null) EditorGUILayout.PropertyField(ctx.HwSilencerStepPhaseProp, new GUIContent("位相ステップ (Step Phase)"));
                if (ctx.HwSilencerStepAmpProp != null) EditorGUILayout.PropertyField(ctx.HwSilencerStepAmpProp, new GUIContent("振幅ステップ (Step Amplitude)"));
            }
        }

        private static void AutoAssignHardwareController(HAP_EditorContext ctx)
        {
            var hw = Object.FindAnyObjectByType<HAP_AUTDHardwareController>();
            if (hw == null)
            {
                hw = ctx.Controller.gameObject.AddComponent<HAP_AUTDHardwareController>();
            }

            ctx.HardwareControllerProp.objectReferenceValue = hw;
            ctx.SerializedObject.ApplyModifiedProperties();
            ctx.RefreshLinkedObjects();
        }
    }
}
#endif
