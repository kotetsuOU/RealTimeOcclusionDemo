using System;
using System.Collections.Generic;
using UnityEngine;
using Core.Logging;
using Features.PhysicalResponse.Bone;

namespace Features.PhysicalResponse
{
    /// <summary>
    /// PR系および触覚系コンポーネント用の一括ボーン検出・同期マネージャ（MonoBehaviour）。
    /// 検出アルゴリズムは PR_BoneSearcher、RestPose 管理は PR_RestPoseCapture に委譲します。
    /// コンポーネントへの同期は PR_BoneSync に委譲します。
    /// </summary>
    [ExecuteAlways]
    [AppLoggable("PR (PhysicalResponse)")]
    public class PR_BoneDetector : MonoBehaviour, IAppLoggable
    {
        public const string TagBoneDetector = "PR_BoneDetector";

        public void RegisterLogTriggers(LogCategoryGroup group, HashSet<string> existingLabels)
        {
            const string label = "[PR_BoneDetector] Bone Sync State";
            if (!existingLabels.Contains(label))
            {
                group.entries.Add(new LogInstanceEntry { label = label, tag = TagBoneDetector, target = this, enabled = true });
                existingLabels.Add(label);
            }
        }

        public static PR_BoneDetector Instance { get; private set; }

        [Header("Target & Virtual Object Manager")]
        [Tooltip("バーチャルオブジェクト一括管理マネージャ（設定されている場合、アクティブなモデルをここから取得します）")]
        public PR_VirtualObjectManager virtualObjectManager;

        [Tooltip("検出対象のモデルルート。未設定または非アクティブ時は自動取得します。")]
        public Transform targetTransform;

        [Tooltip("エディタ非再生時および実行時に、アクティブなモデルの切り替えを自動監視して再検出するか")]
        public bool autoDetectActiveModel = true;

        [Header("Detected Foot Bones")]
        public Transform frontLeftFoot;
        public Transform frontRightFoot;
        public Transform backLeftFoot;
        public Transform backRightFoot;

        [Header("Detected Body Bones")]
        public Transform headBone;
        public Transform leftEarBone;
        public Transform rightEarBone;
        public Transform tailBone;

        // RestPose 管理委譲
        private readonly PR_RestPoseCapture _restPose = new PR_RestPoseCapture();

        public bool HasRestPose    => _restPose.HasRestPose;

        // RestPose ローカル座標（Editor SerializeField で Inspector 表示）
        [Header("Rest Pose (Animation-Independent Feet Local Offsets)")]
        [SerializeField] private bool hasRestPoseSerialized;
        [SerializeField] private Vector3 localFrontLeft;
        [SerializeField] private Vector3 localFrontRight;
        [SerializeField] private Vector3 localBackLeft;
        [SerializeField] private Vector3 localBackRight;

        // ボーン更新通知イベント
        public event Action OnBonesUpdated;

        // ---- ライフサイクル -------------------------------------------

        private void Awake()
        {
            Instance = this;
            RestoreRestPoseFromSerialized();
            SubscribeVirtualObjectManager();
            EnsureDetection();
        }

        private void OnEnable()
        {
            Instance = this;
            SubscribeVirtualObjectManager();
            EnsureDetection();
        }

        private void OnDisable()
        {
            if (virtualObjectManager != null)
                virtualObjectManager.OnActiveObjectChanged -= HandleActiveObjectChanged;
        }

        private void Update()
        {
            if (autoDetectActiveModel)
            {
                if (targetTransform == null || !targetTransform.gameObject.activeInHierarchy || !IsBonesValid())
                    DetectBones();
            }

            // 非Play時: エディタ作業中は毎フレーム RestPose を同期
            if (!Application.isPlaying && targetTransform != null && IsBonesValid())
            {
                _restPose.SyncFromCurrentTransforms(targetTransform, frontLeftFoot, frontRightFoot, backLeftFoot, backRightFoot);
                FlushRestPoseToSerialized();
            }
        }

        // ---- VirtualObjectManager 購読 --------------------------------

        private void SubscribeVirtualObjectManager()
        {
            if (virtualObjectManager == null)
            {
                virtualObjectManager = PR_VirtualObjectManager.Instance != null
                    ? PR_VirtualObjectManager.Instance
                    : FindFirstObjectByType<PR_VirtualObjectManager>();
            }
            if (virtualObjectManager != null)
            {
                virtualObjectManager.OnActiveObjectChanged -= HandleActiveObjectChanged;
                virtualObjectManager.OnActiveObjectChanged += HandleActiveObjectChanged;
            }
        }

        private void HandleActiveObjectChanged(GameObject newActive)
        {
            if (newActive != null) SetTarget(newActive.transform);
        }

