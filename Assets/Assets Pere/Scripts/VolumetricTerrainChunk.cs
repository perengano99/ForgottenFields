using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public class VolumetricTerrainChunk : MonoBehaviour {
    // === MODELO DE DATOS ===
    [SerializeField] private int gridSizeX = 16;
    [SerializeField] private int gridSizeY = 16;
    [SerializeField] private int gridSizeZ = 16;
    [SerializeField] private float voxelSize = 1f;

    private NativeArray<float> densities;
    private NativeArray<int> metadata;

    public void Initialize() {
        int pointCount = (gridSizeX + 1) * (gridSizeY + 1) * (gridSizeZ + 1);

        if (densities.IsCreated) densities.Dispose();
        if (metadata.IsCreated) metadata.Dispose();

        densities = new NativeArray<float>(pointCount, Allocator.Persistent);
        metadata = new NativeArray<int>(pointCount, Allocator.Persistent);
    }

    private void OnDestroy() {
        if (densities.IsCreated) densities.Dispose();
        if (metadata.IsCreated) metadata.Dispose();
    }

    [BurstCompile]
    private struct MarchingCubesJob : IJobParallelFor {
        [ReadOnly] public NativeArray<float> densities;
        [ReadOnly] public NativeArray<int> metadata;

        public int3 gridSize;
        public float voxelSize;

        public NativeQueue<float3>.ParallelWriter vertices;
        public NativeQueue<int>.ParallelWriter triangles;

        public void Execute(int index) {
            int pointsX = gridSize.x + 1;
            int pointsY = gridSize.y + 1;
            int pointsZ = gridSize.z + 1;

            int planeSize = pointsX * pointsY;

            int z = index / planeSize;
            int rem = index - z * planeSize;
            int y = rem / pointsX;
            int x = rem - y * pointsX;

            if (x >= gridSize.x || y >= gridSize.y || z >= gridSize.z) return;

            int i000 = x + y * pointsX + z * planeSize;
            int i100 = i000 + 1;
            int i010 = i000 + pointsX;
            int i110 = i010 + 1;
            int i001 = i000 + planeSize;
            int i101 = i001 + 1;
            int i011 = i001 + pointsX;
            int i111 = i011 + 1;

            float d000 = densities[i000];
            float d100 = densities[i100];
            float d010 = densities[i010];
            float d110 = densities[i110];
            float d001 = densities[i001];
            float d101 = densities[i101];
            float d011 = densities[i011];
            float d111 = densities[i111];

            int m000 = metadata[i000];

            // Note: Marching Cubes Logic Here
        }
    }
}
