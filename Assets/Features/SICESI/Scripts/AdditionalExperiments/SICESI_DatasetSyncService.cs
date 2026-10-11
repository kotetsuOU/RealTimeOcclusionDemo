using System.Collections.Generic;
using UnityEngine;

namespace SICESI
{
    /// <summary>
    /// SICE SI データセット設定における、シーン内オブジェクトのアクティブ状態の検出、
    /// 排他的アクティブ化、Controller との同期、およびダミー点群追従を専門に担うサービス・プロセッサークラス。
    /// </summary>
    public static class SICESI_DatasetSyncService
    {
        /// <summary>
        /// リストの中で現在 activeInHierarchy (アクティブ) な全仮想オブジェクトを取得します。
        /// </summary>
        public static List<GameObject> GetActiveVirtualObjects(SICESI_EvaluationDatasetConfig config)
        {
            var list = new List<GameObject>();
            if (config == null) return list;

            var all = config.GetAllVirtualObjects();
            foreach (var vo in all)
            {
                if (vo != null && vo.activeInHierarchy && !list.Contains(vo))
                {
                    list.Add(vo);
                }
            }
            return list;
        }

        /// <summary>
        /// リストの中で現在 activeInHierarchy (アクティブ) な全手メッシュを取得します。
        /// </summary>
        public static List<GameObject> GetActiveHandMeshes(SICESI_EvaluationDatasetConfig config)
        {
            var list = new List<GameObject>();
            if (config == null) return list;

            var all = config.GetAllHandMeshes();
            foreach (var hand in all)
            {
                if (hand != null && hand.activeInHierarchy && !list.Contains(hand))
                {
                    list.Add(hand);
                }
            }
            return list;
        }

