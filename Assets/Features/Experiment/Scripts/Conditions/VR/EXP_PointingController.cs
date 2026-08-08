using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;
using Core.Logging;
using Features.Experiment.Debug;

#nullable enable

/// <summary>
/// Pointing実験のタスクを制御し、物理的なTool（RealSense等の点群）の位置と仮想ターゲットの距離を判定するコントローラ。
/// <para>
/// <b>Tool先端位置の推定思想（ロバストな到達距離推定）:</b><br/>
/// 画像認識や点群処理で「棒の先端座標」を直接検出するのではなく、棒の点群 $P=\{\mathbf{p}_1, \dots, \mathbf{p}_N\}$ 
/// において根元基準位置 $\mathbf{p}_{base}$ からの距離分布 $d_i = \|\mathbf{p}_i - \mathbf{p}_{base}\|$ を求めます。<br/>
/// 点群ノイズや外れ値の影響を避けるため、最遠側5%の点を除外した95パーセンタイル距離 $d_{\mathrm{tip}} = d_{(\lceil 0.95N \rceil)}$
/// または中心軸方向への投影距離の到達度を「実効的なTool先端位置/到達距離」として評価します。
/// </para>
/// </summary>
[AppLoggable("PointingTask")]
public class EXP_PointingController : MonoBehaviour
{
    [Header("Tracking References")]
    [Tooltip("RealSense等から取得した物理Toolの先端位置（Transformアサイン。指定がない場合は点群から自動推定します）")]
    public Transform? toolTip;

    [Header("Robust Tool Tip Estimation (Point Cloud)")]
    [Tooltip("物理Tool先端座標を直接トラッキングする代わりに、点群から95%パーセンタイル距離でロバスト推定するかどうか")]
    public bool useRobustPercentileEstimation = false;

    [Tooltip("棒の根元基準位置のTransform")]
    public Transform? toolBaseTransform;

    [Tooltip("点群ソース（RsPointCloudRendererなどの参照）")]
    public RsPointCloudRenderer? pointCloudRenderer;

    [Tooltip("カットオフパーセンタイル（既定 0.95 = 最遠5%を除外）")]
    [Range(0.5f, 1.0f)]
    public float reachPercentile = 0.95f;

    [Header("Spatial Sector & Trial Accumulation Settings")]
    [Tooltip("試行中に点群バッファを蓄積し、試行完了時に空間分割計算を行うかどうか")]
    public bool useAccumulatedSpatialAnalysis = true;

    [Tooltip("空間分割の方向数（既定 16方向）")]
    public int spatialSectorCount = 16;

    [Tooltip("1試行で蓄積する最大点数（メモリ過負荷防止）")]
    public int maxTrialAccumulatedPoints = 200000;

    [Header("Early Point Cloud Filtering (Culling)")]
    [Tooltip("背景や離れた環境ノイズなどの不要な点群を即座にカリング・破棄するかどうか")]
    public bool enableEarlyCulling = true;

    [Tooltip("Toolの最大想定長さ [m]（これより遠い・根元より後ろの点は破棄）")]
    public float maxToolLength = 0.5f;

    [Tooltip("Tool中心軸からの最大許容半径 [m]（これより離れた周囲の背景・身体点は破棄）")]
    public float maxToolRadius = 0.08f;

    /// <summary>試行開始～終了までに蓄積された全点群座標</summary>
    private readonly List<Vector3> _accumulatedTrialPoints = new List<Vector3>();

    /// <summary>現在点群を蓄積中かどうか</summary>
    private bool _isAccumulating = false;

    private GameObject? currentTargetInstance;
    private EXP_PointingCondition? currentCondition;
    private EXP_TrialData? currentTrial;

    private EXP_ExperimentManager? manager;

    void Awake()
    {
        manager = UnityEngine.Object.FindFirstObjectByType<EXP_ExperimentManager>();
    }

    void Start()
    {
        if (manager != null)
        {
            manager.OnTrialCompleted += HandleTrialCompleted;
            if (manager.inputHandler != null)
            {
                manager.inputHandler.OnResponse += HandleResponse;
            }
        }
        else
        {
            AppLogger.LogError(this, "EXP_ExperimentManager が見つかりません。");
        }
    }

