// Helper Functions
#ifndef PCD_OCCLUSION_HELPERS_INCLUDED
#define PCD_OCCLUSION_HELPERS_INCLUDED

// ピラミッド構築時に 2x2 px 領域内の最小深度値(手前)を抽出してダウンスケール
uint ZMinDownsample(Texture2D<uint> inputTex, uint2 uv)
{
    uint z0 = inputTex[uv + uint2(0, 0)];
    uint z1 = inputTex[uv + uint2(1, 0)];
    uint z2 = inputTex[uv + uint2(0, 1)];
    uint z3 = inputTex[uv + uint2(1, 1)];
    return min(min(z0, z1), min(z2, z3));
}

// ソーベルフィルタを使用して深度画像の勾配量(エッジ強度)を算出
// - pyramidTex: 計算対象の深度ピラミッドテクスチャ
// - x, y 平面の勾配(Gx, Gy)の大きさを返す
float SobelOnPyramid(Texture2D<uint> pyramidTex, uint2 uv)
{
    uint2 dim;
    pyramidTex.GetDimensions(dim.x, dim.y);
    float tl = (float) pyramidTex[clamp(uv + int2(-1, -1), 0, dim - 1)] / (float) DEPTH_MAX_UINT;
    float t = (float) pyramidTex[clamp(uv + int2(0, -1), 0, dim - 1)] / (float) DEPTH_MAX_UINT;
    float tr = (float) pyramidTex[clamp(uv + int2(1, -1), 0, dim - 1)] / (float) DEPTH_MAX_UINT;
    float l = (float) pyramidTex[clamp(uv + int2(-1, 0), 0, dim - 1)] / (float) DEPTH_MAX_UINT;
    float r = (float) pyramidTex[clamp(uv + int2(1, 0), 0, dim - 1)] / (float) DEPTH_MAX_UINT;
    float bl = (float) pyramidTex[clamp(uv + int2(-1, 1), 0, dim - 1)] / (float) DEPTH_MAX_UINT;
    float b = (float) pyramidTex[clamp(uv + int2(0, 1), 0, dim - 1)] / (float) DEPTH_MAX_UINT;
    float br = (float) pyramidTex[clamp(uv + int2(1, 1), 0, dim - 1)] / (float) DEPTH_MAX_UINT;

    const float MAX_DEPTH_NORMALIZED = 1.0 + 0.1;
    // 近傍8ピクセルのいずれかが描画範囲外やクリア深度の時は勾配なしとして扱う
    if (tl > MAX_DEPTH_NORMALIZED || t > MAX_DEPTH_NORMALIZED || tr > MAX_DEPTH_NORMALIZED ||
        l > MAX_DEPTH_NORMALIZED || r > MAX_DEPTH_NORMALIZED ||
        bl > MAX_DEPTH_NORMALIZED || b > MAX_DEPTH_NORMALIZED || br > MAX_DEPTH_NORMALIZED)
    {
        return 0.0;
    }

    float Gx = (tr + 2.0 * r + br) - (tl + 2.0 * l + bl);
    float Gy = (bl + 2.0 * b + br) - (tl + 2.0 * t + tr);
    return sqrt(Gx * Gx + Gy * Gy);
}

#endif // PCD_OCCLUSION_HELPERS_INCLUDED