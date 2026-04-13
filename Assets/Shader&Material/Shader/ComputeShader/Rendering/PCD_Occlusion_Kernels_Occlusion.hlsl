#ifndef PCD_OCCLUSION_KERNELS_OCCLUSION_INCLUDED
#define PCD_OCCLUSION_KERNELS_OCCLUSION_INCLUDED

[numthreads(8, 8, 1)]
void ComputeOcclusion(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint) _ScreenParams.x || id.y >= (uint) _ScreenParams.y)
        return;
    
    uint currentOriginType = _OriginTypeMap_RW[id.xy];

    // 【新規性①】Tagに基づく冗長計算のスキップのため、機能OFFの時（従来手法）は全ピクセルを実点群(0u)とみなす
    if (_EnableTagBasedOptimization == 0)
    {
        currentOriginType = 0u;
    }

    uint pointDepth_uint = _DepthMap[id.xy];
    
    float3 currentPos = _ViewPositionMap[id.xy].xyz;
    float currentDepth = _ViewPositionMap[id.xy].w;

    // 背景(2u)もスキップせず、100m奥の仮想座標を与えて計算対象にする
    if (currentOriginType == 2u)
    {
        float2 uv = (float2(id.xy) + 0.5) / _ScreenParams.xy;
        float2 ndc = uv * 2.0 - 1.0;
        float farZ = _IsReversedZ > 0 ? 0.001 : 0.999;
        float4 clipPos = float4(ndc.x, ndc.y, farZ, 1.0);
        float4 viewPos = mul(_InverseProjectionMatrix, clipPos);
        currentPos = normalize(viewPos.xyz / viewPos.w) * 100.0;
        currentDepth = 100.0;
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

    uint depthBias = DEPTH_MAX_UINT / 1000;
    if (hasVirtualObj && (vDepth_uint + depthBias) < pointDepth_uint)
    {
        // 狐の方が手前にある場合
        if (_RecordOcclusionDebug > 0) _OcclusionValueMap_RW[id.xy] = 1.0; // 透過なし(1.0)として出力
        _OcclusionResultMap_RW[id.xy] = _ColorMap[id.xy]; 
        _OriginMap_RW[id.xy] = float4(1, 1, 1, 1);
        _OriginTypeMap_RW[id.xy] = 1u;
        return;
    }

    half3 currentPos_h = (half3)currentPos;
    half currentDepth_h = (half)currentDepth;
    half currentPosSq_h = dot(currentPos_h, currentPos_h);

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
            uint neighborOriginType = _OriginTypeMap_RW[uv];

            // C#側で機能OFFの場合は、近傍ピクセルも全て実点群(0u)とみなす
            if (_EnableTagBasedOptimization == 0)
            {
                neighborOriginType = 0u;
            }

            // 【新規性①】Tagに基づく冗長計算のスキップ
            // 対象が実点群(0u)の場合: 近傍は実点群(0u)または仮想オブジェクト(1u)
            // 対象が仮想オブジェクト等(0u以外)の場合: 近傍は実点群(0u)のみ
            // 機能OFFの時は、上が強制的に0uに書き換わっているため、全ての組み合わせで評価が行われる（従来手法の挙動）
            bool isValidNeighbor = (currentOriginType == 0u) ? (neighborOriginType == 0u || neighborOriginType == 1u) : (neighborOriginType == 0u);

            // 遮蔽物(Neighbor)として計算に巻き込む条件を適用
            if (neighborDepth_uint < DEPTH_MAX_UINT && isValidNeighbor)
            {
                half neighborDepth_h = (half)_ViewPositionMap[uv].w;
                 if (currentDepth_h - neighborDepth_h > 0.01h)
                 {
                    half3 neighborPos_h = (half3)_ViewPositionMap[uv].xyz;
                    half sqLen2_h = dot(neighborPos_h, neighborPos_h);
                    half dotP_h = dot(currentPos_h, neighborPos_h);
                    half sqLen1_h = sqLen2_h - 2.0h * dotP_h + currentPosSq_h;
                    if (sqLen1_h > 0.0001h && sqLen2_h > 0.0001h)
                    {
                        half d_h = dotP_h - sqLen2_h;
                        half occlusionValue_h = 1.0h - d_h * rsqrt(sqLen1_h * sqLen2_h);
                        occlusionSum += (float)occlusionValue_h;
                        neighborCount++;
                    }
                 }
            }
        }
    }

    float alpha = 1.0;
    float occlusionAverage = 1.0;
    
    if (neighborCount > 0)
    {
        occlusionAverage = occlusionSum / (float) neighborCount;

        // 【新規性②】Soft Occlusion (FadeWidth) のトグル切り替え
        // C#から _EnableSoftOcclusionFade が 1 として渡された場合のみ実行
        if (_EnableSoftOcclusionFade > 0 && _OcclusionFadeWidth > 1e-4)
        {
            float halfFade = _OcclusionFadeWidth * 0.5;
            float fadeStart = max(0.0, _OcclusionThreshold - halfFade);
            float fadeEnd = min(1.0, _OcclusionThreshold + halfFade);
            alpha = smoothstep(fadeStart, fadeEnd, occlusionAverage);
        }
        else
        {
            // 従来手法: ハードスレッショルド (完全な二値化)
            if (occlusionAverage < _OcclusionThreshold) alpha = 0.0;
        }
    }
    
    // ★【重要】Soft IoU評価用：狐の色や黒塗りに関係なく、純粋な「マスク値(alpha)」をエクスポートする
    if (_RecordOcclusionDebug > 0) 
    {
        if (currentOriginType == 2u)
        {
            _OcclusionValueMap_RW[id.xy] = -1.0; // 背景(-1.0)は白(Color.white)として出力させる
        }
        else
        {
            _OcclusionValueMap_RW[id.xy] = alpha;
        }
    }

    if (alpha <= 0.0)
    {
        // 【新規性③】ジョイントバイラテラル穴埋めのトグル切り替え
        if (_EnableJointBilateralHoleFilling > 0)
        {
            // 提案手法: 黒塗りはするが、OriginTypeはそのまま（後段のFillHolesに「穴埋めして！」と託す）
            _OcclusionResultMap_RW[id.xy] = float4(0, 0, 0, 1.0);
        }
        else
        {
            // 従来手法: 完全に黒塗りで消去し、OriginTypeを0uにしてFillHolesの処理を強制スキップさせる
            _OcclusionResultMap_RW[id.xy] = float4(0, 0, 0, 1.0);
            _OriginTypeMap_RW[id.xy] = 0u; 
        }
    }
    else
    {
        float4 col = _ColorMap[id.xy];
        col.a *= alpha;
        
        // 背景(2u)は透明化させずに残す
        if (currentOriginType == 2u) col.a = 1.0;

        _OcclusionResultMap_RW[id.xy] = col;
        
        if (currentOriginType == 0u) _OriginMap_RW[id.xy] = float4(0, 0, 0, 1);
        else if (currentOriginType == 1u) _OriginMap_RW[id.xy] = float4(1, 1, 1, 1);
    }
}

#endif // PCD_OCCLUSION_KERNELS_OCCLUSION_INCLUDED