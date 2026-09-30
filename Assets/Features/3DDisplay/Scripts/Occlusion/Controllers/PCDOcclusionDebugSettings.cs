using System;
using UnityEngine;

/// <summary>
/// PCD オクルージョンパイプラインのデバッグおよびキャプチャ関連パラメータを統括する調整クラス。
/// インスペクタ上での折りたたみ表示および責務分離を目的とします。
/// </summary>
[Serializable]
public class PCDOcclusionDebugSettings
{
    [Header("Display Debug")]
    [Tooltip("点群(黒)と静的メッシュ(白)の由来を示すデバッグマップ(PixelTagMap)を画面に表示します")]
    public bool enablePixelTagMap = false;

    [Tooltip("内積計算で得た occlusionAverage(0~1) を、Record Occlusion Debug Map と同じ配色ルールで画面上に常時表示します")]
    public bool enableOcclusionMap = false;

    [Tooltip("256占有パターンの単独二値マスク表示 (-1: 通常描画, 0..255: 指定パターンの二値マスク表示)")]
    [Range(-1, 255)]
    public int debugPatternId = -1;

    [Tooltip("セクターマスク表示 (-1: 通常描画, 0..7: 個別セクター二値マスク, 8: 全8セクター統合8bit占有マスク, 9: GPU実判定マスク)")]
    [Range(-1, 9)]
    public int debugSectorId = -1;

    [Header("Record Debug (Single Frame Capture)")]
    [Tooltip("1フレームだけOcclusionMapを保存します（occlusionAverageをPNG/CSVへ出力）")]
    public bool recordOcclusionDebugMap = false;

    [Tooltip("1フレームだけPixelTagMap(由来情報の生値)を記録します")]
    public bool recordPixelTagMap = false;

    [Tooltip("1フレームだけ統合DepthMapを記録します")]
    public bool recordIntegratedDepthMap = false;

    [Tooltip("1フレームだけNeighborhoodMapを記録します")]
    public bool recordNeighborhoodMap = false;

    [Tooltip("1フレームだけNeighborCountMapを記録します")]
    public bool recordNeighborCountMap = false;

    [Header("Log Settings")]
    [Tooltip("PCDPointBufferManager (点群バッファ更新) のログ出力を有効にする")]
    public bool enableBufferManagerLog = false;

    /// <summary>
    /// キャプチャ系フラグをすべてリセットします。
    /// </summary>
    public void ResetRecordFlags()
    {
        recordOcclusionDebugMap = false;
        recordPixelTagMap = false;
        recordIntegratedDepthMap = false;
        recordNeighborhoodMap = false;
        recordNeighborCountMap = false;
    }

    /// <summary>
    /// 全てのデバッグマップを1フレーム記録するフラグをセットします。
    /// </summary>
    public void RequestRecordAll()
    {
        recordOcclusionDebugMap = true;
        recordPixelTagMap = true;
        recordIntegratedDepthMap = true;
        recordNeighborhoodMap = true;
        recordNeighborCountMap = true;
    }
}
