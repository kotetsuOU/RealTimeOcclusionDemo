using System;
using UnityEngine;

namespace Features.PhysicalResponse.Bone
{
    /// <summary>
    /// ターゲットモデルのアクティブ検索およびボーン名マッチングによる再帰検索を担う純粋C#クラス。
    /// PR_BoneDetector から検出アルゴリズムを分離し、テスト・差し替えを容易にします。
    /// </summary>
    public static class PR_BoneSearcher
    {
        // ---- ボーン検出 ------------------------------------------------

        /// <summary>
        /// searchRoot 配下から全ボーンを一括検出し、out パラメータに格納します。
        /// </summary>
        public static void DetectAll(
            Transform searchRoot,
            out Transform frontLeft, out Transform frontRight,
            out Transform backLeft,  out Transform backRight,
            out Transform head,      out Transform lEar,
            out Transform rEar,      out Transform tail)
        {
            // 足ボーン（Digit11 優先 → Ankle フォールバック）
            frontLeft  = FindFrontLeft(searchRoot);
            frontRight = FindFrontRight(searchRoot);
            backLeft   = FindBackLeft(searchRoot);
            backRight  = FindBackRight(searchRoot);

            // 体ボーン
            head  = FindChild(searchRoot, n => n.Equals("Fox_Head", StringComparison.OrdinalIgnoreCase)
                        || n.Equals("Head", StringComparison.OrdinalIgnoreCase)
                        || (n.ToLower().Contains("head") && !n.ToLower().Contains("overhead")));

            lEar  = FindChild(searchRoot, n => n.Equals("Fox_LEar1", StringComparison.OrdinalIgnoreCase)
                        || n.Equals("Fox_LEar2", StringComparison.OrdinalIgnoreCase)
                        || n.Contains("LEar1") || n.Contains("Ear1_L")
                        || (n.ToLower().Contains("ear") && (n.ToLower().Contains("left") || n.ToLower().EndsWith("_l") || n.ToLower().Contains("_l_"))));

            rEar  = FindChild(searchRoot, n => n.Equals("Fox_REar1", StringComparison.OrdinalIgnoreCase)
                        || n.Equals("Fox_REar2", StringComparison.OrdinalIgnoreCase)
                        || n.Contains("REar1") || n.Contains("Ear1_R")
                        || (n.ToLower().Contains("ear") && (n.ToLower().Contains("right") || n.ToLower().EndsWith("_r") || n.ToLower().Contains("_r_"))));

            tail  = FindChild(searchRoot, n => n.Contains("Tail6") || n.Contains("Fox_Tail6"))
                 ?? FindChild(searchRoot, n => n.Contains("Tail5") || n.Contains("Fox_Tail5"))
                 ?? FindChild(searchRoot, n => n.ToLower().Contains("tail"));
        }

        // ---- 個別ボーン検索 -------------------------------------------

        public static Transform FindFrontLeft(Transform root)
            => FindChild(root, n => n.Contains("F_LLegDigit11") || n.Contains("Fox_F_LLegDigit11")
                    || (n.ToLower().Contains("front") && n.ToLower().Contains("left")
                        && (n.ToLower().Contains("foot") || n.ToLower().Contains("digit"))))
            ?? FindChild(root, n => n.Contains("F_LLegAnkle")
                    || (n.ToLower().Contains("front") && n.ToLower().Contains("left") && n.ToLower().Contains("ankle")));

        public static Transform FindFrontRight(Transform root)
            => FindChild(root, n => n.Contains("F_RLegDigit11") || n.Contains("Fox_F_RLegDigit11")
                    || (n.ToLower().Contains("front") && n.ToLower().Contains("right")
                        && (n.ToLower().Contains("foot") || n.ToLower().Contains("digit"))))
            ?? FindChild(root, n => n.Contains("F_RLegAnkle")
                    || (n.ToLower().Contains("front") && n.ToLower().Contains("right") && n.ToLower().Contains("ankle")));

        public static Transform FindBackLeft(Transform root)
            => FindChild(root, n => (n.Contains("LLegDigit11") && !n.Contains("F_"))
                    || (n.ToLower().Contains("left") && (n.ToLower().Contains("foot") || n.ToLower().Contains("digit")) && !n.ToLower().Contains("front")))
            ?? FindChild(root, n => (n.Contains("LLegAnkle") && !n.Contains("F_"))
                    || (n.ToLower().Contains("left") && n.ToLower().Contains("ankle") && !n.ToLower().Contains("front")));

        public static Transform FindBackRight(Transform root)
            => FindChild(root, n => (n.Contains("RLegDigit11") && !n.Contains("F_"))
                    || (n.ToLower().Contains("right") && (n.ToLower().Contains("foot") || n.ToLower().Contains("digit")) && !n.ToLower().Contains("front")))
            ?? FindChild(root, n => (n.Contains("RLegAnkle") && !n.Contains("F_"))
                    || (n.ToLower().Contains("right") && n.ToLower().Contains("ankle") && !n.ToLower().Contains("front")));

        // ---- ボーン有効性検証 -----------------------------------------

        /// <summary>
        /// 4本の足ボーンが全て target の子階層に存在するか検証します。
        /// </summary>
        public static bool IsBonesValid(Transform target,
            Transform fl, Transform fr, Transform bl, Transform br)
        {
            if (target == null) return false;
            if (fl == null || !fl.IsChildOf(target)) return false;
            if (fr == null || !fr.IsChildOf(target)) return false;
            if (bl == null || !bl.IsChildOf(target)) return false;
            if (br == null || !br.IsChildOf(target)) return false;
            return true;
        }

        // ---- アクティブターゲット検索 ----------------------------------

        /// <summary>
        /// シーン内からアクティブなモデルの Transform を検索します。
        /// 優先順位: PR_VirtualObjectManager → PR_AnimationController.toggleObjects → SkinnedMeshRenderer
        /// </summary>
        public static Transform FindActiveTarget(PR_VirtualObjectManager vom)
        {
            // 1. PR_VirtualObjectManager のアクティブオブジェクト
            if (vom != null && vom.ActiveTransform != null) return vom.ActiveTransform;

            // 2. PR_AnimationController.toggleObjects フォールバック
            var animCtrl = UnityEngine.Object.FindAnyObjectByType<PR_AnimationController>();
            if (animCtrl != null && animCtrl.toggleObjects != null)
            {
                foreach (var obj in animCtrl.toggleObjects)
                    if (obj != null && obj.activeInHierarchy) return obj.transform;
                if (animCtrl.toggleObjects.Length > 0 && animCtrl.toggleObjects[0] != null)
                    return animCtrl.toggleObjects[0].transform;
            }

            // 3. SkinnedMeshRenderer を持つアクティブなルートを検索
            var skinned = UnityEngine.Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None);
            foreach (var smr in skinned)
            {
                if (!smr.gameObject.activeInHierarchy || smr.rootBone == null) continue;
                Transform root = smr.transform;
                while (root.parent != null
                    && !root.parent.name.Contains("VirtualObjects")
                    && !root.parent.name.Contains("Scene"))
                    root = root.parent;
                return root;
            }

            return null;
        }

        // ---- ユーティリティ -------------------------------------------

        /// <summary>
        /// 再帰的に子孫を探索し、条件を満たす最初の Transform を返します。
        /// </summary>
        public static Transform FindChild(Transform parent, Func<string, bool> predicate)
        {
            if (parent == null) return null;
            if (predicate(parent.name)) return parent;
            foreach (Transform child in parent)
            {
                var found = FindChild(child, predicate);
                if (found != null) return found;
            }
            return null;
        }
    }
}
