#ifndef PCD_OCCLUSION_KERNELS_FILLHOLES_INCLUDED
#define PCD_OCCLUSION_KERNELS_FILLHOLES_INCLUDED

// 10. Fill Holes
// 点群が描画されなかったピクセル（深度がないピクセル）を対象としたジョイントバイラテラルライクの穴埋め
[numthreads(8, 8, 1)]
void FillHoles(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint) _ScreenParams.x || id.y >= (uint) _ScreenParams.y)
        return;

    uint pointDepth_uint = _DepthMap[id.xy];

    if (pointDepth_uint < DEPTH_MAX_UINT)
    {
        // 既にオクルージョン計算済みのピクセルはスキップ
        return;
    }

    uint vDepth_uint = DEPTH_MAX_UINT;
    bool hasVirtualObj = false;

    if (_UseVirtualDepth > 0)
    {
        float vDepthRaw = _VirtualDepthMap[id.xy];
        float vDepth = _IsReversedZ > 0 ? (1.0 - vDepthRaw) : vDepthRaw;

        if (vDepth < 0.9999)
        {
            hasVirtualObj = true;
            vDepth_uint = (uint) (vDepth * (float) DEPTH_MAX_UINT);
        }
    }

    int fillRadius = 2;
    float totalWeight = 0.0;
    float4 accumulatedColor = float4(0, 0, 0, 0);
    float weightedOriginSum = 0.0;

    uint thresholdDepth = (hasVirtualObj) ? vDepth_uint : DEPTH_MAX_UINT;

    // --- Pass 1: 最小深度の探索 ---
    uint minDepth = thresholdDepth;

    int2 minBound = max(int2(0, 0), (int2)id.xy - fillRadius);
    int2 maxBound = min((int2)_ScreenParams.xy - 1, (int2)id.xy + fillRadius);

    for (int searchY = minBound.y; searchY <= maxBound.y; searchY++)
    {
        for (int searchX = minBound.x; searchX <= maxBound.x; searchX++)
        {
            uint2 uv = uint2(searchX, searchY);
            minDepth = min(minDepth, _DepthMap[uv]);
        }
    }

    // --- Pass 2: ジョイントバイラテラル加重平均 ---
    if (minDepth < thresholdDepth)
    {
        uint depthTolerance = (DEPTH_MAX_UINT / 1000) + (uint)((float)minDepth * 0.02);

        for (int searchY = minBound.y; searchY <= maxBound.y; searchY++)
        {
            for (int searchX = minBound.x; searchX <= maxBound.x; searchX++)
            {
                uint2 uv = uint2(searchX, searchY);
                uint nDepth_uint = _DepthMap[uv];

                if (nDepth_uint < thresholdDepth && nDepth_uint >= minDepth && (nDepth_uint - minDepth) <= depthTolerance)
                {
                    float2 offset = float2(searchX - (int)id.x, searchY - (int)id.y);
                    float distSq = dot(offset, offset);
                    float spatialWeight = 1.0 / (1.0 + distSq * 0.5);

                    float depthDiff = (float)(nDepth_uint - minDepth) / (float)depthTolerance;
                    float depthWeight = 1.0 - smoothstep(0.0, 1.0, depthDiff);
                    float weight = spatialWeight * depthWeight;

                    float4 c = _ColorMap[uv];
                    accumulatedColor += c * weight;
                    totalWeight += weight;

                    uint nType = _OriginTypeMap_RW[uv];
                    weightedOriginSum += (float) nType * weight;
                }
            }
        }
    }

    if (totalWeight > 0.01)
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
}

#endif // PCD_OCCLUSION_KERNELS_FILLHOLES_INCLUDED