    void OnDestroy()
    {
        if (manager != null)
        {
            manager.OnTrialCompleted -= HandleTrialCompleted;
            if (manager.inputHandler != null)
            {
                manager.inputHandler.OnResponse -= HandleResponse;
            }
        }
    }

    /// <summary>
    /// EXP_PointingCondition から呼び出され、ターゲットの配置を行います。
    /// </summary>
    public IEnumerator ExecutePointingTask(EXP_PointingCondition condition, EXP_TrialData trial)
    {
        currentCondition = condition;
        currentTrial = trial;

        AppLogger.Log(this, $"ExecutePointingTask開始: useOcclusion={condition.useOcclusion}, Placement={condition.placementType}");

        // 仮想ターゲットを生成・配置
        if (condition.virtualTargetPrefab != null)
        {
            currentTargetInstance = Instantiate(condition.virtualTargetPrefab, transform);
            currentTargetInstance.transform.localPosition = condition.targetLocalPosition;
        }
        else
        {
            AppLogger.LogWarning(this, "virtualTargetPrefab が未設定です。");
        }

        // 条件情報をメタデータに記録
        trial.metadata["UseOcclusion"] = condition.useOcclusion.ToString();
        trial.metadata["PlacementType"] = condition.placementType.ToString();
        trial.metadata["TargetLocalX"] = condition.targetLocalPosition.x.ToString("F4");
        trial.metadata["TargetLocalY"] = condition.targetLocalPosition.y.ToString("F4");
        trial.metadata["TargetLocalZ"] = condition.targetLocalPosition.z.ToString("F4");

        // コルーチンとしては待機せず終了し、EXP_TrialRunner側の Response フェーズに移行させます
        yield break; 
    }

    /// <summary>
    /// 参加者が決定ボタンを押した瞬間のみ、点群バッファを取得（GetData）して空間分割（16方向等）ロバスト解析を行って誤差を記録します。
    /// フレーム毎の Update 内では GetData を行わないため、リアルタイム描画のスタック（CPU/GPUストール）を完全に回避します。
    /// </summary>
    private void HandleResponse(string response)
    {
        if (currentCondition == null || currentTrial == null) return;
        if (currentTargetInstance == null)
        {
            AppLogger.LogWarning(this, "ターゲットインスタンスが存在しないため誤差計算をスキップします。");
            return;
        }

        Vector3 pTool;

        // 試行完了時のみ1回だけ点群バッファを取得し、フィルタリングと空間分割解析を実行
        if (useAccumulatedSpatialAnalysis && toolBaseTransform != null)
        {
            Vector3[]? rawVertices = null;

            if (pointCloudRenderer != null)
            {
                rawVertices = pointCloudRenderer.GetFilteredVertices();
            }
            else if (RsGlobalPointCloudManager.Instance != null)
            {
                foreach (var r in RsGlobalPointCloudManager.Instance.GetChildRenderers())
                {
                    if (r != null)
                    {
                        rawVertices = r.GetFilteredVertices();
                        if (rawVertices != null && rawVertices.Length > 0) break;
                    }
                }
            }

            if (rawVertices != null && rawVertices.Length > 0)
            {
                // Culling 後の点群リスト
                List<Vector3> filteredPoints = FilterPoints(rawVertices);

                Vector3 axisDir = toolBaseTransform.forward;
                var sectorResult = Features.Experiment.VR.EXP_ToolReachEstimator.AnalyzeAccumulatedReach(
                    filteredPoints,
                    toolBaseTransform.position,
                    axisDir,
                    spatialSectorCount,
                    reachPercentile
                );

                if (sectorResult.isValid)
                {
                    pTool = sectorResult.overallEstimatedTipPosition;

                    currentTrial.metadata["TotalVerticesRead"] = rawVertices.Length.ToString();
                    currentTrial.metadata["FilteredPoints"] = filteredPoints.Count.ToString();
                    currentTrial.metadata["OverallReachDist"] = sectorResult.overallReachDistance.ToString("F4");
                    currentTrial.metadata["MeanSectorReach"] = sectorResult.meanSectorReach.ToString("F4");
                    currentTrial.metadata["SectorCount"] = sectorResult.sectorCount.ToString();

                    string sectorStr = string.Join(";", System.Array.ConvertAll(sectorResult.sectorReachDistances, d => d.ToString("F4")));
                    currentTrial.metadata["SectorReachDistances"] = sectorStr;

                    AppLogger.Log(this, $"試行完了時単一リードバック解析成功: 有効{filteredPoints.Count}/{rawVertices.Length}点, 95%到達={sectorResult.overallReachDistance:F4}m, {sectorResult.sectorCount}空間分割平均={sectorResult.meanSectorReach:F4}m");
                }
                else
                {
                    if (toolTip == null)
                    {
                        AppLogger.LogWarning(this, "点群・ToolTipのいずれも無効のため誤差計算をスキップします。");
                        return;
                    }
                    pTool = toolTip.position;
                }
            }
            else
            {
                if (toolTip == null)
                {
                    AppLogger.LogWarning(this, "点群が取得できずToolTipも無効のため誤差計算をスキップします。");
                    return;
                }
                pTool = toolTip.position;
            }
        }
        else
        {
            if (toolTip == null)
            {
                AppLogger.LogWarning(this, "ToolTip が存在しないため誤差計算をスキップします。");
                return;
            }
            pTool = toolTip.position;
        }

        // ワールド座標での誤差計算
        Vector3 pTarget = currentTargetInstance.transform.position;

        float eX = Mathf.Abs(pTool.x - pTarget.x);
        float eY = Mathf.Abs(pTool.y - pTarget.y);
        float eZ = Mathf.Abs(pTool.z - pTarget.z);
        float e3D = Vector3.Distance(pTool, pTarget);
        bool isSuccess = e3D < currentCondition.successRadius;

        // 計測結果をメタデータに記録
        currentTrial.metadata["ToolPosX"] = pTool.x.ToString("F4");
        currentTrial.metadata["ToolPosY"] = pTool.y.ToString("F4");
        currentTrial.metadata["ToolPosZ"] = pTool.z.ToString("F4");
        
        currentTrial.metadata["E_3D"] = e3D.ToString("F4");
        currentTrial.metadata["E_X"] = eX.ToString("F4");
        currentTrial.metadata["E_Y"] = eY.ToString("F4");
        currentTrial.metadata["E_Z"] = eZ.ToString("F4");
        currentTrial.metadata["IsSuccess"] = isSuccess.ToString();
        
        AppLogger.Log(this, $"Pointing決定: E_3D={e3D:F4}m, E_Z={eZ:F4}m, Success={isSuccess}");
    }

