using UnityEngine;
using Core.Logging;

namespace Features.HapticsCollision.Interaction
{
    /// <summary>
    /// 点群(手)が球を握り込んでいるかを判定し、握っている間は球を手に追従させる。
    /// 触覚の提示は HCD_Pipeline(接触クラスタ) → HAP_AUTDHapticsController が行うため、
    /// このコンポーネントは「球をどう動かすか」だけを担当する。
    ///
    /// 判定は ComputeShader(HCD_Grasp.compute の AccumulateGrasp)でGPU上で集計し、
    /// CPU には集計結果(約48個のint)だけを読み戻す。
    ///   接触殻  : 球の半径 + grabMargin 以内の点群 → 握り込み判定
    ///   追従領域: 球の半径 + trackMargin 以内の点群 → 手の移動量(重心)の推定
    /// 「点数が十分あり」「複数方向から囲まれていて」「一方向に偏っていない」ときに握っているとみなす。
    /// 握っていないときは、指などが球にめり込んだ分だけ球を押し出す（指で押して動かせる）。
    /// </summary>
    [AppLoggable("HCD (Haptic Collision)")]
    public class HCD_GraspInteractor : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("握り込み集計用 Compute Shader (HCD_Grasp.compute)")]
        public ComputeShader graspComputeShader;

        [Tooltip("ONの場合、起動時に Renderer の大きさから球の半径を求めます")]
        public bool autoSphereRadius = true;

        [Tooltip("球の半径(メートル)。autoSphereRadius がOFFのときに使用")]
        public float sphereRadius = 0.05f;

        [Tooltip("握り込み判定に使う接触殻の厚み(メートル)。球の表面からこの距離以内の点を「触れている」とみなす")]
        public float grabMargin = 0.02f;

        [Tooltip("手の移動量を推定する領域の厚み(メートル)。大きいほど追従が安定するが、腕など手以外の点も含みやすい")]
        public float trackMargin = 0.06f;

        [Header("Grab Conditions")]
        [Tooltip("握り込みとみなす接触殻内の最小点数")]
        public int grabMinPoints = 200;

        [Tooltip("方向(オクタント)を「手に覆われている」とみなす最小点数")]
        public int minPointsPerBin = 20;

        [Tooltip("手に覆われている必要があるオクタント数(8分割中)。多いほど「しっかり握り込んだ」ときだけ反応する")]
        [Range(1, 8)]
        public int grabMinBins = 3;

        [Tooltip("接触方向の偏りの上限(0=全方向から均等, 1=完全に一方向)。手のひらを当てただけの状態を弾く")]
        [Range(0.1f, 1f)]
        public float grabMaxDirectionBias = 0.75f;

        [Tooltip("握っている間の解除側のしきい値の緩さ。小さいほど握りが緩んでも保持される(チャタリング防止)")]
        [Range(0.1f, 1f)]
        public float releaseHysteresis = 0.5f;

        [Tooltip("条件を満たさなくなってから実際に離すまでの猶予時間(秒)")]
        public float releaseDelay = 0.15f;

        [Header("Push (指で押す)")]
        [Tooltip("握っていないとき、点群が球にめり込んだ分だけ球を押し出す")]
        public bool enablePush = true;

        [Tooltip("球の表面からこの距離(メートル)手前から押し始める。0なら実際にめり込んでから。grabMargin を超えない")]
        public float pushMargin = 0.005f;

        [Tooltip("押し出しに必要な最小点数。点群のノイズ対策")]
        public int pushMinPoints = 3;

        [Tooltip("押し出し量(メートル)がこれ未満なら無視する。点群の揺れで球が震えるのを防ぐ")]
        public float pushDeadZone = 0.0005f;

        [Tooltip("押し出し量の倍率。1で指が球の表面に当たったまま押し出す。小さいと指が球にめり込む")]
        [Range(0.1f, 1.5f)]
        public float pushGain = 1f;

        [Tooltip("1フレームの最大押し出し量(メートル)")]
        public float maxPushPerFrame = 0.03f;

        [Tooltip("押した速度を引き継いで、はじいた後に少し滑らせる")]
        public bool pushInertia = true;

        [Tooltip("押し込みがこの時間(秒)続くまで、球を動かさない。指が軽く触れただけで球が逃げて、握れなくなるのを防ぐ")]
        public float pushEngageDelay = 0.08f;

        [Tooltip("手で球を囲み始めている(複数方向から触れている)間は押し出しを止め、その場に保持する。握る前に球が逃げるのを防ぐ")]
        public bool holdWhileWrapping = true;

        [Tooltip("囲み始めとみなす接触殻内の最小点数。握り込みの最小点数(Grab Min Points)より小さくする")]
        public int wrapMinPoints = 30;

        [Tooltip("囲み始めとみなす接触方向の偏りの上限。指1本だけだと偏りは1に近く、複数方向から触れると小さくなる")]
        [Range(0.1f, 1f)]
        public float wrapMaxDirectionBias = 0.85f;

        [Header("Physics (転がす)")]
        [Tooltip("Rigidbody があれば物理演算で動かす。床(コライダー)に置くと転がる。握っている間だけ Kinematic になる")]
        public bool usePhysics = true;

        [Tooltip("握り・押し・慣性で球を動かすとき、コライダー(床など)にぶつかったらそこで止める。床のすり抜け防止")]
        public bool blockByColliders = true;

        [Header("Follow")]
        [Tooltip("手への追従の滑らかさ(秒)。小さいほどキビキビ、大きいほどなめらか")]
        public float followSmoothTime = 0.04f;

        [Tooltip("追従時の最大速度(m/s)。点群のノイズで球が飛ぶのを防ぐ")]
        public float maxFollowSpeed = 3f;

        [Tooltip("追従に必要な追従領域内の最小点数。これを下回る間は球を動かさない")]
        public int trackMinPoints = 50;

        [Tooltip("離したときに手の速度を引き継いで慣性で動かす")]
        public bool inheritVelocityOnRelease = true;

        [Tooltip("離した後の減速の強さ(1/秒)")]
        public float releaseDrag = 6f;

        [Header("Debug")]
        public string debugStatus = "Released";
        public bool showDebugGizmo = true;

        /// <summary>現在、球を握り込んでいるか</summary>
        public bool IsGrasped { get; private set; }

        /// <summary>接触殻内の点数</summary>
        public int ContactPointCount { get; private set; }

        /// <summary>接触方向の偏り(0=均等, 1=一方向)</summary>
        public float DirectionBias { get; private set; }

        /// <summary>押し出し領域内の点数</summary>
        public int PushPointCount { get; private set; }

        /// <summary>押し出し領域内の、深い点ほど重みを大きくした平均めり込み量(メートル)</summary>
        public float PushDepth { get; private set; }

        public const string TagGrasp = "HCD_GraspInteractor";

        // GraspStats のレイアウト。HCD_Grasp.compute と一致させること
        private const int STATS_SIZE = 52;
        private const int OCTANT_BASE = 11;
        private const int PUSH_BASE = 44;
        private const float FIXED_SCALE = 1000f;

        private ComputeShader _shader;
        private int _kernel = -1;
        private ComputeBuffer _statsBuffer;
        private readonly int[] _stats = new int[STATS_SIZE];
        private readonly int[] _zeroStats = new int[STATS_SIZE];

        private float _radius;
        private Vector3 _anchorOffset;
        private Vector3 _followVelocity;
        private Vector3 _inertiaVelocity;
        private float _releaseTimer;
        private float _pushTimer;

        private readonly RaycastHit[] _sweepHits = new RaycastHit[8];

        private Rigidbody _rigidbody;
        private bool _rigidbodyWasKinematic;

        private void Start()
        {
            _shader = graspComputeShader;
            if (_shader == null || !_shader.HasKernel("AccumulateGrasp"))
            {
                AppLogger.LogError(this, TagGrasp, "AccumulateGrasp カーネルを持つ ComputeShader (HCD_Grasp.compute) を設定してください。");
                enabled = false;
                return;
            }

            _kernel = _shader.FindKernel("AccumulateGrasp");
            _statsBuffer = new ComputeBuffer(STATS_SIZE, sizeof(int));
            _rigidbody = GetComponent<Rigidbody>();

            _radius = sphereRadius;
            if (autoSphereRadius)
                _radius = MeasureRadius(_radius);

            // Update(フレーム単位)で動かす位置が物理の更新周期とずれても滑らかに見えるようにする
            if (usePhysics && _rigidbody != null)
                _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        }

        /// <summary>SphereCollider があればその大きさ、なければ Renderer の大きさから半径を求める</summary>
        private float MeasureRadius(float fallback)
        {
            var sc = GetComponent<SphereCollider>();
            if (sc != null)
            {
                Vector3 s = transform.lossyScale;
                return sc.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
            }

            var rend = GetComponent<Renderer>();
            if (rend != null)
                return Mathf.Max(rend.bounds.extents.x, rend.bounds.extents.y, rend.bounds.extents.z);
            return fallback;
        }

        /// <summary>手が複数方向から球に触れ始めているか(握り込みの直前)。この間は球を押さず、その場に保つ</summary>
        private bool IsWrapping =>
            holdWhileWrapping && ContactPointCount >= wrapMinPoints && DirectionBias <= wrapMaxDirectionBias;

        /// <summary>握る前に球が逃げたり落ちたりしないよう、速度を止める</summary>
        private void HoldStill()
        {
            _pushTimer = 0f;
            _inertiaVelocity = Vector3.zero;
            if (UsesPhysics)
                _rigidbody.linearVelocity = Vector3.zero;
        }

        /// <summary>物理演算(動的なRigidbody)で動かしているか。握っている間はKinematicなので false</summary>
        private bool UsesPhysics => usePhysics && _rigidbody != null && !_rigidbody.isKinematic;

        private void Update()
        {
            if (RsGlobalPointCloudManager.Instance == null)
            {
                debugStatus = "RsGlobalPointCloudManager がシーンにありません";
                return;
            }

            var globalBuffer = RsGlobalPointCloudManager.Instance.GetGlobalBuffer();
            int pointsCount = RsGlobalPointCloudManager.Instance.CurrentTotalCount;

            Vector3 center = transform.position;

            // 点群が無い間は「何も触れていない」として扱い、握りの解除判定だけ進める
            if (globalBuffer == null || pointsCount == 0)
                System.Array.Clear(_stats, 0, STATS_SIZE);
            else
                GatherStats(globalBuffer, pointsCount, center);

            ContactPointCount = _stats[0];
            PushPointCount = _stats[PUSH_BASE];
            PushDepth = _stats[PUSH_BASE + 4] > 0
                ? new Vector3(_stats[PUSH_BASE + 5], _stats[PUSH_BASE + 6], _stats[PUSH_BASE + 7]).magnitude
                  / (_stats[PUSH_BASE + 4] * FIXED_SCALE)
                : 0f;
            DirectionBias = ContactPointCount > 0
                ? new Vector3(_stats[4], _stats[5], _stats[6]).magnitude / (FIXED_SCALE * ContactPointCount)
                : 1f;

            bool trackValid = _stats[7] >= trackMinPoints;
            Vector3 trackCentroid = center + new Vector3(_stats[8], _stats[9], _stats[10]) / (FIXED_SCALE * Mathf.Max(_stats[7], 1));

            bool meets = MeetsGraspConditions(IsGrasped);

            if (meets)
            {
                _releaseTimer = 0f;
                if (!IsGrasped && trackValid) BeginGrasp(center, trackCentroid);
            }
            else if (IsGrasped)
            {
                _releaseTimer += Time.deltaTime;
                if (_releaseTimer >= releaseDelay) EndGrasp();
            }

            if (IsGrasped)
            {
                if (meets && trackValid)
                    FollowHand(trackCentroid + _anchorOffset);
            }
            else
            {
                if (enablePush)
                {
                    if (IsWrapping)
                        HoldStill();
                    else
                        ApplyPush();
                }
                ApplyInertia();
            }

            string state = IsGrasped ? "GRASPED" : (IsWrapping ? "Wrapping(hold)" : "Released");
            string body = _rigidbody == null ? "rb=なし" : (_rigidbody.isKinematic ? "rb=Kinematic" : "rb=Dynamic");
            debugStatus = $"{state} (cloud={pointsCount}, contact={ContactPointCount}, " +
                          $"bias={DirectionBias:F2}, pushPts={PushPointCount}, pushDepth={PushDepth * 1000f:F1}mm, {body})";
        }

        private void GatherStats(ComputeBuffer globalBuffer, int pointsCount, Vector3 center)
        {
            _statsBuffer.SetData(_zeroStats);

            float graspRadius = _radius + grabMargin;
            float trackRadius = Mathf.Max(_radius + trackMargin, graspRadius);

            _shader.SetBuffer(_kernel, "PointCloudBuffer", globalBuffer);
            _shader.SetBuffer(_kernel, "GraspStats", _statsBuffer);
            _shader.SetInt("PointsCount", pointsCount);
            _shader.SetVector("GraspCenter", center);
            _shader.SetFloat("GraspRadiusSqr", graspRadius * graspRadius);
            _shader.SetFloat("GraspTrackRadiusSqr", trackRadius * trackRadius);
            _shader.SetFloat("GraspPushRadius", _radius + Mathf.Clamp(pushMargin, 0f, grabMargin));

            _shader.Dispatch(_kernel, Mathf.CeilToInt(pointsCount / 256.0f), 1, 1);
            _statsBuffer.GetData(_stats);
        }

        /// <summary>握り込みの成立条件。握っている最中は解除側のしきい値を緩めてチャタリングを防ぐ</summary>
        private bool MeetsGraspConditions(bool alreadyGrasped)
        {
            float k = alreadyGrasped ? releaseHysteresis : 1f;

            if (_stats[0] < grabMinPoints * k) return false;

            float biasLimit = alreadyGrasped ? Mathf.Min(1f, grabMaxDirectionBias + 0.15f) : grabMaxDirectionBias;
            if (DirectionBias > biasLimit) return false;

            int needBins = alreadyGrasped ? Mathf.Max(1, grabMinBins - 1) : grabMinBins;
            int bins = 0;
            for (int o = 0; o < 8; o++)
                if (_stats[OCTANT_BASE + o * 4] >= minPointsPerBin * k) bins++;

            return bins >= needBins;
        }

        private void BeginGrasp(Vector3 center, Vector3 trackCentroid)
        {
            IsGrasped = true;
            _releaseTimer = 0f;
            _anchorOffset = center - trackCentroid;
            _followVelocity = Vector3.zero;
            _inertiaVelocity = Vector3.zero;

            if (_rigidbody != null)
            {
                // Kinematic の Rigidbody に速度を入れると警告になるため、先に止めてから切り替える
                if (UsesPhysics)
                {
                    _rigidbody.linearVelocity = Vector3.zero;
                    _rigidbody.angularVelocity = Vector3.zero;
                }
                _rigidbodyWasKinematic = _rigidbody.isKinematic;
                _rigidbody.isKinematic = true;
            }
        }

        private void EndGrasp()
        {
            IsGrasped = false;
            _inertiaVelocity = inheritVelocityOnRelease ? _followVelocity : Vector3.zero;

            if (_rigidbody != null)
            {
                _rigidbody.isKinematic = _rigidbodyWasKinematic;

                // 物理で動かす場合は、手の速度をそのまま Rigidbody に渡して投げる/転がす
                if (UsesPhysics)
                {
                    _rigidbody.linearVelocity = _inertiaVelocity;
                    _inertiaVelocity = Vector3.zero;
                }
            }
        }

        private void FollowHand(Vector3 target)
        {
            Vector3 next = Vector3.SmoothDamp(
                transform.position, target, ref _followVelocity, followSmoothTime, maxFollowSpeed);
            transform.position += ClampByCollision(next - transform.position);
        }

        /// <summary>
        /// 移動量 delta を、球形のスイープ判定でコライダーにぶつかる手前までに制限する。
        /// 位置を直接書き換えると物理のすり抜け防止が効かないため、動かす前にここで止める。
        /// 自分自身のコライダーと、すでに重なっているコライダー(距離0)は無視する。
        /// </summary>
        private Vector3 ClampByCollision(Vector3 delta)
        {
            float dist = delta.magnitude;
            if (!blockByColliders || dist < 1e-5f) return delta;

            Vector3 dir = delta / dist;
            int n = Physics.SphereCastNonAlloc(transform.position, _radius * 0.98f, dir, _sweepHits,
                dist + 0.001f, ~0, QueryTriggerInteraction.Ignore);

            float nearest = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var hit = _sweepHits[i];
                if (hit.distance <= 0f) continue;
                if (hit.collider.transform == transform || hit.collider.transform.IsChildOf(transform)) continue;
                if (hit.distance < nearest) nearest = hit.distance;
            }

            if (nearest == float.MaxValue) return delta;
            return dir * Mathf.Max(0f, Mathf.Min(dist, nearest - 0.001f));
        }

        /// <summary>球にめり込んだ点群の平均めり込みベクトルだけ、球を押し出す</summary>
        private void ApplyPush()
        {
            int count = _stats[PUSH_BASE];
            if (count < pushMinPoints || _stats[PUSH_BASE + 4] <= 0)
            {
                _pushTimer = 0f;
                return;
            }

            // 深い点ほど重みを大きくした平均。浅く触れている点に薄められて、指先の押し込みが小さくなるのを防ぐ
            Vector3 push = new Vector3(_stats[PUSH_BASE + 5], _stats[PUSH_BASE + 6], _stats[PUSH_BASE + 7])
                           / (_stats[PUSH_BASE + 4] * FIXED_SCALE) * pushGain;
            if (push.magnitude < pushDeadZone)
            {
                _pushTimer = 0f;
                return;
            }

            // 押し込みが一定時間続くまで動かさない(軽く触れただけで球が逃げて、握れなくなるのを防ぐ)
            _pushTimer += Time.deltaTime;
            if (_pushTimer < pushEngageDelay) return;

            push = ClampByCollision(Vector3.ClampMagnitude(push, maxPushPerFrame));
            transform.position += push;

            if (UsesPhysics)
            {
                // 押した方向の速度を、押した速さまで引き上げる（逆向きや横向きの速度はそのまま残す）
                if (Time.deltaTime > 0f)
                {
                    Vector3 dir = push.normalized;
                    float want = Mathf.Min(push.magnitude / Time.deltaTime, maxFollowSpeed);
                    float have = Vector3.Dot(_rigidbody.linearVelocity, dir);
                    if (have < want)
                        _rigidbody.linearVelocity += dir * (want - have);
                }
                return;
            }

            if (pushInertia && Time.deltaTime > 0f)
            {
                Vector3 v = Vector3.ClampMagnitude(push / Time.deltaTime, maxFollowSpeed);
                _inertiaVelocity = Vector3.Lerp(_inertiaVelocity, v, 0.5f);
            }
        }

        private void ApplyInertia()
        {
            if (UsesPhysics) return; // 物理演算に任せる
            if (_inertiaVelocity.sqrMagnitude < 1e-6f) return;

            transform.position += ClampByCollision(_inertiaVelocity * Time.deltaTime);
            _inertiaVelocity *= Mathf.Exp(-releaseDrag * Time.deltaTime);
        }

