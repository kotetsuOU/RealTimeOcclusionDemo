#ifndef PCD_OCCLUSION_KERNELS_OCCLUSION_INCLUDED
#define PCD_OCCLUSION_KERNELS_OCCLUSION_INCLUDED

#include "PCD_Occlusion_Kernels_Occlusion_SingleDirection.hlsl"

[numthreads(8, 8, 1)]
void ComputeOcclusion(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint) _ScreenParams.x || id.y >= (uint) _ScreenParams.y)
        return;

    uint2 fullResUV = id.xy;
    uint originType = _OriginTypeMap_RW[fullResUV];
    float4 currentPos = _ViewPositionMap[fullResUV];

    bool useTagOptimization = (_EnableTagBasedOptimization > 0);

    // ==========================================
    // 1. 動的演算スキップ (ジオメトリバッファ利用)
    // ==========================================
    // 対象が物理点群(0u)または背景(2u)なら演算をスキップ
    // 【新規性要素】仮想オブジェクト(1u)の境界領域のみに計算を限定する
    if (useTagOptimization && (originType == 0u || originType == 2u))
    {
        if (_RecordOcclusionDebug > 0)
        {
            _OcclusionValueMap_RW[fullResUV] = (originType == 0u) ? float2(-3.0, 0.0) : float2(-1.0, 0.0);
            _NeighborCountMap_RW[fullResUV] = 0u;
        }

        _OcclusionResultMap_RW[fullResUV] = _ColorMap[fullResUV];
        if (originType == 0u)
            _OriginMap_RW[fullResUV] = float4(0, 0, 0, 1);
        return;
    }

    // !useTagOptimizationの場合、背景はオクルージョン判定のため仮想的な遠方に配置
    if (!useTagOptimization && originType == 2u)
    {
        // currentPos はすでに_ViewPositionMapからの値(wが1e9など)かもしれないので、
        // 適切なビュー空間方向に設定し直す。_ViewPositionMapのw=1e9のピクセルでも、
        // x,y,zにクリップ空間やUVから逆算したレイベクトルが格納されていればそれを使えるが、
        // 現在の構成では100m奥に押し込む処理を行う
        float3 ray = (currentPos.w >= 1e9 && length(currentPos.xyz) > 0.001) ? currentPos.xyz : float3(0, 0, 1);
        currentPos = float4(normalize(ray) * 100.0, 100.0);
    }

    // 対象が仮想オブジェクト(1u)の場合のみ以下を実行
    if (currentPos.w >= 1e9)
    {
        _OcclusionResultMap_RW[fullResUV] = _ColorMap[fullResUV];
        return;
    }

    // 事前計算
    float3 x = currentPos.xyz;
    float len_x = length(x);
    float currentPosSq = dot(x, x);
    float invCurrentPosSq = 1.0 / max(currentPosSq, 0.0001);

    int level = _FinalNeighborhoodSizeMap[fullResUV];

    // 8方向のサンプリングオフセット（幾何学的円周順: 0=上, 1=右上, 2=右, 3=右下, 4=下, 5=左下, 6=左, 7=左上）
    const int2 sectorOffsets[8] = {
        int2( 0, -1), // 0: North
        int2( 1, -1), // 1: North-East
        int2( 1,  0), // 2: East
        int2( 1,  1), // 3: South-East
        int2( 0,  1), // 4: South
        int2(-1,  1), // 5: South-West
        int2(-1,  0), // 6: West
        int2(-1, -1)  // 7: North-West
    };

    float occlusionSum = 0.0;
    uint validSectorCount = 0u;
    uint binaryOccludedCount = 0u;
    uint occupiedSectorMask = 0u;
    float softOccludedCount = 0.0;

    // ==========================================
    // 2. 共通ピラミッドサンプリングと遮蔽評価
    // ==========================================
    // ピラミッドは構築時フィルタリング(Step3)により物理点群(0u)のみで構成されている。
    // 深度プリチェックで近傍がカメラ側にある場合のみ遮蔽を評価する。
    [unroll]
    for (int s = 0; s < 8; ++s)
    {
        float4 neighborPos = FetchPyramidPosition(level, fullResUV, sectorOffsets[s]);

        // 深度プリチェック: センチネル値を排除し、近傍（物理点群）が仮想オブジェクトより手前にある場合のみ評価
        // .w は NDC深度（0=近, 1=遠）でクリップ空間変換チェーンから得られるため常に正確。
        // 仮想オブジェクト(奥)は .w が大きく、物理点群(手前)は .w が小さい → 差は正。
        // 【旧バグ: 閾値 0.01 は非線形NDC空間で ≈ 1m地点の10cm相当と大きすぎた（40mmで失敗）】
        // 【修正: 1e-5 (≈ 1m地点の0.1mm相当) に縮小し小距離差も検出可能にした】
        // .z（ビュー空間z）は _ViewMatrix の渡し方（行列転置の差異等）で正負が変わりうるため使用しない。
        if (neighborPos.w < 1e9 && (currentPos.w - neighborPos.w) > 1e-5)
        {
            // TagOptimizationがONの場合、ピラミッド(Level>=1)は既に物理点群(0u)のみにフィルタ済。
            // しかし、Level 0 の場合はフル解像度の_ViewPositionMapから直接取得するため、
            // 仮想オブジェクト(1u)などが混ざっている。ここで確実に除外する。
            if (useTagOptimization && level == 0)
            {
                uint2 nUV = clamp(fullResUV + sectorOffsets[s], 0, _ScreenParams.xy - 1);
                if (_OriginTypeMap_RW[nUV] != 0u)
                    continue; // 物理点群以外はオクルーダーとして扱わない
            }

            float3 y = neighborPos.xyz;
            float occlusionValue = 0.0;

            // ==========================================
            // 3. カーネルごとの関数適用
            // ==========================================
            if (_KernelType == 0) // Pintus Operator (UI表記: Bouchiba)
            {
                // Pintus式: dot(normalize(y-x), normalize(-x))
                // 対象点自身の視点からの遮蔽を純粋に評価する。
                // ジオメトリバッファによる異種レイヤー分離により自己遮蔽は原理的に不発生のため、
                // Bouchibaの -y 補正は不要。
                float3 y_minus_x = y - x;
                float len_y_minus_x = length(y_minus_x);

                if (len_y_minus_x > 0.0001 && len_x > 0.0001)
                {
                    // dot() の結果はコサイン値で理論上 [-1,1] だが、
                    // 点群が仮想オブジェクトのすぐ手前に来ると正規化誤差で範囲外になる場合がある。
                    // saturate でクランプして occlusionValue が 1 を超えないよう保護する。
                    float dotP = saturate(dot((y_minus_x / len_y_minus_x), (-x / len_x)));
                    float val = 1.0 - dotP;
                    occlusionValue = val > 0.0 ? max(1e-7, val) : 0.0;
                }
            }
            else // Exponential (1) or Linear (2) or DepthOnly (4)
            {
                // 既存の関数をそのまま流用し、遮蔽度を計算
                occlusionValue = ComputeOcclusionValue_SingleDirection(x, currentPosSq, invCurrentPosSq, y);
            }

            // どのカーネルでも計算誤差等で [0,1] を超えた場合に備えてクランプ
            occlusionValue = saturate(occlusionValue);

            occlusionSum += occlusionValue;
            validSectorCount++;

            // Accumulate sector threshold metrics (支持点が局所条件を満たすとき b_k(x) = 1, それ以外は 0)
            if (occlusionValue < _OcclusionThreshold)
            {
                binaryOccludedCount++; // N_occ(x)
                occupiedSectorMask |= (1u << s); // b_k(x) = 1 (占有セクタ)
            }
            if (_EnableSoftOcclusionFade > 0 && _OcclusionFadeWidth > 1e-4)
            {
                float halfFade = _OcclusionFadeWidth * 0.5;
                float fadeStart = max(0.0, _OcclusionThreshold - halfFade);
                float fadeEnd = min(2.0, _OcclusionThreshold + halfFade);
                float sectorAlpha = smoothstep(fadeStart, fadeEnd, occlusionValue);
                softOccludedCount += (1.0 - sectorAlpha);
            }
        }
    }

    // 遮蔽度の評価と平均値の算出
    float avgOcclusion = 1.0;
    float alpha = 1.0;

    // 円環上の最大連続非占有セクタ数 L_max(x) を計算 (0: 非占有セクタ, 1: 占有セクタ)
    // ※ どの評価モードでもデバッグ記録および分析用に共通で算出
    uint L_max = 0u;
    uint currentZeros = 0u;
    [unroll]
    for (int ci = 0; ci < 16; ++ci)
    {
        if (((occupiedSectorMask >> (ci % 8)) & 1u) == 0u)
        {
            currentZeros++;
            if (currentZeros > L_max) L_max = currentZeros;
        }
        else
        {
            currentZeros = 0u;
        }
    }
    if (occupiedSectorMask == 0u) L_max = 8u;
    else if (occupiedSectorMask == 0xFFu) L_max = 0u;
    else L_max = min(L_max, 8u);

    if (_EvaluationMode == 0) // Average Mode
    {
        // 空セクタ（手前に遮蔽点群が存在しないセクタ）に先行研究 (Bouchiba / Pintus) 通り F_max = 2.0 を代入
        // validSectorCount: 手前に点群が存在したセクタ数 (0〜8)
        // (8u - validSectorCount): 空セクタ数
        float occlusionSumWithEmpty2 = occlusionSum + (float)(8u - validSectorCount) * 2.0;
        avgOcclusion = occlusionSumWithEmpty2 / 8.0;

        if (_EnableSoftOcclusionFade > 0 && _OcclusionFadeWidth > 1e-4)
        {
            float halfFade = _OcclusionFadeWidth * 0.5;
            float fadeStart = max(0.0, _OcclusionThreshold - halfFade);
            float fadeEnd = min(2.0, _OcclusionThreshold + halfFade);
            alpha = smoothstep(fadeStart, fadeEnd, avgOcclusion);
        }
        else
        {
            if (avgOcclusion < _OcclusionThreshold)
                alpha = 0.0;
        }
    }
    else if (_EvaluationMode == 1) // SectorThreshold Mode (Each Mode: 占有数のみ)
    {
        if (_EnableSoftOcclusionFade > 0 && _OcclusionFadeWidth > 1e-4)
        {
            float countFadeStart = max(0.0, (float)_MinOccludedSectors - 1.0);
            float countFadeEnd = (float)_MinOccludedSectors;
            alpha = 1.0 - smoothstep(countFadeStart, countFadeEnd, softOccludedCount);
            avgOcclusion = 1.0 - (softOccludedCount / 8.0);
        }
        else
        {
            if (binaryOccludedCount >= (uint)_MinOccludedSectors)
                alpha = 0.0;
            avgOcclusion = 1.0 - ((float)binaryOccludedCount / 8.0);
        }
    }
    else // SectorConsecutiveZeros Mode (新手法: 占有数 N_occ + 最大連続非占有セクタ数 L_max 判定)
    {
        // 遮蔽条件: N_occ(x) >= R_th  and  L_max(x) <= L_th
        // R_th: _MinOccludedSectors (最低占有セクタ数)
        // L_th: _MaxConsecutiveEmptySectors (許容最大連続非占有セクタ数, 0〜8)
        // ※ L_th = 8 (K) の設定は、全画素で L_max <= 8 が成立するため占有数のみの判定と等価。
        bool passesDirectionCondition = (L_max <= (uint)_MaxConsecutiveEmptySectors);

        if (_EnableSoftOcclusionFade > 0 && _OcclusionFadeWidth > 1e-4)
        {
            float countFadeStart = max(0.0, (float)_MinOccludedSectors - 1.0);
            float countFadeEnd = (float)_MinOccludedSectors;
            float baseAlpha = 1.0 - smoothstep(countFadeStart, countFadeEnd, softOccludedCount);
            alpha = passesDirectionCondition ? baseAlpha : 1.0;
            avgOcclusion = 1.0 - (softOccludedCount / 8.0);
        }
        else
        {
            if (binaryOccludedCount >= (uint)_MinOccludedSectors && passesDirectionCondition)
                alpha = 0.0;
            avgOcclusion = 1.0 - ((float)binaryOccludedCount / 8.0);
        }
    }

    if (alpha <= 0.0)
    {
        if (_EnableJointBilateralHoleFilling > 0)
        {
            _OcclusionResultMap_RW[fullResUV] = float4(0, 0, 0, 1.0);
        }
        else
        {
            _OcclusionResultMap_RW[fullResUV] = float4(0, 0, 0, 1.0);
            _OriginTypeMap_RW[fullResUV] = 0u;
        }
    }
    else
    {
        float4 col = _ColorMap[fullResUV];
        col.a *= alpha;
        _OcclusionResultMap_RW[fullResUV] = col;
        _OriginMap_RW[fullResUV] = float4(1, 1, 1, 1);
    }

    // デバッグ情報の記録
    if (_RecordOcclusionDebug > 0)
    {
        float debugLabel = saturate(alpha);
        if (originType == 0u) debugLabel = -3.0; // 実点群は緑
        else if (originType == 2u) debugLabel = -1.0; // 背景は白

        // DebugMap の生値記録 (.y):
        // avgOcclusion は空セクタに 2.0 を代入したため [0.0, 2.0] の範囲を取りうる。
        // パレットおよび特殊ラベル(1.9以上=マゼンタ)との衝突を防ぎ、従来の配色(0=遮蔽/濃紺, 1=非遮蔽/赤)を
        // 完全に維持するため、デバッグ表示用の値は上限 1.0 にクランプして記録する。
        float debugAvgOcclusion = min(avgOcclusion, 1.0);

        _OcclusionValueMap_RW[fullResUV] = float2(debugLabel, debugAvgOcclusion);

        // 占有マスクとセクタ詳細のパッキング記録 (R32_UInt)
        // bit 0..7   (8bit): 実測占有ビットマスク occupiedSectorMask (0〜255)
        // bit 8..11  (4bit): 点群支持セクタ数 validSectorCount (0〜8)
        // bit 12     (1bit): セクタ評価対象フラグ isEvaluated (1: 仮想オブジェクト画素, 0: 未評価・背景)
        // bit 13     (1bit): GPU生判定結果 isGpuOccluded (1: 遮蔽, 0: 非遮蔽/可視)
        // bit 16..23 (8bit): 占有セクタ数 N_occ (binaryOccludedCount: 0〜8)
        // bit 24..27 (4bit): 最大連続非占有セクタ数 L_max (0〜8)
        uint isEvaluated = 1u;
        uint isGpuOccluded = (alpha <= 0.0) ? 1u : 0u;
        uint packedDebug = (occupiedSectorMask & 0xFFu)
                         | ((validSectorCount & 0x0Fu) << 8)
                         | (isEvaluated << 12)
                         | (isGpuOccluded << 13)
                         | ((binaryOccludedCount & 0xFFu) << 16)
                         | ((L_max & 0x0Fu) << 24);

        _NeighborCountMap_RW[fullResUV] = packedDebug;
    }
}

#endif // PCD_OCCLUSION_KERNELS_OCCLUSION_INCLUDED