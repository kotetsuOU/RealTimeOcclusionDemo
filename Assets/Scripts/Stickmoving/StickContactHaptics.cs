using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>
/// 刷り込み（priming）タスクの触覚側。
/// CylinderMover が動かす stick と手のひらの接触を Collider で検知し、
/// 接触点から手の厚み分オフセットした「手の甲側」の座標へ AUTD の静止フォーカスを出す。
///
/// 接触が続いている間は、接触開始時に取得した座標で出力を出し続ける方針
/// （接触点に毎フレーム追従はしない）。
///
/// stick 側の GameObject にアタッチし、Collider を付けておくこと。
/// </summary>
[RequireComponent(typeof(Collider))]
public class StickContactHaptics : MonoBehaviour
{
    /// <summary>手の甲側へのオフセット方向の決め方</summary>
    private enum OffsetMode
    {
        ContactNormal,          // 接触点の法線の逆向き（Collision 経由でのみ取得可）
        FixedWorldDirection     // ワールド固定方向（掌が上向きなら Vector3.down）
    }

    [Header("参照")]
    [Tooltip("出力先の AUTD コントローラ")]
    [SerializeField] private MultiAUTD3Controller autd;

    [Tooltip("視覚側の CylinderMover。待機中（stick 非表示中）の接触を無視するために参照する")]
    [SerializeField] private CylinderMover mover;

    [Header("接触判定")]
    [Tooltip("手のひら側の Layer。ここに含まれる Collider との接触だけを扱う")]
    [SerializeField] private LayerMask palmLayers = ~0;

    [Tooltip("stick が非表示（待機中）の間の接触を無視する。endZ が手の上にある場合の鳴りっぱなし対策")]
    [SerializeField] private bool ignoreWhileHidden = true;

    [Header("手の甲側オフセット")]
    [Tooltip("オフセット方向の決め方")]
    [SerializeField] private OffsetMode offsetMode = OffsetMode.ContactNormal;

    [Tooltip("手の厚み。接触点からこの距離だけ甲側にずらす [m]")]
    [SerializeField] private float handThickness = 0.03f;

    [Tooltip("FixedWorldDirection 時、および法線が取れない時に使うオフセット方向（ワールド座標）")]
    [SerializeField] private Vector3 fallbackOffsetDirection = Vector3.down;

    [Tooltip("接触法線から求めた向きが fallbackOffsetDirection と逆を向いていたら反転する（法線の向きが不定なメッシュ対策）")]
    [SerializeField] private bool alignOffsetWithFallback = true;

    [Header("AUTD 出力")]
    [Tooltip("どのデバイスグループから出すか。手の甲が下を向く配置なら DownOnly")]
    [SerializeField] private MultiAUTD3Controller.OutputSide outputSide = MultiAUTD3Controller.OutputSide.DownOnly;

    [Tooltip("出力強度。-1 ならコントローラ側の設定値を使う")]
    [Range(-1, 255)] [SerializeField] private int intensity = -1;

    [Header("デバウンス")]
    [Tooltip("Exit してからこの秒数以内に再 Enter したら同一接触として扱い、出力を切らない。0 で無効")]
    [SerializeField] private float debounceSeconds = 0f;

    [Header("ログ")]
    [Tooltip("接触・送信タイムスタンプを CSV に記録する")]
    [SerializeField] private bool writeCsv = true;
    [SerializeField] private string subjectID = "S01";

    [Header("Debug")]
    [Tooltip("実際に送信している焦点位置を表示するマーカー")]
    [SerializeField] private Transform focusMarker;
    [SerializeField] private bool verboseLog = true;

    private Collider activeCollider;        // 現在接触中とみなしている相手
    private bool isContacting = false;
    private bool exitPending = false;       // デバウンス待ち
    private float exitPendingTimer = 0f;

    private Vector3 lastContactPoint;
    private Vector3 lastContactNormal;
    private Vector3 lastFocusPoint;

    private int eventIndex = 0;
    private StreamWriter writer;

    void Start()
    {
        if (autd == null) autd = FindAnyObjectByType<MultiAUTD3Controller>();
        if (autd == null)
            Debug.LogWarning("[StickContactHaptics] MultiAUTD3Controller が見つかりません。触覚出力は行われません。");

        if (writeCsv) OpenCsv();
    }

