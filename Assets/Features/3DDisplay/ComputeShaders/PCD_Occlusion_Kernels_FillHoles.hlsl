#ifndef PCD_OCCLUSION_KERNELS_FILLHOLES_INCLUDED
#define PCD_OCCLUSION_KERNELS_FILLHOLES_INCLUDED

// 10. Fill Holes
// 点群が描画されなかったピクセル（深度がないピクセル）を対象としたジョイントバイラテラルライクの穴埋め
[numthreads(8, 8, 1)]
void FillHoles(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint) _ScreenParams.x || id.y >= (uint) _ScreenParams.y)
        return;
    
    uint originType = _OriginTypeMap_RW[id.xy];
    if (originType == 0u)
    {
        // 既に点群としてオクルージョン計算済みのピクセルはスキップ
        return;
    }

    int level = _FinalNeighborhoodSizeMap[id.xy];
    int l = max(1, level); // levelをベースにした探索距離(radius)

    int2 dirs[8] = {
        int2(1, 0), int2(1, 1), int2(0, 1), int2(-1, 1),
        int2(-1, 0), int2(-1, -1), int2(0, -1), int2(1, -1)
    };

    int validCount = 0;
    float4 accumulatedColor = float4(0, 0, 0, 0);

    for (int i = 0; i < 8; i++)
    {
        int2 uv = (int2)id.xy + dirs[i] * l;
        
        // 境界チェック
        if (uv.x >= 0 && uv.x < (int)_ScreenParams.x && uv.y >= 0 && uv.y < (int)_ScreenParams.y)
        {
            // _OriginTypeMap が 0u (点群として計算済み) なら有効
            if (_OriginTypeMap_RW[uv] == 0u)
            {
                validCount++;
                accumulatedColor += _ColorMap[uv];
            }
        }
    }

    // 8方向すべてに点群が存在した場合のみ穴埋めを適用
    if (validCount == 8)
    {
        _OcclusionResultMap_RW[id.xy] = accumulatedColor / 8.0;
        
        // 穴を埋めたのでタイプを更新
        if (_EnableDebugDisplay > 0)
            _OriginMap_RW[id.xy] = float4(0, 0, 0, 1);
        _OriginTypeMap_RW[id.xy] = 0u;
    }
}

// ==========================================
// Pull-Push Algorithmic Hole Filling
// ==========================================

[numthreads(8, 8, 1)]
void FillHolesPullPushInit(uint3 id : SV_DispatchThreadID)
{
    float w, h;
    _PullPushLevel_Out_RW.GetDimensions(w, h);
    if (id.x >= (uint)w || id.y >= (uint)h) return;

    uint originType = _OriginTypeMap[id.xy];
    float4 color = _OcclusionResultMap[id.xy];
    
    // デフォルトのウェイトは1.0（実点群、背景、スキップ部分含む）
    float weight = 1.0;
    
    // 遮蔽判定されたピクセル（色＝黒かつ不透明度＝1.0）のみウェイトを0.0（穴埋め対象）とする
    if (color.a == 1.0 && all(color.rgb == 0.0))
    {
        weight = 0.0;
    }
    
    _PullPushLevel_Out_RW[id.xy] = float4(color.rgb * weight, weight);
}

[numthreads(8, 8, 1)]
void FillHolesPull(uint3 id : SV_DispatchThreadID)
{
    float w, h;
    _PullPushLevel_Out_RW.GetDimensions(w, h);
    if (id.x >= (uint)w || id.y >= (uint)h) return;

    uint2 src = id.xy * 2;
    
    float4 c00 = _PullPushLevel_In[src + uint2(0, 0)];
    float4 c10 = _PullPushLevel_In[src + uint2(1, 0)];
    float4 c01 = _PullPushLevel_In[src + uint2(0, 1)];
    float4 c11 = _PullPushLevel_In[src + uint2(1, 1)];
    
    float4 sum = c00 + c10 + c01 + c11;
    _PullPushLevel_Out_RW[id.xy] = sum / 4.0;
}

