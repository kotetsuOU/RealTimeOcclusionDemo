#nullable enable
using System;
using UnityEngine;

namespace Features.Haptics.Processors
{
    /// <summary>
    /// Fox キャラクターモデルのルート Transform 解決および全身／足部ボーン階層の自動検出ロジックを集約する純粋ヘルパークラス。
    /// 同一探索ロジックの重複を排除し、各 Fox ハプティクスコントローラーから共通利用します。
    /// </summary>
    public static class HAP_FoxBoneHierarchyResolver
    {
        /// <summary>
        /// 既存ボーンの親階層またはシーン内のアクティブな Fox モデルからルート Transform を自動解決します。
        /// </summary>
        public static Transform? ResolveFoxRootTransform(Transform selfTransform, params Transform?[] candidateBones)
        {
            foreach (var bone in candidateBones)
            {
                if (bone == null || !bone.gameObject.activeInHierarchy) continue;

                var anim = bone.GetComponentInParent<Animator>();
                if (anim != null && anim.gameObject.activeInHierarchy) return anim.transform;

                var smr = bone.GetComponentInParent<SkinnedMeshRenderer>();
                if (smr != null && smr.gameObject.activeInHierarchy) return smr.transform;

                if (bone.root != null && bone.root != selfTransform.root && bone.root.gameObject.activeInHierarchy)
                {
                    return bone.root;
                }
            }

            var parentAnim = selfTransform.GetComponentInParent<Animator>();
            if (parentAnim != null && parentAnim.gameObject.activeInHierarchy) return parentAnim.transform;

            var parentSmr = selfTransform.GetComponentInParent<SkinnedMeshRenderer>();
            if (parentSmr != null && parentSmr.gameObject.activeInHierarchy) return parentSmr.transform;

            var smrs = UnityEngine.Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var s in smrs)
            {
                if (s.name.IndexOf("fox", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    s.transform.root.name.IndexOf("fox", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var a = s.GetComponentInParent<Animator>();
                    return a != null ? a.transform : s.transform.root;
                }
            }

            return null;
        }

        /// <summary>
        /// Fox の 4 本の足および尻尾ボーンを検索ルート配下から自動検出します。
        /// </summary>
        public static void DetectFootBones(
            Transform searchRoot,
            bool forceOverwrite,
            ref Transform? frontLeftFoot,
            ref Transform? frontRightFoot,
            ref Transform? backLeftFoot,
            ref Transform? backRightFoot,
            ref Transform? tailBone)
        {
            if (forceOverwrite)
            {
                frontLeftFoot = null;
                frontRightFoot = null;
                backLeftFoot = null;
                backRightFoot = null;
                tailBone = null;
            }

            if (frontLeftFoot == null || !frontLeftFoot.IsChildOf(searchRoot))
            {
                frontLeftFoot = FindChildRecursive(searchRoot, name =>
                    name.Contains("F_LLegDigit11") || name.Contains("Fox_F_LLegDigit11") ||
                    (ContainsIgnoreCase(name, "front") && ContainsIgnoreCase(name, "left") && (ContainsIgnoreCase(name, "foot") || ContainsIgnoreCase(name, "digit"))));
            }

            if (frontRightFoot == null || !frontRightFoot.IsChildOf(searchRoot))
            {
                frontRightFoot = FindChildRecursive(searchRoot, name =>
                    name.Contains("F_RLegDigit11") || name.Contains("Fox_F_RLegDigit11") ||
                    (ContainsIgnoreCase(name, "front") && ContainsIgnoreCase(name, "right") && (ContainsIgnoreCase(name, "foot") || ContainsIgnoreCase(name, "digit"))));
            }

            if (backLeftFoot == null || !backLeftFoot.IsChildOf(searchRoot))
            {
                backLeftFoot = FindChildRecursive(searchRoot, name =>
                    (name.Contains("LLegDigit11") && !name.Contains("F_")) ||
                    (ContainsIgnoreCase(name, "left") && (ContainsIgnoreCase(name, "foot") || ContainsIgnoreCase(name, "digit")) && !ContainsIgnoreCase(name, "front")));
            }

            if (backRightFoot == null || !backRightFoot.IsChildOf(searchRoot))
            {
                backRightFoot = FindChildRecursive(searchRoot, name =>
                    (name.Contains("RLegDigit11") && !name.Contains("F_")) ||
                    (ContainsIgnoreCase(name, "right") && (ContainsIgnoreCase(name, "foot") || ContainsIgnoreCase(name, "digit")) && !ContainsIgnoreCase(name, "front")));
            }

            if (tailBone == null || !tailBone.IsChildOf(searchRoot))
                tailBone = FindChildRecursive(searchRoot, name => name.Contains("Tail6") || name.Contains("Fox_Tail6"));
            if (tailBone == null || !tailBone.IsChildOf(searchRoot))
                tailBone = FindChildRecursive(searchRoot, name => name.Contains("Tail5") || name.Contains("Fox_Tail5"));
            if (tailBone == null || !tailBone.IsChildOf(searchRoot))
                tailBone = FindChildRecursive(searchRoot, name => ContainsIgnoreCase(name, "tail"));

            // 先端ボーン未検出時は Ankle へフォールバックする
            if (frontLeftFoot == null)
            {
                frontLeftFoot = FindChildRecursive(searchRoot, name =>
                    name.Contains("F_LLegAnkle") || (ContainsIgnoreCase(name, "front") && ContainsIgnoreCase(name, "left") && ContainsIgnoreCase(name, "ankle")));
            }
            if (frontRightFoot == null)
            {
                frontRightFoot = FindChildRecursive(searchRoot, name =>
                    name.Contains("F_RLegAnkle") || (ContainsIgnoreCase(name, "front") && ContainsIgnoreCase(name, "right") && ContainsIgnoreCase(name, "ankle")));
            }
            if (backLeftFoot == null)
            {
                backLeftFoot = FindChildRecursive(searchRoot, name =>
                    (name.Contains("LLegAnkle") && !name.Contains("F_")) || (ContainsIgnoreCase(name, "left") && ContainsIgnoreCase(name, "ankle") && !ContainsIgnoreCase(name, "front")));
            }
            if (backRightFoot == null)
            {
                backRightFoot = FindChildRecursive(searchRoot, name =>
                    (name.Contains("RLegAnkle") && !name.Contains("F_")) || (ContainsIgnoreCase(name, "right") && ContainsIgnoreCase(name, "ankle") && !ContainsIgnoreCase(name, "front")));
            }
        }

        /// <summary>
        /// Fox の全身 8 部位（頭・両耳・4 足・尻尾）ボーンを検索ルート配下から自動検出します。
        /// </summary>
        public static void DetectBodyBones(
            Transform searchRoot,
            bool forceOverwrite,
            ref Transform? headBone,
            ref Transform? leftEarBone,
            ref Transform? rightEarBone,
            ref Transform? frontLeftFoot,
            ref Transform? frontRightFoot,
            ref Transform? backLeftFoot,
            ref Transform? backRightFoot,
            ref Transform? tailBone)
        {
            if (forceOverwrite)
            {
                headBone = null;
                leftEarBone = null;
                rightEarBone = null;
            }

            if (headBone == null || !headBone.IsChildOf(searchRoot))
            {
                headBone = FindChildRecursive(searchRoot, name =>
                    name.Equals("Fox_Head", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Head", StringComparison.OrdinalIgnoreCase) ||
                    (ContainsIgnoreCase(name, "head") && !ContainsIgnoreCase(name, "overhead")));
            }

            if (leftEarBone == null || !leftEarBone.IsChildOf(searchRoot))
            {
                leftEarBone = FindChildRecursive(searchRoot, name =>
                    name.Equals("Fox_LEar1", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Fox_LEar2", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("LEar1") ||
                    name.Contains("Ear1_L") ||
                    (ContainsIgnoreCase(name, "ear") && (ContainsIgnoreCase(name, "left") || name.EndsWith("_l", StringComparison.OrdinalIgnoreCase) || ContainsIgnoreCase(name, "_l_"))));
            }

            if (rightEarBone == null || !rightEarBone.IsChildOf(searchRoot))
            {
                rightEarBone = FindChildRecursive(searchRoot, name =>
                    name.Equals("Fox_REar1", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Fox_REar2", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("REar1") ||
                    name.Contains("Ear1_R") ||
                    (ContainsIgnoreCase(name, "ear") && (ContainsIgnoreCase(name, "right") || name.EndsWith("_r", StringComparison.OrdinalIgnoreCase) || ContainsIgnoreCase(name, "_r_"))));
            }

            DetectFootBones(searchRoot, forceOverwrite, ref frontLeftFoot, ref frontRightFoot, ref backLeftFoot, ref backRightFoot, ref tailBone);

            if (headBone == null)
            {
                headBone = FindChildRecursive(searchRoot, name => name.Contains("Fox_Neck") || ContainsIgnoreCase(name, "neck"));
            }
        }

        private static bool ContainsIgnoreCase(string source, string value)
            => source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;

        private static Transform? FindChildRecursive(Transform parent, Func<string, bool> predicate)
        {
            if (predicate(parent.name)) return parent;
            foreach (Transform child in parent)
            {
                var found = FindChildRecursive(child, predicate);
                if (found != null) return found;
            }
            return null;
        }
    }
}

