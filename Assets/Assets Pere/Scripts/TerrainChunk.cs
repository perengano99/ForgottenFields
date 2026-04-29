using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

// TODO: Las brochas no funcionan bien. Primeramente, deberian existir dos tipos de brocha: Editar terreno y pintar terreno (pintar es establecer material).
// Además, la aplicación de brochas debería ser un proceso separado que modifique el campo escalar y luego ejecute un job de actualización de malla, en lugar de intentar modificar la malla directamente desde el job de brocha.
// Los tipos de brocha no funcionan como se espera. la funcion Vertical, debe jalar desde la base del terreno hacia arriba o abajo, impidiendo que queden "flotando" bloques en el aire. Actualmente, la brocha es una esfera que modifica el terreno de forma uniforme, lo que no es ideal para crear paredes verticales o suelos planos. Se necesita implementar una lógica de brocha que considere la dirección y la forma del terreno para aplicar modificaciones más precisas y controladas.
// Faltan shapes de brocha como box, irregular plane (extrulla de a manera de accidente natural como una montaña o cerro en lugar de una figura geometrica regular), actualmente el modo ruido crea literalmente ruido.
// La fuerza de la brocha no se aplica de manera efectiva, es muy fuerte y sensible. Ademas, es muy suave y debe mantenerse el aspecto low poly.
// El modo extraccion, subtraccion, suavizado y aplanado no deben ser modos seleccionables en inspector, sino combinaciones del teclado + lmb. Por ejemplo, Shift + LMB para aplanar, Ctrl + LMB para suavizar, Alt + LMB para subtraccion, y sin modificadores para extraccion. Esto permitirá una edición más fluida y rápida sin necesidad de cambiar constantemente entre modos en el inspector.

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

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class TerrainChunk : MonoBehaviour {
    // === MODELO DE DATOS ===
    [SerializeField] private int gridSizeX = 16;
    [SerializeField] private int gridSizeY = 16;
    [SerializeField] private int gridSizeZ = 16;
    [SerializeField] private float voxelSize = 1f;
    [SerializeField] private bool showDebugNodes = false;

    private MaterialPropertyBlock propBlock;

    private NativeArray<float> densities;
    private NativeArray<byte> metadata;
    private NativeArray<int> labels;
    public NativeReference<int> islandCount;

    // === ACCESO EXTERNO A DATOS ===
    public NativeArray<float> Densities => densities;
    public NativeArray<byte> Metadata => metadata;

    [SerializeField, HideInInspector] private float[] persistedDensities;

    // === VARIABLES DE TABLA ===
    private NativeArray<int> nativeEdgeTable;
    private NativeArray<int> nativeTriTable;

    // === BUFFERS DE MALLA ===
    private NativeList<Vector3> meshVertices;
    private NativeList<int> meshTriangles;
    private NativeList<Vector2> meshUVs;

    private Mesh chunkMesh;

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
        } else MaterialRegistry.Instance.InitializeIfNeeded();

        return MaterialRegistry.Instance.RegistryData.IsCreated;
    }

    private void OnEnable() {
        if (!EnsureRegistryIsReady()) return;

        if (chunkMesh == null) {
            chunkMesh = new Mesh { name = "VoxelChunk" };
            chunkMesh.hideFlags = HideFlags.DontSave;
            GetComponent<MeshFilter>().sharedMesh = chunkMesh;
        }

        int pointCount = (gridSizeX + 1) * (gridSizeY + 1) * (gridSizeZ + 1);

        if (densities.IsCreated) densities.Dispose();
        if (metadata.IsCreated) metadata.Dispose();
        if (labels.IsCreated) labels.Dispose();
        if (islandCount.IsCreated) islandCount.Dispose();
        if (nativeEdgeTable.IsCreated) nativeEdgeTable.Dispose();
        if (nativeTriTable.IsCreated) nativeTriTable.Dispose();
        if (meshVertices.IsCreated) meshVertices.Dispose();
        if (meshTriangles.IsCreated) meshTriangles.Dispose();
        if (meshUVs.IsCreated) meshUVs.Dispose();

        densities = new NativeArray<float>(pointCount, Allocator.Persistent);
        metadata = new NativeArray<byte>(pointCount, Allocator.Persistent);
        labels = new NativeArray<int>(pointCount, Allocator.Persistent);
        islandCount = new NativeReference<int>(Allocator.Persistent);

        nativeEdgeTable = new NativeArray<int>(256, Allocator.Persistent);
        nativeTriTable = new NativeArray<int>(256 * 16, Allocator.Persistent);

        meshVertices = new NativeList<Vector3>(Allocator.Persistent);
        meshTriangles = new NativeList<int>(Allocator.Persistent);
        meshUVs = new NativeList<Vector2>(Allocator.Persistent);

        for (int i = 0; i < 256; i++)
            nativeEdgeTable[i] = MarchingCubesTables.EdgeTable[i];

        for (int row = 0; row < 256; row++)
            for (int col = 0; col < 16; col++)
                nativeTriTable[row * 16 + col] = MarchingCubesTables.TriTable[row, col];

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
        if (meshVertices.IsCreated) meshVertices.Dispose();
        if (meshTriangles.IsCreated) meshTriangles.Dispose();
        if (meshUVs.IsCreated) meshUVs.Dispose();
        if (chunkMesh != null) DestroyImmediate(chunkMesh);
        isInitialized = false;
    }

    private void OnDestroy() {
        if (meshVertices.IsCreated) meshVertices.Dispose();
        if (meshTriangles.IsCreated) meshTriangles.Dispose();
        if (meshUVs.IsCreated) meshUVs.Dispose();
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
        if (!EnsureRegistryIsReady()) return;

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
        float surfaceHeight = (gridSizeY / 2f) * voxelSize;

        for (int z = 0; z <= gridSizeZ; z++) {
            for (int y = 0; y <= gridSizeY; y++) {
                for (int x = 0; x <= gridSizeX; x++) {
                    int index = x + y * pointsX + z * pointsX * pointsY;
                    densities[index] = y * voxelSize - surfaceHeight;
                    metadata[index] = 0;
                }
            }
        }
    }

    // === ORQUESTACIÓN Y MALLA ===
    public void UpdateMesh() {
        if (!EnsureRegistryIsReady()) return;
        if (!densities.IsCreated || !metadata.IsCreated || !nativeEdgeTable.IsCreated || !nativeTriTable.IsCreated) return;
        if (!meshVertices.IsCreated || !meshTriangles.IsCreated || !meshUVs.IsCreated) return;

        if (chunkMesh == null) {
            chunkMesh = new Mesh { name = "VoxelChunk" };
            chunkMesh.hideFlags = HideFlags.DontSave;
        }

        // === LIMPIAR BUFFERS ===
        meshVertices.Clear();
        meshTriangles.Clear();
        meshUVs.Clear();

        // === CONFIGURAR Y EJECUTAR JOB ===
        MarchingCubesJob job = new MarchingCubesJob {
            densities = densities,
            metadata = metadata,
            edgeTable = nativeEdgeTable,
            triTable = nativeTriTable,
            gridSize = new int3(gridSizeX, gridSizeY, gridSizeZ),
            voxelSize = voxelSize,
            outVertices = meshVertices,
            outTriangles = meshTriangles,
            outUVs = meshUVs
        };

        job.Schedule().Complete();

        // === VOLCAR A MESH ===
        chunkMesh.Clear();
        chunkMesh.SetVertices(meshVertices.AsArray());
        chunkMesh.SetIndices(meshTriangles.AsArray(), MeshTopology.Triangles, 0);
        chunkMesh.SetUVs(1, meshUVs.AsArray());
        chunkMesh.RecalculateNormals();
        chunkMesh.RecalculateBounds();

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
    // === CSG (DEFORMACIÓN VOLUMÉTRICA) — GESTIONADO EXTERNAMENTE ===
    // public void ModifyTerrain(Vector3 worldHitPoint, float brushRadius, float brushStrength, BrushType brushType, byte materialID = 0) {
    //     if (!densities.IsCreated || voxelSize <= 0f || brushRadius <= 0f) return;
    //     Vector3 localHitPoint = worldHitPoint - transform.position;
    //     ModifyTerrainJob job = new ModifyTerrainJob {
    //         densities = densities,
    //         metadata = metadata,
    //         gridSize = new int3(gridSizeX, gridSizeY, gridSizeZ),
    //         voxelSize = voxelSize,
    //         localHitPoint = new float3(localHitPoint.x, localHitPoint.y, localHitPoint.z),
    //         radius = brushRadius,
    //         strength = brushStrength,
    //         brushType = brushType,
    //         brushMaterialID = materialID
    //     };
    //     job.Schedule(densities.Length, 64).Complete();
    //     if (labels.IsCreated && islandCount.IsCreated) {
    //         for (int i = 0; i < labels.Length; i++) labels[i] = 0;
    //         GravityCheckJob gravityJob = new GravityCheckJob {
    //             densities = densities,
    //             gridSize = new int3(gridSizeX, gridSizeY, gridSizeZ),
    //             labels = labels,
    //             islandCount = islandCount,
    //             isPlayMode = Application.isPlaying
    //         };
    //         gravityJob.Schedule().Complete();
    //     }
    //     UpdateMesh();
    // }

    // [BurstCompile]
    // private struct ModifyTerrainJob : IJobParallelFor {
    //     public NativeArray<float> densities;
    //     public NativeArray<byte> metadata;
    //     public int3 gridSize;
    //     public float voxelSize;
    //     public float3 localHitPoint;
    //     public float radius;
    //     public float strength;
    //     public BrushType brushType;
    //     public byte brushMaterialID;
    //     public void Execute(int index) {
    //         int pointsX = gridSize.x + 1;
    //         int pointsY = gridSize.y + 1;
    //         int planeSize = pointsX * pointsY;
    //         int z = index / planeSize;
    //         int rem = index - z * planeSize;
    //         int y = rem / pointsX;
    //         int x = rem - y * pointsX;
    //         if (x > gridSize.x || y > gridSize.y || z > gridSize.z) return;
    //         float3 nodePos = new float3(x, y, z) * voxelSize;
    //         float distance = math.distance(nodePos, localHitPoint);
    //         if (distance > radius) return;
    //         float falloff = math.smoothstep(radius, 0f, distance);
    //         switch (brushType) {
    //             case BrushType.SphereAdd:
    //                 densities[index] -= strength * falloff;
    //                 if (densities[index] < 0f) metadata[index] = brushMaterialID;
    //                 break;
    //             case BrushType.SphereSubtract:
    //                 densities[index] += strength * falloff;
    //                 break;
    //             case BrushType.Flatten:
    //                 float dy = (y * voxelSize) - localHitPoint.y;
    //                 densities[index] = math.lerp(densities[index], dy, strength * falloff);
    //                 if (densities[index] < 0f) metadata[index] = brushMaterialID;
    //                 break;
    //         }
    //     }
    // }
    // === NOTA DE ARQUITECTURA ===
    // Nota: Mecánicas de debris y aplastamiento removidas por diseño.
    // Pendiente: Implementar shading de partículas (vfx_shading_particles) para desprendimiento visual.

    // === VISUALIZACIÓN GIZMOS (SCALAR FIELD) ===
    private void OnDrawGizmosSelected() {
        if (!showDebugNodes || !densities.IsCreated) return;

        int pointsX = gridSizeX + 1;
        int pointsY = gridSizeY + 1;

        for (int z = 0; z <= gridSizeZ; z++) {
            for (int y = 0; y <= gridSizeY; y++) {
                for (int x = 0; x <= gridSizeX; x++) {
                    int index = x + y * pointsX + z * pointsX * pointsY;
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

        public NativeList<Vector3> outVertices;
        public NativeList<int> outTriangles;
        public NativeList<Vector2> outUVs;

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

            for (int index = 0; index < densities.Length; index++) {
                int z = index / planeSize;
                int rem = index - z * planeSize;
                int y = rem / pointsX;
                int x = rem - y * pointsX;

                if (x >= gridSize.x || y >= gridSize.y || z >= gridSize.z) continue;

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

                    int t1 = triTable[triBase + i + 1];
                    int t2 = triTable[triBase + i + 2];

                    int baseVert = outVertices.Length;

                    outVertices.Add(new Vector3(ev[t0].x, ev[t0].y, ev[t0].z));
                    outVertices.Add(new Vector3(ev[t2].x, ev[t2].y, ev[t2].z));
                    outVertices.Add(new Vector3(ev[t1].x, ev[t1].y, ev[t1].z));

                    outTriangles.Add(baseVert);
                    outTriangles.Add(baseVert + 1);
                    outTriangles.Add(baseVert + 2);

                    outUVs.Add(new Vector2(matID, 0f));
                    outUVs.Add(new Vector2(matID, 0f));
                    outUVs.Add(new Vector2(matID, 0f));
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
}