    void OnDestroy()
    {
        CloseCsv();
    }

    void OnApplicationQuit()
    {
        // 接触したままアプリを落としても出力が残らないようにする
        if (isContacting || exitPending) autd?.ClearExternalFocus();
        CloseCsv();
    }

    void OnDisable()
    {
        if (isContacting || exitPending)
        {
            autd?.ClearExternalFocus();
            isContacting = false;
            exitPending = false;
            activeCollider = null;
        }
    }

    void Update()
    {
        // 待機中（stick 非表示）に接触が残っていたら強制的に切る
        if (ignoreWhileHidden && isContacting && mover != null && !mover.IsMoving)
        {
            EndContact("hidden");
            return;
        }

        // デバウンス待ちの消化
        if (exitPending)
        {
            exitPendingTimer -= Time.deltaTime;
            if (exitPendingTimer <= 0f) EndContact("exit");
        }
    }

    // ------------------------------------------------------------------
    // 接触検知（3.4-1）
    // Collider が isTrigger かどうかの両方に対応する。
    // Collision 経由なら接触点と法線が取れるので精度が高い。
    // ------------------------------------------------------------------

    void OnCollisionEnter(Collision collision)
    {
        if (!IsTargetCollider(collision.collider)) return;
        if (collision.contactCount == 0) return;

        var contact = collision.GetContact(0);
        BeginContact(collision.collider, contact.point, contact.normal, true);
    }

    void OnCollisionExit(Collision collision)
    {
        if (collision.collider != activeCollider) return;
        RequestExit();
    }

    void OnTriggerEnter(Collider other)
    {
        if (!IsTargetCollider(other)) return;

        // Trigger では接触点・法線が取れないので、最近傍点とフォールバック方向で代用する
        Vector3 point = ClosestPointOrSelf(other, transform.position);
        BeginContact(other, point, Vector3.zero, false);
    }

    void OnTriggerExit(Collider other)
    {
        if (other != activeCollider) return;
        RequestExit();
    }

    private bool IsTargetCollider(Collider other)
    {
        if (other == null) return false;
        if ((palmLayers.value & (1 << other.gameObject.layer)) == 0) return false;
        if (ignoreWhileHidden && mover != null && !mover.IsMoving) return false;
        return true;
    }

    private static Vector3 ClosestPointOrSelf(Collider other, Vector3 from)
    {
        // Collider.ClosestPoint は凸でない MeshCollider では from をそのまま返す仕様
        return other.ClosestPoint(from);
    }

    // ------------------------------------------------------------------
    // 接触開始 → オフセット適用（3.4-2） → AUTD 出力（3.4-4）
    // ------------------------------------------------------------------

    private void BeginContact(Collider other, Vector3 contactPoint, Vector3 contactNormal, bool hasNormal)
    {
        // デバウンス中の再接触なら、出力を切らずに継続扱いにする
        if (exitPending && other == activeCollider)
        {
            exitPending = false;
            if (verboseLog) Debug.Log($"[StickContactHaptics] debounce: 再接触のため継続 ({other.name})");
            return;
        }

        if (isContacting) return;   // 既に別の接触を処理中

        double contactTime = Time.realtimeSinceStartupAsDouble;

        activeCollider = other;
        isContacting = true;
        exitPending = false;

        lastContactPoint = contactPoint;
        lastContactNormal = hasNormal ? contactNormal : Vector3.zero;
        lastFocusPoint = ApplyBackOfHandOffset(contactPoint, contactNormal, hasNormal);

        bool sent = false;
        if (autd != null)
        {
            int? amp = (intensity >= 0) ? intensity : (int?)null;
            sent = autd.SendFocusAtWorld(lastFocusPoint, outputSide, amp);
        }

        double sendTime = Time.realtimeSinceStartupAsDouble;

        if (focusMarker != null) focusMarker.position = lastFocusPoint;

        WriteEvent("ContactEnter", contactTime, sendTime, sent, other.name);

        if (verboseLog)
            Debug.Log($"[StickContactHaptics] Enter {other.name} " +
                      $"contact={Fmt(lastContactPoint)} focus={Fmt(lastFocusPoint)} " +
                      $"sent={sent} latency={(sendTime - contactTime) * 1000.0:F2}ms");
    }

