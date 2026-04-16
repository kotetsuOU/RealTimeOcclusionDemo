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

    float rawDepth = _VirtualDepthMap[id.xy];
    float cameraDepth = _IsReversedZ > 0 ? (1.0 - rawDepth) : rawDepth;

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

    InterlockedAdd(_StaticMeshCounter_RW[0], 1u);
}

int _DebugDisplayMode;

// 13. Visualize Occlusion Debug
// OcclusionValueMapの値をREADME/Exporterと同じルールでカラー変換し、
// 画面表示用のOriginMapへ出力する。
[numthreads(8, 8, 1)]
void VisualizeOcclusionDebug(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint)_ScreenParams.x || id.y >= (uint)_ScreenParams.y)
        return;

    // mode 1: PixelTagMap(判定後), mode 2: OcclusionMap(生値)
    if (_DebugDisplayMode == 2)
    {
        float vOcclusion = _OcclusionValueMap_RW[id.xy].y;
        if (isnan(vOcclusion) || isinf(vOcclusion))
        {
            _OriginMap_RW[id.xy] = float4(0, 0, 0, 1);
            return;
        }

        float v = saturate(vOcclusion);
        if (v <= 0.0001)
        {
            _OriginMap_RW[id.xy] = float4(0.5, 0.5, 0.5, 1); // gray
            return;
        }

        const float rangeMin = 0.0;
        const float rangeMax = 1.0;
        const float steps = 15.0;
        const float stepSize = (rangeMax - rangeMin) / steps;

        int paletteIndex;
        if (v >= rangeMax)
        {
            paletteIndex = 15;
        }
        else if (v <= rangeMin)
        {
            paletteIndex = 1;
        }
        else
        {
            paletteIndex = 1 + clamp((int)((v - rangeMin) / stepSize), 0, 14);
        }

        float3 c;
        switch (paletteIndex)
        {
            case 1: c = float3(0.00, 0.00, 0.50); break;
            case 2: c = float3(0.00, 0.00, 0.75); break;
            case 3: c = float3(0.00, 0.00, 1.00); break;
            case 4: c = float3(0.00, 0.25, 1.00); break;
            case 5: c = float3(0.00, 0.50, 1.00); break;
            case 6: c = float3(0.00, 0.75, 1.00); break;
            case 7: c = float3(0.00, 1.00, 1.00); break;
            case 8: c = float3(0.25, 1.00, 0.75); break;
            case 9: c = float3(0.50, 1.00, 0.50); break;
            case 10: c = float3(0.75, 1.00, 0.25); break;
            case 11: c = float3(1.00, 1.00, 0.00); break;
            case 12: c = float3(1.00, 0.75, 0.00); break;
            case 13: c = float3(1.00, 0.50, 0.00); break;
            case 14: c = float3(1.00, 0.25, 0.00); break;
            default: c = float3(1.00, 0.00, 0.00); break;
        }

        _OriginMap_RW[id.xy] = float4(c, 1);
        return;
    }

    float labelValue = _OcclusionValueMap_RW[id.xy].x;
    float v = labelValue;

    if (isnan(v) || isinf(v))
    {
        _OriginMap_RW[id.xy] = float4(0, 0, 0, 1);
        return;
    }

    // 特殊ラベルの色分けは常に x(label) を使う
    if (labelValue >= 1.9)
    {
        _OriginMap_RW[id.xy] = float4(1, 0, 1, 1); // magenta
        return;
    }

    if (labelValue <= -2.5)
    {
        _OriginMap_RW[id.xy] = float4(0, 1, 0, 1); // green
        return;
    }

    if (labelValue <= -1.5)
    {
        _OriginMap_RW[id.xy] = float4(0, 1, 1, 1); // cyan
        return;
    }

    if (labelValue < -0.5)
    {
        _OriginMap_RW[id.xy] = float4(1, 1, 1, 1); // white
        return;
    }

    if (v <= 0.0001)
    {
        _OriginMap_RW[id.xy] = float4(0.5, 0.5, 0.5, 1); // gray
        return;
    }

    const float rangeMin = 0.0;
    const float rangeMax = 1.0;
    const float steps = 15.0;
    const float stepSize = (rangeMax - rangeMin) / steps;

    int paletteIndex;
    if (v >= rangeMax)
    {
        paletteIndex = 15;
    }
    else if (v <= rangeMin)
    {
        paletteIndex = 1;
    }
    else
    {
        paletteIndex = 1 + clamp((int)((v - rangeMin) / stepSize), 0, 14);
    }

    float3 c;
    switch (paletteIndex)
    {
        case 1: c = float3(0.00, 0.00, 0.50); break;
        case 2: c = float3(0.00, 0.00, 0.75); break;
        case 3: c = float3(0.00, 0.00, 1.00); break;
        case 4: c = float3(0.00, 0.25, 1.00); break;
        case 5: c = float3(0.00, 0.50, 1.00); break;
        case 6: c = float3(0.00, 0.75, 1.00); break;
        case 7: c = float3(0.00, 1.00, 1.00); break;
        case 8: c = float3(0.25, 1.00, 0.75); break;
        case 9: c = float3(0.50, 1.00, 0.50); break;
        case 10: c = float3(0.75, 1.00, 0.25); break;
        case 11: c = float3(1.00, 1.00, 0.00); break;
        case 12: c = float3(1.00, 0.75, 0.00); break;
        case 13: c = float3(1.00, 0.50, 0.00); break;
        case 14: c = float3(1.00, 0.25, 0.00); break;
        default: c = float3(1.00, 0.00, 0.00); break;
    }

    _OriginMap_RW[id.xy] = float4(c, 1);
}

#endif // PCD_OCCLUSION_KERNELS_POST_INCLUDED
