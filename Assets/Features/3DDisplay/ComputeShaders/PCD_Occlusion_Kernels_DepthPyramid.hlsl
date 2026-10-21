#ifndef PCD_OCCLUSION_KERNELS_DEPTHPYRAMID_INCLUDED
#define PCD_OCCLUSION_KERNELS_DEPTHPYRAMID_INCLUDED

// 8a-1. Build Depth Pyramid L1
// 【構築時フィルタリング】_ViewPositionMapから物理点群(OriginType==0u)のみを抽出し、
// ビュー空間座標(xyz)と深度(w)をfloat4としてダウンスケール格納する
[numthreads(8, 8, 1)]
void BuildDepthPyramidL1(uint3 id : SV_DispatchThreadID)
{
    uint2 dim;
    _DepthPyramidL1_RW.GetDimensions(dim.x, dim.y);
    if (id.x >= dim.x || id.y >= dim.y)
        return;
    _DepthPyramidL1_RW[id.xy] = ZMinPhysicalDownsample(_ViewPositionMap, _OriginTypeMap, id.xy * 2u);
}

// 8a-2. Build Depth Pyramid L2 (1/4 レベル)
// L1（物理点群のみ）をさらにダウンサンプルし、最小深度ベクトルを選択
[numthreads(8, 8, 1)]
void BuildDepthPyramidL2(uint3 id : SV_DispatchThreadID)
{
    uint2 dim;
    _DepthPyramidL2_RW.GetDimensions(dim.x, dim.y);
    if (id.x >= dim.x || id.y >= dim.y)
        return;
    _DepthPyramidL2_RW[id.xy] = ZMinPositionDownsample(_DepthPyramidL1, id.xy * 2u);
}

// 8a-3. Build Depth Pyramid L3 (1/8 レベル)
[numthreads(8, 8, 1)]
void BuildDepthPyramidL3(uint3 id : SV_DispatchThreadID)
{
    uint2 dim;
    _DepthPyramidL3_RW.GetDimensions(dim.x, dim.y);
    if (id.x >= dim.x || id.y >= dim.y)
        return;
    _DepthPyramidL3_RW[id.xy] = ZMinPositionDownsample(_DepthPyramidL2, id.xy * 2u);
}

// 8a-4. Build Depth Pyramid L4
[numthreads(8, 8, 1)]
void BuildDepthPyramidL4(uint3 id : SV_DispatchThreadID)
{
    uint2 dim;
    _DepthPyramidL4_RW.GetDimensions(dim.x, dim.y);
    if (id.x >= dim.x || id.y >= dim.y)
        return;
    _DepthPyramidL4_RW[id.xy] = ZMinPositionDownsample(_DepthPyramidL3, id.xy * 2u);
}

// 8a-5. Build Depth Pyramid L5
[numthreads(8, 8, 1)]
void BuildDepthPyramidL5(uint3 id : SV_DispatchThreadID)
{
    uint2 dim;
    _DepthPyramidL5_RW.GetDimensions(dim.x, dim.y);
    if (id.x >= dim.x || id.y >= dim.y)
        return;
    _DepthPyramidL5_RW[id.xy] = ZMinPositionDownsample(_DepthPyramidL4, id.xy * 2u);
}

// 8a-6. Build Depth Pyramid L6
[numthreads(8, 8, 1)]
void BuildDepthPyramidL6(uint3 id : SV_DispatchThreadID)
{
    uint2 dim;
    _DepthPyramidL6_RW.GetDimensions(dim.x, dim.y);
    if (id.x >= dim.x || id.y >= dim.y)
        return;
    _DepthPyramidL6_RW[id.xy] = ZMinPositionDownsample(_DepthPyramidL5, id.xy * 2u);
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
    uint2 gridID = fullResUV / GRID_SIZE;
    int level = max(_MinSearchLevel, _FilteredGridLevelMap[gridID]);

    // 仮想物体 (originType == 1u) 以外のピクセル (物理点群 0u や背景 2u) は
    // オクルージョン判定を行わないため、重いソーベル勾配計算をスキップして即座に終了する
    uint originType = _OriginTypeMap[fullResUV];
    if (originType != 1u)
    {
        _CorrectedNeighborhoodSizeMap_RW[fullResUV] = level;
        return;
    }

    int correctedLevel = level;

    // 粗いレベル（大きい探索半径）から細かいレベルへ向かって回帰的にチェックし、境界を絞り込む
    for (int l = level; l > 0; --l)
    {
        float gradient = 0.0;
        uint2 uv_lowres = fullResUV >> (uint)l;

        if (l == 1)      gradient = SobelOnPyramid(_DepthPyramidL1, uv_lowres);
        else if (l == 2) gradient = SobelOnPyramid(_DepthPyramidL2, uv_lowres);
        else if (l == 3) gradient = SobelOnPyramid(_DepthPyramidL3, uv_lowres);
        else if (l == 4) gradient = SobelOnPyramid(_DepthPyramidL4, uv_lowres);
        else if (l == 5) gradient = SobelOnPyramid(_DepthPyramidL5, uv_lowres);
        else             gradient = SobelOnPyramid(_DepthPyramidL6, uv_lowres);

        if (gradient > _GradientThreshold_g_th)
        {
            // このレベルの探索範囲内にはエッジが存在するため、さらに1段階縮小する
            correctedLevel = l - 1;
        }
        else
        {
            // このレベルの探索範囲内にはエッジが存在しないため、これ以上縮小する必要はない
            break;
        }
    }
    _CorrectedNeighborhoodSizeMap_RW[fullResUV] = correctedLevel;
}

#endif // PCD_OCCLUSION_KERNELS_DEPTHPYRAMID_INCLUDED
