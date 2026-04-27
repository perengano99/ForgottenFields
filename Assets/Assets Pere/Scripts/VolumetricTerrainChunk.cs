using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public struct Triangle {
    public float3 v0;
    public float3 v1;
    public float3 v2;
}

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class VolumetricTerrainChunk : MonoBehaviour {
    // === MODELO DE DATOS ===
    [SerializeField] private int gridSizeX = 16;
    [SerializeField] private int gridSizeY = 16;
    [SerializeField] private int gridSizeZ = 16;
    [SerializeField] private float voxelSize = 1f;

    private NativeArray<float> densities;
    private NativeArray<int> metadata;

    // === VARIABLES DE TABLA ===
    private NativeArray<int> nativeEdgeTable;
    private NativeArray<int> nativeTriTable;

    private Mesh chunkMesh;

    public void Initialize() {
        if (chunkMesh == null) {
            chunkMesh = new Mesh { name = "VoxelChunk" };
            GetComponent<MeshFilter>().sharedMesh = chunkMesh;
        }

        int pointCount = (gridSizeX + 1) * (gridSizeY + 1) * (gridSizeZ + 1);

        if (densities.IsCreated) densities.Dispose();
        if (metadata.IsCreated) metadata.Dispose();
        if (nativeEdgeTable.IsCreated) nativeEdgeTable.Dispose();
        if (nativeTriTable.IsCreated) nativeTriTable.Dispose();

        densities = new NativeArray<float>(pointCount, Allocator.Persistent);
        metadata = new NativeArray<int>(pointCount, Allocator.Persistent);

        nativeEdgeTable = new NativeArray<int>(256, Allocator.Persistent);
        nativeTriTable = new NativeArray<int>(256 * 16, Allocator.Persistent);

        for (int i = 0; i < 256; i++)
            nativeEdgeTable[i] = MarchingCubesTables.EdgeTable[i];

        for (int row = 0; row < 256; row++)
            for (int col = 0; col < 16; col++)
                nativeTriTable[row * 16 + col] = MarchingCubesTables.TriTable[row, col];

        GenerateBasicTerrain();
        UpdateMesh();
    }

    private void OnDestroy() {
        if (densities.IsCreated) densities.Dispose();
        if (metadata.IsCreated) metadata.Dispose();
        if (nativeEdgeTable.IsCreated) nativeEdgeTable.Dispose();
        if (nativeTriTable.IsCreated) nativeTriTable.Dispose();
        if (chunkMesh != null) DestroyImmediate(chunkMesh);
    }

    // === GENERACIÓN DE CAMPO ESCALAR ===
    public void GenerateBasicTerrain() {
        if (!densities.IsCreated) return;

        int pointsX = gridSizeX + 1;
        int pointsY = gridSizeY + 1;

        for (int z = 0; z <= gridSizeZ; z++) {
            for (int y = 0; y <= gridSizeY; y++) {
                for (int x = 0; x <= gridSizeX; x++) {
                    int index = x + y * pointsX + z * pointsX * pointsY;
                    float height = 5f + Mathf.PerlinNoise(x * 0.1f, z * 0.1f) * 3f;
                    densities[index] = (y * voxelSize) - height;
                }
            }
        }
    }

    // === ORQUESTACIÓN Y MALLA ===
    public void UpdateMesh() {
        if (!densities.IsCreated || !metadata.IsCreated || !nativeEdgeTable.IsCreated || !nativeTriTable.IsCreated) return;

        if (chunkMesh == null) {
            chunkMesh = new Mesh { name = "VoxelChunk" };
            GetComponent<MeshFilter>().sharedMesh = chunkMesh;
        }

        NativeQueue<Triangle> triangleQueue = new NativeQueue<Triangle>(Allocator.TempJob);

        MarchingCubesJob job = new MarchingCubesJob {
            densities = densities,
            metadata = metadata,
            edgeTable = nativeEdgeTable,
            triTable = nativeTriTable,
            gridSize = new int3(gridSizeX, gridSizeY, gridSizeZ),
            voxelSize = voxelSize,
            triangles = triangleQueue.AsParallelWriter()
        };

        JobHandle handle = job.Schedule(densities.Length, 64);
        handle.Complete();

        int count = triangleQueue.Count;
        NativeArray<Vector3> vertices = new NativeArray<Vector3>(count * 3, Allocator.Temp);
        NativeArray<int> indices = new NativeArray<int>(count * 3, Allocator.Temp);

        int vIndex = 0;
        while (triangleQueue.TryDequeue(out Triangle t)) {
            vertices[vIndex] = new Vector3(t.v0.x, t.v0.y, t.v0.z); indices[vIndex] = vIndex++;
            vertices[vIndex] = new Vector3(t.v1.x, t.v1.y, t.v1.z); indices[vIndex] = vIndex++;
            vertices[vIndex] = new Vector3(t.v2.x, t.v2.y, t.v2.z); indices[vIndex] = vIndex++;
        }

        chunkMesh.Clear();
        chunkMesh.SetVertices(vertices);
        chunkMesh.SetIndices(indices, MeshTopology.Triangles, 0);
        chunkMesh.RecalculateNormals();
        chunkMesh.RecalculateBounds();

        vertices.Dispose();
        indices.Dispose();
        triangleQueue.Dispose();
    }

    [BurstCompile]
    private struct MarchingCubesJob : IJobParallelFor {
        [ReadOnly] public NativeArray<float> densities;
        [ReadOnly] public NativeArray<int> metadata;
        [ReadOnly] public NativeArray<int> edgeTable;
        [ReadOnly] public NativeArray<int> triTable;

        public int3 gridSize;
        public float voxelSize;

        public NativeQueue<Triangle>.ParallelWriter triangles;

        private static float3 InterpolateIso(float3 p0, float3 p1, float d0, float d1) {
            float denom = d0 - d1;
            float t = math.select(0.5f, d0 / denom, math.abs(denom) > 1e-8f);
            t = math.clamp(t, 0f, 1f);
            return math.lerp(p0, p1, t);
        }

        public void Execute(int index) {
            int pointsX = gridSize.x + 1;
            int pointsY = gridSize.y + 1;
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

            int cubeIndex = 0;
            if (d000 < 0f) cubeIndex |= 1;
            if (d100 < 0f) cubeIndex |= 2;
            if (d110 < 0f) cubeIndex |= 4;
            if (d010 < 0f) cubeIndex |= 8;
            if (d001 < 0f) cubeIndex |= 16;
            if (d101 < 0f) cubeIndex |= 32;
            if (d111 < 0f) cubeIndex |= 64;
            if (d011 < 0f) cubeIndex |= 128;

            int edgeMask = edgeTable[cubeIndex];
            if (edgeMask == 0) return;

            // === INTERPOLACIÓN DE VÉRTICES ===
            float3 basePos = new float3(x, y, z) * voxelSize;

            float3 p000 = basePos;
            float3 p100 = basePos + new float3(voxelSize, 0f, 0f);
            float3 p110 = basePos + new float3(voxelSize, voxelSize, 0f);
            float3 p010 = basePos + new float3(0f, voxelSize, 0f);
            float3 p001 = basePos + new float3(0f, 0f, voxelSize);
            float3 p101 = basePos + new float3(voxelSize, 0f, voxelSize);
            float3 p111 = basePos + new float3(voxelSize, voxelSize, voxelSize);
            float3 p011 = basePos + new float3(0f, voxelSize, voxelSize);

            FixedList512Bytes<float3> edgeVertices = default;
            for (int i = 0; i < 12; i++) edgeVertices.Add(float3.zero);

            if ((edgeMask & 1) != 0) edgeVertices[0] = InterpolateIso(p000, p100, d000, d100);
            if ((edgeMask & 2) != 0) edgeVertices[1] = InterpolateIso(p100, p110, d100, d110);
            if ((edgeMask & 4) != 0) edgeVertices[2] = InterpolateIso(p110, p010, d110, d010);
            if ((edgeMask & 8) != 0) edgeVertices[3] = InterpolateIso(p010, p000, d010, d000);
            if ((edgeMask & 16) != 0) edgeVertices[4] = InterpolateIso(p001, p101, d001, d101);
            if ((edgeMask & 32) != 0) edgeVertices[5] = InterpolateIso(p101, p111, d101, d111);
            if ((edgeMask & 64) != 0) edgeVertices[6] = InterpolateIso(p111, p011, d111, d011);
            if ((edgeMask & 128) != 0) edgeVertices[7] = InterpolateIso(p011, p001, d011, d001);
            if ((edgeMask & 256) != 0) edgeVertices[8] = InterpolateIso(p000, p001, d000, d001);
            if ((edgeMask & 512) != 0) edgeVertices[9] = InterpolateIso(p100, p101, d100, d101);
            if ((edgeMask & 1024) != 0) edgeVertices[10] = InterpolateIso(p110, p111, d110, d111);
            if ((edgeMask & 2048) != 0) edgeVertices[11] = InterpolateIso(p010, p011, d010, d011);

            // === ENSAMBLAJE DE TRIÁNGULOS ===
            int triBase = cubeIndex * 16;
            for (int i = 0; i < 16; i += 3) {
                int triIndex = triTable[triBase + i];
                if (triIndex == -1) break;

                int i1 = triTable[triBase + i + 1];
                int i2 = triTable[triBase + i + 2];

                Triangle newTriangle;
                newTriangle.v0 = edgeVertices[triIndex];
                newTriangle.v1 = edgeVertices[i1];
                newTriangle.v2 = edgeVertices[i2];

                triangles.Enqueue(newTriangle);
            }
        }
    }
}
