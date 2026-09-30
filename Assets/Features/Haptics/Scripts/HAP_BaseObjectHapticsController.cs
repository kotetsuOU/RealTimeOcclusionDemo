using UnityEngine;
using UnityEngine.Serialization;
using System.Collections.Generic;

#nullable enable

public enum HapticsTrackMode
{
    Simultaneous,
    Sequential
}

public enum HapticsSTMMode
{
    FociSTM,
    GainSTM
}

/// <summary>
/// ハプティクス照射ターゲットの位置と設定を定義する構造体。
/// </summary>
public struct HapticsTargetInfo
{
    public string Name;
    public Transform Transform;
    public bool IsEnabled;
    public bool IsTail; // 接地判定 (disableWhenInAir) を行わない特殊部位判定
    public Vector3 Offset; // ターゲット位置オフセット
    public Vector3 TouchDirection; // 照射・接触向き（メッシュに向かうベクトル）
}

/// <summary>
/// オブジェクトの特定部位（足、尻尾、関節など）にハプティクス（超音波焦点）を照射するための
/// ターゲット座標データを提供する抽象基底クラス。
/// </summary>
public abstract class HAP_BaseObjectHapticsController : MonoBehaviour
{
    [Header("Dependencies")]
    [Tooltip("超音波を制御する HAP_AUTDHapticsController の参照。未指定の場合はシーン内から自動取得します。")]
    public HAP_AUTDHapticsController? autdController;

    [Header("Animation State Settings")]
    [Tooltip("有効時、対象が空中に浮いているとき（ジャンプ中など）は触覚をオフにします。")]
    public bool disableWhenInAir = false;
    [Tooltip("接地判定を行うための、ルート位置からの高さのしきい値（メートル）。")]
    public float airborneHeightThreshold = 0.05f;
    [Tooltip("接地の基準となるキャラクターのルートTransform。未指定の場合は本GameObjectのTransformを使用します。")]
    public Transform? rootTransform;

    [Header("Hand Contact Settings")]
    [Tooltip("有効時、HCD_Pipelineで検出された手の点群（クラスタ）がターゲットの近くにある時のみ照射します。")]
    public bool onlyTargetHandContact = false;
    [Tooltip("手との接触と判定する距離のしきい値（メートル）。")]
    public float handContactThreshold = 0.1f;

    [FormerlySerializedAs("footTargetNormal")]
    [Tooltip("バーチャルオブジェクト（足・尻尾）が接触対象（地面や手）へ触れる/押し当てる向き（デフォルト: Vector3.down）。\n最適デバイス判定（方向グルーピング）の基準として使用されます。")]
    public Vector3 footTargetTouchDirection = Vector3.down;

    [Header("Custom Mode Settings")]
    [Tooltip("STMの種類を選択。FociSTM(ハードウェア計算・単焦点)、GainSTM(CPU計算・GSPAT等の複数焦点に対応)")]
    public HapticsSTMMode stmMode = HapticsSTMMode.FociSTM;

    [Tooltip("ハードウェアSTMを用いた高速シーケンシャル照射時の周波数（Hz）。")]
    public float sequentialSTMFrequency = 150f;

    [Tooltip("照射ターゲットの追跡・照射モード。\nSimultaneous: すべて同時に狙う（複数焦点）。\nSequential: 1つずつ順次切り替える（単焦点）。")]
    public HapticsTrackMode trackMode = HapticsTrackMode.Sequential;

    [Header("Debug Visualization")]
    [Tooltip("Sceneビュー上にターゲットの位置を示すGizmoを描画します。")]
    public bool drawGizmos = true;
    [Tooltip("照射対象となっているターゲットのGizmo色。")]
    public Color activeColor = Color.green;
    [Tooltip("照射対象から外れている（非アクティブまたは非接地）ターゲットのGizmo色。")]
    public Color inactiveColor = Color.red;

    /// <summary>
    /// 実験条件クラスによる刺激提示の一時抑制フラグ。
    /// true の場合、このコントローラーからのハプティクス出力が抑制されます。
    /// <para>
    /// <see cref="HAP_AUTDHapticsController.bypassHaptics"/> とは独立して動作します。
    /// bypassHaptics は SourceMode 切り替え等のグローバル制御用、
    /// こちらは実験フロー中の刺激 ON/OFF 制御専用です。
    /// </para>
    /// </summary>
    [HideInInspector]
    [System.NonSerialized]
    public bool experimentStimulusSuppressed = false;