    /// <summary>接触点（手のひら側）から手の厚み分ずらして、手の甲側の座標を求める。</summary>
    private Vector3 ApplyBackOfHandOffset(Vector3 contactPoint, Vector3 contactNormal, bool hasNormal)
    {
        Vector3 fallback = fallbackOffsetDirection.sqrMagnitude > 1e-8f
            ? fallbackOffsetDirection.normalized
            : Vector3.down;

        Vector3 dir;
        if (offsetMode == OffsetMode.ContactNormal && hasNormal && contactNormal.sqrMagnitude > 1e-8f)
        {
            // 法線は手のひら表面から外向き。甲側はその逆向き
            dir = -contactNormal.normalized;

            // メッシュによっては法線の向きが安定しないため、明らかに逆を向いていたら反転する
            if (alignOffsetWithFallback && Vector3.Dot(dir, fallback) < 0f) dir = -dir;
        }
        else
        {
            dir = fallback;
        }

        return contactPoint + dir * handThickness;
    }

    // ------------------------------------------------------------------
    // 出力停止（3.4-5）
    // ------------------------------------------------------------------

    private void RequestExit()
    {
        if (!isContacting) return;

        if (debounceSeconds > 0f)
        {
            exitPending = true;
            exitPendingTimer = debounceSeconds;
            return;
        }

        EndContact("exit");
    }

    private void EndContact(string reason)
    {
        if (!isContacting) return;

        double contactTime = Time.realtimeSinceStartupAsDouble;

        autd?.ClearExternalFocus();

        double sendTime = Time.realtimeSinceStartupAsDouble;

        string otherName = activeCollider != null ? activeCollider.name : "NA";

        isContacting = false;
        exitPending = false;
        activeCollider = null;

        WriteEvent(reason == "hidden" ? "ContactExitHidden" : "ContactExit",
                   contactTime, sendTime, autd != null, otherName);

        if (verboseLog)
            Debug.Log($"[StickContactHaptics] Exit ({reason}) {otherName} " +
                      $"latency={(sendTime - contactTime) * 1000.0:F2}ms");
    }

    // ------------------------------------------------------------------
    // ログ（3.4-6）CrossmodalTask.cs のロギングパターンに合わせる
    // ------------------------------------------------------------------

    private void OpenCsv()
    {
        string stamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string path = Path.Combine(Application.persistentDataPath, $"{subjectID}_priming_{stamp}.csv");

        writer = new StreamWriter(path, false);
        writer.WriteLine("subjectID,eventIndex,passIndex,event,contactTime,sendTime,sendLatency_ms,sent," +
                         "contactX,contactY,contactZ,focusX,focusY,focusZ,normalX,normalY,normalZ,otherCollider");
        writer.Flush();

        Debug.Log($"[StickContactHaptics] CSV: {path}");
    }

    private void CloseCsv()
    {
        if (writer == null) return;
        writer.Close();
        writer = null;
    }

    private void WriteEvent(string eventName, double contactTime, double sendTime, bool sent, string otherName)
    {
        int index = eventIndex++;
        if (writer == null) return;

        int pass = (mover != null) ? mover.PassIndex : -1;
        var c = CultureInfo.InvariantCulture;

        writer.WriteLine(string.Join(",",
            subjectID,
            index.ToString(c),
            pass.ToString(c),
            eventName,
            contactTime.ToString("F6", c),
            sendTime.ToString("F6", c),
            ((sendTime - contactTime) * 1000.0).ToString("F3", c),
            sent ? "1" : "0",
            lastContactPoint.x.ToString("F5", c), lastContactPoint.y.ToString("F5", c), lastContactPoint.z.ToString("F5", c),
            lastFocusPoint.x.ToString("F5", c), lastFocusPoint.y.ToString("F5", c), lastFocusPoint.z.ToString("F5", c),
            lastContactNormal.x.ToString("F4", c), lastContactNormal.y.ToString("F4", c), lastContactNormal.z.ToString("F4", c),
            otherName));
        writer.Flush();
    }

    private static string Fmt(Vector3 v) => $"({v.x:F4}, {v.y:F4}, {v.z:F4})";

    void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || !isContacting) return;

        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(lastContactPoint, 0.005f);
        Gizmos.color = Color.magenta;
        Gizmos.DrawSphere(lastFocusPoint, 0.005f);
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(lastContactPoint, lastFocusPoint);
    }
}