[numthreads(8, 8, 1)]
void FillHolesPush(uint3 id : SV_DispatchThreadID)
{
    float w, h;
    _PullPushLevel_Out_RW.GetDimensions(w, h);
    if (id.x >= (uint)w || id.y >= (uint)h) return;

    float4 current = _PullPushLevel_Out_RW[id.xy];
    float currentWeight = current.a;
    
    if (currentWeight < 1.0)
    {
        float srcW, srcH;
        _PullPushLevel_In.GetDimensions(srcW, srcH);
        
        float2 uv = (id.xy + 0.5) / float2(w, h);
        float2 srcPos = uv * float2(srcW, srcH) - 0.5;
        
        int2 p00 = max(0, min((int2)srcPos, int2(srcW - 1, srcH - 1)));
        int2 p11 = min(p00 + 1, int2(srcW - 1, srcH - 1));
        
        float2 f = frac(srcPos);
        
        float4 c00 = _PullPushLevel_In[p00];
        float4 c10 = _PullPushLevel_In[int2(p11.x, p00.y)];
        float4 c01 = _PullPushLevel_In[int2(p00.x, p11.y)];
        float4 c11 = _PullPushLevel_In[p11];
        
        float4 interp = lerp(lerp(c00, c10, f.x), lerp(c01, c11, f.x), f.y);
        
        float4 blended = current + (1.0 - currentWeight) * interp;
        _PullPushLevel_Out_RW[id.xy] = blended;
    }
}

[numthreads(8, 8, 1)]
void FillHolesPullPushFinalize(uint3 id : SV_DispatchThreadID)
{
    float w, h;
    _OcclusionResultMap_RW.GetDimensions(w, h);
    if (id.x >= (uint)w || id.y >= (uint)h) return;

    uint originType = _OriginTypeMap_RW[id.xy];
    
    if (originType == 0u)
    {
        // 実際に遮蔽判定されていた（穴埋めの必要があった）ピクセルのみを対象にする
        float4 color = _OcclusionResultMap_RW[id.xy];
        if (color.a == 1.0 && all(color.rgb == 0.0))
        {
            float4 pulled = _PullPushLevel_In[id.xy];
            if (pulled.a > 0.0001)
            {
                _OcclusionResultMap_RW[id.xy] = float4(pulled.rgb / pulled.a, 1.0);
                _OriginTypeMap_RW[id.xy] = 0u; 
                if (_EnableDebugDisplay > 0)
                    _OriginMap_RW[id.xy] = float4(0, 0, 0, 1);
            }
        }
    }
}

// ==========================================
// Morphology Pyramid Building
// ==========================================

[numthreads(8, 8, 1)]
void BuildMorphPyramidL1(uint3 id : SV_DispatchThreadID)
{
    uint2 dim;
    _MorphTypePyramidL1_RW.GetDimensions(dim.x, dim.y);
    if (id.x >= dim.x || id.y >= dim.y) return;

    uint2 src = id.xy * 2u;
    
    uint t0 = _MorphTypeIn[src + uint2(0, 0)];
    uint t1 = _MorphTypeIn[src + uint2(1, 0)];
    uint t2 = _MorphTypeIn[src + uint2(0, 1)];
    uint t3 = _MorphTypeIn[src + uint2(1, 1)];
    
    uint mask0 = (t0 == 0u ? 1u : 0u) | (t0 == 1u ? 2u : 0u) | (t0 == 2u ? 4u : 0u);
    uint mask1 = (t1 == 0u ? 1u : 0u) | (t1 == 1u ? 2u : 0u) | (t1 == 2u ? 4u : 0u);
    uint mask2 = (t2 == 0u ? 1u : 0u) | (t2 == 1u ? 2u : 0u) | (t2 == 2u ? 4u : 0u);
    uint mask3 = (t3 == 0u ? 1u : 0u) | (t3 == 1u ? 2u : 0u) | (t3 == 2u ? 4u : 0u);
    
    _MorphTypePyramidL1_RW[id.xy] = mask0 | mask1 | mask2 | mask3;

    float4 c0 = (t0 != 2u) ? _MorphColorIn[src + uint2(0, 0)] : float4(0,0,0,0);
    float4 c1 = (t1 != 2u) ? _MorphColorIn[src + uint2(1, 0)] : float4(0,0,0,0);
    float4 c2 = (t2 != 2u) ? _MorphColorIn[src + uint2(0, 1)] : float4(0,0,0,0);
    float4 c3 = (t3 != 2u) ? _MorphColorIn[src + uint2(1, 1)] : float4(0,0,0,0);
    
    float w0 = (t0 != 2u) ? 1.0 : 0.0;
    float w1 = (t1 != 2u) ? 1.0 : 0.0;
    float w2 = (t2 != 2u) ? 1.0 : 0.0;
    float w3 = (t3 != 2u) ? 1.0 : 0.0;
    
    float totalW = w0 + w1 + w2 + w3;
    _MorphColorPyramidL1_RW[id.xy] = (totalW > 0.0) ? ((c0 + c1 + c2 + c3) / totalW) : float4(0,0,0,0);
}

