#if UNITY_EDITOR
#nullable enable
using UnityEditor;
using UnityEngine;

namespace Features.Haptics.Editor
{
    /// <summary>
    /// HAP_AUTDHapticsController の「Acoustics &amp; STM」タブ描画クラス。
    /// ホログラフィアルゴリズム、出力強度、GSPAT 反復回数、指向性グルーピング、
    /// および STM（AutoHCD 用楕円 STM / 接触形状ソース含む）を一元管理します。
    /// </summary>
    public static class HAP_AcousticTabDrawer
    {
        private const string PrefFoldEllipseSources = "HAP_AcousticTab_FoldPrecisionSources";

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

                EditorGUILayout.Space(4);
                DrawAutoHcdPrecisionSection(ctx);
            }
        }

        private static void DrawAutoHcdPrecisionSection(HAP_EditorContext ctx)
        {
            if (ctx.StmGenerationModeProp == null) return;

            EditorGUILayout.PropertyField(ctx.StmGenerationModeProp, new GUIContent("AutoHCD 焦点生成方式"));
            var genMode = (HapticsGenerationMode)ctx.StmGenerationModeProp.enumValueIndex;

            if (genMode == HapticsGenerationMode.Simplified)
            {
                EditorGUILayout.HelpBox("Simplified: 1クラスタあたり重心1点の軽量出力です。", MessageType.None);
                return;
            }

            bool fold = SessionState.GetBool(PrefFoldEllipseSources, true);
            fold = EditorGUILayout.Foldout(fold, "楕円 STM / 接触形状ソース設定 (Precision)", true);
            SessionState.SetBool(PrefFoldEllipseSources, fold);

            if (!fold) return;

            EditorGUI.indentLevel++;
            if (ctx.StmEllipseSourceProp != null)
            {
                EditorGUILayout.PropertyField(ctx.StmEllipseSourceProp, new GUIContent("楕円 STM ソース (Ellipse)"), true);
            }
            if (ctx.StmCentroidSourceProp != null)
            {
                EditorGUILayout.PropertyField(ctx.StmCentroidSourceProp, new GUIContent("重心ソース (Centroid)"), true);
            }
            if (ctx.StmRandomSourceProp != null)
            {
                EditorGUILayout.PropertyField(ctx.StmRandomSourceProp, new GUIContent("ランダムノイズソース (Random)"), true);
            }
            EditorGUI.indentLevel--;
        }
    }
}
#endif
