#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Features.Haptics.Editor
{
    /// <summary>
    /// HAP_AUTDHapticsController の「Acoustics & STM」タブ描画クラス。
    /// ホログラフィアルゴリズム、出力強度、GSPAT反復回数、指向性グルーピング、およびSTM（時空間変調）を一元管理します。
    /// </summary>
    public static class HAP_AcousticTabDrawer
    {
        public static void Draw(HAP_EditorContext ctx)
        {
            EditorGUILayout.LabelField("🔊 音響演算 & STM (時空間変調) 設定", EditorStyles.boldLabel);

            // ─── 1. 音響ホログラフィ ───
            EditorGUILayout.LabelField("ホログラフィアルゴリズム & 出力強度", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (ctx.HoloAlgorithmProp != null) EditorGUILayout.PropertyField(ctx.HoloAlgorithmProp, new GUIContent("アルゴリズム"));
                if (ctx.FocusIntensityPascalProp != null) EditorGUILayout.PropertyField(ctx.FocusIntensityPascalProp, new GUIContent("焦点強度 (Pa)"));

                if (ctx.GspatRepeatCountProp != null)
                {
                    EditorGUILayout.PropertyField(ctx.GspatRepeatCountProp, new GUIContent("GSPAT 反復回数"));
                    EditorGUILayout.HelpBox("デフォルト20回。値を小さくするとCPU計算負荷が劇的に軽減されます（例: 100回→20回で約1/5）。", MessageType.None);
                }
            }

            EditorGUILayout.Space(8);

            // ─── 2. 指向性デバイスグルーピング ───
            EditorGUILayout.LabelField("指向性デバイスグルーピング (Directional Grouping)", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (ctx.EnableDirectionalGroupingProp != null)
                {
                    EditorGUILayout.PropertyField(ctx.EnableDirectionalGroupingProp, new GUIContent("有効化"));
                    if (ctx.EnableDirectionalGroupingProp.boolValue && ctx.DirectionalAngleThresholdProp != null)
                    {
                        EditorGUI.indentLevel++;
                        EditorGUILayout.PropertyField(ctx.DirectionalAngleThresholdProp, new GUIContent("許容角度閾値 (度)"));
                        EditorGUILayout.HelpBox("接触面の法線ベクトルとデバイスの向きを比較し、対向するデバイスからのみ照射します。", MessageType.None);
                        EditorGUI.indentLevel--;
                    }
                }
            }

            EditorGUILayout.Space(8);

            // ─── 3. STM (Spatio-Temporal Modulation) 設定 ───
            EditorGUILayout.LabelField("STM (時空間変調) 設定", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (ctx.StmModeProp != null) EditorGUILayout.PropertyField(ctx.StmModeProp, new GUIContent("STM モード"));
                if (ctx.StmFrequencyProp != null) EditorGUILayout.PropertyField(ctx.StmFrequencyProp, new GUIContent("STM 再生周波数 (Hz)"));

                var stmMode = (HapticsSTMMode)ctx.StmModeProp.enumValueIndex;
                if (stmMode == HapticsSTMMode.FociSTM)
                {
                    EditorGUILayout.HelpBox("FociSTM: ハードウェア（FPGA）で単焦点の軌跡を高速再生します（軽量）。", MessageType.None);
                }
                else if (stmMode == HapticsSTMMode.GainSTM)
                {
                    if (ctx.GainStmModeProp != null) EditorGUILayout.PropertyField(ctx.GainStmModeProp, new GUIContent("Gain STM モード"));
                    EditorGUILayout.HelpBox("GainSTM: CPU上でGSPATホログラム波形を全フレーム事前生成し、マルチフォーカスの軌跡を再生します。", MessageType.None);
                }
            }
        }
    }
}
#endif