#if UNITY_EDITOR
        [ContextMenu("Setup Physics (Rigidbody + SphereCollider + Floor)")]
        private void SetupPhysics()
        {
            float r = autoSphereRadius ? MeasureRadius(sphereRadius) : sphereRadius;
            Vector3 s = transform.lossyScale;
            float maxScale = Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));

            var sc = GetComponent<SphereCollider>();
            if (sc == null)
            {
                sc = UnityEditor.Undo.AddComponent<SphereCollider>(gameObject);
                sc.radius = r / Mathf.Max(maxScale, 1e-6f);
            }

            var rb = GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = UnityEditor.Undo.AddComponent<Rigidbody>(gameObject);
                rb.mass = 0.1f;
                rb.linearDamping = 0.3f;
                rb.angularDamping = 0.3f;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
            }

            var existingFloor = GameObject.Find("HapFloor");
            if (existingFloor == null)
            {
                // 厚みのある Cube(BoxCollider)。薄い Plane(MeshCollider)は片面で、下に入ると当たらなくなるため使わない。
                // 上面が球の底に一致するように置く。AR 映像に床が映り込まないよう、見た目(MeshRenderer)は消す
                const float thickness = 0.1f;
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                floor.name = "HapFloor";
                floor.transform.position = transform.position + Vector3.down * (r + thickness * 0.5f);
                floor.transform.localScale = new Vector3(0.5f, thickness, 0.5f);
                floor.GetComponent<MeshRenderer>().enabled = false;
                floor.isStatic = true;
                UnityEditor.Undo.RegisterCreatedObjectUndo(floor, "Create HapFloor");
            }
            else
            {
                // 旧版が作った Plane(MeshCollider) の床は、厚みのある BoxCollider に置き換える
                var meshCollider = existingFloor.GetComponent<MeshCollider>();
                if (meshCollider != null)
                {
                    UnityEditor.Undo.DestroyObjectImmediate(meshCollider);
                    var box = UnityEditor.Undo.AddComponent<BoxCollider>(existingFloor);
                    box.size = new Vector3(10f, 1f, 10f);   // Plane は 10x10。厚み 1 を下方向に持たせる
                    box.center = new Vector3(0f, -0.5f, 0f);
                }
            }

            AppLogger.Log(this, TagGrasp, "Rigidbody / SphereCollider / HapFloor を設定しました。床の位置・大きさは手に合わせて調整してください。");
        }

        private void OnDrawGizmos()
        {
            if (!showDebugGizmo) return;

            float r = Application.isPlaying ? _radius : sphereRadius;
            Gizmos.color = IsGrasped ? new Color(1f, 0.5f, 0f, 0.8f) : new Color(0.3f, 0.6f, 1f, 0.4f);
            Gizmos.DrawWireSphere(transform.position, r + grabMargin);

        }
#endif

        private void OnDestroy()
        {
            _statsBuffer?.Release();
            if (IsGrasped && _rigidbody != null)
                _rigidbody.isKinematic = _rigidbodyWasKinematic;
        }
    }
}
