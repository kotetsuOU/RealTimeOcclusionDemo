// Helper Functions
#ifndef PCD_OCCLUSION_HELPERS_INCLUDED
#define PCD_OCCLUSION_HELPERS_INCLUDED

// ==========================================
// Level 1用: 物理点群(OriginType == 0u)のみを抽出し、最小深度ベクトルを返す
// 【新規性要素】ジオメトリバッファを用いた構築時フィルタリング
// ==========================================
float4 ZMinPhysicalDownsample(Texture2D<float4> posTex, Texture2D<uint> typeTex, uint2 uv)
{
    float4 p0 = posTex[uv + uint2(0, 0)]; uint t0 = typeTex[uv + uint2(0, 0)];
    float4 p1 = posTex[uv + uint2(1, 0)]; uint t1 = typeTex[uv + uint2(1, 0)];
    float4 p2 = posTex[uv + uint2(0, 1)]; uint t2 = typeTex[uv + uint2(0, 1)];
    float4 p3 = posTex[uv + uint2(1, 1)]; uint t3 = typeTex[uv + uint2(1, 1)];

    bool useTagOpt = (_EnableTagBasedOptimization > 0);

    float4 minP = useTagOpt ? float4(0.0, 0.0, 0.0, 1e9) : p0;
    
    if (useTagOpt)
    {
        if (t0 == 0u && p0.w < minP.w) minP = p0;
        if (t1 == 0u && p1.w < minP.w) minP = p1;
        if (t2 == 0u && p2.w < minP.w) minP = p2;
        if (t3 == 0u && p3.w < minP.w) minP = p3;
    }
    else
    {
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
        return float4(0, 0, 0, 1e9);

    uint2 uv_mip = (uint2)target_uv;
    
    float4 result = float4(0, 0, 0, 1e9);
    
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
            return float4(0, 0, 0, 1e9);
        result = _ViewPositionMap[(uint2)full_target];
    }
    
    return result;
}

// ==========================================
// ソーベルフィルタによる深度勾配計算（float4 ピラミッド対応版）
// .w 成分をビュー空間深度として使用。センチネル値(1e9)検知で勾配ゼロ返却。
// ※ _GradientThreshold_g_th はメートル単位の勾配に対応するため再チューニングが必要
// ==========================================
float SobelOnPyramid(Texture2D<float4> pyramidTex, uint2 uv)
{
    uint2 dim;
    pyramidTex.GetDimensions(dim.x, dim.y);

    float tl = pyramidTex[clamp(uv + int2(-1, -1), 0, dim - 1)].w;
    float t  = pyramidTex[clamp(uv + int2( 0, -1), 0, dim - 1)].w;
    float tr = pyramidTex[clamp(uv + int2( 1, -1), 0, dim - 1)].w;
    float l  = pyramidTex[clamp(uv + int2(-1,  0), 0, dim - 1)].w;
    float r  = pyramidTex[clamp(uv + int2( 1,  0), 0, dim - 1)].w;
    float bl = pyramidTex[clamp(uv + int2(-1,  1), 0, dim - 1)].w;
    float b  = pyramidTex[clamp(uv + int2( 0,  1), 0, dim - 1)].w;
    float br = pyramidTex[clamp(uv + int2( 1,  1), 0, dim - 1)].w;

    // センチネル値(1e9)が混入している領域は有効な表面ではないため勾配0とする
    const float SENTINEL_THRESHOLD = 1e8;
    if (tl > SENTINEL_THRESHOLD || t > SENTINEL_THRESHOLD || tr > SENTINEL_THRESHOLD ||
        l  > SENTINEL_THRESHOLD || r > SENTINEL_THRESHOLD ||
        bl > SENTINEL_THRESHOLD || b > SENTINEL_THRESHOLD || br > SENTINEL_THRESHOLD)
    {
        return 0.0;
    }

    float Gx = (tr + 2.0 * r + br) - (tl + 2.0 * l + bl);
    float Gy = (bl + 2.0 * b + br) - (tl + 2.0 * t + tr);
    return sqrt(Gx * Gx + Gy * Gy);
}

#endif // PCD_OCCLUSION_HELPERS_INCLUDED