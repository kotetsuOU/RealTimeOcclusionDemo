#ifndef UNIFIED_VOXEL_STRUCTURES
#define UNIFIED_VOXEL_STRUCTURES

struct Point
{
    float4 position;
    float4 color;
};

struct VoxelData
{
    int3 index;
    int pointCount;
    int dataOffset;
    int writeCounter;
};

int3 GetVoxelIndex(float3 position, float voxelSize)
{
    return int3(
        floor(position.x / voxelSize),
        floor(position.y / voxelSize),
        floor(position.z / voxelSize)
    );
}

uint HashVoxelIndex(int3 voxelIndex, uint hashTableSize)
{
    const uint p1 = 73856093;
    const uint p2 = 19349663;
    const uint p3 = 83492791;
    uint hash = ((uint)voxelIndex.x * p1) ^ ((uint)voxelIndex.y * p2) ^ ((uint)voxelIndex.z * p3);
    return hash % hashTableSize;
}

int FindVoxelDataFast(int3 voxelIndex, 
                      StructuredBuffer<VoxelData> voxelData,
                      StructuredBuffer<int> hashTable,
                      StructuredBuffer<int> hashChains,
                      uint hashTableSize)
{
    uint hash = HashVoxelIndex(voxelIndex, hashTableSize);
    int idx = hashTable[hash];
    
    while (idx != -1 && idx != 0x7FFFFFFF)
    {
        if (all(voxelData[idx].index == voxelIndex))
        {
            return idx;
        }
        idx = hashChains[idx];
    }
    return -1;
}

#endif