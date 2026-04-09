using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 全体の点群に適用されるPCA（主成分分析）処理を管理する部分的クラス。
/// 統合されたサンプリング結果から、主軸となる直線と点（重心）を毎フレーム高速に計算します。
/// </summary>
public partial class RsGlobalPointCloudManager
{
    /// <summary>
    /// 全ての点群から計算された統合主軸上の中心点（重心）
    /// </summary>
    private Vector3 _integratedLinePoint = Vector3.zero;

    /// <summary>
    /// 全ての点群から計算された統合主軸の方向ベクトル
    /// </summary>
    private Vector3 _integratedLineDir = Vector3.forward;

    public Vector3 IntegratedLinePoint => _integratedLinePoint;

    public Vector3 IntegratedLineDir => _integratedLineDir;

    /// <summary>
    /// 各カメラ（レンダラー）から毎フレーム取得される最新のサンプリング結果をまとめるリスト
    /// </summary>
    private readonly List<RsSamplingResult> _samplingResults = new List<RsSamplingResult>();

    /// <summary>
    /// 万が一新しい結果が来なかった際に再利用するためのキャッシュ用コンテナ。
    /// （各カメラの非同期での更新などを想定）
    /// </summary>
    private readonly Dictionary<RsPointCloudRenderer, RsSamplingResult> _cachedSamplingResults = 
        new Dictionary<RsPointCloudRenderer, RsSamplingResult>();

    /// <summary>
    /// 統合されたPCA(主成分分析)によって計算される基準の直線（点と方向）を取得します。
    /// 例えばこの基準直線から特定の深度フィルタを適用するなどに使用されます。
    /// </summary>
    /// <returns>point: 手群全体の重心位置、dir: 手群全体の主要な伸びる方向を示すベクトル</returns>
    public (Vector3 point, Vector3 dir) GetLineEstimation()
    {
        return (_integratedLinePoint, _integratedLineDir);
    }

    /// <summary>
    /// 各カメラ（レンダラー）に分散された点群のサンプリング結果を一つに集め、
    /// グローバルなPCAに基づいて全体の直線を推定します。
    /// 
    /// パフォーマンスとキャッシュミスの追跡（デバッグ）の機能も内包しており、
    /// 更新されるたびに各カメラのキャッシュヒット／ミスを集計します。
    /// </summary>
    private void ComputeIntegratedPCA()
    {
        _pcaCallsCounter++;
        _samplingResults.Clear();

        // アクティブなすべての子レンダラーを巡回取得
        foreach (var renderer in GetChildRenderers())
        {
            if (renderer == null) continue;

            // 各レンダラーが毎フレーム計算した「最新のサンプリング結果」を要求する
            if (renderer.TryGetLatestSamplingResult(out var samplingResult))
            {
                _samplingResults.Add(samplingResult);
                
                // 次回のためにキャッシュを更新
                _cachedSamplingResults[renderer] = samplingResult;
            }
            else if (_cachedSamplingResults.TryGetValue(renderer, out var cached) && cached.IsValid)
            {
                // 新しい結果がない場合（計算中やフレームドロップ時）は、
                // 前回キャッシュされた有効なサンプリング結果で代用（補完）する
                _samplingResults.Add(cached);
                _pcaCacheHitsCounter++; // ヒット回数を記録（パフォーマンス監視用）
            }
            else
            {
                // 結果も更新もなく、キャッシュも無効な場合（完全な失敗）
                _pcaCacheMissesCounter++;
            }
        }

        // コレクションにサンプリング可能な十分な結果が蓄積できていれば、
        // 計算用の関数を利用して直線を統合結果から推定（更新）する
        if (_samplingResults.Count > 0)
        {
            // PointCloudを用いた計算ヘルパ（RsPointCloudCompute等）経由で
            // 個別のサンプリングデータを一本の主要なラインへと統合演算
            var (point, dir) = RsPointCloudCompute.EstimateLineFromMergedSamples(_samplingResults);
            
            // 推定された重心点と主成分方向ベクトルを記憶
            _integratedLinePoint = point;
            _integratedLineDir = dir;
        }
    }
}
