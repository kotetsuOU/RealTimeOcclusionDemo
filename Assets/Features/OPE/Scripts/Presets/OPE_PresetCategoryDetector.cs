namespace OPE
{
    /// <summary>
    /// 現在のシーン・Controller に設定またはアクティブになっている仮想オブジェクト・手メッシュから、
    /// 実験カテゴリ（「検証用 (Generalization)」または「最適化用 (Optimization)」）を自動判定するクラス。
    /// </summary>
    public static class OPE_PresetCategoryDetector
    {
        /// <summary>
        /// 現在のシーンの対象オブジェクトから、最適化用か検証用かを自動判定します。
        /// </summary>
        public static OPE_PresetPoseManager.PresetCategory DetectCurrentCategory(
            OPE_StereoEvaluationController controller,
            out string reason)
        {
            if (controller == null)
            {
                reason = "コントローラーが未初期化です";
                return OPE_PresetPoseManager.PresetCategory.Generalization;
            }

            var config = controller.datasetConfig;

            // 1. Controller に設定されている現在のオブジェクトを最優先に認識
            var currentVo = controller.virtualObject;
            var currentHand = controller.groundTruthObject;

            if (config != null)
            {
                if (currentVo != null && config.IsEvaluationObject(currentVo))
                {
                    reason = $"検証用オブジェクト [{currentVo.name}] を認識";
                    return OPE_PresetPoseManager.PresetCategory.Generalization;
                }
                if (currentHand != null && config.IsEvaluationObject(currentHand))
                {
                    reason = $"検証用手メッシュ [{currentHand.name}] を認識";
                    return OPE_PresetPoseManager.PresetCategory.Generalization;
                }

                if (currentVo != null && config.IsOptimizationObject(currentVo))
                {
                    reason = $"最適化用オブジェクト [{currentVo.name}] を認識";
                    return OPE_PresetPoseManager.PresetCategory.Optimization;
                }
                if (currentHand != null && config.IsOptimizationObject(currentHand))
                {
                    reason = $"最適化用手メッシュ [{currentHand.name}] を認識";
                    return OPE_PresetPoseManager.PresetCategory.Optimization;
                }

                // 2. Controller に未設定の場合のみリスト内のアクティブオブジェクトから判定
                var activeVo = config.GetActiveVirtualObject(out bool isVoEval);
                if (activeVo != null)
                {
                    reason = isVoEval
                        ? $"検証用リストでアクティブな仮想オブジェクト [{activeVo.name}] を認識"
                        : $"最適化用リストでアクティブな仮想オブジェクト [{activeVo.name}] を認識";
                    return isVoEval ? OPE_PresetPoseManager.PresetCategory.Generalization : OPE_PresetPoseManager.PresetCategory.Optimization;
                }

                var activeHand = config.GetActiveHandMesh(out bool isHandEval);
                if (activeHand != null)
                {
                    reason = isHandEval
                        ? $"検証用リストでアクティブな手メッシュ [{activeHand.name}] を認識"
                        : $"最適化用リストでアクティブな手メッシュ [{activeHand.name}] を認識";
                    return isHandEval ? OPE_PresetPoseManager.PresetCategory.Generalization : OPE_PresetPoseManager.PresetCategory.Optimization;
                }
            }

            reason = "リスト未登録のオブジェクトです（手動選択に従います）";
            return OPE_PresetPoseManager.PresetCategory.Generalization;
        }
    }
}
