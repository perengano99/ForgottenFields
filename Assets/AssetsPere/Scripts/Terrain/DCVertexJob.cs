using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace FF.Terrain {
    [BurstCompile(CompileSynchronously = true)]
    public struct GenerateVerticesJob : IJobParallelFor {
        [ReadOnly] public NativeArray<VoxelData> voxels;
        public int3 logicalChunkSize;
        public NativeArray<Vertex> generatedVertices;

        public void Execute(int index) {
            int3 logicalPos = ToCoord(index, logicalChunkSize);
            int3 physicalSize = logicalChunkSize + 2;
            int3 cellMin = logicalPos + 1;

            float3 sumPos = 0f;
            float3 sumNrm = 0f;
            int hitCount = 0;
            uint material = 0;

            for (int edgeIndex = 0; edgeIndex < DCTables.EdgeVertices.Length; edgeIndex++) {
                int2 edge = DCTables.EdgeVertices[edgeIndex];
                int3 c0 = cellMin + DCTables.VoxelVertices[edge.x];
                int3 c1 = cellMin + DCTables.VoxelVertices[edge.y];

                VoxelData v0 = voxels[ToIndex(c0, physicalSize)];
                VoxelData v1 = voxels[ToIndex(c1, physicalSize)];

                float d0 = v0.density;
                float d1 = v1.density;
                bool crosses = (d0 < 0f && d1 > 0f) || (d0 > 0f && d1 < 0f);
                if (!crosses) continue;

                float t = d0 / (d0 - d1);
                float3 p0 = DCTables.VoxelVertices[edge.x];
                float3 p1 = DCTables.VoxelVertices[edge.y];
                float3 cutPos = math.lerp(p0, p1, t);

                float3 n0 = CalcNormal(c0, voxels, physicalSize);
                float3 n1 = CalcNormal(c1, voxels, physicalSize);
                float3 cutNrm = math.normalizesafe(math.lerp(n0, n1, t));

                sumPos += cutPos;
                sumNrm += cutNrm;
                hitCount++;

                if (material == 0)
                    if (d0 > 0f) material = v0.material;
                    else if (d1 > 0f) material = v1.material;
            }

            if (hitCount == 0) {
                generatedVertices[index] = new Vertex {
                    position = Vector3.zero,
                    normal = Vector3.zero,
                    material = 0
                };
                return;
            }

            float invCount = 1f / hitCount;
            float3 finalPos = sumPos * invCount;
            float3 finalNrm = math.normalizesafe(sumNrm);

            generatedVertices[index] = new Vertex {
                position = new Vector3(finalPos.x, finalPos.y, finalPos.z),
                normal = new Vector3(finalNrm.x, finalNrm.y, finalNrm.z),
                material = material
            };
        }

        static float3 CalcNormal(int3 p, NativeArray<VoxelData> data, int3 size) {
            float dx = SampleDensity(p + new int3(1, 0, 0), data, size) - SampleDensity(p + new int3(-1, 0, 0), data, size);
            float dy = SampleDensity(p + new int3(0, 1, 0), data, size) - SampleDensity(p + new int3(0, -1, 0), data, size);
            float dz = SampleDensity(p + new int3(0, 0, 1), data, size) - SampleDensity(p + new int3(0, 0, -1), data, size);
            return math.normalizesafe(new float3(dx, dy, dz));
        }

        static float SampleDensity(int3 p, NativeArray<VoxelData> data, int3 size) {
            int3 clamped = math.clamp(p, 0, size - 1);
            return data[ToIndex(clamped, size)].density;
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
    }
}
