using System.Collections.Generic;
using UnityEngine;
using Features.HapticsCollision;
using Core.Logging;

namespace Features.PhysicalResponse
{
    public enum LiftCalculationMode
    {
        [Tooltip("初期接地位置 + 手の持ち上げ変位。手が静止している際にアニメーションやノイズでオブジェクトが沈むのを防ぎます。")]
        InitialPositionPlusLift,

        [Tooltip("フレーム間差分を相対加算（従来の方式）。")]
        IncrementalDelta
    }

    /// <summary>
    /// 手の平によるオブジェクト持ち上げ（Lift）および追従・落下を統括するコントローラー。
    /// 骨検出は PR_LiftBoneProvider、幾何計算は PR_LiftPlaneCalculator、
    /// クラスタ評価は PR_LiftClusterFilter、座標適用は PR_LiftMotionApplier に委譲します。
    /// </summary>
    [AppLoggable("PR (PhysicalResponse)")]
    public class PR_LiftController : MonoBehaviour, IAppLoggable
    {
        [Header("Target & Bone Detector Settings")]
        [Tooltip("一括ボーン検出・同期マネージャ（設定されている場合、ターゲットや足ボーンはここから同期されます）")]
        public PR_BoneDetector boneDetector;

        [Tooltip("移動させる対象（Fox全体）")]
        public Transform targetTransform;

        [Header("Foot Bone Transforms")]
        public Transform frontLeftFoot;
        public Transform frontRightFoot;
        public Transform backLeftFoot;
        public Transform backRightFoot;

        [Header("Foot Toggles")]
        public bool enableFrontLeft = true;
        public bool enableFrontRight = true;
        public bool enableBackLeft = true;
        public bool enableBackRight = true;

        [Header("Rest Pose (Animation-Independent Feet)")]
        [Tooltip("アニメーションの影響を受けない静止状態の足4点ローカル座標が保持されているか")]
        [SerializeField] private bool hasRestPose = false;
        [SerializeField] private Vector3 localFrontLeft;
        [SerializeField] private Vector3 localFrontRight;
        [SerializeField] private Vector3 localBackLeft;
        [SerializeField] private Vector3 localBackRight;

        [Header("Foot Plane & Area Settings")]
        [Tooltip("足4点が構成する四角形から外側に広げるマージン（m）。ノイズ防止と手の幅を考慮します。")]
        public float planeMargin = 0.03f;

        [Tooltip("平面より下部（手の平側）を抽出する深さ閾値（m）。")]
        public float underPlaneDepthThreshold = 0.05f;

        [Tooltip("平面より上部への微小許容マージン（m）。体側ノイズを防ぐため通常は0〜0.005mにします。")]
        public float upperPlaneMargin = 0.005f;

        [Tooltip("HCD_Pipeline の DetectionMode を自動的に FootPlane モードに切り替えて同期するか")]
        public bool syncWithHcd = true;

        [Header("Lift Calculation Mode")]
        [Tooltip("持ち上げ計算モード。\n・InitialPositionPlusLift: 初期位置 + 手の持ち上げ変位（静止時に下がらず最も安定）\n・IncrementalDelta: フレーム間差分を相対加算（従来の方式）")]
        public LiftCalculationMode liftMode = LiftCalculationMode.InitialPositionPlusLift;

        [Header("Lift & Sensitivity Settings")]
        [Tooltip("持ち上げ時の追従感度（重心変位に乗算する定数倍。初期設定: 1.0）")]
        public float liftSensitivity = 1.0f;

        [Tooltip("InitialPositionPlusLift モード時の最大持ち上げ高さ（m）")]
        public float maxLiftHeight = 0.3f;

        [Tooltip("InitialPositionPlusLift モード時の最小持ち上げ高さ（m。0で初期高さ以下への沈み込みを防止）")]
        public float minLiftHeight = 0.0f;

        [Tooltip("手の水平移動（前後左右）にも追従させるか（InitialPositionPlusLiftモード時）")]
        public bool followHorizontalHand = false;

        [Tooltip("1フレームあたりの最大変位限界値（m）。IncrementalDeltaモード時の急激なジャンプやワープを防止します。")]
        public float maxLiftDelta = 0.05f;

        [Tooltip("前回重心との距離がこの値を超えたら急激な飛び移り・追跡ロストとみなす閾値（m）")]
        public float maxCentroidJump = 0.15f;

