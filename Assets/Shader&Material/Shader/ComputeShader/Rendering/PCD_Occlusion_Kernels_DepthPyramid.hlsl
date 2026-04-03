#ifndef PCD_OCCLUSION_KERNELS_DEPTHPYRAMID_INCLUDED
#define PCD_OCCLUSION_KERNELS_DEPTHPYRAMID_INCLUDED

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

#endif // PCD_OCCLUSION_KERNELS_DEPTHPYRAMID_INCLUDED