    /// <summary>
    /// 各コントローラーが持つ照射部位（足、尻尾など）のターゲット一覧を返します。
    /// </summary>
    public abstract List<HapticsTargetInfo> TargetInfos { get; }

    /// <summary>
    /// 現在有効な照射ターゲットが1つ以上あるかどうかを返します。
    /// </summary>
    public virtual bool HasActiveTargets()
    {
        foreach (var info in TargetInfos)
        {
            if (IsTargetActive(info.Transform, info.IsEnabled, info.IsTail))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// ハプティクス照射用のターゲット座標リスト (ClusterFociData) を生成して返します。
    /// TargetInfos からアクティブなターゲットを収集し、STMおよび追跡モードに応じた焦点データを HAP_ObjectFociGenerator 経由で組み立てます。
    /// </summary>
    public virtual List<HAP_FociGenerator.ClusterFociData> GetHapticsTargets(float defaultIntensityPascal, Vector3 offset)
    {
        return HAP_ObjectFociGenerator.Generate(this, defaultIntensityPascal, offset);
    }

    /// <summary>
    /// STMの照射モード。
    /// </summary>
    public virtual HapticsSTMMode STMMode => stmMode;

    /// <summary>
    /// 照射ターゲットの追跡・照射モード。
    /// </summary>
    public virtual HapticsTrackMode TrackMode => trackMode;

    /// <summary>
    /// 接地判定（空中判定）の基準となる有効なキャラクターのルートTransformを取得します。
    /// rootTransform が未設定、非アクティブ、または自身のTransform（コントローラー単体配置時）の場合は、
    /// ターゲット階層（Animator / SkinnedMeshRenderer / ルート）から自動解決して同期します。
    /// </summary>
    public virtual Transform? GetEffectiveRootTransform(Transform? fallbackTarget = null)
    {
        if (rootTransform != null && rootTransform != this.transform && rootTransform.gameObject.activeInHierarchy)
        {
            return rootTransform;
        }

        // fallbackTarget から親方向へモデルのルート（Animator または SkinnedMeshRenderer を持つ階層）を探索
        if (fallbackTarget != null && fallbackTarget.gameObject.activeInHierarchy)
        {
            var animator = fallbackTarget.GetComponentInParent<Animator>();
            if (animator != null && animator.gameObject.activeInHierarchy)
            {
                rootTransform = animator.transform;
                return rootTransform;
            }
            var smr = fallbackTarget.GetComponentInParent<SkinnedMeshRenderer>();
            if (smr != null && smr.gameObject.activeInHierarchy)
            {
                rootTransform = smr.transform;
                return rootTransform;
            }
            if (fallbackTarget.root != null && fallbackTarget.root != this.transform.root && fallbackTarget.root.gameObject.activeInHierarchy)
            {
                rootTransform = fallbackTarget.root;
                return rootTransform;
            }
        }

        return rootTransform != null ? rootTransform : this.transform;
    }

    /// <summary>
    /// 指定されたターゲットが現在ハプティクス照射可能なアクティブ状態であるかどうかを返します。
    /// </summary>
    public bool IsTargetActive(Transform? targetTransform, bool isEnabled, bool isTail)
    {
        if (targetTransform == null) return false;
        if (!isEnabled) return false;

        // 接地判定（空中判定。足などの非Tailパーツのみ適用）
        if (!isTail && disableWhenInAir)
        {
            Transform? effectiveRoot = GetEffectiveRootTransform(targetTransform);
            if (effectiveRoot != null)
            {
                float relHeight = targetTransform.position.y - effectiveRoot.position.y;
                if (relHeight > airborneHeightThreshold) return false;
            }
        }

        // 手との近接接触判定
        if (onlyTargetHandContact)
        {
            bool hasContact = false;
            HCD_Pipeline? pipeline = (autdController != null) ? autdController.hcdPipeline : null;
            if (pipeline == null) pipeline = FindAnyObjectByType<HCD_Pipeline>();

            if (pipeline != null)
            {
                var clusters = pipeline.GetTrackedClusters();
                if (clusters != null)
                {
                    foreach (var c in clusters)
                    {
                        if (c.IsAlive && Vector3.Distance(c.Centroid, targetTransform.position) <= handContactThreshold)
                        {
                            hasContact = true;
                            break;
                        }
                    }
                }
            }
            if (!hasContact) return false;
        }

        return true;
    }

    /// <summary>
    /// Gizmo描画を行うべき状態（drawGizmos, enabled, activeInHierarchy かつ sourceMode == ObjectTarget）であるかを判定します。
    /// </summary>
    protected virtual bool ShouldDrawGizmos()
    {
        if (!drawGizmos || !enabled || !gameObject.activeInHierarchy) return false;

        HAP_AUTDHapticsController? ctrl = autdController;
#if UNITY_EDITOR
        if (ctrl == null)
        {
            ctrl = FindAnyObjectByType<HAP_AUTDHapticsController>();
        }
#endif
        if (ctrl != null && ctrl.sourceMode != HapticsSourceMode.ObjectTarget)
        {
            return false;
        }

        return true;
    }

    protected virtual void OnDrawGizmos()
    {
        if (!ShouldDrawGizmos()) return;

        foreach (var info in TargetInfos)
        {
            DrawTargetGizmo(info);
        }
    }

    protected virtual void DrawTargetGizmo(HapticsTargetInfo info)
    {
        if (info.Transform == null) return;

        Vector3 pos = info.Transform.position + info.Offset;
        bool isEnabled = info.IsEnabled;
        bool active = IsTargetActive(info.Transform, isEnabled, info.IsTail);
        bool isGrounded = true;

        Transform? effectiveRoot = GetEffectiveRootTransform(info.Transform);

        if (!info.IsTail && disableWhenInAir && effectiveRoot != null)
        {
            float relHeight = pos.y - effectiveRoot.position.y;
            if (relHeight > airborneHeightThreshold)
            {
                isGrounded = false;
                active = false;
            }
        }

        if (onlyTargetHandContact)
        {
            bool hasContact = false;
            HCD_Pipeline? pipeline = (autdController != null) ? autdController.hcdPipeline : null;
            if (pipeline == null) pipeline = FindAnyObjectByType<HCD_Pipeline>();

            if (pipeline != null)
            {
                var clusters = pipeline.GetTrackedClusters();
                if (clusters != null)
                {
                    foreach (var c in clusters)
                    {
                        if (c.IsAlive && Vector3.Distance(c.Centroid, pos) <= handContactThreshold)
                        {
                            hasContact = true;
                            break;
                        }
                    }
                }
            }
            if (!hasContact)
            {
                active = false;
            }
        }

        Color baseColor = isEnabled ? activeColor : inactiveColor;

        // 1. 接地判定（高さ判定）の可視化線を描画 (足のみ適用)
        if (!info.IsTail && disableWhenInAir && effectiveRoot != null)
        {
            Vector3 groundPt = new Vector3(pos.x, effectiveRoot.position.y, pos.z);
            Vector3 threshPt = new Vector3(pos.x, effectiveRoot.position.y + airborneHeightThreshold, pos.z);

            // 許容高さしきい値を示す小さな十字を描画
            Gizmos.color = baseColor;
            Gizmos.DrawLine(threshPt - Vector3.left * 0.01f, threshPt + Vector3.left * 0.01f);
            Gizmos.DrawLine(threshPt - Vector3.forward * 0.01f, threshPt + Vector3.forward * 0.01f);

            if (isGrounded)
            {
                Gizmos.color = baseColor;
                Gizmos.DrawLine(pos, groundPt);
            }
            else
            {
                Gizmos.color = baseColor;
                Gizmos.DrawLine(threshPt, groundPt);

                Gizmos.color = Color.red;
                Gizmos.DrawLine(pos, threshPt);
            }
        }

        // 手の接触判定の可視化
        if (onlyTargetHandContact)
        {
            HCD_Pipeline? pipeline = (autdController != null) ? autdController.hcdPipeline : null;
            if (pipeline == null) pipeline = FindAnyObjectByType<HCD_Pipeline>();

            if (pipeline != null)
            {
                var clusters = pipeline.GetTrackedClusters();
                if (clusters != null)
                {
                    foreach (var c in clusters)
                    {
                        if (c.IsAlive && Vector3.Distance(c.Centroid, pos) <= handContactThreshold)
                        {
                            Gizmos.color = activeColor;
                            Gizmos.DrawLine(pos, c.Centroid);
                            Gizmos.DrawWireSphere(c.Centroid, 0.01f);
                            break;
                        }
                    }
                }
            }
        }

        // 2. 照射ターゲットの描画
        if (active)
        {
            Gizmos.color = activeColor;
            Gizmos.DrawSphere(pos, 0.01f);
        }
        else
        {
            Gizmos.color = baseColor;
            Gizmos.DrawWireSphere(pos, 0.01f);
        }
    }
}
