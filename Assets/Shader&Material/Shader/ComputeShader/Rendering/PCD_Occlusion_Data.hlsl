// Data Structures
#ifndef PCD_OCCLUSION_DATA_INCLUDED
#define PCD_OCCLUSION_DATA_INCLUDED

struct Point
{
    float3 position;
    float3 color;
    uint originType; // 0 = PointCloud, 1 = StaticMesh
};

// Buffers and Textures
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

// Uniforms
uint _PointCount;
float4 _ScreenParams;
float4x4 _ViewMatrix;
float4x4 _ProjectionMatrix;
float _DensityThreshold_e;
float _NeighborhoodParam_p_prime;
float _GradientThreshold_g_th;
float _OcclusionThreshold;
float _OcclusionFadeWidth;

#define GRID_SIZE 16u
#define DEPTH_MAX_UINT 0x7FFFFFFFu

groupshared uint shared_z_min;
groupshared uint shared_point_count;

#endif // PCD_OCCLUSION_DATA_INCLUDED