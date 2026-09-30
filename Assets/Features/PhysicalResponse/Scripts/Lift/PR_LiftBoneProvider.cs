using System;
using UnityEngine;

namespace Features.PhysicalResponse
{
    /// <summary>
    /// PR_LiftController 用の骨検出および静止基準姿勢（RestPose）座標解決を担う純粋 C# クラス。
    /// PR_BoneDetector との協調・フォールバック検索・ローカル座標からのワールド位置復元を一元管理します。
    /// </summary>
    public class PR_LiftBoneProvider
    {
        // 依存コンポーネントの参照（外部から代入）
        public PR_BoneDetector BoneDetector;
        public Transform TargetTransform;
        public Transform FrontLeftFoot;
        public Transform FrontRightFoot;
        public Transform BackLeftFoot;
        public Transform BackRightFoot;

        // RestPose ローカル座標
        public Vector3 LocalFrontLeft;
        public Vector3 LocalFrontRight;
        public Vector3 LocalBackLeft;
        public Vector3 LocalBackRight;
        public bool HasRestPose { get; private set; }

        /// <summary>
        /// 現在の足ボーンのワールド姿勢を、アニメーション非依存の基準ポーズとして記録します。
        /// </summary>
        public void CaptureRestPose(Transform targetTransform, Transform fl, Transform fr, Transform bl, Transform br)
        {
            TargetTransform = targetTransform;
            FrontLeftFoot = fl;
            FrontRightFoot = fr;
            BackLeftFoot = bl;
            BackRightFoot = br;

            if (TargetTransform == null) return;

            if (FrontLeftFoot != null)  LocalFrontLeft  = TargetTransform.InverseTransformPoint(FrontLeftFoot.position);
            if (FrontRightFoot != null) LocalFrontRight = TargetTransform.InverseTransformPoint(FrontRightFoot.position);
            if (BackLeftFoot != null)   LocalBackLeft   = TargetTransform.InverseTransformPoint(BackLeftFoot.position);
            if (BackRightFoot != null)  LocalBackRight  = TargetTransform.InverseTransformPoint(BackRightFoot.position);

            HasRestPose = (FrontLeftFoot != null && FrontRightFoot != null && BackLeftFoot != null && BackRightFoot != null);
        }

        /// <summary>
        /// アニメーション非依存の足4点ワールド座標を返します。
        /// PR_BoneDetector が存在する場合はそこから取得し、無ければ RestPose から復元します。
        /// </summary>
        public void GetRestFeetWorldPositions(out Vector3 fl, out Vector3 fr, out Vector3 bl, out Vector3 br)
        {
            if (BoneDetector != null && BoneDetector.targetTransform != null)
            {
                BoneDetector.GetRestFeetWorldPositions(out fl, out fr, out bl, out br);
                return;
            }

            // 非プレイ中はボーン Transform から直接取得
            if (!Application.isPlaying)
            {
                if (FrontLeftFoot != null && FrontRightFoot != null && BackLeftFoot != null && BackRightFoot != null)
                {
                    fl = FrontLeftFoot.position;
                    fr = FrontRightFoot.position;
                    bl = BackLeftFoot.position;
                    br = BackRightFoot.position;
                    return;
                }
            }

            // RestPose からワールド座標を復元
            if (HasRestPose && TargetTransform != null)
            {
                fl = TargetTransform.TransformPoint(LocalFrontLeft);
                fr = TargetTransform.TransformPoint(LocalFrontRight);
                bl = TargetTransform.TransformPoint(LocalBackLeft);
                br = TargetTransform.TransformPoint(LocalBackRight);
                return;
            }

            // 最終フォールバック
            fl = FrontLeftFoot  != null ? FrontLeftFoot.position  : Vector3.zero;
            fr = FrontRightFoot != null ? FrontRightFoot.position : Vector3.zero;
            bl = BackLeftFoot   != null ? BackLeftFoot.position   : Vector3.zero;
            br = BackRightFoot  != null ? BackRightFoot.position  : Vector3.zero;
        }

