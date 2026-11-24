#nullable enable
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Features.Haptics.Processors
{
    /// <summary>
    /// HapticsSourceMode に応じて触覚刺激のターゲットデータ（HCD接触クラスタ / CustomControllerターゲット）を抽出・収集し、
    /// オブジェクトコントローラーの排他切り替えを管理する専任ディスパッチャー。
    /// </summary>
    public class HAP_TargetSourceDispatcher
    {
        /// <summary>
        /// 現在のモードおよび設定に基づいて触覚ターゲットを収集します。
        /// </summary>
        /// <returns>有効な照射ターゲットが存在する場合は true</returns>
        public bool CollectTargets(
            HapticsSourceMode sourceMode,
            bool bypassHaptics,
            HCD_Pipeline? hcdPipeline,
            List<HAP_BaseObjectHapticsController> objectControllers,
            int activeControllerIndex,
            float focusIntensityPascal,
            Vector3 offset,
            out List<TrackedCluster> activeClusters,
            out List<HAP_FociGenerator.ClusterFociData> objectFociList)
        {
            activeClusters = new List<TrackedCluster>();
            objectFociList = new List<HAP_FociGenerator.ClusterFociData>();

            // バイパス時または Manual モード時はターゲットなしとして扱う
            if (bypassHaptics || sourceMode == HapticsSourceMode.Manual)
            {
                return false;
            }

            if (sourceMode == HapticsSourceMode.ObjectTarget)
            {
                objectControllers.RemoveAll(c => c == null);
                if (objectControllers.Count > 0)
                {
                    int validIdx = Mathf.Clamp(activeControllerIndex, 0, objectControllers.Count - 1);
                    var activeCtrl = objectControllers[validIdx];
                    if (activeCtrl != null && activeCtrl.enabled && !activeCtrl.experimentStimulusSuppressed && activeCtrl.HasActiveTargets())
                    {
                        var foci = activeCtrl.GetHapticsTargets(focusIntensityPascal, offset);
                        objectFociList.AddRange(foci);
                    }
                }
                return objectFociList.Count > 0;
            }
            else if (sourceMode == HapticsSourceMode.AutoHCD)
            {
                if (hcdPipeline != null)
                {
                    var trackedClusters = hcdPipeline.GetTrackedClusters();
                    activeClusters = trackedClusters.Where(c => c.IsAlive && c.Force > 0.01f).ToList();
                    return activeClusters.Count > 0;
                }
            }

            return false;
        }

        /// <summary>
        /// 指定されたインデックスのコントローラーのみを有効化し、他を非アクティブへ同期切り替えします。
        /// </summary>
        public void SynchronizeActiveController(List<HAP_BaseObjectHapticsController> controllers, ref int activeIndex, GameObject owner)
        {
            controllers.RemoveAll(c => c == null);
            if (controllers.Count == 0)
            {
                activeIndex = 0;
                return;
            }

            activeIndex = Mathf.Clamp(activeIndex, 0, controllers.Count - 1);

            for (int i = 0; i < controllers.Count; i++)
            {
                var ctrl = controllers[i];
                if (ctrl != null)
                {
                    bool isActive = (i == activeIndex);
                    ctrl.enabled = isActive;

                    // 自身とは別の GameObject にアタッチされている場合、GameObject 自体の SetActive も同期
                    if (ctrl.gameObject != owner)
                    {
                        ctrl.gameObject.SetActive(isActive);
                    }
                }
            }
        }
    }
}