        [Header("Fall Settings")]
        [Tooltip("手が離れた際に落下・復帰する目標ポイント（任意）")]
        public Transform fallbackPoint;

        [Tooltip("落下・復帰の速度（m/s）")]
        public float fallSpeed = 2.0f;

        // 分割プロセッサインスタンス
        private readonly PR_LiftBoneProvider _boneProvider = new PR_LiftBoneProvider();
        private readonly PR_LiftClusterFilter _clusterFilter = new PR_LiftClusterFilter();
        private readonly PR_LiftMotionApplier _motionApplier = new PR_LiftMotionApplier();

        // 外部・Editor 公開プロパティ
        public bool HasRestPose => hasRestPose;
        public bool IsContacting => _clusterFilter.IsContacting;
        public float AppliedLiftOffset => _motionApplier.AppliedLiftOffset;

        // --- IAppLoggable ---
        public const string TagLift = "PR_LiftController";

        public void RegisterLogTriggers(LogCategoryGroup group, HashSet<string> existingLabels)
        {
            AddSubTrigger(group, this, "[PR_LiftController] Lift & Contact State", TagLift, existingLabels);
        }

        private static void AddSubTrigger(LogCategoryGroup group, Object target, string label, string tag, HashSet<string> existing)
        {
            if (existing.Contains(label)) return;
            group.entries.Add(new LogInstanceEntry { label = label, tag = tag, target = target, enabled = true });
            existing.Add(label);
        }

        // --- Unity Lifecycle ---

        private void Reset()
        {
            if (targetTransform == null) targetTransform = transform;
            AutoDetectBones();
            CaptureRestPose();
        }

        private void OnValidate()
        {
            if (!Application.isPlaying)
            {
                if (targetTransform == null || frontLeftFoot == null || frontRightFoot == null
                    || backLeftFoot == null || backRightFoot == null)
                {
                    AutoDetectBones();
                }
                if (frontLeftFoot != null && frontRightFoot != null && backLeftFoot != null && backRightFoot != null)
                {
                    CaptureRestPose();
                }
            }
        }

        private void Awake()
        {
            if (targetTransform == null) targetTransform = transform;
            AutoDetectBones();
            if (!hasRestPose) CaptureRestPose();
        }

        private void LateUpdate()
        {
            if (targetTransform == null || HCD_Pipeline.Instance == null) return;
            if (frontLeftFoot == null || frontRightFoot == null || backLeftFoot == null || backRightFoot == null) return;

            // 1. RestPose 足座標の取得（アニメーション非依存）
            _boneProvider.BoneDetector = boneDetector;
            _boneProvider.TargetTransform = targetTransform;
            _boneProvider.GetRestFeetWorldPositions(out Vector3 fl, out Vector3 fr, out Vector3 bl, out Vector3 br);

            // BoneDetector 同期後にターゲットを反映
            if (_boneProvider.BoneDetector != null)
            {
                targetTransform = _boneProvider.TargetTransform;
            }

            // 2. 足平面・バウンディングの計算
            var plane = PR_LiftPlaneCalculator.CalculatePlane(
                targetTransform, fl, fr, bl, br,
                enableFrontLeft, enableFrontRight, enableBackLeft, enableBackRight,
                planeMargin, hasRestPose,
                localFrontLeft, localFrontRight, localBackLeft, localBackRight
            );
            if (!plane.IsValid) return;

            // 3. HCD_Pipeline との同期
            SyncWithHcdPipeline(plane);

            // 4. 点群クラスタの評価と重心選定
            var clusters = HCD_Pipeline.Instance.GetTrackedClusters();
            bool wasContacting = _clusterFilter.IsContacting;
            Vector3 previousCentroid = _clusterFilter.PreviousCentroid;

            bool hasContact = _clusterFilter.EvaluateClusters(
                clusters, targetTransform, in plane,
                underPlaneDepthThreshold, upperPlaneMargin, planeMargin, maxCentroidJump,
                out Vector3 currentCentroid
            );

            // 5. 追従移動または落下挙動
            if (!hasContact)
            {
                _motionApplier.ApplyFall(targetTransform, fallSpeed, Time.deltaTime, followHorizontalHand);

                if (AppLogger.IsEnabled(this, TagLift) && Time.frameCount % 120 == 0)
                    AppLogger.Log(this, $"[PR_LiftController] 非接触（落下中）: fallSpeed={fallSpeed}");

                return;
            }

            if (!wasContacting)
            {
                _motionApplier.OnContactStart(currentCentroid, plane.Normal, liftSensitivity);
                AppLogger.Log(this, $"[PR_LiftController] 接触開始: centroid={currentCentroid}");
            }
            else
            {
                _motionApplier.ApplyLiftTracking(
                    targetTransform, currentCentroid, previousCentroid, plane.Normal,
                    liftMode, liftSensitivity, minLiftHeight, maxLiftHeight, maxLiftDelta, followHorizontalHand
                );

                if (AppLogger.IsEnabled(this, TagLift) && Time.frameCount % 120 == 0)
                    AppLogger.Log(this, $"[PR_LiftController] 接触追従中: offset={_motionApplier.AppliedLiftOffset:F3}m");
            }
        }