    /// <summary>
    /// 取得した生の頂点配列から、無効座標や領域外の不要点群（背景・身体ノイズ）を早期フィルタリングします。
    /// </summary>
    private List<Vector3> FilterPoints(Vector3[] rawVertices)
    {
        List<Vector3> filtered = new List<Vector3>(rawVertices.Length);
        Vector3 basePos = toolBaseTransform != null ? toolBaseTransform.position : Vector3.zero;
        Vector3 normAxis = toolBaseTransform != null && toolBaseTransform.forward.sqrMagnitude > 1e-6f 
            ? toolBaseTransform.forward.normalized 
            : Vector3.forward;

        for (int i = 0; i < rawVertices.Length; i++)
        {
            Vector3 p = rawVertices[i];

            // 1. 無効座標（NaN, Zero）の除外
            if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z) || p.sqrMagnitude < 1e-6f)
                continue;

            // 2. 空間フィルタリング (Culling)
            if (enableEarlyCulling && toolBaseTransform != null)
            {
                Vector3 diff = p - basePos;
                float projDist = Vector3.Dot(diff, normAxis);

                // 軸方向の距離チェック（根元より手前・後ろ、または Tool最大長さを超える点は破棄）
                if (projDist < 0f || projDist > maxToolLength)
                    continue;

                // 軸直交方向の半径チェック（Tool中心軸から一定以上離れた周囲の背景・身体点を破棄）
                Vector3 perpComponent = diff - projDist * normAxis;
                if (perpComponent.sqrMagnitude > maxToolRadius * maxToolRadius)
                    continue;
            }

            filtered.Add(p);
        }

        return filtered;
    }

    /// <summary>
    /// 試行終了時の後片付け
    /// </summary>
    private void HandleTrialCompleted(EXP_TrialData trial)
    {
        if (currentTargetInstance != null)
        {
            Destroy(currentTargetInstance);
            currentTargetInstance = null;
        }
        currentCondition = null;
        currentTrial = null;
    }
}
