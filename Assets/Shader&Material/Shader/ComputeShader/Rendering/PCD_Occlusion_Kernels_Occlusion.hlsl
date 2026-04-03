#ifndef PCD_OCCLUSION_KERNELS_OCCLUSION_INCLUDED
#define PCD_OCCLUSION_KERNELS_OCCLUSION_INCLUDED

// 9. Compute Occlusion
// 点群が描画されているピクセル（深度があるピクセル）のみを対象としたオクルージョン計算
[numthreads(8, 8, 1)]
void ComputeOcclusion(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint) _ScreenParams.x || id.y >= (uint) _ScreenParams.y)
        return;

    uint pointDepth_uint = _DepthMap[id.xy];

    if (pointDepth_uint >= DEPTH_MAX_UINT)
    {
        // ここはFillHolesカーネルに任せるためスキップ
        return;
    }

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
    float currentPosSq = dot(currentPos, currentPos);

    int level = _FinalNeighborhoodSizeMap[id.xy];
    uint radius = max(1u << (uint)max(0, level), 1u);

    float occlusionSum = 0.0;
    uint neighborCount = 0u;

    int2 minBound = max(int2(0, 0), (int2)id.xy - (int)radius);
    int2 maxBound = min((int2)_ScreenParams.xy - 1, (int2)id.xy + (int)radius);

    for (int searchY = minBound.y; searchY <= maxBound.y; searchY++)
    {
        for (int searchX = minBound.x; searchX <= maxBound.x; searchX++)
        {
            uint2 uv = uint2(searchX, searchY);

            uint neighborDepth_uint = _DepthMap[uv];

            if (neighborDepth_uint < DEPTH_MAX_UINT)
            {
                float neighborDepth = _ViewPositionMap[uv].w;
                float3 neighborPos = _ViewPositionMap[uv].xyz;

                 if (currentDepth - neighborDepth > 0.01)
                 {
                    float sqLen2 = dot(neighborPos, neighborPos);
                    float dotP = dot(currentPos, neighborPos);

                    // Math展開: sqLen1 = |neighborPos - currentPos|^2
                    float sqLen1 = sqLen2 - 2.0 * dotP + currentPosSq;

                    if (sqLen1 > 1e-12 && sqLen2 > 1e-12)
                    {
                        // Math展開: d = -dot(neighborPos - currentPos, neighborPos) = dotP - sqLen2
                        float d = dotP - sqLen2;
                        float occlusionValue = 1.0 - d * rsqrt(sqLen1 * sqLen2);
                        occlusionSum += occlusionValue;
                        neighborCount++;
                    }
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