[numthreads(8, 8, 1)]
void BuildMorphPyramidL2(uint3 id : SV_DispatchThreadID)
{
    uint2 dim;
    _MorphTypePyramidL2_RW.GetDimensions(dim.x, dim.y);
    if (id.x >= dim.x || id.y >= dim.y) return;
    uint2 src = id.xy * 2u;
    uint m0 = _MorphTypePyramidL1[src + uint2(0, 0)];
    uint m1 = _MorphTypePyramidL1[src + uint2(1, 0)];
    uint m2 = _MorphTypePyramidL1[src + uint2(0, 1)];
    uint m3 = _MorphTypePyramidL1[src + uint2(1, 1)];
    _MorphTypePyramidL2_RW[id.xy] = m0 | m1 | m2 | m3;

    float4 c0 = ((m0 & 3u) != 0u) ? _MorphColorPyramidL1[src + uint2(0, 0)] : float4(0,0,0,0);
    float4 c1 = ((m1 & 3u) != 0u) ? _MorphColorPyramidL1[src + uint2(1, 0)] : float4(0,0,0,0);
    float4 c2 = ((m2 & 3u) != 0u) ? _MorphColorPyramidL1[src + uint2(0, 1)] : float4(0,0,0,0);
    float4 c3 = ((m3 & 3u) != 0u) ? _MorphColorPyramidL1[src + uint2(1, 1)] : float4(0,0,0,0);
    float w0 = ((m0 & 3u) != 0u) ? 1.0 : 0.0;
    float w1 = ((m1 & 3u) != 0u) ? 1.0 : 0.0;
    float w2 = ((m2 & 3u) != 0u) ? 1.0 : 0.0;
    float w3 = ((m3 & 3u) != 0u) ? 1.0 : 0.0;
    float totalW = w0 + w1 + w2 + w3;
    _MorphColorPyramidL2_RW[id.xy] = (totalW > 0.0) ? ((c0 + c1 + c2 + c3) / totalW) : float4(0,0,0,0);
}

[numthreads(8, 8, 1)]
void BuildMorphPyramidL3(uint3 id : SV_DispatchThreadID)
{
    uint2 dim;
    _MorphTypePyramidL3_RW.GetDimensions(dim.x, dim.y);
    if (id.x >= dim.x || id.y >= dim.y) return;
    uint2 src = id.xy * 2u;
    uint m0 = _MorphTypePyramidL2[src + uint2(0, 0)];
    uint m1 = _MorphTypePyramidL2[src + uint2(1, 0)];
    uint m2 = _MorphTypePyramidL2[src + uint2(0, 1)];
    uint m3 = _MorphTypePyramidL2[src + uint2(1, 1)];
    _MorphTypePyramidL3_RW[id.xy] = m0 | m1 | m2 | m3;

    float4 c0 = ((m0 & 3u) != 0u) ? _MorphColorPyramidL2[src + uint2(0, 0)] : float4(0,0,0,0);
    float4 c1 = ((m1 & 3u) != 0u) ? _MorphColorPyramidL2[src + uint2(1, 0)] : float4(0,0,0,0);
    float4 c2 = ((m2 & 3u) != 0u) ? _MorphColorPyramidL2[src + uint2(0, 1)] : float4(0,0,0,0);
    float4 c3 = ((m3 & 3u) != 0u) ? _MorphColorPyramidL2[src + uint2(1, 1)] : float4(0,0,0,0);
    float w0 = ((m0 & 3u) != 0u) ? 1.0 : 0.0;
    float w1 = ((m1 & 3u) != 0u) ? 1.0 : 0.0;
    float w2 = ((m2 & 3u) != 0u) ? 1.0 : 0.0;
    float w3 = ((m3 & 3u) != 0u) ? 1.0 : 0.0;
    float totalW = w0 + w1 + w2 + w3;
    _MorphColorPyramidL3_RW[id.xy] = (totalW > 0.0) ? ((c0 + c1 + c2 + c3) / totalW) : float4(0,0,0,0);
}

