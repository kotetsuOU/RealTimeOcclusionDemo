#ifndef PCD_OCCLUSION_KERNELS_OCCLUSION_INCLUDED
#define PCD_OCCLUSION_KERNELS_OCCLUSION_INCLUDED

// 8a-1. Build Depth Pyramid L1
// 距離計算やエッジ判定を効率化するためのMIPマップ(ピラミッド)構築
// 前レベルの解像度を1/2にしながら、2x2の最小深度を下位レベルへ格納する
[numthreads(8, 8, 1)]
void BuildDepthPyramidL1(uint3 id : SV_DispatchThreadID)
{
    uint2 dim;
    _DepthPyramidL1_RW.GetDimensions(dim.x, dim.y);
    if (id.x >= dim.x || id.y >= dim.y)
        return;
    _DepthPyramidL1_RW[id.xy] = ZMinDownsample(_DepthMap, id.xy * 2u);
}

// 8a-2. Build Depth Pyramid L2 (1/4 レベル)
[numthreads(8, 8, 1)]
void BuildDepthPyramidL2(uint3 id : SV_DispatchThreadID)
{
    uint2 dim;
    _DepthPyramidL2_RW.GetDimensions(dim.x, dim.y);
    if (id.x >= dim.x || id.y >= dim.y)
        return;
    _DepthPyramidL2_RW[id.xy] = ZMinDownsample(_DepthPyramidL1, id.xy * 2u);
}

// 8a-3. Build Depth Pyramid L3 (1/8 レベル)
[numthreads(8, 8, 1)]
void BuildDepthPyramidL3(uint3 id : SV_DispatchThreadID)
{
    uint2 dim;
    _DepthPyramidL3_RW.GetDimensions(dim.x, dim.y);
    if (id.x >= dim.x || id.y >= dim.y)
        return;
    _DepthPyramidL3_RW[id.xy] = ZMinDownsample(_DepthPyramidL2, id.xy * 2u);
}

// 8a-4. Build Depth Pyramid L4 (1/16 レベル)
[numthreads(8, 8, 1)]
void BuildDepthPyramidL4(uint3 id : SV_DispatchThreadID)
{
    uint2 dim;
    _DepthPyramidL4_RW.GetDimensions(dim.x, dim.y);
    if (id.x >= dim.x || id.y >= dim.y)
        return;
    _DepthPyramidL4_RW[id.xy] = ZMinDownsample(_DepthPyramidL3, id.xy * 2u);
}

// 8b+8c. Apply Adaptive Gradient Correction
// 近傍探索サイズ(Level)が輪郭(エッジ)を大きく跨いでおかしな箇所を参照しないように、
// 適したレベルの深度ピラミッドでの勾配(ソーベルフィルタによるエッジ強度)を計算し、
// 一定閾値以上なら探索エリアを縮小(レベル -1 等)する補正を行う。
[numthreads(8, 8, 1)]
void ApplyAdaptiveGradientCorrection(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint) _ScreenParams.x || id.y >= (uint) _ScreenParams.y)
        return;

    uint2 fullResUV = id.xy;
    int level = _NeighborhoodSizeMap[fullResUV];
    int correctedLevel = level;

    if (level > 0)
    {
        float gradient = 0.0;
        uint2 uv_lowres;

        if (level == 1)
        {
            uv_lowres = fullResUV / 2u;
            gradient = SobelOnPyramid(_DepthPyramidL1, uv_lowres);
        }
        else if (level == 2)
        {
            uv_lowres = fullResUV / 4u;
            gradient = SobelOnPyramid(_DepthPyramidL2, uv_lowres);
        }
        else if (level == 3)
        {
            uv_lowres = fullResUV / 8u;
            gradient = SobelOnPyramid(_DepthPyramidL3, uv_lowres);
        }
        else
        {
            uv_lowres = fullResUV / 16u;
            gradient = SobelOnPyramid(_DepthPyramidL4, uv_lowres);
        }

        if (gradient > _GradientThreshold_g_th)
        {
            correctedLevel = max(0, level - 1);
        }
    }
    _CorrectedNeighborhoodSizeMap_RW[fullResUV] = correctedLevel;
}

