using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

public struct Triangle {
    public float3 v0;
    public float3 v1;
    public float3 v2;
    public float m0;
    public float m1;
    public float m2;
}

public enum BrushType {
    SphereAdd,
    SphereSubtract,
    Flatten
}

[StructLayout(LayoutKind.Sequential)]
public struct TerrainVertex {
    public float3 position;
    public float3 normal;
    public float2 uv;
}

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class TerrainChunk : MonoBehaviour {
    // === MODELO DE DATOS ===
    [SerializeField] private int gridSizeX = 16;
    [SerializeField] private int gridSizeY = 16;
    [SerializeField] private int gridSizeZ = 16;
    [SerializeField] private float voxelSize = 1f;
    [SerializeField] private bool showDebugNodes = false;
    [SerializeField] private byte baseMaterialID = 0;
    public bool forceLowPoly = true;

    private MaterialPropertyBlock propBlock;

    private NativeArray<float> densities;
    private NativeArray<byte> metadata;
    private NativeArray<int> labels;
    public NativeReference<int> islandCount;

    // === ACCESO EXTERNO A DATOS ===
    public NativeArray<float> Densities => densities;
    public NativeArray<byte> Metadata => metadata;

    public void SyncBordersWith(TerrainChunk neighbor, Vector3Int direction) {
        if (neighbor == null) return;
        if (!densities.IsCreated || !metadata.IsCreated) return;
        if (!neighbor.Densities.IsCreated || !neighbor.Metadata.IsCreated) return;

        if (gridSizeX != neighbor.gridSizeX || gridSizeY != neighbor.gridSizeY || gridSizeZ != neighbor.gridSizeZ) return;

        int pointsX = gridSizeX + 1;
        int pointsY = gridSizeY + 1;
        int pointsZ = gridSizeZ + 1;
        int planeSize = pointsX * pointsY;

        NativeArray<float> nDensities = neighbor.Densities;
        NativeArray<byte> nMetadata = neighbor.Metadata;

        if (direction == Vector3Int.right) {
            int dstX = gridSizeX;
            int srcX = 0;

            for (int z = 0; z < pointsZ; z++)
                for (int y = 0; y < pointsY; y++) {
                    int dst = dstX + y * pointsX + z * planeSize;
                    int src = srcX + y * pointsX + z * planeSize;
                    densities[dst] = nDensities[src];
                    metadata[dst] = nMetadata[src];
                }

            return;
        }

        if (direction == Vector3Int.left) {
            int dstX = 0;
            int srcX = gridSizeX;

            for (int z = 0; z < pointsZ; z++)
                for (int y = 0; y < pointsY; y++) {
                    int dst = dstX + y * pointsX + z * planeSize;
                    int src = srcX + y * pointsX + z * planeSize;
                    densities[dst] = nDensities[src];
                    metadata[dst] = nMetadata[src];
                }

            return;
        }

        if (direction == Vector3Int.up) {
            int dstY = gridSizeY;
            int srcY = 0;

            for (int z = 0; z < pointsZ; z++)
                for (int x = 0; x < pointsX; x++) {
                    int dst = x + dstY * pointsX + z * planeSize;
                    int src = x + srcY * pointsX + z * planeSize;
                    densities[dst] = nDensities[src];
                    metadata[dst] = nMetadata[src];
                }

            return;
        }

        if (direction == Vector3Int.down) {
            int dstY = 0;
            int srcY = gridSizeY;

            for (int z = 0; z < pointsZ; z++)
                for (int x = 0; x < pointsX; x++) {
                    int dst = x + dstY * pointsX + z * planeSize;
                    int src = x + srcY * pointsX + z * planeSize;
                    densities[dst] = nDensities[src];
                    metadata[dst] = nMetadata[src];
                }

            return;
        }

        if (direction == Vector3Int.forward) {
            int dstZ = gridSizeZ;
            int srcZ = 0;

            for (int y = 0; y < pointsY; y++)
                for (int x = 0; x < pointsX; x++) {
                    int dst = x + y * pointsX + dstZ * planeSize;
                    int src = x + y * pointsX + srcZ * planeSize;
                    densities[dst] = nDensities[src];
                    metadata[dst] = nMetadata[src];
                }

            return;
        }

        if (direction == Vector3Int.back) {
            int dstZ = 0;
            int srcZ = gridSizeZ;

            for (int y = 0; y < pointsY; y++)
                for (int x = 0; x < pointsX; x++) {
                    int dst = x + y * pointsX + dstZ * planeSize;
                    int src = x + y * pointsX + srcZ * planeSize;
                    densities[dst] = nDensities[src];
                    metadata[dst] = nMetadata[src];
                }
        }
    }

    public bool TryExportTerrainData(out float[] outDensities, out byte[] outMetadata) {
        outDensities = null;
        outMetadata = null;

        if (!densities.IsCreated || !metadata.IsCreated) return false;

        int len = densities.Length;
        outDensities = new float[len];
        outMetadata = new byte[len];

        for (int i = 0; i < len; i++) {
            outDensities[i] = densities[i];
            outMetadata[i] = metadata[i];
        }

        return true;
    }

    public bool TryImportTerrainData(float[] inDensities, byte[] inMetadata) {
        if (!densities.IsCreated || !metadata.IsCreated) return false;
        if (inDensities == null || inMetadata == null) return false;
        if (inDensities.Length != densities.Length || inMetadata.Length != metadata.Length) return false;

        for (int i = 0; i < densities.Length; i++) {
            densities[i] = inDensities[i];
            metadata[i] = inMetadata[i];
        }

        return true;
    }

    [SerializeField, HideInInspector] private float[] persistedDensities;

    // === VARIABLES DE TABLA ===
    private NativeArray<int> nativeEdgeTable;
    private NativeArray<int> nativeTriTable;

    // === BUFFERS DE GENERACIÓN ===
    private NativeArray<int> cellTriangleCounts;
    private NativeArray<int> cellVertexOffsets;
    private NativeReference<int> totalTriangleCount;

    private Mesh chunkMesh;

    private static readonly VertexAttributeDescriptor[] VertexLayout = {
        new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0),
        new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, 0),
        new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2, 0)
    };

    private bool needsReinitialization;
    private bool validationInitialized;
    private int validatedGridSizeX;
    private int validatedGridSizeY;
    private int validatedGridSizeZ;
    private float validatedVoxelSize;
    private bool isInitialized = false;

    private bool wasPlaying;
    private float[] editorSnapshotDensities;

    // === VERIFICACIÓN DE DEPENDENCIA ===
    private bool EnsureRegistryIsReady() {
        if (MaterialRegistry.Instance == null) {
            MaterialRegistry registry = Object.FindAnyObjectByType<MaterialRegistry>();
            if (registry == null) {
                Debug.LogWarning("MaterialRegistry no encontrado en la escena. Pausando generación del Chunk.");
                return false;
            }

            registry.InitializeIfNeeded();
            if (MaterialRegistry.Instance == null)
                return false;
        }
        else MaterialRegistry.Instance.InitializeIfNeeded();

        return MaterialRegistry.Instance.RegistryData.IsCreated;
    }

    private void InitializeData() {
        int pointCount = (gridSizeX + 1) * (gridSizeY + 1) * (gridSizeZ + 1);
        int cellCount = gridSizeX * gridSizeY * gridSizeZ;

        if (densities.IsCreated) densities.Dispose();
        if (metadata.IsCreated) metadata.Dispose();
        if (labels.IsCreated) labels.Dispose();
        if (islandCount.IsCreated) islandCount.Dispose();
        if (nativeEdgeTable.IsCreated) nativeEdgeTable.Dispose();
        if (nativeTriTable.IsCreated) nativeTriTable.Dispose();
        if (cellTriangleCounts.IsCreated) cellTriangleCounts.Dispose();
        if (cellVertexOffsets.IsCreated) cellVertexOffsets.Dispose();
        if (totalTriangleCount.IsCreated) totalTriangleCount.Dispose();

        densities = new NativeArray<float>(pointCount, Allocator.Persistent);
        metadata = new NativeArray<byte>(pointCount, Allocator.Persistent);
        labels = new NativeArray<int>(pointCount, Allocator.Persistent);
        islandCount = new NativeReference<int>(Allocator.Persistent);

        nativeEdgeTable = new NativeArray<int>(256, Allocator.Persistent);
        nativeTriTable = new NativeArray<int>(256 * 16, Allocator.Persistent);
        cellTriangleCounts = new NativeArray<int>(cellCount, Allocator.Persistent);
        cellVertexOffsets = new NativeArray<int>(cellCount, Allocator.Persistent);
        totalTriangleCount = new NativeReference<int>(Allocator.Persistent);

        for (int i = 0; i < 256; i++)
            nativeEdgeTable[i] = MarchingCubesTables.EdgeTable[i];

        for (int row = 0; row < 256; row++)
            for (int col = 0; col < 16; col++)
                nativeTriTable[row * 16 + col] = MarchingCubesTables.TriTable[row, col];
    }

    private void OnEnable() {
        if (!EnsureRegistryIsReady()) return;

        if (chunkMesh == null) {
            chunkMesh = new Mesh { name = "VoxelChunk" };
            chunkMesh.hideFlags = HideFlags.DontSave;
            GetComponent<MeshFilter>().sharedMesh = chunkMesh;
        }

        InitializeData();

        validatedGridSizeX = gridSizeX;
        validatedGridSizeY = gridSizeY;
        validatedGridSizeZ = gridSizeZ;
        validatedVoxelSize = voxelSize;
        validationInitialized = true;

        if (!TryRestoreDensities()) GenerateBasicTerrain();
        UpdateMesh();

        wasPlaying = Application.isPlaying;
        isInitialized = true;
    }

    private void OnDisable() {
        if (Application.isEditor && !Application.isPlaying) SaveDensities();

        if (densities.IsCreated) densities.Dispose();
        if (metadata.IsCreated) metadata.Dispose();
        if (labels.IsCreated) labels.Dispose();
        if (islandCount.IsCreated) islandCount.Dispose();
        if (nativeEdgeTable.IsCreated) nativeEdgeTable.Dispose();
        if (nativeTriTable.IsCreated) nativeTriTable.Dispose();
        if (cellTriangleCounts.IsCreated) cellTriangleCounts.Dispose();
        if (cellVertexOffsets.IsCreated) cellVertexOffsets.Dispose();
        if (totalTriangleCount.IsCreated) totalTriangleCount.Dispose();
        if (chunkMesh != null) DestroyImmediate(chunkMesh);
        isInitialized = false;
    }

    private void OnDestroy() {
        if (cellTriangleCounts.IsCreated) cellTriangleCounts.Dispose();
        if (cellVertexOffsets.IsCreated) cellVertexOffsets.Dispose();
        if (totalTriangleCount.IsCreated) totalTriangleCount.Dispose();
    }

    private void OnValidate() {
        if (!validationInitialized) {
            validatedGridSizeX = gridSizeX;
            validatedGridSizeY = gridSizeY;
            validatedGridSizeZ = gridSizeZ;
            validatedVoxelSize = voxelSize;
            validationInitialized = true;
            return;
        }

        if (validatedGridSizeX == gridSizeX && validatedGridSizeY == gridSizeY && validatedGridSizeZ == gridSizeZ && Mathf.Approximately(validatedVoxelSize, voxelSize)) return;

        validatedGridSizeX = gridSizeX;
        validatedGridSizeY = gridSizeY;
        validatedGridSizeZ = gridSizeZ;
        validatedVoxelSize = voxelSize;
        needsReinitialization = true;
    }

    private void Update() {
        // if (!EnsureRegistry.IsReady()) return;

        if (!isInitialized || needsReinitialization) {
            OnDisable();
            OnEnable();
            needsReinitialization = false;
        }

        if (Application.isEditor) {
            if (!wasPlaying && Application.isPlaying) CaptureEditorSnapshot();
            if (wasPlaying && !Application.isPlaying) RestoreEditorSnapshot();
            wasPlaying = Application.isPlaying;
        }

        if (!Application.isEditor || Application.isPlaying) return;
    }

    private void CaptureEditorSnapshot() {
        if (!densities.IsCreated) return;

        int len = densities.Length;
        if (editorSnapshotDensities == null || editorSnapshotDensities.Length != len)
            editorSnapshotDensities = new float[len];

        for (int i = 0; i < len; i++)
            editorSnapshotDensities[i] = densities[i];
    }

    private void RestoreEditorSnapshot() {
        if (!densities.IsCreated) return;
        if (editorSnapshotDensities == null || editorSnapshotDensities.Length != densities.Length) return;

        for (int i = 0; i < densities.Length; i++)
            densities[i] = editorSnapshotDensities[i];

        if (persistedDensities == null || persistedDensities.Length != editorSnapshotDensities.Length)
            persistedDensities = new float[editorSnapshotDensities.Length];

        for (int i = 0; i < editorSnapshotDensities.Length; i++)
            persistedDensities[i] = editorSnapshotDensities[i];

        UpdateMesh();
    }

    private void SaveDensities() {
        if (!densities.IsCreated) return;

        int len = densities.Length;
        if (persistedDensities == null || persistedDensities.Length != len)
            persistedDensities = new float[len];

        for (int i = 0; i < len; i++)
            persistedDensities[i] = densities[i];
    }

    private bool TryRestoreDensities() {
        if (!densities.IsCreated) return false;
        if (persistedDensities == null || persistedDensities.Length != densities.Length) return false;

        for (int i = 0; i < densities.Length; i++)
            densities[i] = persistedDensities[i];

        return true;
    }

    // === GENERACIÓN DE CAMPO ESCALAR ===
    public void GenerateBasicTerrain() {
        if (!densities.IsCreated) return;

        int pointsX = gridSizeX + 1;
        int pointsY = gridSizeY + 1;
        int pointsZ = gridSizeZ + 1;
        int planeSize = pointsX * pointsY;
        float surfaceHeight = (gridSizeY / 2f) * voxelSize;

        for (int z = 0; z < pointsZ; z++) {
            for (int y = 0; y < pointsY; y++) {
                for (int x = 0; x < pointsX; x++) {
                    int index = x + y * pointsX + z * planeSize;
                    densities[index] = y * voxelSize - surfaceHeight;
                    metadata[index] = densities[index] <= 0f ? baseMaterialID : (byte)0;
                }
            }
        }
    }

    // === ORQUESTACIÓN Y MALLA ===
    public void UpdateMesh() {
        if (!EnsureRegistryIsReady()) return;
        if (!densities.IsCreated || !metadata.IsCreated || !nativeEdgeTable.IsCreated || !nativeTriTable.IsCreated) return;
        if (!cellTriangleCounts.IsCreated || !cellVertexOffsets.IsCreated || !totalTriangleCount.IsCreated) return;

        if (chunkMesh == null) {
            chunkMesh = new Mesh { name = "VoxelChunk" };
            chunkMesh.hideFlags = HideFlags.DontSave;
        }

        int3 gridSize = new int3(gridSizeX, gridSizeY, gridSizeZ);
        int cellCount = gridSizeX * gridSizeY * gridSizeZ;
        int expectedPointCount = (gridSize.x + 1) * (gridSize.y + 1) * (gridSize.z + 1);
        if (densities.Length != expectedPointCount || metadata.Length != expectedPointCount) return;

        _ = new MarchingCubesJob {
            densities = densities,
            metadata = metadata,
            edgeTable = nativeEdgeTable,
            triTable = nativeTriTable,
            gridSize = gridSize,
            voxelSize = voxelSize,
            forceLowPoly = true,
            outCellTriangleCounts = cellTriangleCounts,
            outCellVertexOffsets = cellVertexOffsets
        };

        CountTrianglesJob countJob = new CountTrianglesJob {
            densities = densities,
            edgeTable = nativeEdgeTable,
            triTable = nativeTriTable,
            gridSize = gridSize,
            cellTriangleCounts = cellTriangleCounts,
            totalTriangleCount = totalTriangleCount
        };
        countJob.Schedule().Complete();

        PrefixSumJob prefixJob = new PrefixSumJob {
            cellTriangleCounts = cellTriangleCounts,
            cellVertexOffsets = cellVertexOffsets,
            totalTriangleCount = totalTriangleCount
        };
        prefixJob.Schedule().Complete();

        int triangleCount = totalTriangleCount.Value;
        int vertexCount = triangleCount * 3;
        int indexCount = vertexCount;

        var meshDataArray = Mesh.AllocateWritableMeshData(1);
        var meshData = meshDataArray[0];

        meshData.SetVertexBufferParams(vertexCount, VertexLayout);
        meshData.SetIndexBufferParams(indexCount, IndexFormat.UInt32);

        GenerateMeshJob meshJob = new GenerateMeshJob {
            densities = densities,
            metadata = metadata,
            edgeTable = nativeEdgeTable,
            triTable = nativeTriTable,
            gridSize = gridSize,
            voxelSize = voxelSize,
            cellTriangleCounts = cellTriangleCounts,
            cellVertexOffsets = cellVertexOffsets,
            meshData = meshData,
            forceLowPoly = forceLowPoly
        };
        meshJob.Schedule().Complete();

        meshData.subMeshCount = 1;
        meshData.SetSubMesh(0, new SubMeshDescriptor(0, indexCount) {
            vertexCount = vertexCount,
            bounds = new Bounds(
                new Vector3(gridSizeX, gridSizeY, gridSizeZ) * (voxelSize * 0.5f),
                new Vector3(gridSizeX, gridSizeY, gridSizeZ) * voxelSize)
        }, MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds);

        chunkMesh.Clear(false);
        Mesh.ApplyAndDisposeWritableMeshData(meshDataArray, chunkMesh, MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds);
        chunkMesh.bounds = new Bounds(
            new Vector3(gridSizeX, gridSizeY, gridSizeZ) * (voxelSize * 0.5f),
            new Vector3(gridSizeX, gridSizeY, gridSizeZ) * voxelSize);

        GetComponent<MeshFilter>().sharedMesh = chunkMesh;
        MeshCollider mc = GetComponent<MeshCollider>();
        if (mc != null) mc.sharedMesh = chunkMesh;

        if (Application.isEditor && !Application.isPlaying) SaveDensities();

        MeshRenderer renderer = GetComponent<MeshRenderer>();
        if (renderer != null && MaterialRegistry.Instance != null && MaterialRegistry.Instance.SplatTextureArray != null) {
            if (propBlock == null) propBlock = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(propBlock);
            propBlock.SetTexture("_TerrainSplatArray", MaterialRegistry.Instance.SplatTextureArray);
            renderer.SetPropertyBlock(propBlock);
        }
    }

    // === NOTA DE ARQUITECTURA ===
    // Pendiente: Implementar shading de partículas (vfx_shading_particles) para desprendimiento visual.

    // === VISUALIZACIÓN GIZMOS (SCALAR FIELD) ===
    private void OnDrawGizmosSelected() {
        if (!showDebugNodes || !densities.IsCreated) return;

        int pointsX = gridSizeX + 1;
        int pointsY = gridSizeY + 1;
        int pointsZ = gridSizeZ + 1;
        int planeSize = pointsX * pointsY;

        for (int z = 0; z < pointsZ; z++) {
            for (int y = 0; y < pointsY; y++) {
                for (int x = 0; x < pointsX; x++) {
                    int index = x + y * pointsX + z * planeSize;
                    float density = densities[index];
                    if (density >= 0f) continue;

                    if (labels.IsCreated && labels[index] >= 2) Gizmos.color = Color.magenta;
                    else Gizmos.color = Color.red;

                    Vector3 nodePos = transform.position + new Vector3(x, y, z) * voxelSize;
                    Gizmos.DrawCube(nodePos, Vector3.one * (voxelSize * 0.15f));
                }
            }
        }
    }

    [BurstCompile]
    private struct MarchingCubesJob : IJob {
        [ReadOnly] public NativeArray<float> densities;
        [ReadOnly] public NativeArray<byte> metadata;
        [ReadOnly] public NativeArray<int> edgeTable;
        [ReadOnly] public NativeArray<int> triTable;

        public int3 gridSize;
        public float voxelSize;
        public bool forceLowPoly;

        public NativeArray<int> outCellTriangleCounts;
        public NativeArray<int> outCellVertexOffsets;

        private void TryPickCornerMaterial(int cornerIndex, float cornerDensity, ref float bestSolidDensity, ref byte bestMatID) {
            if (cornerDensity > 0f) return;

            byte candidate = metadata[cornerIndex];
            if (candidate == 0) return;

            if (cornerDensity < bestSolidDensity) {
                bestSolidDensity = cornerDensity;
                bestMatID = candidate;
            }
        }

        private static float3 InterpolateIso(float3 p0, float3 p1, float d0, float d1) {
            float denom = d0 - d1;
            float t = math.select(0.5f, d0 / denom, math.abs(denom) > 1e-8f);
            t = math.clamp(t, 0f, 1f);
            return math.lerp(p0, p1, t);
        }

        public void Execute() {
            int pointsX = gridSize.x + 1;
            int pointsY = gridSize.y + 1;
            int planeSize = pointsX * pointsY;

            for (int z = 0; z < gridSize.z; z++) {
                for (int y = 0; y < gridSize.y; y++) {
                    for (int x = 0; x < gridSize.x; x++) {
                        int i000 = x + y * pointsX + z * planeSize;
                        int i100 = i000 + 1;
                        int i010 = i000 + pointsX;
                        int i110 = i010 + 1;
                        int i001 = i000 + planeSize;
                        int i101 = i001 + 1;
                        int i011 = i001 + pointsX;
                        int i111 = i011 + 1;

                        byte matID = metadata[i000];

                        float d000 = densities[i000];
                        float d100 = densities[i100];
                        float d010 = densities[i010];
                        float d110 = densities[i110];
                        float d001 = densities[i001];
                        float d101 = densities[i101];
                        float d011 = densities[i011];
                        float d111 = densities[i111];

                        // === SELECCIÓN DE MATERIAL POR CELDA ===
                        float bestSolidDensity = float.MaxValue;
                        byte bestMatID = 0;

                        TryPickCornerMaterial(i000, d000, ref bestSolidDensity, ref bestMatID);
                        TryPickCornerMaterial(i100, d100, ref bestSolidDensity, ref bestMatID);
                        TryPickCornerMaterial(i010, d010, ref bestSolidDensity, ref bestMatID);
                        TryPickCornerMaterial(i110, d110, ref bestSolidDensity, ref bestMatID);
                        TryPickCornerMaterial(i001, d001, ref bestSolidDensity, ref bestMatID);
                        TryPickCornerMaterial(i101, d101, ref bestSolidDensity, ref bestMatID);
                        TryPickCornerMaterial(i011, d011, ref bestSolidDensity, ref bestMatID);
                        TryPickCornerMaterial(i111, d111, ref bestSolidDensity, ref bestMatID);

                        if (bestMatID != 0) matID = bestMatID;

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
                        if (edgeMask == 0) continue;

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

                        FixedList512Bytes<float3> ev = default;
                        for (int i = 0; i < 12; i++) ev.Add(float3.zero);

                        if ((edgeMask & 1) != 0) ev[0] = InterpolateIso(p000, p100, d000, d100);
                        if ((edgeMask & 2) != 0) ev[1] = InterpolateIso(p100, p110, d100, d110);
                        if ((edgeMask & 4) != 0) ev[2] = InterpolateIso(p110, p010, d110, d010);
                        if ((edgeMask & 8) != 0) ev[3] = InterpolateIso(p010, p000, d010, d000);
                        if ((edgeMask & 16) != 0) ev[4] = InterpolateIso(p001, p101, d001, d101);
                        if ((edgeMask & 32) != 0) ev[5] = InterpolateIso(p101, p111, d101, d111);
                        if ((edgeMask & 64) != 0) ev[6] = InterpolateIso(p111, p011, d111, d011);
                        if ((edgeMask & 128) != 0) ev[7] = InterpolateIso(p011, p001, d011, d001);
                        if ((edgeMask & 256) != 0) ev[8] = InterpolateIso(p000, p001, d000, d001);
                        if ((edgeMask & 512) != 0) ev[9] = InterpolateIso(p100, p101, d100, d101);
                        if ((edgeMask & 1024) != 0) ev[10] = InterpolateIso(p110, p111, d110, d111);
                        if ((edgeMask & 2048) != 0) ev[11] = InterpolateIso(p010, p011, d010, d011);

                        // === ENSAMBLAJE DE TRIÁNGULOS ===
                        int triBase = cubeIndex * 16;
                        for (int i = 0; i < 16; i += 3) {
                            int t0 = triTable[triBase + i];
                            if (t0 == -1) break;
                        }
                    }
                }
            }
        }
    }

    [BurstCompile]
    private struct GravityCheckJob : IJob {
        public NativeArray<float> densities;
        public int3 gridSize;
        public NativeArray<int> labels;
        public NativeReference<int> islandCount;
        public bool isPlayMode;

        public void Execute() {
            int pointsX = gridSize.x + 1;
            int pointsY = gridSize.y + 1;
            int planeSize = pointsX * pointsY;

            NativeQueue<int> queue = new NativeQueue<int>(Allocator.Temp);

            // === PASO 1: ANCLAJE (SUELO = 1) ===
            for (int z = 0; z <= gridSize.z; z++) {
                for (int x = 0; x <= gridSize.x; x++) {
                    int index = x + z * planeSize;
                    if (densities[index] >= 0f || labels[index] != 0) continue;

                    labels[index] = 1;
                    queue.Enqueue(index);
                }
            }

            while (queue.TryDequeue(out int current)) {
                int z = current / planeSize;
                int rem = current - z * planeSize;
                int y = rem / pointsX;
                int x = rem - y * pointsX;

                int nx = x + 1;
                if (nx <= gridSize.x) {
                    int ni = nx + y * pointsX + z * planeSize;
                    if (densities[ni] < 0f && labels[ni] == 0) {
                        labels[ni] = 1;
                        queue.Enqueue(ni);
                    }
                }

                nx = x - 1;
                if (nx >= 0) {
                    int ni = nx + y * pointsX + z * planeSize;
                    if (densities[ni] < 0f && labels[ni] == 0) {
                        labels[ni] = 1;
                        queue.Enqueue(ni);
                    }
                }

                int ny = y + 1;
                if (ny <= gridSize.y) {
                    int ni = x + ny * pointsX + z * planeSize;
                    if (densities[ni] < 0f && labels[ni] == 0) {
                        labels[ni] = 1;
                        queue.Enqueue(ni);
                    }
                }

                ny = y - 1;
                if (ny >= 0) {
                    int ni = x + ny * pointsX + z * planeSize;
                    if (densities[ni] < 0f && labels[ni] == 0) {
                        labels[ni] = 1;
                        queue.Enqueue(ni);
                    }
                }

                int nz = z + 1;
                if (nz <= gridSize.z) {
                    int ni = x + y * pointsX + nz * planeSize;
                    if (densities[ni] < 0f && labels[ni] == 0) {
                        labels[ni] = 1;
                        queue.Enqueue(ni);
                    }
                }

                nz = z - 1;
                if (nz >= 0) {
                    int ni = x + y * pointsX + nz * planeSize;
                    if (densities[ni] < 0f && labels[ni] == 0) {
                        labels[ni] = 1;
                        queue.Enqueue(ni);
                    }
                }
            }

            // === PASO 2: DETECCIÓN DE ISLAS (2+) ===
            int currentIslandID = 2;

            for (int i = 0; i < densities.Length; i++) {
                if (densities[i] >= 0f || labels[i] != 0) continue;

                labels[i] = currentIslandID;
                queue.Enqueue(i);

                while (queue.TryDequeue(out int current)) {
                    int z = current / planeSize;
                    int rem = current - z * planeSize;
                    int y = rem / pointsX;
                    int x = rem - y * pointsX;

                    int nx = x + 1;
                    if (nx <= gridSize.x) {
                        int ni = nx + y * pointsX + z * planeSize;
                        if (densities[ni] < 0f && labels[ni] == 0) {
                            labels[ni] = currentIslandID;
                            queue.Enqueue(ni);
                        }
                    }

                    nx = x - 1;
                    if (nx >= 0) {
                        int ni = nx + y * pointsX + z * planeSize;
                        if (densities[ni] < 0f && labels[ni] == 0) {
                            labels[ni] = currentIslandID;
                            queue.Enqueue(ni);
                        }
                    }

                    int ny = y + 1;
                    if (ny <= gridSize.y) {
                        int ni = x + ny * pointsX + z * planeSize;
                        if (densities[ni] < 0f && labels[ni] == 0) {
                            labels[ni] = currentIslandID;
                            queue.Enqueue(ni);
                        }
                    }

                    ny = y - 1;
                    if (ny >= 0) {
                        int ni = x + ny * pointsX + z * planeSize;
                        if (densities[ni] < 0f && labels[ni] == 0) {
                            labels[ni] = currentIslandID;
                            queue.Enqueue(ni);
                        }
                    }

                    int nz = z + 1;
                    if (nz <= gridSize.z) {
                        int ni = x + y * pointsX + nz * planeSize;
                        if (densities[ni] < 0f && labels[ni] == 0) {
                            labels[ni] = currentIslandID;
                            queue.Enqueue(ni);
                        }
                    }

                    nz = z - 1;
                    if (nz >= 0) {
                        int ni = x + y * pointsX + nz * planeSize;
                        if (densities[ni] < 0f && labels[ni] == 0) {
                            labels[ni] = currentIslandID;
                            queue.Enqueue(ni);
                        }
                    }
                }

                currentIslandID++;
            }

            // === PASO 3: LIMPIEZA DE ISLAS FLOTANTES (SOLO RUNTIME) ===
            if (isPlayMode)
                for (int i = 0; i < densities.Length; i++)
                    if (labels[i] >= 2) densities[i] = 1f;

            islandCount.Value = currentIslandID - 2;

            queue.Dispose();
        }
    }

    [BurstCompile]
    private struct CountTrianglesJob : IJob {
        [ReadOnly] public NativeArray<float> densities;
        [ReadOnly] public NativeArray<int> edgeTable;
        [ReadOnly] public NativeArray<int> triTable;
        public int3 gridSize;
        public NativeArray<int> cellTriangleCounts;
        public NativeReference<int> totalTriangleCount;

        public void Execute() {
            int pointsX = gridSize.x + 1;
            int pointsY = gridSize.y + 1;
            int planeSize = pointsX * pointsY;
            int total = 0;

            for (int z = 0; z < gridSize.z; z++) {
                for (int y = 0; y < gridSize.y; y++) {
                    for (int x = 0; x < gridSize.x; x++) {
                        int cellIndex = x + y * gridSize.x + z * gridSize.x * gridSize.y;
                        int i000 = x + y * pointsX + z * planeSize;
                        int i100 = i000 + 1;
                        int i010 = i000 + pointsX;
                        int i110 = i010 + 1;
                        int i001 = i000 + planeSize;
                        int i101 = i001 + 1;
                        int i011 = i001 + pointsX;
                        int i111 = i011 + 1;

                        int cubeIndex = 0;
                        if (densities[i000] < 0f) cubeIndex |= 1;
                        if (densities[i100] < 0f) cubeIndex |= 2;
                        if (densities[i110] < 0f) cubeIndex |= 4;
                        if (densities[i010] < 0f) cubeIndex |= 8;
                        if (densities[i001] < 0f) cubeIndex |= 16;
                        if (densities[i101] < 0f) cubeIndex |= 32;
                        if (densities[i111] < 0f) cubeIndex |= 64;
                        if (densities[i011] < 0f) cubeIndex |= 128;

                        if (edgeTable[cubeIndex] == 0) {
                            cellTriangleCounts[cellIndex] = 0;
                            continue;
                        }

                        int triCount = 0;
                        int triBase = cubeIndex * 16;
                        for (int i = 0; i < 16; i += 3) {
                            if (triTable[triBase + i] == -1) break;
                            triCount++;
                        }

                        cellTriangleCounts[cellIndex] = triCount;
                        total += triCount;
                    }
                }
            }

            totalTriangleCount.Value = total;
        }
    }

    [BurstCompile]
    private struct PrefixSumJob : IJob {
        [ReadOnly] public NativeArray<int> cellTriangleCounts;
        public NativeArray<int> cellVertexOffsets;
        public NativeReference<int> totalTriangleCount;

        public void Execute() {
            int offset = 0;
            for (int i = 0; i < cellTriangleCounts.Length; i++) {
                cellVertexOffsets[i] = offset;
                offset += cellTriangleCounts[i] * 3;
            }

            totalTriangleCount.Value = offset / 3;
        }
    }

    [BurstCompile]
    private struct GenerateMeshJob : IJob {
        [ReadOnly] public NativeArray<float> densities;
        [ReadOnly] public NativeArray<byte> metadata;
        [ReadOnly] public NativeArray<int> edgeTable;
        [ReadOnly] public NativeArray<int> triTable;
        [ReadOnly] public NativeArray<int> cellTriangleCounts;
        [ReadOnly] public NativeArray<int> cellVertexOffsets;

        public int3 gridSize;
        public float voxelSize;
        public bool forceLowPoly;

        [NativeDisableContainerSafetyRestriction] public Mesh.MeshData meshData;

        private float SampleDensityClamped(int x, int y, int z) {
            x = math.clamp(x, 0, gridSize.x);
            y = math.clamp(y, 0, gridSize.y);
            z = math.clamp(z, 0, gridSize.z);

            int pointsX = gridSize.x + 1;
            int pointsY = gridSize.y + 1;
            int index = x + y * pointsX + z * pointsX * pointsY;
            return densities[index];
        }

        private float3 SampleGradient(float3 pos) {
            float inv = 1f / voxelSize;
            int x = (int)math.round(pos.x * inv);
            int y = (int)math.round(pos.y * inv);
            int z = (int)math.round(pos.z * inv);

            float dx = SampleDensityClamped(x + 1, y, z) - SampleDensityClamped(x - 1, y, z);
            float dy = SampleDensityClamped(x, y + 1, z) - SampleDensityClamped(x, y - 1, z);
            float dz = SampleDensityClamped(x, y, z + 1) - SampleDensityClamped(x, y, z - 1);

            float3 normal = math.normalize(new float3(dx, dy, dz));
            return math.any(!math.isfinite(normal)) ? math.up() : normal;
        }

        private void TryPickCornerMaterial(int cornerIndex, float cornerDensity, ref float bestSolidDensity, ref byte bestMatID) {
            if (cornerDensity > 0f) return;

            byte candidate = metadata[cornerIndex];
            if (candidate == 0) return;

            if (cornerDensity < bestSolidDensity) {
                bestSolidDensity = cornerDensity;
                bestMatID = candidate;
            }
        }

        private static float3 InterpolateIso(float3 p0, float3 p1, float d0, float d1) {
            float denom = d0 - d1;
            float t = math.select(0.5f, d0 / denom, math.abs(denom) > 1e-8f);
            t = math.clamp(t, 0f, 1f);
            return math.lerp(p0, p1, t);
        }

        private static void WriteVertex(NativeArray<TerrainVertex> vertices, NativeArray<uint> indices, int vertexIndex, float3 position, float3 normal, byte matID) {
            vertices[vertexIndex] = new TerrainVertex {
                position = position,
                normal = normal,
                uv = new float2(matID, 0f)
            };
            indices[vertexIndex] = (uint)vertexIndex;
        }

        public void Execute() {
            NativeArray<TerrainVertex> vertices = meshData.GetVertexData<TerrainVertex>();
            NativeArray<uint> indices = meshData.GetIndexData<uint>();

            int pointsX = gridSize.x + 1;
            int pointsY = gridSize.y + 1;
            int planeSize = pointsX * pointsY;

            for (int z = 0; z < gridSize.z; z++) {
                for (int y = 0; y < gridSize.y; y++) {
                    for (int x = 0; x < gridSize.x; x++) {
                        int cellIndex = x + y * gridSize.x + z * gridSize.x * gridSize.y;
                        int triCount = cellTriangleCounts[cellIndex];
                        if (triCount == 0) continue;

                        int i000 = x + y * pointsX + z * planeSize;
                        int i100 = i000 + 1;
                        int i010 = i000 + pointsX;
                        int i110 = i010 + 1;
                        int i001 = i000 + planeSize;
                        int i101 = i001 + 1;
                        int i011 = i001 + pointsX;
                        int i111 = i011 + 1;

                        byte matID = metadata[i000];

                        float d000 = densities[i000];
                        float d100 = densities[i100];
                        float d010 = densities[i010];
                        float d110 = densities[i110];
                        float d001 = densities[i001];
                        float d101 = densities[i101];
                        float d011 = densities[i011];
                        float d111 = densities[i111];

                        // === SELECCIÓN DE MATERIAL POR CELDA ===
                        float bestSolidDensity = float.MaxValue;
                        byte bestMatID = 0;

                        TryPickCornerMaterial(i000, d000, ref bestSolidDensity, ref bestMatID);
                        TryPickCornerMaterial(i100, d100, ref bestSolidDensity, ref bestMatID);
                        TryPickCornerMaterial(i010, d010, ref bestSolidDensity, ref bestMatID);
                        TryPickCornerMaterial(i110, d110, ref bestSolidDensity, ref bestMatID);
                        TryPickCornerMaterial(i001, d001, ref bestSolidDensity, ref bestMatID);
                        TryPickCornerMaterial(i101, d101, ref bestSolidDensity, ref bestMatID);
                        TryPickCornerMaterial(i011, d011, ref bestSolidDensity, ref bestMatID);
                        TryPickCornerMaterial(i111, d111, ref bestSolidDensity, ref bestMatID);

                        if (bestMatID != 0) matID = bestMatID;

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
                        if (edgeMask == 0) continue;

                        float3 basePos = new float3(x, y, z) * voxelSize;
                        float3 p000 = basePos;
                        float3 p100 = basePos + new float3(voxelSize, 0f, 0f);
                        float3 p110 = basePos + new float3(voxelSize, voxelSize, 0f);
                        float3 p010 = basePos + new float3(0f, voxelSize, 0f);
                        float3 p001 = basePos + new float3(0f, 0f, voxelSize);
                        float3 p101 = basePos + new float3(voxelSize, 0f, voxelSize);
                        float3 p111 = basePos + new float3(voxelSize, voxelSize, voxelSize);
                        float3 p011 = basePos + new float3(0f, voxelSize, voxelSize);

                        FixedList512Bytes<float3> ev = default;
                        for (int i = 0; i < 12; i++) ev.Add(float3.zero);

                        if ((edgeMask & 1) != 0) ev[0] = InterpolateIso(p000, p100, d000, d100);
                        if ((edgeMask & 2) != 0) ev[1] = InterpolateIso(p100, p110, d100, d110);
                        if ((edgeMask & 4) != 0) ev[2] = InterpolateIso(p110, p010, d110, d010);
                        if ((edgeMask & 8) != 0) ev[3] = InterpolateIso(p010, p000, d010, d000);
                        if ((edgeMask & 16) != 0) ev[4] = InterpolateIso(p001, p101, d001, d101);
                        if ((edgeMask & 32) != 0) ev[5] = InterpolateIso(p101, p111, d101, d111);
                        if ((edgeMask & 64) != 0) ev[6] = InterpolateIso(p111, p011, d111, d011);
                        if ((edgeMask & 128) != 0) ev[7] = InterpolateIso(p011, p001, d011, d001);
                        if ((edgeMask & 256) != 0) ev[8] = InterpolateIso(p000, p001, d000, d001);
                        if ((edgeMask & 512) != 0) ev[9] = InterpolateIso(p100, p101, d100, d101);
                        if ((edgeMask & 1024) != 0) ev[10] = InterpolateIso(p110, p111, d110, d111);
                        if ((edgeMask & 2048) != 0) ev[11] = InterpolateIso(p010, p011, d010, d011);

                        int triBase = cubeIndex * 16;
                        int writeOffset = cellVertexOffsets[cellIndex];
                        int localTri = 0;

                        for (int i = 0; i < 16; i += 3) {
                            int t0 = triTable[triBase + i];
                            if (t0 == -1) break;

                            int t1 = triTable[triBase + i + 1];
                            int t2 = triTable[triBase + i + 2];

                            float3 a = ev[t0];
                            float3 b = ev[t2];
                            float3 c = ev[t1];

                            float3 e1 = b - a;
                            float3 e2 = c - a;
                            float3 faceN = math.cross(e1, e2);
                            float area2 = math.lengthsq(faceN);

                            float3 center = (a + b + c) * (1f / 3f);
                            float3 gradNa = SampleGradient(a);
                            float3 gradNb = SampleGradient(b);
                            float3 gradNc = SampleGradient(c);

                            if (!math.all(math.isfinite(gradNa))) gradNa = faceN;
                            if (!math.all(math.isfinite(gradNb))) gradNb = faceN;
                            if (!math.all(math.isfinite(gradNc))) gradNc = faceN;

                            if (math.dot(gradNa, faceN) < 0f) gradNa = -gradNa;
                            if (math.dot(gradNb, faceN) < 0f) gradNb = -gradNb;
                            if (math.dot(gradNc, faceN) < 0f) gradNc = -gradNc;

                            int baseVertex = writeOffset + localTri * 3;
                            WriteVertex(vertices, indices, baseVertex, a, math.normalize(gradNa), matID);
                            WriteVertex(vertices, indices, baseVertex + 1, b, math.normalize(gradNb), matID);
                            WriteVertex(vertices, indices, baseVertex + 2, c, math.normalize(gradNc), matID);
                            localTri++;
                        }
                    }
                }
            }
        }
    }

    private void TryPickCornerMaterial(int cornerIndex, float cornerDensity, ref float bestSolidDensity, ref byte bestMatID) {
        if (cornerDensity > 0f) return;

        byte candidate = metadata[cornerIndex];
        if (candidate == 0) return;

        if (cornerDensity < bestSolidDensity) {
            bestSolidDensity = cornerDensity;
            bestMatID = candidate;
        }
    }
}