        /// <summary>
        /// シーン内から骨を自動検出します。PR_BoneDetector が存在する場合は委譲し、
        /// 存在しない場合は PR_VirtualObjectManager / PR_AnimationController から
        /// アクティブモデルを特定して足ボーンを再検出します。
        /// </summary>
        public void AutoDetectBones(MonoBehaviour owner, Transform searchRoot = null)
        {
            // BoneDetector 経由
            if (BoneDetector == null)
            {
                BoneDetector = UnityEngine.Object.FindFirstObjectByType<PR_BoneDetector>();
            }
            if (BoneDetector != null)
            {
                BoneDetector.DetectBones(searchRoot);
                SyncFromBoneDetector();
                return;
            }

            // アクティブモデルの特定
            if (TargetTransform == null || !TargetTransform.gameObject.activeInHierarchy)
            {
                var vom = PR_VirtualObjectManager.Instance
                    ?? UnityEngine.Object.FindFirstObjectByType<PR_VirtualObjectManager>();
                if (vom != null && vom.ActiveTransform != null)
                {
                    TargetTransform = vom.ActiveTransform;
                    ClearFootBones();
                }
                else
                {
                    var animCtrl = UnityEngine.Object.FindAnyObjectByType<PR_AnimationController>();
                    if (animCtrl != null && animCtrl.toggleObjects != null)
                    {
                        foreach (var obj in animCtrl.toggleObjects)
                        {
                            if (obj != null && obj.activeInHierarchy)
                            {
                                TargetTransform = obj.transform;
                                ClearFootBones();
                                break;
                            }
                        }
                        if (TargetTransform == null && animCtrl.toggleObjects.Length > 0 && animCtrl.toggleObjects[0] != null)
                        {
                            TargetTransform = animCtrl.toggleObjects[0].transform;
                        }
                    }
                }
            }

            if (searchRoot == null)
            {
                searchRoot = TargetTransform != null ? TargetTransform : owner.transform;
            }

            bool needFL = (FrontLeftFoot  == null || !FrontLeftFoot.IsChildOf(searchRoot));
            bool needFR = (FrontRightFoot == null || !FrontRightFoot.IsChildOf(searchRoot));
            bool needBL = (BackLeftFoot   == null || !BackLeftFoot.IsChildOf(searchRoot));
            bool needBR = (BackRightFoot  == null || !BackRightFoot.IsChildOf(searchRoot));

            if (needFL) FrontLeftFoot  = FindChildRecursive(searchRoot, n => n.Contains("F_LLegDigit11") || n.Contains("Fox_F_LLegDigit11") || (n.ToLower().Contains("front") && n.ToLower().Contains("left")  && (n.ToLower().Contains("foot") || n.ToLower().Contains("digit"))));
            if (needFR) FrontRightFoot = FindChildRecursive(searchRoot, n => n.Contains("F_RLegDigit11") || n.Contains("Fox_F_RLegDigit11") || (n.ToLower().Contains("front") && n.ToLower().Contains("right") && (n.ToLower().Contains("foot") || n.ToLower().Contains("digit"))));
            if (needBL) BackLeftFoot   = FindChildRecursive(searchRoot, n => (n.Contains("LLegDigit11") && !n.Contains("F_")) || (n.ToLower().Contains("left")  && (n.ToLower().Contains("foot") || n.ToLower().Contains("digit")) && !n.ToLower().Contains("front")));
            if (needBR) BackRightFoot  = FindChildRecursive(searchRoot, n => (n.Contains("RLegDigit11") && !n.Contains("F_")) || (n.ToLower().Contains("right") && (n.ToLower().Contains("foot") || n.ToLower().Contains("digit")) && !n.ToLower().Contains("front")));

            // Ankle フォールバック
            if (FrontLeftFoot  == null) FrontLeftFoot  = FindChildRecursive(searchRoot, n => n.Contains("F_LLegAnkle") || (n.ToLower().Contains("front") && n.ToLower().Contains("left")  && n.ToLower().Contains("ankle")));
            if (FrontRightFoot == null) FrontRightFoot = FindChildRecursive(searchRoot, n => n.Contains("F_RLegAnkle") || (n.ToLower().Contains("front") && n.ToLower().Contains("right") && n.ToLower().Contains("ankle")));
            if (BackLeftFoot   == null) BackLeftFoot   = FindChildRecursive(searchRoot, n => (n.Contains("LLegAnkle") && !n.Contains("F_")) || (n.ToLower().Contains("left")  && n.ToLower().Contains("ankle") && !n.ToLower().Contains("front")));
            if (BackRightFoot  == null) BackRightFoot  = FindChildRecursive(searchRoot, n => (n.Contains("RLegAnkle") && !n.Contains("F_")) || (n.ToLower().Contains("right") && n.ToLower().Contains("ankle") && !n.ToLower().Contains("front")));
        }

        /// <summary>
        /// PR_BoneDetector の最新状態をプロバイダに反映します。
        /// </summary>
        public void SyncFromBoneDetector()
        {
            if (BoneDetector == null) return;
            TargetTransform = BoneDetector.targetTransform;
            FrontLeftFoot   = BoneDetector.frontLeftFoot;
            FrontRightFoot  = BoneDetector.frontRightFoot;
            BackLeftFoot    = BoneDetector.backLeftFoot;
            BackRightFoot   = BoneDetector.backRightFoot;
        }

        private void ClearFootBones()
        {
            FrontLeftFoot  = null;
            FrontRightFoot = null;
            BackLeftFoot   = null;
            BackRightFoot  = null;
        }

        private Transform FindChildRecursive(Transform parent, Func<string, bool> predicate)
        {
            if (predicate(parent.name)) return parent;
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform found = FindChildRecursive(parent.GetChild(i), predicate);
                if (found != null) return found;
            }
            return null;
        }
    }
}
