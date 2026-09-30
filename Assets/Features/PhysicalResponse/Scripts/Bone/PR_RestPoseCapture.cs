using UnityEngine;

namespace Features.PhysicalResponse.Bone
{
    /// <summary>
    /// アニメーション非依存の静止基準ポーズ（RestPose）のキャプチャ・同期・ワールド座標復元を担う純粋C#クラス。
    /// PR_BoneDetector から RestPose 管理ロジックを分離します。
    /// </summary>
    public class PR_RestPoseCapture
    {
        // ローカル座標でキャプチャした足4点
        public Vector3 LocalFrontLeft  { get; private set; }
        public Vector3 LocalFrontRight { get; private set; }
        public Vector3 LocalBackLeft   { get; private set; }
        public Vector3 LocalBackRight  { get; private set; }

        public bool HasRestPose { get; private set; }

        /// <summary>
        /// 現在の足ボーン位置を targetTransform ローカル座標として記録します（静止基準ポーズ）。
        /// </summary>
        public void Capture(Transform target, Transform fl, Transform fr, Transform bl, Transform br)
        {
            if (target == null) return;
            if (fl != null) LocalFrontLeft  = target.InverseTransformPoint(fl.position);
            if (fr != null) LocalFrontRight = target.InverseTransformPoint(fr.position);
            if (bl != null) LocalBackLeft   = target.InverseTransformPoint(bl.position);
            if (br != null) LocalBackRight  = target.InverseTransformPoint(br.position);
            HasRestPose = (fl != null && fr != null && bl != null && br != null);
        }

        /// <summary>
        /// エディタ作業中（非Play時）に毎フレーム現在のボーン位置でローカル座標を上書き同期します。
        /// </summary>
        public void SyncFromCurrentTransforms(Transform target, Transform fl, Transform fr, Transform bl, Transform br)
        {
            if (target == null || fl == null || fr == null || bl == null || br == null) return;
            LocalFrontLeft  = target.InverseTransformPoint(fl.position);
            LocalFrontRight = target.InverseTransformPoint(fr.position);
            LocalBackLeft   = target.InverseTransformPoint(bl.position);
            LocalBackRight  = target.InverseTransformPoint(br.position);
            HasRestPose = true;
        }

        /// <summary>
        /// アニメーション非依存の足4点ワールド座標を取得します。
        /// 非Play時は実際のボーン位置を直接返し、Play時はキャプチャ済みローカル座標から復元します。
        /// </summary>
        public void GetWorldPositions(
            Transform target,
            Transform fl, Transform fr, Transform bl, Transform br,
            out Vector3 outFl, out Vector3 outFr, out Vector3 outBl, out Vector3 outBr)
        {
            if (!Application.isPlaying)
            {
                outFl = fl != null ? fl.position : Vector3.zero;
                outFr = fr != null ? fr.position : Vector3.zero;
                outBl = bl != null ? bl.position : Vector3.zero;
                outBr = br != null ? br.position : Vector3.zero;
                return;
            }

            if (HasRestPose && target != null)
            {
                outFl = target.TransformPoint(LocalFrontLeft);
                outFr = target.TransformPoint(LocalFrontRight);
                outBl = target.TransformPoint(LocalBackLeft);
                outBr = target.TransformPoint(LocalBackRight);
            }
            else
            {
                outFl = fl != null ? fl.position : Vector3.zero;
                outFr = fr != null ? fr.position : Vector3.zero;
                outBl = bl != null ? bl.position : Vector3.zero;
                outBr = br != null ? br.position : Vector3.zero;
            }
        }

        /// <summary>
        /// キャプチャ済みデータをクリアします。
        /// </summary>
        public void Clear()
        {
            LocalFrontLeft  = Vector3.zero;
            LocalFrontRight = Vector3.zero;
            LocalBackLeft   = Vector3.zero;
            LocalBackRight  = Vector3.zero;
            HasRestPose = false;
        }
    }
}
