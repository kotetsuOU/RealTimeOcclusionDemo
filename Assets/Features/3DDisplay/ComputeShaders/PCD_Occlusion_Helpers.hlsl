// Helper Functions
#ifndef PCD_OCCLUSION_HELPERS_INCLUDED
#define PCD_OCCLUSION_HELPERS_INCLUDED

// ==========================================
// Level 1用: 物理点群(OriginType == 0u)のみを抽出し、最小深度ベクトルを返す
// 【新規性要素】ジオメトリバッファを用いた構築時フィルタリング
// ==========================================
float4 ZMinPhysicalDownsample(Texture2D<float4> posTex, Texture2D<uint> typeTex, uint2 uv)
{
    float4 minP = float4(0.0, 0.0, 0.0, SENTINEL_DEPTH);

    if (_EnableTagBasedOptimization > 0)
    {
        // 先に軽量な OriginTypeMap (R8_UInt) のみを読み取り、物理点群(0u)が存在しないブロックの ViewPositionMap 読み取りを回避する
        uint t0 = typeTex[uv + uint2(0, 0)];
        uint t1 = typeTex[uv + uint2(1, 0)];
        uint t2 = typeTex[uv + uint2(0, 1)];
        uint t3 = typeTex[uv + uint2(1, 1)];

        if (t0 == 0u) { float4 p0 = posTex[uv + uint2(0, 0)]; if (p0.w < minP.w) minP = p0; }
        if (t1 == 0u) { float4 p1 = posTex[uv + uint2(1, 0)]; if (p1.w < minP.w) minP = p1; }
        if (t2 == 0u) { float4 p2 = posTex[uv + uint2(0, 1)]; if (p2.w < minP.w) minP = p2; }
        if (t3 == 0u) { float4 p3 = posTex[uv + uint2(1, 1)]; if (p3.w < minP.w) minP = p3; }
    }
    else
    {
        float4 p0 = posTex[uv + uint2(0, 0)];
        float4 p1 = posTex[uv + uint2(1, 0)];
        float4 p2 = posTex[uv + uint2(0, 1)];
        float4 p3 = posTex[uv + uint2(1, 1)];

        minP = p0;
        if (p1.w < minP.w) minP = p1;
        if (p2.w < minP.w) minP = p2;
        if (p3.w < minP.w) minP = p3;
    }

    return minP;
}

// ==========================================
// Level 2〜4用: 物理点群のみで構成されたピラミッドからの単純ダウンサンプル
// .w 成分で最小深度のベクトル全体を選択
// ==========================================
float4 ZMinPositionDownsample(Texture2D<float4> inputTex, uint2 uv)
{
    float4 p0 = inputTex[uv + uint2(0, 0)];
    float4 p1 = inputTex[uv + uint2(1, 0)];
    float4 p2 = inputTex[uv + uint2(0, 1)];
    float4 p3 = inputTex[uv + uint2(1, 1)];
    
    float4 minP = p0;
    if (p1.w < minP.w) minP = p1;
    if (p2.w < minP.w) minP = p2;
    if (p3.w < minP.w) minP = p3;
    return minP;
}

// ==========================================
// ピラミッドレベルに応じた座標フェッチ
// level 0 は _ViewPositionMap（フル解像度）、level 1〜4 はピラミッドテクスチャを参照
// ==========================================
float4 FetchPyramidPosition(int level, uint2 uv_base, int2 offset)
{
    int2 base_signed = int2(uv_base >> (uint)level);
    int2 target_uv = base_signed + offset;
    if (target_uv.x < 0 || target_uv.y < 0)
        return float4(0, 0, 0, SENTINEL_DEPTH);

    uint2 uv_mip = (uint2)target_uv;
    
    float4 result = float4(0, 0, 0, SENTINEL_DEPTH);
    
    if (level == 1) result = _DepthPyramidL1[uv_mip];
    else if (level == 2) result = _DepthPyramidL2[uv_mip];
    else if (level == 3) result = _DepthPyramidL3[uv_mip];
    else if (level == 4) result = _DepthPyramidL4[uv_mip];
    else if (level == 5) result = _DepthPyramidL5[uv_mip];
    else if (level == 6) result = _DepthPyramidL6[uv_mip];
    else
    {
        int2 full_target = int2(uv_base) + offset;
        if (full_target.x < 0 || full_target.y < 0)
            return float4(0, 0, 0, SENTINEL_DEPTH);
        result = _ViewPositionMap[(uint2)full_target];
    }
    
    return result;
}

// ==========================================
// ソーベルフィルタによる深度勾配の二乗計算（float4 ピラミッド対応版）
// sqrt を省略し、呼び出し側で閾値の二乗と比較することで演算コストを削減する。
// ==========================================
float SobelOnPyramidSq(Texture2D<float4> pyramidTex, uint2 uv)
{
    uint2 dim;
    pyramidTex.GetDimensions(dim.x, dim.y);
    int2 iuv = (int2)uv;
    int2 maxUV = (int2)dim - 1;

    float tl = pyramidTex[clamp(iuv + int2(-1, -1), 0, maxUV)].w;
    float t  = pyramidTex[clamp(iuv + int2( 0, -1), 0, maxUV)].w;
    float tr = pyramidTex[clamp(iuv + int2( 1, -1), 0, maxUV)].w;
    float l  = pyramidTex[clamp(iuv + int2(-1,  0), 0, maxUV)].w;
    float r  = pyramidTex[clamp(iuv + int2( 1,  0), 0, maxUV)].w;
    float bl = pyramidTex[clamp(iuv + int2(-1,  1), 0, maxUV)].w;
    float b  = pyramidTex[clamp(iuv + int2( 0,  1), 0, maxUV)].w;
    float br = pyramidTex[clamp(iuv + int2( 1,  1), 0, maxUV)].w;

    // センチネル値(SENTINEL_DEPTH)が混入している領域は有効な表面ではないため勾配0とする
    float maxNeighbor = max(max(max(tl, t), max(tr, l)), max(max(r, bl), max(b, br)));
    if (maxNeighbor > SENTINEL_THRESHOLD)
    {
        return 0.0;
    }

    float Gx = (tr + 2.0 * r + br) - (tl + 2.0 * l + bl);
    float Gy = (bl + 2.0 * b + br) - (tl + 2.0 * t + tr);
    return Gx * Gx + Gy * Gy;
}

#endif // PCD_OCCLUSION_HELPERS_INCLUDED