        // --- Public API ---

        /// <summary>
        /// 現在のボーン姿勢を、アニメーション非依存の基準ポーズとして記録します。
        /// </summary>
        public void CaptureRestPose()
        {
            if (targetTransform == null) targetTransform = transform;

            _boneProvider.CaptureRestPose(targetTransform, frontLeftFoot, frontRightFoot, backLeftFoot, backRightFoot);
            hasRestPose = _boneProvider.HasRestPose;

            localFrontLeft  = _boneProvider.LocalFrontLeft;
            localFrontRight = _boneProvider.LocalFrontRight;
            localBackLeft   = _boneProvider.LocalBackLeft;
            localBackRight  = _boneProvider.LocalBackRight;
        }

        /// <summary>
        /// アニメーション非依存の足4点ワールド座標を取得します。
        /// </summary>
        public void GetRestFeetWorldPositions(out Vector3 fl, out Vector3 fr, out Vector3 bl, out Vector3 br)
        {
            _boneProvider.BoneDetector = boneDetector;
            _boneProvider.TargetTransform = targetTransform;
            _boneProvider.GetRestFeetWorldPositions(out fl, out fr, out bl, out br);
        }

        /// <summary>
        /// シーン内から骨を自動検出します。
        /// </summary>
        public void AutoDetectBones(Transform searchRoot = null)
        {
            _boneProvider.BoneDetector = boneDetector;
            _boneProvider.TargetTransform = targetTransform;
            _boneProvider.FrontLeftFoot   = frontLeftFoot;
            _boneProvider.FrontRightFoot  = frontRightFoot;
            _boneProvider.BackLeftFoot    = backLeftFoot;
            _boneProvider.BackRightFoot   = backRightFoot;

            _boneProvider.AutoDetectBones(this, searchRoot);

            // 検出結果を自身に反映
            boneDetector   = _boneProvider.BoneDetector;
            targetTransform = _boneProvider.TargetTransform;
            frontLeftFoot  = _boneProvider.FrontLeftFoot;
            frontRightFoot = _boneProvider.FrontRightFoot;
            backLeftFoot   = _boneProvider.BackLeftFoot;
            backRightFoot  = _boneProvider.BackRightFoot;
        }

        // --- Internal ---

        private void SyncWithHcdPipeline(in PR_LiftPlaneCalculator.FootPlaneData plane)
        {
            if (!syncWithHcd || HCD_Pipeline.Instance.distanceProcessor == null) return;

            var dp = HCD_Pipeline.Instance.distanceProcessor;
            dp.detectionMode          = HCD_DistanceProcessor.DetectionMode.FootPlane;
            dp.footPlaneTarget        = targetTransform;
            dp.footPlaneBoundsMin     = new Vector3(plane.MinX, 0, plane.MinZ);
            dp.footPlaneBoundsMax     = new Vector3(plane.MaxX, 0, plane.MaxZ);
            dp.footPlaneRefY          = plane.RefY;
            dp.footPlaneDepthThreshold = underPlaneDepthThreshold;
            dp.footPlaneUpperMargin   = upperPlaneMargin;
            dp.footPlaneNormal        = plane.Normal;
        }

        // Editor Gizmos 描画用に接触状態を公開（型を漏洩させない）
        public bool IsContactingGizmo    => _clusterFilter.IsContacting;
        public Vector3 PreviousCentroidGizmo => _clusterFilter.PreviousCentroid;
    }
}