[numthreads(8, 8, 1)]
void BuildMorphPyramidL4(uint3 id : SV_DispatchThreadID)
{
    uint2 dim;
    _MorphTypePyramidL4_RW.GetDimensions(dim.x, dim.y);
    if (id.x >= dim.x || id.y >= dim.y) return;
    uint2 src = id.xy * 2u;
    uint m0 = _MorphTypePyramidL3[src + uint2(0, 0)];
    uint m1 = _MorphTypePyramidL3[src + uint2(1, 0)];
    uint m2 = _MorphTypePyramidL3[src + uint2(0, 1)];
    uint m3 = _MorphTypePyramidL3[src + uint2(1, 1)];
    _MorphTypePyramidL4_RW[id.xy] = m0 | m1 | m2 | m3;

    float4 c0 = ((m0 & 3u) != 0u) ? _MorphColorPyramidL3[src + uint2(0, 0)] : float4(0,0,0,0);
    float4 c1 = ((m1 & 3u) != 0u) ? _MorphColorPyramidL3[src + uint2(1, 0)] : float4(0,0,0,0);
    float4 c2 = ((m2 & 3u) != 0u) ? _MorphColorPyramidL3[src + uint2(0, 1)] : float4(0,0,0,0);
    float4 c3 = ((m3 & 3u) != 0u) ? _MorphColorPyramidL3[src + uint2(1, 1)] : float4(0,0,0,0);
    float w0 = ((m0 & 3u) != 0u) ? 1.0 : 0.0;
    float w1 = ((m1 & 3u) != 0u) ? 1.0 : 0.0;
    float w2 = ((m2 & 3u) != 0u) ? 1.0 : 0.0;
    float w3 = ((m3 & 3u) != 0u) ? 1.0 : 0.0;
    float totalW = w0 + w1 + w2 + w3;
    _MorphColorPyramidL4_RW[id.xy] = (totalW > 0.0) ? ((c0 + c1 + c2 + c3) / totalW) : float4(0,0,0,0);
}

[numthreads(8, 8, 1)]
void BuildMorphPyramidL5(uint3 id : SV_DispatchThreadID)
{
    uint2 dim;
    _MorphTypePyramidL5_RW.GetDimensions(dim.x, dim.y);
    if (id.x >= dim.x || id.y >= dim.y) return;
    uint2 src = id.xy * 2u;
    uint m0 = _MorphTypePyramidL4[src + uint2(0, 0)];
    uint m1 = _MorphTypePyramidL4[src + uint2(1, 0)];
    uint m2 = _MorphTypePyramidL4[src + uint2(0, 1)];
    uint m3 = _MorphTypePyramidL4[src + uint2(1, 1)];
    _MorphTypePyramidL5_RW[id.xy] = m0 | m1 | m2 | m3;

    float4 c0 = ((m0 & 3u) != 0u) ? _MorphColorPyramidL4[src + uint2(0, 0)] : float4(0,0,0,0);
    float4 c1 = ((m1 & 3u) != 0u) ? _MorphColorPyramidL4[src + uint2(1, 0)] : float4(0,0,0,0);
    float4 c2 = ((m2 & 3u) != 0u) ? _MorphColorPyramidL4[src + uint2(0, 1)] : float4(0,0,0,0);
    float4 c3 = ((m3 & 3u) != 0u) ? _MorphColorPyramidL4[src + uint2(1, 1)] : float4(0,0,0,0);
    float w0 = ((m0 & 3u) != 0u) ? 1.0 : 0.0;
    float w1 = ((m1 & 3u) != 0u) ? 1.0 : 0.0;
    float w2 = ((m2 & 3u) != 0u) ? 1.0 : 0.0;
    float w3 = ((m3 & 3u) != 0u) ? 1.0 : 0.0;
    float totalW = w0 + w1 + w2 + w3;
    _MorphColorPyramidL5_RW[id.xy] = (totalW > 0.0) ? ((c0 + c1 + c2 + c3) / totalW) : float4(0,0,0,0);
}

