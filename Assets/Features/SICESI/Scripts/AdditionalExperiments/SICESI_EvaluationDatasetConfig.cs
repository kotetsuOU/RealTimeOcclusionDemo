using System;
using System.Collections.Generic;
using UnityEngine;

namespace SICESI
{
    /// <summary>
    /// SICE SI 追加評価実験用 データセット設定クラス。
    /// 最適化・ルール導出用 (Optimization / Train) の3オブジェクト & GTリストと、
    /// 汎化性能検証用 (Evaluation / Test) の新規仮想オブジェクト & 新規GTリストのデータ保持・クエリを担当します。
    /// （※ シーン同期・排他化処理は <see cref="SICESI_DatasetSyncService"/> に委譲されています）
    /// </summary>
    [Serializable]
    public class SICESI_EvaluationDatasetConfig
    {
        [Header("1. Optimization Set (最適化・ルール導出用: 3オブジェクト & GT)")]
        [Tooltip("最適化・ルール導出に用いた仮想オブジェクト (例: Fox1, happy, elephal の3種)")]
        public List<GameObject> optimizationVirtualObjects = new List<GameObject>();

        [Tooltip("最適化・ルール導出に用いた手メッシュ / 手の配置 (GT) のリスト")]
        public List<GameObject> optimizationHandMeshes = new List<GameObject>();

        [Header("2. Generalization Evaluation Set (新規評価用: 新仮想オブジェクト & 新GT)")]
        [Tooltip("汎化性能評価用の新しい仮想オブジェクトのリスト (新規モデル群)")]
        public List<GameObject> evaluationVirtualObjects = new List<GameObject>();

        [Tooltip("汎化性能評価用の新しい手メッシュ / 新しい手の配置 (GT) のリスト")]
        public List<GameObject> evaluationHandMeshes = new List<GameObject>();

        #region データ一覧取得・クエリ

        /// <summary>
        /// 全ての仮想オブジェクト候補（最適化用 + 新規評価用）を統合したリストを取得します。
        /// </summary>
        public List<GameObject> GetAllVirtualObjects()
        {
            var list = new List<GameObject>();
            if (optimizationVirtualObjects != null)
            {
                foreach (var obj in optimizationVirtualObjects)
                {
                    if (obj != null && !list.Contains(obj)) list.Add(obj);
                }
            }
            if (evaluationVirtualObjects != null)
            {
                foreach (var obj in evaluationVirtualObjects)
                {
                    if (obj != null && !list.Contains(obj)) list.Add(obj);
                }
            }
            return list;
        }

        /// <summary>
        /// 全ての手メッシュ/GT候補を統合したリストを取得します。
        /// </summary>
        public List<GameObject> GetAllHandMeshes()
        {
            var list = new List<GameObject>();
            if (optimizationHandMeshes != null)
            {
                foreach (var obj in optimizationHandMeshes)
                {
                    if (obj != null && !list.Contains(obj)) list.Add(obj);
                }
            }
            if (evaluationHandMeshes != null)
            {
                foreach (var obj in evaluationHandMeshes)
                {
                    if (obj != null && !list.Contains(obj)) list.Add(obj);
                }
            }
            return list;
        }

        /// <summary>
        /// 名前から仮想オブジェクト（最適化用または評価用）を検索します。
        /// </summary>
        public GameObject FindVirtualObjectByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            var all = GetAllVirtualObjects();
            foreach (var vo in all)
            {
                if (vo != null && vo.name == name) return vo;
            }
            return null;
        }

        /// <summary>
        /// 名前から手メッシュ（最適化用または評価用）を検索します。
        /// </summary>
        public GameObject FindHandMeshByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            var all = GetAllHandMeshes();
            foreach (var hand in all)
            {
                if (hand != null && hand.name == name) return hand;
            }
            return null;
        }

        /// <summary>
        /// 指定されたオブジェクトが検証用リストに含まれているかを判定します。
        /// </summary>
        public bool IsEvaluationObject(GameObject obj)
        {
            if (obj == null) return false;
            if (evaluationVirtualObjects != null && evaluationVirtualObjects.Contains(obj)) return true;
            if (evaluationHandMeshes != null && evaluationHandMeshes.Contains(obj)) return true;
            return false;
        }

        /// <summary>
        /// 指定されたオブジェクトが最適化用リストに含まれているかを判定します。
        /// </summary>
        public bool IsOptimizationObject(GameObject obj)
        {
            if (obj == null) return false;
            if (optimizationVirtualObjects != null && optimizationVirtualObjects.Contains(obj)) return true;
            if (optimizationHandMeshes != null && optimizationHandMeshes.Contains(obj)) return true;
            return false;
        }

        #endregion

        #region 後方互換 API (SICESI_DatasetSyncService 委譲)

        public List<GameObject> GetActiveVirtualObjects() => SICESI_DatasetSyncService.GetActiveVirtualObjects(this);
        public List<GameObject> GetActiveHandMeshes() => SICESI_DatasetSyncService.GetActiveHandMeshes(this);
        public GameObject GetActiveVirtualObject(out bool isEvaluation) => SICESI_DatasetSyncService.GetActiveVirtualObject(this, out isEvaluation);
        public GameObject GetActiveHandMesh(out bool isEvaluation) => SICESI_DatasetSyncService.GetActiveHandMesh(this, out isEvaluation);
        public void ActivateSoleVirtualObject(GameObject target) => SICESI_DatasetSyncService.ActivateSoleVirtualObject(this, target);
        public void ActivateSoleHandMesh(GameObject target) => SICESI_DatasetSyncService.ActivateSoleHandMesh(this, target);
        public void SwitchVirtualObject(SICESI_StereoEvaluationController controller, GameObject target) => SICESI_DatasetSyncService.SwitchVirtualObject(controller, target);
        public void SwitchHandMesh(SICESI_StereoEvaluationController controller, GameObject target) => SICESI_DatasetSyncService.SwitchHandMesh(controller, target);
        public void SyncActiveObjectsWithController(SICESI_StereoEvaluationController controller) => SICESI_DatasetSyncService.SyncActiveObjectsWithController(controller);
        public void ForceSyncActiveObjectsFromScene(SICESI_StereoEvaluationController controller) => SICESI_DatasetSyncService.ForceSyncActiveObjectsFromScene(controller);

        #endregion
    }
}
