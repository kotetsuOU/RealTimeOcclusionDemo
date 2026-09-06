using UnityEngine;
using UnityEditor;
using System.IO;

namespace Features.PointingTask.Editor
{
    /// <summary>
    /// Pointing実験用の条件設定（EXP_PointingCondition）を一括生成するエディタツール
    /// </summary>
    public class EXP_PointingConditionGenerator : EditorWindow
    {
        private string savePath = "Assets/Features/PointingTask/Data/Conditions";
        private int repetitions = 3;
        private float successRadius = 0.015f; // 15mm
        
        // ターゲット配置パラメータ
        private float[] xOffsets = new float[] { -0.1f, 0f, 0.1f };
        private float[] yOffsets = new float[] { -0.05f, 0f, 0.05f };
        private float[] zOffsets = new float[] { -0.1f, 0.1f }; // 手前, 奥

        [MenuItem("Tools/Experiment/Pointing Condition Generator")]
        public static void ShowWindow()
        {
            GetWindow<EXP_PointingConditionGenerator>("Pointing Gen");
        }

        void OnGUI()
        {
            GUILayout.Label("Pointing Condition Generator", EditorStyles.boldLabel);
            
            savePath = EditorGUILayout.TextField("Save Path", savePath);
            repetitions = EditorGUILayout.IntField("Repetitions per Condition", repetitions);
            successRadius = EditorGUILayout.FloatField("Success Radius (m)", successRadius);

            EditorGUILayout.Space();
            GUILayout.Label("This will generate a cross product of positions and Occlusion ON/OFF.", EditorStyles.helpBox);

            if (GUILayout.Button("Generate Conditions", GUILayout.Height(30)))
            {
                GenerateConditions();
            }
        }

        private void GenerateConditions()
        {
            if (!Directory.Exists(savePath))
            {
                Directory.CreateDirectory(savePath);
            }

            bool[] occlusionModes = new bool[] { true, false };

            int generatedCount = 0;

            foreach (bool useOcclusion in occlusionModes)
            {
                string modeStr = useOcclusion ? "Proposed" : "Baseline";

                foreach (float x in xOffsets)
                {
                    foreach (float y in yOffsets)
                    {
                        foreach (float z in zOffsets)
                        {
                            var cond = ScriptableObject.CreateInstance<EXP_PointingCondition>();
                            cond.useOcclusion = useOcclusion;
                            cond.targetLocalPosition = new Vector3(x, y, z);
                            
                            // 位置によるラベル付け
                            if (z < 0) cond.placementType = TargetPlacementType.Front;
                            else if (z > 0) cond.placementType = TargetPlacementType.Behind;
                            else cond.placementType = TargetPlacementType.Side; // z==0の場合 (今回は-0.1, 0.1なのでSideは出ないが拡張用)

                            cond.successRadius = successRadius;
                            cond.repetitions = repetitions;
                            
                            string posName = $"X{x*100:F0}Y{y*100:F0}Z{z*100:F0}";
                            cond.conditionName = $"Pointing_{modeStr}_{posName}";

                            string filename = $"{savePath}/{cond.conditionName}.asset";
                            AssetDatabase.CreateAsset(cond, filename);
                            generatedCount++;
                        }
                    }
                }
            }
            
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Successfully generated {generatedCount} conditions at {savePath}");
        }
    }
}