[numthreads(8, 8, 1)]
void BuildMorphPyramidL6(uint3 id : SV_DispatchThreadID)
{
    uint2 dim;
    _MorphTypePyramidL6_RW.GetDimensions(dim.x, dim.y);
    if (id.x >= dim.x || id.y >= dim.y) return;
    uint2 src = id.xy * 2u;
    uint m0 = _MorphTypePyramidL5[src + uint2(0, 0)];
    uint m1 = _MorphTypePyramidL5[src + uint2(1, 0)];
    uint m2 = _MorphTypePyramidL5[src + uint2(0, 1)];
    uint m3 = _MorphTypePyramidL5[src + uint2(1, 1)];
    _MorphTypePyramidL6_RW[id.xy] = m0 | m1 | m2 | m3;

    float4 c0 = ((m0 & 3u) != 0u) ? _MorphColorPyramidL5[src + uint2(0, 0)] : float4(0,0,0,0);
    float4 c1 = ((m1 & 3u) != 0u) ? _MorphColorPyramidL5[src + uint2(1, 0)] : float4(0,0,0,0);
    float4 c2 = ((m2 & 3u) != 0u) ? _MorphColorPyramidL5[src + uint2(0, 1)] : float4(0,0,0,0);
    float4 c3 = ((m3 & 3u) != 0u) ? _MorphColorPyramidL5[src + uint2(1, 1)] : float4(0,0,0,0);
    float w0 = ((m0 & 3u) != 0u) ? 1.0 : 0.0;
    float w1 = ((m1 & 3u) != 0u) ? 1.0 : 0.0;
    float w2 = ((m2 & 3u) != 0u) ? 1.0 : 0.0;
    float w3 = ((m3 & 3u) != 0u) ? 1.0 : 0.0;
    float totalW = w0 + w1 + w2 + w3;
    _MorphColorPyramidL6_RW[id.xy] = (totalW > 0.0) ? ((c0 + c1 + c2 + c3) / totalW) : float4(0,0,0,0);
}

// ==========================================
// Morphology Passes (Erode & Dilate)
// ==========================================

[numthreads(8, 8, 1)]
void MorphologyErode(uint3 id : SV_DispatchThreadID)
{
    float w, h;
    _MorphColorOut_RW.GetDimensions(w, h);
    if (id.x >= (uint)w || id.y >= (uint)h) return;

    uint originType = _MorphTypeIn[id.xy];
    float4 color = _MorphColorIn[id.xy];

    // 対象ピクセルは OriginType == 0u のみ
    if (originType == 0u)
    {
        bool hasVirtualObjectNeighbor = false;
        int level = min(6, _FinalNeighborhoodSizeMap[id.xy]);
        
        int2 dirs[9] = {
            int2(0, 0), int2(1, 0), int2(1, 1), int2(0, 1), int2(-1, 1),
            int2(-1, 0), int2(-1, -1), int2(0, -1), int2(1, -1)
        };

        for (int i = 0; i < 9; i++)
        {
            uint2 uv_mip = (id.xy >> (uint)level) + dirs[i];
            
            uint mask = 0u;
            if (level == 0) mask = (_MorphTypeIn[uv_mip] == 0u ? 1u : 0u) | (_MorphTypeIn[uv_mip] == 1u ? 2u : 0u) | (_MorphTypeIn[uv_mip] == 2u ? 4u : 0u);
            else if (level == 1) mask = _MorphTypePyramidL1[uv_mip];
            else if (level == 2) mask = _MorphTypePyramidL2[uv_mip];
            else if (level == 3) mask = _MorphTypePyramidL3[uv_mip];
            else if (level == 4) mask = _MorphTypePyramidL4[uv_mip];
            else if (level == 5) mask = _MorphTypePyramidL5[uv_mip];
            else mask = _MorphTypePyramidL6[uv_mip];

            // 近傍に仮想オブジェクト(1u)が1点でも存在するか検証
            if ((mask & 2u) != 0u)
            {
                hasVirtualObjectNeighbor = true;
                break;
            }
        }

        if (hasVirtualObjectNeighbor)
        {
            originType = 1u; // 仮想オブジェクトに戻す
            color = float4(0, 0, 0, 0);
        }
    }

    _MorphTypeOut_RW[id.xy] = originType;
    _MorphColorOut_RW[id.xy] = color;
}

