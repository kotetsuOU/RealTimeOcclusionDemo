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
                    string backendName = hw.Backend?.BackendName ?? "None";
                    string effectiveLink = GetEffectiveLinkDisplay(hw);
                    string connText = hw.IsConnected
                        ? $"● 接続中 ({effectiveLink}) - デバイス数: {hw.ConnectedDevices.Count}"
                        : $"○ 未接続 (Disconnected / 設定: {effectiveLink})";
                    EditorGUILayout.LabelField("ハードウェア状態:", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(connText, EditorStyles.miniLabel);
                    EditorGUILayout.LabelField($"Backend: {backendName}", EditorStyles.miniLabel);
                }
            }

            EditorGUILayout.Space(8);

            // ─── 1. 通信リンク設定 ───
            EditorGUILayout.LabelField("物理通信リンク (Link Settings)", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawLinkSettings(ctx);
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

        private static string GetEffectiveLinkDisplay(HAP_AUTDHardwareController hw)
        {
#if USE_AUTD3_LEGACY
            return hw.linkType switch
            {
                AUTDLinkType.LAN => "TwinCAT (LAN fallback)",
                AUTDLinkType.SOEM => string.IsNullOrWhiteSpace(hw.soemAdapterName) ? "SOEM" : $"SOEM ({hw.soemAdapterName})",
                _ => hw.linkType.ToString()
            };
#else
            return hw.linkType switch
            {
                AUTDLinkType.Simulator => string.IsNullOrWhiteSpace(hw.simulatorAddress) ? "Simulator ([::1]:44336)" : $"Simulator ({hw.simulatorAddress})",
                _ => string.IsNullOrWhiteSpace(hw.lanInterfaceName) ? "LAN (Auto)" : $"LAN ({hw.lanInterfaceName})"
            };
#endif
        }

        private static void DrawLinkSettings(HAP_EditorContext ctx)
        {
            if (ctx.HwLinkTypeProp == null) return;

            var currentType = (AUTDLinkType)ctx.HwLinkTypeProp.intValue;

#if USE_AUTD3_LEGACY
            var legacyOptions = new[]
            {
                AUTDLinkType.TwinCAT,
                AUTDLinkType.SOEM,
                AUTDLinkType.Simulator
            };
            var legacyLabels = new[]
            {
                "TwinCAT",
                "SOEM",
                "Simulator"
            };

            int currentIdx = System.Array.IndexOf(legacyOptions, currentType);
            if (currentIdx < 0)
            {
                currentIdx = 0; // LAN 等の新SDK専用値は TwinCAT として表示
                EditorGUILayout.HelpBox("新SDK向け設定 (LAN) が保存されています。旧SDKでは TwinCAT として接続されます。", MessageType.Info);
            }

            EditorGUI.BeginChangeCheck();
            int newIdx = EditorGUILayout.Popup("接続リンク種別", currentIdx, legacyLabels);
            if (EditorGUI.EndChangeCheck())
            {
                ctx.HwLinkTypeProp.intValue = (int)legacyOptions[newIdx];
            }

            if (legacyOptions[newIdx] == AUTDLinkType.SOEM && ctx.HwSoemAdapterProp != null)
            {
                EditorGUILayout.PropertyField(ctx.HwSoemAdapterProp, new GUIContent("SOEM アダプタ名"));
            }
#else
            var currentOptions = new[]
            {
                AUTDLinkType.LAN,
                AUTDLinkType.Simulator
            };
            var currentLabels = new[]
            {
                "LAN (UDP/IPv6)",
                "Simulator"
            };

            int currentIdx = System.Array.IndexOf(currentOptions, currentType);
            bool isLegacyValue = currentIdx < 0;
            if (isLegacyValue)
            {
                currentIdx = 0; // TwinCAT / SOEM は LAN として扱う
            }

            EditorGUI.BeginChangeCheck();
            int newIdx = EditorGUILayout.Popup("接続リンク種別", currentIdx, currentLabels);
            if (EditorGUI.EndChangeCheck())
            {
                ctx.HwLinkTypeProp.intValue = (int)currentOptions[newIdx];
            }

            if (isLegacyValue)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.HelpBox($"旧版設定 ({currentType}) が保存されています。新SDKでは LAN (UDP) として接続されます。", MessageType.Info);
                    if (GUILayout.Button("LAN に更新", GUILayout.Width(84), GUILayout.Height(36)))
                    {
                        ctx.HwLinkTypeProp.intValue = (int)AUTDLinkType.LAN;
                    }
                }
            }

            if (currentOptions[newIdx] == AUTDLinkType.Simulator)
            {
                if (ctx.HwSimulatorAddressProp != null)
                {
                    EditorGUILayout.PropertyField(ctx.HwSimulatorAddressProp, new GUIContent("Simulator アドレス"));
                }
            }
            else
            {
                if (ctx.HwLanInterfaceProp != null)
                {
                    EditorGUILayout.PropertyField(ctx.HwLanInterfaceProp, new GUIContent("ネットワークインタフェース名 (空欄で自動)"));
                }
            }
#endif
        }
    }
}
#endif
