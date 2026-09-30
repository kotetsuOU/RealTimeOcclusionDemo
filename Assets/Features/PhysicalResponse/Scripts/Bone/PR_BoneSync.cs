using UnityEngine;

namespace Features.PhysicalResponse.Bone
{
    /// <summary>
    /// PR_BoneDetector が検出したボーン情報を、各コンポーネント（PR_LiftController / HAP系）へ
    /// 同期する純粋C#クラス。MonoBehaviour に依存せず、テストや再利用が容易な形で実装します。
    /// </summary>
    public class PR_BoneSync
    {
        /// <summary>
        /// PR_LiftController にボーン情報を同期します。
        /// </summary>
        public static void SyncTo(PR_LiftController lc, Transform target,
            Transform fl, Transform fr, Transform bl, Transform br, PR_BoneDetector detector)
        {
            if (lc == null) return;
            lc.boneDetector   = detector;
            lc.targetTransform = target;
            lc.frontLeftFoot  = fl;
            lc.frontRightFoot = fr;
            lc.backLeftFoot   = bl;
            lc.backRightFoot  = br;
        }

        /// <summary>
        /// HAP_FoxBodyHapticsController にボーン情報を同期します。
        /// </summary>
        public static void SyncTo(HAP_FoxBodyHapticsController bc, Transform target,
            Transform head, Transform lEar, Transform rEar,
            Transform fl, Transform fr, Transform bl, Transform br, Transform tail)
        {
            if (bc == null) return;
            bc.rootTransform  = target;
            bc.headBone       = head;
            bc.leftEarBone    = lEar;
            bc.rightEarBone   = rEar;
            bc.frontLeftFoot  = fl;
            bc.frontRightFoot = fr;
            bc.backLeftFoot   = bl;
            bc.backRightFoot  = br;
            bc.tailBone       = tail;
        }

        /// <summary>
        /// HAP_FoxFootHapticsController にボーン情報を同期します。
        /// </summary>
        public static void SyncTo(HAP_FoxFootHapticsController fc, Transform target,
            Transform fl, Transform fr, Transform bl, Transform br, Transform tail)
        {
            if (fc == null) return;
            fc.rootTransform  = target;
            fc.frontLeftFoot  = fl;
            fc.frontRightFoot = fr;
            fc.backLeftFoot   = bl;
            fc.backRightFoot  = br;
            fc.tailBone       = tail;
        }
    }
}