[numthreads(8, 8, 1)]
void MorphologyDilate(uint3 id : SV_DispatchThreadID)
{
    float w, h;
    _MorphColorOut_RW.GetDimensions(w, h);
    if (id.x >= (uint)w || id.y >= (uint)h) return;

    uint originType = _MorphTypeIn[id.xy];
    float4 color = _MorphColorIn[id.xy];

    // 対象ピクセルは OriginType == 1u のみ
    if (originType == 1u)
    {
        float4 sumColor = float4(0, 0, 0, 0);
        float totalWeight = 0.0;
        int level = min(6, _FinalNeighborhoodSizeMap[id.xy]);

        int2 dirs[9] = {
            int2(0, 0), int2(1, 0), int2(1, 1), int2(0, 1), int2(-1, 1),
            int2(-1, 0), int2(-1, -1), int2(0, -1), int2(1, -1)
        };

        for (int i = 0; i < 9; i++)
        {
            uint2 uv_mip = (id.xy >> (uint)level) + dirs[i];

            uint mask = 0u;
            float4 c = float4(0, 0, 0, 0);

            if (level == 0) 
            {
                uint t = _MorphTypeIn[uv_mip];
                mask = (t == 0u ? 1u : 0u) | (t == 1u ? 2u : 0u);
                c = (t == 0u) ? _MorphColorIn[uv_mip] : float4(0,0,0,0);
            }
            else if (level == 1) { mask = _MorphTypePyramidL1[uv_mip]; c = _MorphColorPyramidL1[uv_mip]; }
            else if (level == 2) { mask = _MorphTypePyramidL2[uv_mip]; c = _MorphColorPyramidL2[uv_mip]; }
            else if (level == 3) { mask = _MorphTypePyramidL3[uv_mip]; c = _MorphColorPyramidL3[uv_mip]; }
            else if (level == 4) { mask = _MorphTypePyramidL4[uv_mip]; c = _MorphColorPyramidL4[uv_mip]; }
            else if (level == 5) { mask = _MorphTypePyramidL5[uv_mip]; c = _MorphColorPyramidL5[uv_mip]; }
            else                 { mask = _MorphTypePyramidL6[uv_mip]; c = _MorphColorPyramidL6[uv_mip]; }

            // 近傍に実点群(0u)が1点でも存在するか検証
            if ((mask & 1u) != 0u)
            {
                sumColor += c;
                totalWeight += 1.0;
            }
        }

        if (totalWeight > 0.0)
        {
            originType = 0u; // 実点群に置き換える
            color = float4(0, 0, 0, 1.0);
        }
    }

    _MorphTypeOut_RW[id.xy] = originType;
    _MorphColorOut_RW[id.xy] = color;
}

[numthreads(8, 8, 1)]
void MorphologyCopy(uint3 id : SV_DispatchThreadID)
{
    float w, h;
    _MorphColorOut_RW.GetDimensions(w, h);
    if (id.x >= (uint)w || id.y >= (uint)h) return;

    _MorphColorOut_RW[id.xy] = _MorphColorIn[id.xy];
    _MorphTypeOut_RW[id.xy] = _MorphTypeIn[id.xy];
}

#endif // PCD_OCCLUSION_KERNELS_FILLHOLES_INCLUDED