#ifndef PCD_OCCLUSION_KERNELS_POST_INCLUDED
#define PCD_OCCLUSION_KERNELS_POST_INCLUDED

// 11. Interpolate (Simple Dilation)
// オクルージョンパスを通過後、まだ残ってしまった微小な穴（描画されていないピクセル）に対して
// 直近の近傍ピクセル(3x3)の色を参照して補間(Dilation)をかけ、最終的な画像を埋める。
[numthreads(8, 8, 1)]
void Interpolate(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint) _ScreenParams.x || id.y >= (uint) _ScreenParams.y)
        return;

    float4 centerColor = _OcclusionResultMap[id.xy];

    if (centerColor.a > 0)
    {
        _FinalImage_RW[id.xy] = centerColor;
        return;
    }

    float4 accumulatedColor = float4(0, 0, 0, 0);
    uint count = 0u;
    uint chosenOriginType = 2u;

    for (int y = -1; y <= 1; y++)
    {
        for (int x = -1; x <= 1; x++)
        {
            if (x == 0 && y == 0)
                continue;

            uint2 uv = clamp(id.xy + int2(x, y), 0, _ScreenParams.xy - 1);
            float4 neighborColor = _OcclusionResultMap[uv];

            if (neighborColor.a > 0)
            {
                accumulatedColor += neighborColor;
                count++;

                uint neighborOriginType = _OriginTypeMap[uv];
                if (neighborOriginType < chosenOriginType)
                {
                    chosenOriginType = neighborOriginType;
                }
            }
        }
    }

    if (count > 0u)
    {
        _FinalImage_RW[id.xy] = accumulatedColor / (float) count;

        if (chosenOriginType == 0u)
            _OriginMap_RW[id.xy] = float4(0, 0, 0, 1);
        else if (chosenOriginType == 1u)
            _OriginMap_RW[id.xy] = float4(1, 1, 1, 1);
    }
    else
    {
        _FinalImage_RW[id.xy] = float4(0, 0, 0, 0);
    }
}

// 12. InitFromCamera
// URPなどの描画パイプラインから取得したカメラの現在の深度およびカラーをベースにする。
// 点群を描画する前にこれを初期状態としてマップに反映することで、
// 通常の3D仮想オブジェクト（メッシュ）などが点群と相互に正しく遮蔽(オクルージョン)されるようにする。
[numthreads(8, 8, 1)]
void InitFromCamera(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint) _ScreenParams.x || id.y >= (uint) _ScreenParams.y)
        return;

    float cameraDepth = _VirtualDepthMap[id.xy];

    if (cameraDepth >= 0.9999)
    {
        _DepthMap_RW[id.xy] = DEPTH_MAX_UINT;
        _ColorMap_RW[id.xy] = float4(0, 0, 0, 0);
        _ViewPositionMap_RW[id.xy] = float4(0, 0, 0, 1e9);
        _OriginTypeMap_RW[id.xy] = 2u;
        return;
    }

    uint depth_uint = (uint) (cameraDepth * (float) DEPTH_MAX_UINT);
    _DepthMap_RW[id.xy] = depth_uint;

    float4 cameraColor = _CameraColorTexture[id.xy];
    _ColorMap_RW[id.xy] = float4(cameraColor.rgb, 1.0);

    float2 uv = float2(id.xy) / _ScreenParams.xy;
    float2 ndc = uv * 2.0 - 1.0;
    float4 clipPos = float4(ndc.x, ndc.y, cameraDepth * 2.0 - 1.0, 1.0);
    float4 viewPos = mul(_InverseProjectionMatrix, clipPos);
    viewPos /= viewPos.w;

    _ViewPositionMap_RW[id.xy] = float4(viewPos.xyz, cameraDepth);
    _OriginTypeMap_RW[id.xy] = 1u;
}

#endif // PCD_OCCLUSION_KERNELS_POST_INCLUDED