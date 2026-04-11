#ifndef PCD_OCCLUSION_KERNELS_OCCLUSION_INCLUDED
#define PCD_OCCLUSION_KERNELS_OCCLUSION_INCLUDED

void ComputeOcclusion_Proposed(uint3 id)
{
    if (id.x >= (uint) _ScreenParams.x || id.y >= (uint) _ScreenParams.y)
        return;

    uint currentOriginType = _OriginTypeMap_RW[id.xy];
    uint pointDepth_uint = _DepthMap[id.xy];

    if (pointDepth_uint >= DEPTH_MAX_UINT)
    {
        if (_RecordOcclusionDebug > 0)
        {
            _OcclusionValueMap_RW[id.xy] = -1.0; // 穴 (点群がないピクセル)
        }
        // ここはFillHolesカーネルに任せるためスキップ
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

    uint depthBias = DEPTH_MAX_UINT / 1000;

    if (hasVirtualObj && (vDepth_uint + depthBias) < pointDepth_uint)
    {
        if (_RecordOcclusionDebug > 0)
        {
            _OcclusionValueMap_RW[id.xy] = 1.0; // 仮想オブジェクトによる隠蔽
        }
        _OcclusionResultMap_RW[id.xy] = float4(0, 0, 0, 0);
        _OriginMap_RW[id.xy] = float4(1, 1, 1, 1);
        _OriginTypeMap_RW[id.xy] = 1u;
        return;
    }

    float3 currentPos = _ViewPositionMap[id.xy].xyz;
    float currentDepth = _ViewPositionMap[id.xy].w;

    // FP16(半精度)に変換して演算スループットを2倍に（レジスタ使用量も半減）
    // ハンドトラッキングのスケール（メートル単位なら0.1〜2.0m等）ならオーバーフローしません
    half3 currentPos_h = (half3)currentPos;
    half currentDepth_h = (half)currentDepth;
    half currentPosSq_h = dot(currentPos_h, currentPos_h);

    int level = _FinalNeighborhoodSizeMap[id.xy];
    uint radius = max(1u << (uint)max(0, level), 1u);

    // 和はfloatで保持（多数の足し合わせによる精度落ちを防ぐ）
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

            // 【重要・新規性】遮蔽物(Neighbor)としての評価条件
            // 対象ピクセルが実点群(0u)の場合: 近傍は実点群(0u)または仮想オブジェクト(1u)
            // 対象ピクセルが仮想オブジェクト等(0u以外)の場合: 近傍は実点群(0u)のみ
            bool isValidNeighbor = (currentOriginType == 0u) ? (neighborOriginType == 0u || neighborOriginType == 1u) : (neighborOriginType == 0u);

            if (neighborDepth_uint < DEPTH_MAX_UINT && isValidNeighbor)
            {
                half neighborDepth_h = (half)_ViewPositionMap[uv].w;

                 // 奥行き差の判定。ここではFP16で比較
                 if (currentDepth_h - neighborDepth_h > 0.01h)
                 {
                    half3 neighborPos_h = (half3)_ViewPositionMap[uv].xyz;
                    half sqLen2_h = dot(neighborPos_h, neighborPos_h);
                    half dotP_h = dot(currentPos_h, neighborPos_h);

                    // Math展開: sqLen1 = |neighborPos - currentPos|^2
                    half sqLen1_h = sqLen2_h - 2.0h * dotP_h + currentPosSq_h;

                    // half精度での安全な微小値判定（1e-12だとFP16では0とみなされるため 1e-4h を使用）
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
    
    if (_RecordOcclusionDebug > 0)
    {
        // これにより、論文のSoft IoU評価が正しく行えるようになります。
        _OcclusionValueMap_RW[id.xy] = occlusionAverage;
    }

    if (alpha <= 0.0)
    {
        //_OcclusionResultMap_RW[id.xy] = float4(0, 0, 0, 0);
        _OcclusionResultMap_RW[id.xy] = float4(0, 0, 0, 1.0);
    }
    else
    {
        float4 col = _ColorMap[id.xy];
        col.a *= alpha;
        _OcclusionResultMap_RW[id.xy] = col;

        if (currentOriginType == 0u)
            _OriginMap_RW[id.xy] = float4(0, 0, 0, 1);
        else if (currentOriginType == 1u)
            _OriginMap_RW[id.xy] = float4(1, 1, 1, 1);
    }
}

// 9. Compute Occlusion (真の従来手法ベースライン版 - Hole Filling OFF時)
void ComputeOcclusion_Traditional(uint3 id)
{
    if (id.x >= (uint) _ScreenParams.x || id.y >= (uint) _ScreenParams.y)
        return;
    
    uint originType = _OriginTypeMap_RW[id.xy];
    uint pointDepth_uint = _DepthMap[id.xy];
    
    float3 currentPos = _ViewPositionMap[id.xy].xyz;
    float currentDepth = _ViewPositionMap[id.xy].w;

    // 【修正1】背景(OriginType==2u)もスキップせず、100m奥の仮想座標を与えて計算対象にする
    if (originType == 2u)
    {
        float2 uv = (float2(id.xy) + 0.5) / _ScreenParams.xy;
        float2 ndc = uv * 2.0 - 1.0;
        float farZ = _IsReversedZ > 0 ? 0.001 : 0.999;
        float4 clipPos = float4(ndc.x, ndc.y, farZ, 1.0);
        float4 viewPos = mul(_InverseProjectionMatrix, clipPos);
        currentPos = normalize(viewPos.xyz / viewPos.w) * 100.0; // 100m奥に配置
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
        if (_RecordOcclusionDebug > 0) _OcclusionValueMap_RW[id.xy] = 1.0;
        _OcclusionResultMap_RW[id.xy] = _ColorMap[id.xy]; // 奥にある狐はそのまま描画
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

            bool isValidNeighbor = (originType == 0u) ? (neighborOriginType == 0u || neighborOriginType == 1u) : (neighborOriginType == 0u);

            // 【修正2】手前にある「対象オブジェクト」だけを遮蔽物として計算に使う
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
        if (_OcclusionFadeWidth > 1e-4) {
            float fadeEnd = _OcclusionThreshold + _OcclusionFadeWidth;
            alpha = smoothstep(_OcclusionThreshold, fadeEnd, occlusionAverage);
        } else {
            if (occlusionAverage < _OcclusionThreshold) alpha = 0.0;
        }
    }
    
    if (_RecordOcclusionDebug > 0) _OcclusionValueMap_RW[id.xy] = occlusionAverage;

    // 【修正3】完全に遮蔽された(alpha=0)部分は、透明にするのではなく「黒」で物理的に消去(上書き)する！
    if (alpha <= 0.0)
    {
        _OcclusionResultMap_RW[id.xy] = float4(0, 0, 0, 1.0); // Alpha=1の黒でARの背景(狐)を上書きして消す
    }
    else
    {
        float4 col = _ColorMap[id.xy];
        col.a *= alpha;
        // 背景(2u)は透明にせずそのまま残す
        if (originType == 2u) col.a = 1.0;

        _OcclusionResultMap_RW[id.xy] = col;
        
        if (originType == 0u) _OriginMap_RW[id.xy] = float4(0, 0, 0, 1);
        else if (originType == 1u) _OriginMap_RW[id.xy] = float4(1, 1, 1, 1);
    }
}

// 9. Compute Occlusion
// 点群が描画されているピクセル（深度があるピクセル）のみを対象としたオクルージョン計算
[numthreads(8, 8, 1)]
void ComputeOcclusion(uint3 id : SV_DispatchThreadID)
{
    if (_EnableJointBilateralHoleFilling > 0)
    {
        ComputeOcclusion_Proposed(id);
    }
    else
    {
        ComputeOcclusion_Traditional(id);
    }
}

#endif // PCD_OCCLUSION_KERNELS_OCCLUSION_INCLUDED
