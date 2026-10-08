using UnityEngine;
using UnityEngine.Serialization;
using System.Collections.Generic;
using Features.Haptics.Debug;
using Features.Haptics.Processors;

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
/// オブジェクトの特定部位（足、尻尾、頭部、関節など）にハプティクス（超音波焦点）を照射するための
/// ターゲット座標データを提供する抽象基底コントローラー。
/// <para>
/// 神クラス化および責務肥大化を防ぐため、以下の責務は専用クラスに委譲されています：
/// <list type="bullet">
/// <item><description>接触・接地判定: <see cref="HAP_TargetContactEvaluator"/></description></item>
/// <item><description>焦点データ生成: <see cref="HAP_ObjectFociGenerator"/></description></item>
/// <item><description>SceneビューGizmo描画: <see cref="HAP_ObjectGizmoDrawer"/></description></item>
/// <item><description>診断ログ生成・出力: <see cref="HAP_ObjectHapticsDiagnostics"/></description></item>
/// <item><description>AppLogManager 連携: <see cref="HAP_LogTriggers"/></description></item>
/// </list>
/// </para>
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
    /// 判定処理は <see cref="HAP_TargetContactEvaluator.GetEffectiveRootTransform"/> に委譲しています。
    /// </summary>
    public virtual Transform? GetEffectiveRootTransform(Transform? fallbackTarget = null)
    {
        return HAP_TargetContactEvaluator.GetEffectiveRootTransform(this, fallbackTarget);
    }

    /// <summary>
    /// 指定されたターゲットが現在ハプティクス照射可能なアクティブ状態であるかどうかを判定します。
    /// 判定処理は <see cref="HAP_TargetContactEvaluator.IsTargetActive"/> に委譲しています。
    /// </summary>
    public bool IsTargetActive(Transform? targetTransform, bool isEnabled, bool isTail)
    {
        return HAP_TargetContactEvaluator.IsTargetActive(this, targetTransform, isEnabled, isTail);
    }

    /// <summary>
    /// Gizmo描画を行うべき状態（drawGizmos, enabled, activeInHierarchy かつ sourceMode == ObjectTarget）であるかを判定します。
    /// 判定処理は <see cref="HAP_ObjectGizmoDrawer.ShouldDrawGizmos"/> に委譲しています。
    /// </summary>
    protected virtual bool ShouldDrawGizmos()
    {
        return HAP_ObjectGizmoDrawer.ShouldDrawGizmos(this);
    }

    /// <summary>
    /// Sceneビューにターゲット位置、接地ライン、手との近接判定球などを描画します。
    /// 描画処理は <see cref="HAP_ObjectGizmoDrawer.Draw"/> に委譲しています。
    /// </summary>
    protected virtual void OnDrawGizmos()
    {
        HAP_ObjectGizmoDrawer.Draw(this);
    }

    /// <summary>
    /// 現在の HCD 判定モード、クラスタ数、各部位の近接接触状態（距離と閾値）、AUTD 送信状態を診断ログとして出力します。
    /// ログ構築および出力は <see cref="HAP_ObjectHapticsDiagnostics.LogDiagnostics"/> に委譲しています。
    /// </summary>
    public virtual void LogDiagnostics()
    {
        HAP_ObjectHapticsDiagnostics.LogDiagnostics(this);
    }
}
