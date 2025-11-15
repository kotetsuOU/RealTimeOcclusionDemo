using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 複数の RSPC_FocusCentroidAggregator の結果を集計し、
/// 最終的なグローバル重心を計算します。
/// 
/// 計算された重心に基づき、焦点オブジェクト(focusTransform)の
/// 反対側に目的位置(targetPosition)を設定します。
/// ただし、焦点からの最大変位(maxDisplacement)を超えないように制限します。
/// </summary>
public class RSPC_GlobalCentroidAggregator : MonoBehaviour
{
    [Header("Dependencies")]
    [Tooltip("集計対象の Calculator のリスト")]
    [SerializeField]
    private List<RSPC_FocusCentroidCalculator> calculators;

    [Header("Target Object Control")]
    [Tooltip("重心の動きに応じて移動させたいオブジェクト")]
    [SerializeField]
    private Transform targetObjectToMove;

    [Tooltip("重心計算の基準点（焦点）となるオブジェクトの Transform")]
    [SerializeField]
    private Transform focusTransform; // ※ RSPC_FocusCentroidCalculator と同じものを設定

    [Tooltip("オブジェクトが移動する最大速度 (メートル/秒)")]
    [SerializeField]
    private float maxSpeedPerSecond = 0.1f;

    [Tooltip("焦点(FocusTransform)の位置から移動できる最大の距離 (メートル)")]
    [SerializeField]
    private float maxDisplacement = 0.1f; // ★ 新しく追加

    [Header("Logging Settings")]
    [Tooltip("ログ出力の間隔（秒）")]
    [SerializeField]
    private float logInterval = 1.0f;
    private float _logTimer = 0f;

    void Start()
    {
        if (calculators == null || calculators.Count == 0)
        {
            UnityEngine.Debug.LogError("RSPC_GlobalCentroidAggregator: 'Calculators' リストが設定されていません。", this);
            this.enabled = false;
        }

        if (targetObjectToMove == null)
        {
            UnityEngine.Debug.LogWarning("RSPC_GlobalCentroidAggregator: 'Target Object To Move' が設定されていません。差分移動は実行されません。", this);
        }

        if (focusTransform == null)
        {
            UnityEngine.Debug.LogError("RSPC_GlobalCentroidAggregator: 'Focus Transform' が設定されていません。移動ロジックが機能しません。", this);
            this.enabled = false;
        }

        if (maxSpeedPerSecond <= 0)
        {
            UnityEngine.Debug.LogWarning("RSPC_GlobalCentroidAggregator: 'Max Speed Per Second' は 0 より大きい値にしてください。速度制限を無効にします。", this);
            maxSpeedPerSecond = float.PositiveInfinity;
        }
    }

    void LateUpdate()
    {
        if (calculators == null || calculators.Count == 0 || focusTransform == null)
        {
            return;
        }

        // --- 1. 全ての Calculator から結果を集計 ---
        Vector3 globalSum = Vector3.zero;
        int globalCount = 0;

        foreach (var calculator in calculators)
        {
            if (calculator != null && calculator.enabled)
            {
                globalSum += calculator.TotalSum;
                globalCount += calculator.TotalCount;
            }
        }

        // --- 2. 目的位置の計算 ---
        Vector3 targetPosition;
        Vector3 focusPos = focusTransform.position;

        if (globalCount > 0)
        {
            // A. 現在の重心を計算
            Vector3 currentGlobalCentroid = globalSum / globalCount;

            // B. 焦点から重心とは反対側への「反発ベクトル」を計算
            //    (焦点から重心へのベクトル) = currentGlobalCentroid - focusPos
            //    (反発ベクトル) = focusPos - currentGlobalCentroid
            Vector3 repelVector = focusPos - currentGlobalCentroid;

            // C. ★ 反発ベクトルの大きさを maxDisplacement (0.1m) に制限（Clamp）する
            //    これにより、移動先が焦点から 0.1m より離れることを防ぐ
            Vector3 clampedRepelVector = Vector3.ClampMagnitude(repelVector, maxDisplacement);

            // D. 最終的な目的位置を決定
            //    (焦点の位置 + 制限された反発ベクトル)
            targetPosition = focusPos + clampedRepelVector;

            // --- 3. ログ出力（一定間隔） ---
            LogMovement(globalCount, currentGlobalCentroid);
        }
        else
        {
            // --- 点群が検出されなかった場合 ---
            // A. 目的位置を焦点の位置（デフォルト位置）に戻す
            targetPosition = focusPos;

            // ログ出力
            LogMovement(0, Vector3.zero);
        }


        // --- 4. オブジェクトを「滑らかに」移動 ---
        if (targetObjectToMove != null)
        {
            // このフレームで許容される最大の移動距離
            float maxFrameDistance = maxSpeedPerSecond * Time.deltaTime;

            // 現在位置から目的位置(targetPosition)へ、最大速度を超えないように移動
            targetObjectToMove.position = Vector3.MoveTowards(
                targetObjectToMove.position,
                targetPosition,
                maxFrameDistance
            );
        }
    }

    /// <summary>
    /// ログ出力（タイマー制御）
    /// </summary>
    private void LogMovement(int count, Vector3 centroid)
    {
        _logTimer += Time.deltaTime;
        if (_logTimer < logInterval)
        {
            return;
        }
        _logTimer = 0f;

        if (count > 0)
        {
            UnityEngine.Debug.Log($"[Global Centroid] 合計 {count} 個の点を発見。グローバル重心: {centroid:F4}");
        }
        else
        {
            UnityEngine.Debug.Log("[Global Centroid] 焦点の半径内に点群は検出されませんでした。");
        }
    }
}