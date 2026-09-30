using System.Collections.Generic;
using UnityEngine;
using Core.Logging;

namespace Features.PhysicalResponse.Debug
{
    /// <summary>
    /// PR（PhysicalResponse）モジュール全体の AppLogManager 連動ログトリガー定義および登録を担うヘルパーコンポーネント。
    /// PR_LiftController・PR_BoneDetector・PR_VirtualObjectManager 等のコア本体から AppLogger 登録処理を分離します。
    /// </summary>
    [AppLoggable("PR (PhysicalResponse)")]
    [DisallowMultipleComponent]
    public class PR_LogTriggers : MonoBehaviour, IAppLoggable
    {
        // --- サブタグ定数 ---
        public const string TagLiftController = "PR_LiftController";
        public const string TagBoneDetector   = "PR_BoneDetector";
        public const string TagVirtualObject  = "PR_VirtualObjectManager";
        public const string TagAnimController = "PR_AnimationController";

        public void RegisterLogTriggers(LogCategoryGroup group, HashSet<string> existingLabels)
        {
            // PR_LiftController
            var liftCtrl = GetComponent<PR_LiftController>()
                ?? FindFirstObjectByType<PR_LiftController>();
            Object liftTarget = liftCtrl != null ? (Object)liftCtrl : this;
            AddSubTrigger(group, liftTarget, "[PR_LiftController] Lift & Contact State", TagLiftController, existingLabels);

            // PR_BoneDetector
            var bd = GetComponent<PR_BoneDetector>()
                ?? PR_BoneDetector.Instance
                ?? FindFirstObjectByType<PR_BoneDetector>();
            Object bdTarget = bd != null ? (Object)bd : this;
            AddSubTrigger(group, bdTarget, "[PR_BoneDetector] Bone Sync State", TagBoneDetector, existingLabels);

            // PR_VirtualObjectManager
            var vom = PR_VirtualObjectManager.Instance
                ?? FindFirstObjectByType<PR_VirtualObjectManager>();
            Object vomTarget = vom != null ? (Object)vom : this;
            AddSubTrigger(group, vomTarget, "[PR_VirtualObjectManager] Active Object", TagVirtualObject, existingLabels);

            // PR_AnimationController
            var animCtrl = FindFirstObjectByType<PR_AnimationController>();
            Object animTarget = animCtrl != null ? (Object)animCtrl : this;
            AddSubTrigger(group, animTarget, "[PR_AnimationController] Animation State", TagAnimController, existingLabels);
        }

        private void AddSubTrigger(LogCategoryGroup group, Object target, string label, string tag, HashSet<string> existing)
        {
            if (existing.Contains(label)) return;
            group.entries.Add(new LogInstanceEntry
            {
                label   = label,
                tag     = tag,
                target  = target,
                enabled = true
            });
            existing.Add(label);
        }
    }
}
