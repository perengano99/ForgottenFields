using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace FF.Terrain {
    [BurstCompile(CompileSynchronously = true)]
    public struct CompactVerticesJob : IJob {
        [ReadOnly] public NativeArray<Vertex> denseVertices;
        public NativeList<Vertex> compactedVertices;
        public NativeArray<int> vertexMap;

        public void Execute() {
            for (int i = 0; i < denseVertices.Length; i++) {
                Vertex v = denseVertices[i];
                if (v.material != 0) {
                    int newIndex = compactedVertices.Length;
                    compactedVertices.Add(v);
                    vertexMap[i] = newIndex;
                }
                else vertexMap[i] = -1;
            }
        }
    }

    [BurstCompile(CompileSynchronously = true)]
    public struct GenerateQuadsJob : IJobParallelFor {
        [ReadOnly] public NativeArray<VoxelData> voxels;
        [ReadOnly] public NativeArray<int> vertexMap;
        public int3 logicalChunkSize;
        public NativeList<int>.ParallelWriter indices;

        public void Execute(int index) {
            int3 p = ToCoord(index, logicalChunkSize);

            for (int axis = 0; axis < 3; axis++) {
                int3 dir = axis == 0 ? new int3(1, 0, 0) : axis == 1 ? new int3(0, 1, 0) : new int3(0, 0, 1);

                float d0 = SampleDensity(p);
                float d1 = SampleDensity(p + dir);
                bool hasCross = (d0 > 0f && d1 < 0f) || (d0 < 0f && d1 > 0f);
                if (!hasCross) continue;

                int offsetStart = axis * 4;
                int4 quadVertexIndices = -1;
                bool valid = true;

                for (int i = 0; i < 4; i++) {
                    int3 c = p + DCTables.QuadAdjacencyOffsets[offsetStart + i];
                    if (!InRange(c, logicalChunkSize)) {
                        valid = false;
                        break;
                    }

                    int mapIndex = ToIndex(c, logicalChunkSize);
                    int mappedVertex = vertexMap[mapIndex];
                    if (mappedVertex == -1) {
                        valid = false;
                        break;
                    }

                    quadVertexIndices[i] = mappedVertex;
                }

                if (!valid) continue;

                int[] triangulation = d0 > 0f ? DCTables.BaseTriangulation : DCTables.FlippedTriangulation;
                for (int t = 0; t < 6; t++)
                    indices.AddNoResize(quadVertexIndices[triangulation[t]]);
            }
        }

        float SampleDensity(int3 logicalSample) {
            int3 physicalSize = logicalChunkSize + 2;
            int3 physical = logicalSample + 1;
            return voxels[ToIndex(physical, physicalSize)].density;
        }

        static int ToIndex(int3 p, int3 size) {
            return p.x + size.x * (p.y + size.y * p.z);
        }

        static int3 ToCoord(int index, int3 size) {
            int x = index % size.x;
            int y = (index / size.x) % size.y;
            int z = index / (size.x * size.y);
            return new int3(x, y, z);
        }

        static bool InRange(int3 p, int3 size) {
            return p.x >= 0 && p.y >= 0 && p.z >= 0 && p.x < size.x && p.y < size.y && p.z < size.z;
        }
    }

    [BurstCompile(CompileSynchronously = true)]
    public struct BasicTerrainSDFJob : IJobParallelFor {
        public NativeArray<VoxelData> voxels;
        public int3 chunkCoord;
        public int logicalSize;
        public int padding;

        public void Execute(int index) {
            int physicalSize = logicalSize + (padding * 2);

            int x = index % physicalSize;
            int y = (index / physicalSize) % physicalSize;
            int z = index / (physicalSize * physicalSize);
            int3 localPhysicalPos = new int3(x, y, z);

            int3 worldPos = (chunkCoord * logicalSize) + localPhysicalPos - padding;

            float density = 16f - worldPos.y;
            uint material = (uint)(density > 0f ? 1 : 0);

            voxels[index] = new VoxelData {
                density = density,
                material = material,
                flags = 0,
                properties = 0
            };
        }
    }
}
