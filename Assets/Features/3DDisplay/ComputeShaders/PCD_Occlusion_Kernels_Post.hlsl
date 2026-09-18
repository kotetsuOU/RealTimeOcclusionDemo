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
groupshared uint shared_mesh_counters[64];

[numthreads(8, 8, 1)]
void InitFromCamera(uint3 id : SV_DispatchThreadID, uint groupIndex : SV_GroupIndex)
{
    uint local_hit = 0;

    if (id.x < (uint) _ScreenParams.x && id.y < (uint) _ScreenParams.y)
    {
        uint targetX = id.x;
        if (_IsHalfMirrorEnabled > 0)
        {
            targetX = (uint)_ScreenParams.x - 1 - id.x;
        }
        
        uint2 writeUV = uint2(targetX, id.y);

        // ClearMaps の代わりとして OriginMap もここでクリアしておく
        _OriginMap_RW[writeUV] = float4(0, 0, 0, 1);

        float rawDepth = _VirtualDepthMap[id.xy];
        float cameraDepth = _IsReversedZ > 0 ? (1.0 - rawDepth) : rawDepth;

        if (cameraDepth >= 0.9999)
        {
            _DepthMap_RW[writeUV] = DEPTH_MAX_UINT;
            _ColorMap_RW[writeUV] = float4(0, 0, 0, 0);
            _ViewPositionMap_RW[writeUV] = float4(0, 0, 0, 1e9);
            _OriginTypeMap_RW[writeUV] = 2u;
        }
        else
        {
            uint depth_uint = (uint) (cameraDepth * (float) DEPTH_MAX_UINT);
            _DepthMap_RW[writeUV] = depth_uint;

            float4 cameraColor = _CameraColorTexture[id.xy];
            _ColorMap_RW[writeUV] = float4(cameraColor.rgb, 1.0);

            float2 uv = (float2(id.xy) + 0.5) / _ScreenParams.xy;
            float2 ndc = uv * 2.0 - 1.0;

            // GPU生深度 (rawDepth: 1=近, 0=遠) をクリップ空間 Z に直接渡す
            float4 clipPos = float4(ndc.x, ndc.y, rawDepth, 1.0);
            float4 viewPos = mul(_InverseProjectionMatrix, clipPos);
            viewPos /= viewPos.w;

            // Unity のカメラビュー空間は右手系 (前方が -Z) のため、逆投影結果の Z を反転
            // これにより、点群側の mul(_ViewMatrix, worldPos) とXYZ座標系が完全に一致する
            viewPos.z = -viewPos.z;

            _ViewPositionMap_RW[writeUV] = float4(viewPos.xyz, cameraDepth);
            _OriginTypeMap_RW[writeUV] = 1u;

            local_hit = 1;
        }
    }

    // 共有メモリを用いた高効率な並列リダクションによりアトミック競合を回避
    shared_mesh_counters[groupIndex] = local_hit;
    GroupMemoryBarrierWithGroupSync();

    if (groupIndex < 32)
    {
        shared_mesh_counters[groupIndex] += shared_mesh_counters[groupIndex + 32];
    }
    GroupMemoryBarrierWithGroupSync();

    if (groupIndex < 16)
    {
        shared_mesh_counters[groupIndex] += shared_mesh_counters[groupIndex + 16];
    }
    GroupMemoryBarrierWithGroupSync();

    if (groupIndex < 8)
    {
        shared_mesh_counters[groupIndex] += shared_mesh_counters[groupIndex + 8];
    }
    GroupMemoryBarrierWithGroupSync();

    if (groupIndex < 4)
    {
        shared_mesh_counters[groupIndex] += shared_mesh_counters[groupIndex + 4];
    }
    GroupMemoryBarrierWithGroupSync();

    if (groupIndex < 2)
    {
        shared_mesh_counters[groupIndex] += shared_mesh_counters[groupIndex + 2];
    }
    GroupMemoryBarrierWithGroupSync();

    if (groupIndex == 0)
    {
        uint total_group_hits = shared_mesh_counters[0] + shared_mesh_counters[1];
        if (total_group_hits > 0)
        {
            InterlockedAdd(_StaticMeshCounter_RW[0], total_group_hits);
        }
    }
}

int _DebugDisplayMode;
int _DebugPatternId;
int _DebugSectorId;

// 13. Visualize Occlusion Debug
// OcclusionValueMapの値をREADME/Exporterと同じルールでカラー変換し、
// 画面表示用のOriginMapへ出力する。
[numthreads(8, 8, 1)]
void VisualizeOcclusionDebug(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint)_ScreenParams.x || id.y >= (uint)_ScreenParams.y)
        return;

    // mode 3: 256占有パターン単独二値マスク表示 (指定パターン 0..255 と完全一致する画素を白、他を黒)
    if (_DebugDisplayMode == 3)
    {
        uint val = _NeighborCountMap_RW[id.xy];
        uint isEvaluated = (val >> 12) & 1u;
        if (isEvaluated == 0u)
        {
            _OriginMap_RW[id.xy] = float4(0, 0, 0, 1);
            return;
        }

        uint mask = val & 0xFFu;
        if (_DebugPatternId >= 0 && _DebugPatternId < 256 && mask == (uint)_DebugPatternId)
        {
            _OriginMap_RW[id.xy] = float4(1, 1, 1, 1); // 所属画素: 白
        }
        else
        {
            _OriginMap_RW[id.xy] = float4(0, 0, 0, 1); // 非所属画素: 黒
        }
        return;
    }

    // mode 4: 8セクター二値マスク表示 (指定セクター _DebugSectorId 0..7 の bit が 1 なら白、0 なら黒)
    // 更新: mode 分岐を増やさず、_DebugSectorId の指定により 8bit 占有パターン直接出力 (8) および GPU 実判定マスク直接出力 (9) を統合サポート
    if (_DebugDisplayMode == 4)
    {
        uint val = _NeighborCountMap_RW[id.xy];
        uint isEvaluated = (val >> 12) & 1u;
        if (isEvaluated == 0u)
        {
            _OriginMap_RW[id.xy] = float4(0, 0, 0, 1);
            return;
        }

        uint mask = val & 0xFFu;

        // _DebugSectorId == 8: 全8セクター統合 8-bit グレースケール占有マスク (各画素のパターン m ∈ [0, 255] を直接出力)
        if (_DebugSectorId == 8)
        {
            float p = (float)mask / 255.0;
            _OriginMap_RW[id.xy] = float4(p, p, p, 1.0);
            return;
        }

        // _DebugSectorId == 9: GPU 実遮蔽判定マスク (bit 13: 遮蔽なら白, 可視なら黒)
        if (_DebugSectorId == 9)
        {
            uint isGpuOccluded = (val >> 13) & 1u;
            float p = (isGpuOccluded != 0u) ? 1.0 : 0.0;
            _OriginMap_RW[id.xy] = float4(p, p, p, 1.0);
            return;
        }

        // 従来互換: _DebugSectorId 0..7 (個別セクター二値マスク)
        if (_DebugSectorId >= 0 && _DebugSectorId < 8 && (((mask >> (uint)_DebugSectorId) & 1u) != 0u))
        {
            _OriginMap_RW[id.xy] = float4(1, 1, 1, 1); // セクターが占有(1): 白
        }
        else
        {
            _OriginMap_RW[id.xy] = float4(0, 0, 0, 1); // 非占有(0): 黒
        }
        return;
    }

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