// 9. Occlusion and Filtering
// 本シェーダーの中核を成すパス。
// 自ピクセルと周囲ピクセルの深度や位置情報の差分(オクルージョンの度合い)を計算し、
// 該当ピクセルの点群が表示されるべきか、あるい背後に隠れて見えないべきかを導出して、
// 透過(アルファ)やジョイントバイラテラルライクのブレンドを適用する。穴埋め処理も内包。
[numthreads(8, 8, 1)]
void OcclusionAndFilter(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint) _ScreenParams.x || id.y >= (uint) _ScreenParams.y)
        return;

    uint pointDepth_uint = _DepthMap[id.xy];

    uint vDepth_uint = DEPTH_MAX_UINT;
    bool hasVirtualObj = false;

    if (_UseVirtualDepth > 0)
    {
        float vDepthRaw = _VirtualDepthMap[id.xy];
        float vDepth = 1.0 - vDepthRaw;

        if (vDepth < 0.9999)
        {
            hasVirtualObj = true;
            vDepth_uint = (uint) (vDepth * (float) DEPTH_MAX_UINT);
        }
    }

    if (pointDepth_uint >= DEPTH_MAX_UINT)
    {
        int fillRadius = 2;
        float totalWeight = 0.0;
        float4 accumulatedColor = float4(0, 0, 0, 0);
        float weightedOriginSum = 0.0;

        uint thresholdDepth = (hasVirtualObj) ? vDepth_uint : DEPTH_MAX_UINT;

        // --- Pass 1: 最小深度の探索 ---
        uint minDepth = thresholdDepth;

        for (int searchY = -fillRadius; searchY <= fillRadius; searchY++)
        {
            for (int searchX = -fillRadius; searchX <= fillRadius; searchX++)
            {
                if (searchX == 0 && searchY == 0)
                    continue;

                int2 offset = int2(searchX, searchY);
                uint2 uv = clamp((int2) id.xy + offset, 0, (int2) _ScreenParams.xy - 1);
                uint nDepth_uint = _DepthMap[uv];

                if (nDepth_uint < minDepth)
                {
                    minDepth = nDepth_uint;
                }
            }
        }

        // --- Pass 2: ジョイントバイラテラル加重平均 ---
        if (minDepth < thresholdDepth)
        {
            // Z値の非線形性をある程度考慮し、深度に応じた許容値を近似的に算出
            // ※カメラから遠い（値が大きい）ほど許容幅を広げ、固定値の限界を補う設計
            // 最小許容値として DEPTH_MAX_UINT / 1000 は維持しつつ、深度に応じたマージンを加算
            uint depthTolerance = (DEPTH_MAX_UINT / 1000) + (uint)((float)minDepth * 0.02);

            for (int y = -fillRadius; y <= fillRadius; y++)
            {
                for (int x = -fillRadius; x <= fillRadius; x++)
                {
                    if (x == 0 && y == 0)
                        continue;

                    int2 offset = int2(x, y);
                    uint2 uv = clamp((int2) id.xy + offset, 0, (int2) _ScreenParams.xy - 1);
                    uint nDepth_uint = _DepthMap[uv];

                    if (nDepth_uint < thresholdDepth && nDepth_uint >= minDepth && (nDepth_uint - minDepth) <= depthTolerance)
                    {
                        float distSq = dot(float2(x, y), float2(x, y));
                        float spatialWeight = 1.0 / (1.0 + distSq * 0.5);

                        float depthDiff = (float)(nDepth_uint - minDepth) / (float)depthTolerance;
                        float depthWeight = 1.0 - smoothstep(0.0, 1.0, depthDiff);
                        float weight = spatialWeight * depthWeight;

                        float4 c = _ColorMap[uv];
                        accumulatedColor += c * weight;
                        totalWeight += weight;

                        uint nType = _OriginTypeMap[uv];
                        weightedOriginSum += (float) nType * weight;
                    }
                }
            }
        }

        // 続く重みの判定処理等は元のロジックを維持
        if (totalWeight > 0.01) // 重みが極端に小さくなる可能性があるため判定閾値を調整
        {
            _OcclusionResultMap_RW[id.xy] = accumulatedColor / totalWeight;

            float avgType = weightedOriginSum / totalWeight;
            if (avgType < 0.5)
            {
                _OriginMap_RW[id.xy] = float4(0, 0, 0, 1);
                _OriginTypeMap_RW[id.xy] = 0u;
            }
            else
            {
                _OriginMap_RW[id.xy] = float4(1, 1, 1, 1);
                _OriginTypeMap_RW[id.xy] = 1u;
            }
        }
        else
        {
            _OcclusionResultMap_RW[id.xy] = float4(0, 0, 0, 0);

            if (hasVirtualObj)
            {
                _OriginMap_RW[id.xy] = float4(1, 1, 1, 1);
                _OriginTypeMap_RW[id.xy] = 1u;
            }
            else
            {
                _OriginTypeMap_RW[id.xy] = 2u;
            }
        }
        return;
    }

    uint depthBias = DEPTH_MAX_UINT / 1000;

    if (hasVirtualObj && (vDepth_uint + depthBias) < pointDepth_uint)
    {
        _OcclusionResultMap_RW[id.xy] = float4(0, 0, 0, 0);
        _OriginMap_RW[id.xy] = float4(1, 1, 1, 1);
        _OriginTypeMap_RW[id.xy] = 1u;
        return;
    }

    float3 currentPos = _ViewPositionMap[id.xy].xyz;
    float currentDepth = _ViewPositionMap[id.xy].w;

    int level = _FinalNeighborhoodSizeMap[id.xy];
    uint radius = (uint) pow(2.0, (float) level);
    radius = max(radius, 1u);

    float occlusionSum = 0.0;
    uint neighborCount = 0u;

    for (uint y = 0u; y <= radius * 2u; y++)
    {
        for (uint x = 0u; x <= radius * 2u; x++)
        {
            if (x == radius && y == radius)
                continue;

            int2 offset = int2((int) x - (int) radius, (int) y - (int) radius);
            uint2 uv = clamp((int2) id.xy + offset, 0, (int2) _ScreenParams.xy - 1);

            uint neighborDepth_uint = _DepthMap[uv];

            if (neighborDepth_uint < DEPTH_MAX_UINT)
            {
                float3 neighborPos = _ViewPositionMap[uv].xyz;
                float neighborDepth = _ViewPositionMap[uv].w;

                /*
                if (currentDepth - neighborDepth > 0.01)
                {
                    float3 y_minus_x = neighborPos - currentPos;
                    float3 minus_y = -neighborPos;

                    if (length(y_minus_x) > 1e-6 && length(minus_y) > 1e-6)
                    {
                        float occlusionValue = 1.0 - dot(normalize(y_minus_x), normalize(minus_y));
                        occlusionSum += occlusionValue;
                        neighborCount++;
                    }
                }*/
                
                float3 y_minus_x = neighborPos - currentPos;
                float3 minus_y = -neighborPos;

                if (length(y_minus_x) > 1e-6 && length(minus_y) > 1e-6)
                {
                    float occlusionValue = 1.0 - dot(normalize(y_minus_x), normalize(minus_y));
                    occlusionSum += occlusionValue;
                    neighborCount++;
                }

            }
        }
    }

    float alpha = 1.0;
    if (neighborCount > 0)
    {
        float occlusionAverage = occlusionSum / (float) neighborCount;

        if (_RecordOcclusionDebug > 0)
        {
            _OcclusionValueMap_RW[id.xy] = occlusionAverage;
        }

        if (_OcclusionFadeWidth > 1e-4)
        {
            float fadeEnd = _OcclusionThreshold + _OcclusionFadeWidth;
            alpha = smoothstep(_OcclusionThreshold, fadeEnd, occlusionAverage);
        }
        else
        {
            if (occlusionAverage < _OcclusionThreshold)
            {
                alpha = 0.0;
            }
        }
    }

    if (alpha <= 0.0)
    {
        _OcclusionResultMap_RW[id.xy] = float4(0, 0, 0, 0);
    }
    else
    {
        float4 col = _ColorMap[id.xy];
        col.a *= alpha;
        _OcclusionResultMap_RW[id.xy] = col;

        uint originType = _OriginTypeMap[id.xy];
        if (originType == 0u)
            _OriginMap_RW[id.xy] = float4(0, 0, 0, 1);
        else if (originType == 1u)
            _OriginMap_RW[id.xy] = float4(1, 1, 1, 1);
    }
}

#endif // PCD_OCCLUSION_KERNELS_OCCLUSION_INCLUDED