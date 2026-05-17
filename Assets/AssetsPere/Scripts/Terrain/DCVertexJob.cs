using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace FF.Terrain {
    [BurstCompile(CompileSynchronously = true)]
    public struct GenerateVerticesJob : IJobParallelFor {
        [ReadOnly] public NativeArray<VoxelData> voxels;
        public int3 vertexGridSize;
        public NativeArray<Vertex> generatedVertices;

        public void Execute(int index) {
            int3 gridPos = ToCoord(index, vertexGridSize);
            int3 physicalSize = vertexGridSize + 1;
            int3 cellMin = gridPos;
            float3 localCellPos = gridPos - 1;

            float3 sumPos = float3.zero;
            float3 sumNrm = float3.zero;
            int hitCount = 0;
            uint material = 0;

            for (int i = 0; i < 12; i++) {
                int2 edge = DCTables.EdgeVertices[i];
                int3 local0 = DCTables.VoxelVertices[edge.x];
                int3 local1 = DCTables.VoxelVertices[edge.y];

                int3 p0 = cellMin + local0;
                int3 p1 = cellMin + local1;

                VoxelData v0 = voxels[ToIndex(p0, physicalSize)];
                VoxelData v1 = voxels[ToIndex(p1, physicalSize)];
                float d0 = v0.density;
                float d1 = v1.density;

                bool crosses = (d0 > 0f && d1 < 0f) || (d0 < 0f && d1 > 0f);
                if (!crosses) continue;

                float t = d0 / (d0 - d1);
                float3 hitPos = math.lerp(localCellPos + local0, localCellPos + local1, t);
                sumPos += hitPos;

                int3 sp = d0 > 0f ? p0 : p1;
                float nx = SampleDensity(sp + new int3(-1, 0, 0), voxels, physicalSize) - SampleDensity(sp + new int3(1, 0, 0), voxels, physicalSize);
                float ny = SampleDensity(sp + new int3(0, -1, 0), voxels, physicalSize) - SampleDensity(sp + new int3(0, 1, 0), voxels, physicalSize);
                float nz = SampleDensity(sp + new int3(0, 0, -1), voxels, physicalSize) - SampleDensity(sp + new int3(0, 0, 1), voxels, physicalSize);

                sumNrm += math.normalizesafe(new float3(nx, ny, nz));
                hitCount++;

                if (material == 0)
                    if (d0 > 0f) material = v0.material;
                    else if (d1 > 0f) material = v1.material;
            }

            if (hitCount == 0) {
                generatedVertices[index] = new Vertex { material = 0 };
                return;
            }

            float3 finalPos = sumPos / hitCount;
            float3 finalNrm = math.normalizesafe(sumNrm);

            generatedVertices[index] = new Vertex {
                position = new Vector3(finalPos.x, finalPos.y, finalPos.z),
                normal = new Vector3(finalNrm.x, finalNrm.y, finalNrm.z),
                material = material
            };
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

        static float SampleDensity(int3 p, NativeArray<VoxelData> voxels, int3 size) {
            int3 clamped = math.clamp(p, 0, size - 1);
            return voxels[ToIndex(clamped, size)].density;
        }
    }
}
