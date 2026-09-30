#if false
using UnityEngine;
using System.Collections.Generic;

namespace Features.PhysicalResponse
{
    /// <summary>
    /// PR_Controller (PhysicsProfile) の設定値と HCD_Pipeline の点群衝突データを組み合わせて、
    /// NativePackageに依存せずに独自のボーン制御（曲げ）を行うカスタムスクリプトです。
    /// </summary>
    [RequireComponent(typeof(PhysicsProfile))]
    public class PR_HcdBoneApplier : MonoBehaviour
    {
        [Header("Target Setup")]
        [Tooltip("力を適用して曲げたいボーン（複数指定可能）")]
        public List<Transform> targetBones = new List<Transform>();

        [Tooltip("ボーンが力の影響を受ける最大距離（メートル）")]
        public float interactionRadius = 0.05f;

        [Tooltip("力の感度（HCD_Pipelineからの力をどれくらい増幅するか）")]
        public float forceMultiplier = 10f;

        // PR_Controllerによって値がセットされるPhysicsProfile
        private PhysicsProfile _profile;

        // 本来のアニメーション角度を保持する辞書
        private Dictionary<Transform, Quaternion> _originalLocalRotations = new Dictionary<Transform, Quaternion>();

        private void Start()
        {
            _profile = GetComponent<PhysicsProfile>();

            if (targetBones.Count == 0)
            {
                UnityEngine.Debug.LogWarning("[PR_HcdBoneApplier] 対象となる targetBones が設定されていません。");
            }
        }

        private void LateUpdate()
        {
            if (_profile == null || HCD_Pipeline.Instance == null) return;

            // 1. 本来のアニメーション角度（LateUpdateの最初時点）を記録・更新
            UpdateOriginalRotations();

            // 2. HCD_Pipeline から現在の衝突データ（点群クラスタ）を取得
            var activeClusters = HCD_Pipeline.Instance.GetTrackedClusters();

            // 3. 各ボーンに対して力の計算と曲げ処理を適用
            foreach (var bone in targetBones)
            {
                if (bone == null) continue;

                Vector3 totalAppliedForce = CalculateForceOnBone(bone, activeClusters);

                // 4. PR_Controller のパラメータを反映
                // forceScale: 力をさらに全体的に強める/弱める
                // coeffKBoneRot: バネの硬さ（元の姿勢に戻ろうとする強さ）
                float appliedScale = _profile.forceScale;
                float stiffness = Mathf.Clamp01(_profile.coeffKBoneRot);

                // 5. ボーンを回転させる
                ApplyBending(bone, totalAppliedForce * appliedScale, stiffness);
            }
        }

        private void UpdateOriginalRotations()
        {
            foreach (var bone in targetBones)
            {
                if (bone != null)
                {
                    // 毎フレームのアニメーションの目標角度を保存
                    _originalLocalRotations[bone] = bone.localRotation;
                }
            }
        }

        private Vector3 CalculateForceOnBone(Transform bone, IReadOnlyList<TrackedCluster> clusters)
        {
            Vector3 totalForce = Vector3.zero;

            foreach (var cluster in clusters)
            {
                if (!cluster.IsAlive) continue;

                float distance = Vector3.Distance(bone.position, cluster.Centroid);
                
                // ボーンの近くに衝突点がある場合
                if (distance <= interactionRadius)
                {
                    // 距離が近いほど強く影響するように減衰させる（オプション）
                    float distanceFactor = 1.0f - (distance / interactionRadius);
                    
                    // クラスタの法線方向に、Forceの強さを掛けて追加
                    totalForce += cluster.Normal * (cluster.Force * forceMultiplier * distanceFactor);
                }
            }

            return totalForce;
        }

        private void ApplyBending(Transform bone, Vector3 appliedForce, float stiffness)
        {
            if (!_originalLocalRotations.TryGetValue(bone, out Quaternion originalRot))
            {
                return;
            }

            // 力がほとんど加わっていない場合は、元の（アニメーションの）角度に戻す
            if (appliedForce.sqrMagnitude < 0.0001f)
            {
                bone.localRotation = Quaternion.Slerp(bone.localRotation, originalRot, 0.1f); // 少しずつ戻る
                return;
            }

            // 力のベクトルをボーンのローカル空間に変換（ボーンの向きに合わせて曲がるようにする）
            // ※今回はシンプルにグローバルな力ベクトルを回転に変換する簡易計算
            // 必要に応じてボーンの軸(Axis)を考慮した計算にカスタマイズできます。
            Quaternion forceRotation = Quaternion.Euler(appliedForce.x, appliedForce.y, appliedForce.z);
            Quaternion targetPhysicsRot = originalRot * forceRotation;

            // PR_Controllerのstiffness (coeffKBoneRot) を使って、
            // 「本来のアニメーション角度(originalRot)」と「物理で曲がった角度(targetPhysicsRot)」をブレンド
            // ※stiffnessが1なら完全に本来の角度に戻ろうとする。0なら完全に曲がりっぱなし。
            bone.localRotation = Quaternion.Slerp(targetPhysicsRot, originalRot, stiffness);
        }
    }
}
#endif
