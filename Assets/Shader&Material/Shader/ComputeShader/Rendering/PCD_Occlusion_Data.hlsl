// Data Structures
#ifndef PCD_OCCLUSION_DATA_INCLUDED
#define PCD_OCCLUSION_DATA_INCLUDED

struct Point
{
    float3 position;    // ワールド座標
    float3 color;       // 点群の色情報
    uint originType;    // ピクセルのソース (0: 点群, 1: 静的メッシュ / 仮想オブジェクト, 2: 背景)
};

// ==========================================
// Buffers and Textures (データ構造体とテクスチャの定義)
// ==========================================
StructuredBuffer<Point> _PointBuffer;
RWTexture2D<float4> _ColorMap_RW;
RWTexture2D<uint> _DepthMap_RW;
RWTexture2D<float4> _ViewPositionMap_RW;
RWTexture2D<uint> _GridZMinMap_RW;
RWTexture2D<float> _DensityMap_RW;
RWTexture2D<int> _GridLevelMap_RW;
RWTexture2D<int> _FilteredGridLevelMap_RW;
RWTexture2D<int> _NeighborhoodSizeMap_RW;
RWTexture2D<uint> _DepthPyramidL1_RW;
RWTexture2D<uint> _DepthPyramidL2_RW;
RWTexture2D<uint> _DepthPyramidL3_RW;
RWTexture2D<uint> _DepthPyramidL4_RW;
RWTexture2D<int> _CorrectedNeighborhoodSizeMap_RW;
RWTexture2D<uint> _OriginTypeMap_RW;
RWTexture2D<float4> _OcclusionResultMap_RW;
RWTexture2D<float> _OcclusionValueMap_RW;
RWTexture2D<float4> _FinalImage_RW;
RWTexture2D<float4> _OriginMap_RW;

Texture2D<float4> _ColorMap;
Texture2D<uint> _DepthMap;
Texture2D<float4> _ViewPositionMap;
Texture2D<uint> _GridZMinMap;
Texture2D<float> _DensityMap;
Texture2D<int> _GridLevelMap;
Texture2D<int> _FilteredGridLevelMap;
Texture2D<int> _NeighborhoodSizeMap;
Texture2D<uint> _DepthPyramidL1;
Texture2D<uint> _DepthPyramidL2;
Texture2D<uint> _DepthPyramidL3;
Texture2D<uint> _DepthPyramidL4;
Texture2D<uint> _OriginTypeMap;
Texture2D<int> _FinalNeighborhoodSizeMap;
Texture2D<float4> _OcclusionResultMap;

// Hybrid virtual depth (URP camera depth)
Texture2D<float> _VirtualDepthMap;
Texture2D<float4> _CameraColorTexture;
int _UseVirtualDepth;
float4x4 _InverseProjectionMatrix;
int _RecordOcclusionDebug;

// Merge Buffers
StructuredBuffer<Point> _MergeSrcBuffer;
RWStructuredBuffer<Point> _MergeDstBuffer;
int _MergeSrcOffset;
int _MergeDstOffset;
int _MergeCopyCount;

// ==========================================
// Uniforms (C#側のPCDRendererFeature等から設定される変数)
// ==========================================
uint _PointCount;                   // 対象点群バッファの要素数
float4 _ScreenParams;               // スクリーン解像度 (x: 幅, y: 高さ)
float4x4 _ViewMatrix;               // ワールド => ビュー変換行列
float4x4 _ProjectionMatrix;         // ビュー => プロジェクション変換行列
float _DensityThreshold_e;          // 密度計算時、表面近傍の点を判定するための厚み閾値
float _NeighborhoodParam_p_prime;   // フィルタリング(サンプリング)範囲を決めるための基本係数
float _GradientThreshold_g_th;      // 輪郭(エッジ)判定の強さ。超える場合は近傍範囲を縮小する
float _OcclusionThreshold;          // オクルージョン強度のカットオフ閾値
float _OcclusionFadeWidth;          // ピクセルを滑らかにフェードアウトさせるための幅

#define GRID_SIZE 16u
#define DEPTH_MAX_UINT 0x7FFFFFFFu

groupshared uint shared_z_min;
groupshared uint shared_point_count;

#endif // PCD_OCCLUSION_DATA_INCLUDED