        /// <summary>
        /// リストの中で現在アクティブな仮想オブジェクトを取得します（検証用優先）。
        /// </summary>
        public static GameObject GetActiveVirtualObject(SICESI_EvaluationDatasetConfig config, out bool isEvaluation)
        {
            isEvaluation = false;
            if (config == null) return null;

            if (config.evaluationVirtualObjects != null)
            {
                foreach (var obj in config.evaluationVirtualObjects)
                {
                    if (obj != null && obj.activeInHierarchy)
                    {
                        isEvaluation = true;
                        return obj;
                    }
                }
            }

            if (config.optimizationVirtualObjects != null)
            {
                foreach (var obj in config.optimizationVirtualObjects)
                {
                    if (obj != null && obj.activeInHierarchy)
                    {
                        isEvaluation = false;
                        return obj;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// リストの中で現在アクティブな手メッシュを取得します（検証用優先）。
        /// </summary>
        public static GameObject GetActiveHandMesh(SICESI_EvaluationDatasetConfig config, out bool isEvaluation)
        {
            isEvaluation = false;
            if (config == null) return null;

            if (config.evaluationHandMeshes != null)
            {
                foreach (var obj in config.evaluationHandMeshes)
                {
                    if (obj != null && obj.activeInHierarchy)
                    {
                        isEvaluation = true;
                        return obj;
                    }
                }
            }

            if (config.optimizationHandMeshes != null)
            {
                foreach (var obj in config.optimizationHandMeshes)
                {
                    if (obj != null && obj.activeInHierarchy)
                    {
                        isEvaluation = false;
                        return obj;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 指定した仮想オブジェクトのみをアクティブにし、他の管理対象仮想オブジェクトを非アクティブにします。
        /// </summary>
        public static void ActivateSoleVirtualObject(SICESI_EvaluationDatasetConfig config, GameObject target)
        {
            if (config == null) return;
            var all = config.GetAllVirtualObjects();
            foreach (var vo in all)
            {
                if (vo != null)
                {
                    vo.SetActive(vo == target);
                }
            }
        }

        /// <summary>
        /// 指定した手メッシュのみをアクティブにし、他の管理対象手メッシュを非アクティブにします。
        /// </summary>
        public static void ActivateSoleHandMesh(SICESI_EvaluationDatasetConfig config, GameObject target)
        {
            if (config == null) return;
            var all = config.GetAllHandMeshes();
            foreach (var hand in all)
            {
                if (hand != null)
                {
                    hand.SetActive(hand == target);
                }
            }
        }

        /// <summary>
        /// 仮想オブジェクトを排他的にアクティブ化し、Controller に設定します。
        /// </summary>
        public static void SwitchVirtualObject(SICESI_StereoEvaluationController controller, GameObject target)
        {
            if (target == null) return;
            if (controller != null && controller.datasetConfig != null)
            {
                ActivateSoleVirtualObject(controller.datasetConfig, target);
                controller.virtualObject = target;
            }
        }

        /// <summary>
        /// 手メッシュを排他的にアクティブ化し、Controller に設定して点群サンプリングを即時同期します。
        /// </summary>
        public static void SwitchHandMesh(SICESI_StereoEvaluationController controller, GameObject target)
        {
            if (target == null) return;
            if (controller != null)
            {
                if (controller.datasetConfig != null)
                {
                    ActivateSoleHandMesh(controller.datasetConfig, target);
                }
                controller.groundTruthObject = target;
                controller.SyncDummyPointCloudWithGroundTruth(true);
            }
        }

        /// <summary>
        /// Controller の virtualObject や groundTruthObject を現在のシーン状態と同期します。
        /// - 既存の参照が有効かつアクティブであれば維持します。
        /// - 既存の参照が null または非アクティブになっている場合は、アクティブな管理対象オブジェクトを自動検出し補完します。
        /// </summary>
        public static void SyncActiveObjectsWithController(SICESI_StereoEvaluationController controller)
        {
            if (controller == null) return;
            var config = controller.datasetConfig;
            if (config == null) return;

            // 1. 仮想オブジェクト
            bool needUpdateVo = controller.virtualObject == null || !controller.virtualObject.activeInHierarchy;
            if (needUpdateVo)
            {
                var activeVos = GetActiveVirtualObjects(config);
                if (activeVos.Count == 1)
                {
                    controller.virtualObject = activeVos[0];
                }
                else if (activeVos.Count > 1)
                {
                    var activeVo = GetActiveVirtualObject(config, out _);
                    if (activeVo != null) controller.virtualObject = activeVo;
                }
            }

            // 2. 手メッシュ
            bool needUpdateHand = controller.groundTruthObject == null || !controller.groundTruthObject.activeInHierarchy;
            if (needUpdateHand)
            {
                var activeHands = GetActiveHandMeshes(config);
                if (activeHands.Count == 1)
                {
                    controller.groundTruthObject = activeHands[0];
                    controller.SyncDummyPointCloudWithGroundTruth(true);
                }
                else if (activeHands.Count > 1)
                {
                    var activeHand = GetActiveHandMesh(config, out _);
                    if (activeHand != null)
                    {
                        controller.groundTruthObject = activeHand;
                        controller.SyncDummyPointCloudWithGroundTruth(true);
                    }
                }
            }
            else if (controller.groundTruthObject != null)
            {
                controller.SyncDummyPointCloudWithGroundTruth(false);
            }
        }

        /// <summary>
        /// シーン内で現在アクティブなオブジェクトを検出し、Controller の設定を強制的に上書き更新します。
        /// </summary>
        public static void ForceSyncActiveObjectsFromScene(SICESI_StereoEvaluationController controller)
        {
            if (controller == null) return;
            var config = controller.datasetConfig;
            if (config == null) return;

            var activeVos = GetActiveVirtualObjects(config);
            if (activeVos.Count == 1)
            {
                controller.virtualObject = activeVos[0];
            }
            else if (activeVos.Count > 1)
            {
                var activeVo = GetActiveVirtualObject(config, out _);
                if (activeVo != null) controller.virtualObject = activeVo;
            }

            var activeHands = GetActiveHandMeshes(config);
            if (activeHands.Count == 1)
            {
                controller.groundTruthObject = activeHands[0];
            }
            else if (activeHands.Count > 1)
            {
                var activeHand = GetActiveHandMesh(config, out _);
                if (activeHand != null) controller.groundTruthObject = activeHand;
            }

            if (controller.groundTruthObject != null)
            {
                controller.SyncDummyPointCloudWithGroundTruth(true);
            }
        }
    }
}
