using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 点群(手)が球を握り込んでいるかを判定し、握っている間は球を手に追従させる。
/// 握り込み中は、手が球に触れている方向ごとの接触位置を GraspFoci として公開する
/// （MultiAUTD3Controller がこれを複数フォーカスの触覚提示に使う）。
///
/// 判定は ComputeShader(HapCollisionDetectors.compute の AccumulateGrasp)でGPU上で集計し、
/// CPU には集計結果(約44個のint)だけを読み戻す。
///   接触殻  : 球の半径 + grabMargin 以内の点群 → 握り込み判定・接触位置の算出
///   追従領域: 球の半径 + trackMargin 以内の点群 → 手の移動量(重心)の推定
/// 「点数が十分あり」「複数方向から囲まれていて」「一方向に偏っていない」ときに握っているとみなす。
/// 握っていないときは、指などが球にめり込んだ分だけ球を押し出す（指で押して動かせる）。
/// </summary>
public class HapGraspInteractor : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("握り込み集計用 Compute Shader。未指定なら同じGameObjectの HapCollisionDetectors が持つものを使用します")]
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
    public int pushMinPoints = 5;

    [Tooltip("押し出し量(メートル)がこれ未満なら無視する。点群の揺れで球が震えるのを防ぐ")]
    public float pushDeadZone = 0.001f;

    [Tooltip("押し出し量の倍率。1で指が球の表面に当たったまま押し出す。小さいと指が球にめり込む")]
    [Range(0.1f, 1.5f)]
    public float pushGain = 1f;

    [Tooltip("1フレームの最大押し出し量(メートル)")]
    public float maxPushPerFrame = 0.03f;

    [Tooltip("押した速度を引き継いで、はじいた後に少し滑らせる")]
    public bool pushInertia = true;

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
    public float fociGizmoSize = 0.008f;

    /// <summary>現在、球を握り込んでいるか</summary>
    public bool IsGrasped { get; private set; }

    /// <summary>握り込み中の、手が球に触れている各方向の代表位置(ワールド座標)。握っていないときは空</summary>
    public List<Vector3> GraspFoci { get; private set; } = new List<Vector3>();

    /// <summary>接触殻内の点数</summary>
    public int ContactPointCount { get; private set; }

    /// <summary>接触方向の偏り(0=均等, 1=一方向)</summary>
    public float DirectionBias { get; private set; }

    // GraspStats のレイアウト。HapCollisionDetectors.compute と一致させること
    private const int STATS_SIZE = 48;
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

    private Rigidbody _rigidbody;
    private bool _rigidbodyWasKinematic;

    private void Start()
    {
        _shader = graspComputeShader;
        if (_shader == null)
        {
            var detector = GetComponent<HapCollisionDetectors>();
            if (detector != null) _shader = detector.collisionComputeShader;
        }

        if (_shader == null || !_shader.HasKernel("AccumulateGrasp"))
        {
            Debug.LogError("[HapGraspInteractor] AccumulateGrasp カーネルを持つ ComputeShader が見つかりません。", this);
            enabled = false;
            return;
        }

        _kernel = _shader.FindKernel("AccumulateGrasp");
        _statsBuffer = new ComputeBuffer(STATS_SIZE, sizeof(int));
        _rigidbody = GetComponent<Rigidbody>();

        _radius = sphereRadius;
        if (autoSphereRadius)
        {
            var rend = GetComponent<Renderer>();
            if (rend != null)
                _radius = Mathf.Max(rend.bounds.extents.x, rend.bounds.extents.y, rend.bounds.extents.z);
        }
    }

    private void Update()
    {
        if (RsGlobalPointCloudManager.Instance == null) return;

        var globalBuffer = RsGlobalPointCloudManager.Instance.GetGlobalBuffer();
        int pointsCount = RsGlobalPointCloudManager.Instance.CurrentTotalCount;

        Vector3 center = transform.position;

        // 点群が無い間は「何も触れていない」として扱い、握りの解除判定だけ進める
        if (globalBuffer == null || pointsCount == 0)
            System.Array.Clear(_stats, 0, STATS_SIZE);
        else
            GatherStats(globalBuffer, pointsCount, center);

        ContactPointCount = _stats[0];
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
            UpdateFoci(center);
        }
        else
        {
            if (enablePush) ApplyPush();
            ApplyInertia();
        }

        debugStatus = IsGrasped
            ? $"GRASPED (contact={ContactPointCount}, bias={DirectionBias:F2}, foci={GraspFoci.Count})"
            : $"Released (contact={ContactPointCount}, bias={DirectionBias:F2})";
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
            _rigidbodyWasKinematic = _rigidbody.isKinematic;
            _rigidbody.isKinematic = true;
        }
    }

    private void EndGrasp()
    {
        IsGrasped = false;
        GraspFoci.Clear();
        _inertiaVelocity = inheritVelocityOnRelease ? _followVelocity : Vector3.zero;

        if (_rigidbody != null)
            _rigidbody.isKinematic = _rigidbodyWasKinematic;
    }

    private void FollowHand(Vector3 target)
    {
        transform.position = Vector3.SmoothDamp(
            transform.position, target, ref _followVelocity, followSmoothTime, maxFollowSpeed);
    }

    /// <summary>球にめり込んだ点群の平均めり込みベクトルだけ、球を押し出す</summary>
    private void ApplyPush()
    {
        int count = _stats[PUSH_BASE];
        if (count < pushMinPoints) return;

        Vector3 push = new Vector3(_stats[PUSH_BASE + 1], _stats[PUSH_BASE + 2], _stats[PUSH_BASE + 3])
                       / (FIXED_SCALE * count) * pushGain;
        if (push.magnitude < pushDeadZone) return;

        push = Vector3.ClampMagnitude(push, maxPushPerFrame);
        transform.position += push;

        if (pushInertia && Time.deltaTime > 0f)
        {
            Vector3 v = Vector3.ClampMagnitude(push / Time.deltaTime, maxFollowSpeed);
            _inertiaVelocity = Vector3.Lerp(_inertiaVelocity, v, 0.5f);
        }
    }

    private void ApplyInertia()
    {
        if (_inertiaVelocity.sqrMagnitude < 1e-6f) return;

        transform.position += _inertiaVelocity * Time.deltaTime;
        _inertiaVelocity *= Mathf.Exp(-releaseDrag * Time.deltaTime);
    }

    /// <summary>手に覆われているオクタントごとの接触位置の平均(≒手の表面位置)を求める</summary>
    private void UpdateFoci(Vector3 center)
    {
        GraspFoci.Clear();
        for (int o = 0; o < 8; o++)
        {
            int b = OCTANT_BASE + o * 4;
            int count = _stats[b];
            if (count < minPointsPerBin * releaseHysteresis) continue;

            Vector3 mean = new Vector3(_stats[b + 1], _stats[b + 2], _stats[b + 3]) / (FIXED_SCALE * count);
            GraspFoci.Add(center + mean);
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!showDebugGizmo) return;

        float r = Application.isPlaying ? _radius : sphereRadius;
        Gizmos.color = IsGrasped ? new Color(1f, 0.5f, 0f, 0.8f) : new Color(0.3f, 0.6f, 1f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, r + grabMargin);

        Gizmos.color = Color.yellow;
        foreach (var p in GraspFoci)
            Gizmos.DrawSphere(p, fociGizmoSize);
    }
#endif

    private void OnDestroy()
    {
        _statsBuffer?.Release();
        if (IsGrasped && _rigidbody != null)
            _rigidbody.isKinematic = _rigidbodyWasKinematic;
    }
}
