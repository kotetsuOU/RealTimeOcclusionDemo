#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Features.HapticsCollision.Editor
{
    /// <summary>
    /// HCD_PipelineEditor の「Clustering（空間クラスタリング）」タブの専用描画クラス。
    /// グリッド解像度、最大クラスタ数、重心集約モード、および精度オプションを描画します。
    /// </summary>
    public static class HCD_ClusteringTabDrawer
    {
        public static void Draw(HCD_EditorContext ctx)
        {
            if (ctx.ScpProp == null) return;

            EditorGUILayout.LabelField("🧩 空間ハッシュクラスタリング設定", EditorStyles.boldLabel);

            // ボクセル・グリッド設定
            EditorGUILayout.LabelField("グリッド & クラスタ解像度", EditorStyles.miniBoldLabel);
            if (ctx.MaxClustersProp != null) EditorGUILayout.PropertyField(ctx.MaxClustersProp);
            if (ctx.CellSizeProp != null) EditorGUILayout.PropertyField(ctx.CellSizeProp);

            EditorGUILayout.Space(8);

            // 接触面・重心推定アルゴリズム
            EditorGUILayout.LabelField("接触面・重心の集約アルゴリズム", EditorStyles.miniBoldLabel);
            if (ctx.AggModeProp != null) EditorGUILayout.PropertyField(ctx.AggModeProp);
            if (ctx.PosSourceProp != null) EditorGUILayout.PropertyField(ctx.PosSourceProp);

            if (ctx.AggModeProp != null && ctx.AggModeProp.enumValueIndex == (int)ClusterAggregationMode.DistanceWeightedCentroid)
            {
                if (ctx.DistPowerProp != null)
                {
                    EditorGUILayout.PropertyField(ctx.DistPowerProp);
                    EditorGUILayout.HelpBox("DistanceWeightedCentroid: 0mm（メッシュ表面）に近い点を強調し、指腹などの真の接触面を推定します。", MessageType.None);
                }
            }

            EditorGUILayout.Space(8);

            // 第2パス・精密計算
            EditorGUILayout.LabelField("高度な触覚フィードバック（第2パス）", EditorStyles.miniBoldLabel);
            if (ctx.PrecisionModeProp != null)
            {
                EditorGUILayout.PropertyField(ctx.PrecisionModeProp);
                if (ctx.PrecisionModeProp.boolValue)
                {
                    EditorGUILayout.HelpBox("Precision Mode 有効: GPU で接触面の広がり（共分散・主軸方向）を計算します。なぞる感覚やテクスチャ感の提示に使用されます。", MessageType.Info);
                }
            }
        }
    }
}
#endif