        // ---- 公開 API --------------------------------------------------

        public bool IsBonesValid()
            => PR_BoneSearcher.IsBonesValid(targetTransform, frontLeftFoot, frontRightFoot, backLeftFoot, backRightFoot);

        public void EnsureDetection()
        {
            if (!IsBonesValid()) DetectBones();
            else                 SyncToAllComponents();
        }

        /// <summary>
        /// 制御対象モデルを明示的に変更し、ボーンを一括再検出します。
        /// </summary>
        public void SetTarget(Transform newTarget)
        {
            if (newTarget == null) return;
            targetTransform = newTarget;
            ClearBones();
            DetectBones(targetTransform);
        }

        public void ClearBones()
        {
            frontLeftFoot = frontRightFoot = backLeftFoot = backRightFoot = null;
            headBone = leftEarBone = rightEarBone = tailBone = null;
            _restPose.Clear();
            FlushRestPoseToSerialized();
        }

        /// <summary>
        /// アクティブなターゲットおよび全ボーンを一括検出します。
        /// </summary>
        public void DetectBones(Transform searchRoot = null)
        {
            // 1. targetTransform の特定
            if (searchRoot != null)
            {
                targetTransform = searchRoot;
            }
            else if (targetTransform == null || !targetTransform.gameObject.activeInHierarchy)
            {
                targetTransform = PR_BoneSearcher.FindActiveTarget(virtualObjectManager);
            }

            if (targetTransform == null) return;

            // 2. 全ボーン検出を委譲
            PR_BoneSearcher.DetectAll(
                targetTransform,
                out frontLeftFoot, out frontRightFoot,
                out backLeftFoot,  out backRightFoot,
                out headBone,      out leftEarBone,
                out rightEarBone,  out tailBone
            );

            // 3. RestPose キャプチャ
            _restPose.Capture(targetTransform, frontLeftFoot, frontRightFoot, backLeftFoot, backRightFoot);
            FlushRestPoseToSerialized();

            // 4. 同期 & イベント通知
            SyncToAllComponents();
            AppLogger.Log(this, $"[PR_BoneDetector] ボーン検出 & 一括同期完了: target={targetTransform.name}, hasRestPose={HasRestPose}");
            OnBonesUpdated?.Invoke();
        }

        /// <summary>
        /// アニメーション非依存の足4点ワールド座標を取得します。
        /// </summary>
        public void GetRestFeetWorldPositions(out Vector3 fl, out Vector3 fr, out Vector3 bl, out Vector3 br)
            => _restPose.GetWorldPositions(targetTransform, frontLeftFoot, frontRightFoot, backLeftFoot, backRightFoot,
                                           out fl, out fr, out bl, out br);

        /// <summary>
        /// 検出したボーン情報を関連コンポーネントに一括同期します。
        /// </summary>
        public void SyncToAllComponents()
        {
            if (targetTransform == null) return;

            foreach (var lc in FindObjectsByType<PR_LiftController>(FindObjectsSortMode.None))
                PR_BoneSync.SyncTo(lc, targetTransform, frontLeftFoot, frontRightFoot, backLeftFoot, backRightFoot, this);

            foreach (var bc in FindObjectsByType<HAP_FoxBodyHapticsController>(FindObjectsSortMode.None))
                PR_BoneSync.SyncTo(bc, targetTransform, headBone, leftEarBone, rightEarBone, frontLeftFoot, frontRightFoot, backLeftFoot, backRightFoot, tailBone);

            foreach (var fc in FindObjectsByType<HAP_FoxFootHapticsController>(FindObjectsSortMode.None))
                PR_BoneSync.SyncTo(fc, targetTransform, frontLeftFoot, frontRightFoot, backLeftFoot, backRightFoot, tailBone);
        }

        // ---- Serialization helper -------------------------------------

        /// RestPose を SerializeField（Inspector 表示 & シリアライズ）に書き出す
        private void FlushRestPoseToSerialized()
        {
            hasRestPoseSerialized = _restPose.HasRestPose;
            localFrontLeft  = _restPose.LocalFrontLeft;
            localFrontRight = _restPose.LocalFrontRight;
            localBackLeft   = _restPose.LocalBackLeft;
            localBackRight  = _restPose.LocalBackRight;
        }

        /// シリアライズ済みデータを PR_RestPoseCapture に復元する（Awake 時）
        private void RestoreRestPoseFromSerialized()
        {
            if (!hasRestPoseSerialized) return;
            _restPose.Capture(targetTransform,
                frontLeftFoot, frontRightFoot, backLeftFoot, backRightFoot);
        }
    }
}
