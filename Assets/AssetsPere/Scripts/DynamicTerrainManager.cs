using Unity.Mathematics;
using UnityEngine;

[ExecuteAlways]
public class DynamicTerrainManager : MonoBehaviour {
    // === CONFIGURACIÓN GLOBAL ===
    [Header("Tamaño del Mundo")]
    [SerializeField] private int worldSizeX = 2;
    [SerializeField] private int worldSizeY = 1;
    [SerializeField] private int worldSizeZ = 2;

    [Header("Configuración de Chunk")]
    [SerializeField] private int chunkGridSize = 16;
    [SerializeField] private float voxelSize = 1f;
    [SerializeField] private Material terrainMaterial;

    [System.Serializable]
    private struct ChunkSnapshot {
        public int x;
        public int y;
        public int z;
        public float[] densities;
        public byte[] metadata;
    }

    [SerializeField, HideInInspector] private ChunkSnapshot[] editorToPlaySnapshot;

    private bool wasPlaying;
    private bool pendingApplyEditorSnapshotInPlay;

    // === ESTADO DEL PINCEL ===
    [Header("Pincel")]
    public bool isEditing = false;
    public BrushShape CurrentBrushShape = BrushShape.Sphere;
    public BrushType CurrentBrushType = BrushType.SphereAdd;
    public float BrushRadius = 5f;
    public float BrushStrength = 1f;
    public byte SelectedMaterialID = 0;
    public bool IsVerticalBrush = false;

    // === ARREGLO TRIDIMENSIONAL DE CHUNKS ===
    private TerrainChunk[,,] chunks;

    // === ACCESO EXTERNO ===
    public TerrainChunk[,,] Chunks => chunks;
    public int WorldSizeX => worldSizeX;
    public int WorldSizeY => worldSizeY;
    public int WorldSizeZ => worldSizeZ;

    private void OnEnable() {
        RebuildChunks();

        if (Application.isPlaying && pendingApplyEditorSnapshotInPlay) {
            ApplyEditorSnapshotToChunks();
            pendingApplyEditorSnapshotInPlay = false;
        }

        wasPlaying = Application.isPlaying;
    }

    private void OnDisable() {
        // === FLUJO ESTRICTO: EDITOR -> JUEGO ===
        if (Application.isEditor && !Application.isPlaying) CaptureEditorSnapshot();
        DestroyChunks();
    }

    private void Update() {
        if (!Application.isEditor) return;

        if (!wasPlaying && Application.isPlaying) {
            pendingApplyEditorSnapshotInPlay = true;
            RebuildChunks();
            ApplyEditorSnapshotToChunks();
            pendingApplyEditorSnapshotInPlay = false;
        }

        // === NO PERSISTIR JUEGO -> EDITOR ===
        if (wasPlaying && !Application.isPlaying)
            RebuildChunks();

        wasPlaying = Application.isPlaying;
    }

    // === CONSTRUCCIÓN DE CHUNKS ===
    private void RebuildChunks() {
        DestroyChunks();

        chunks = new TerrainChunk[worldSizeX, worldSizeY, worldSizeZ];

        float chunkWorldSize = chunkGridSize * voxelSize;

        for (int x = 0; x < worldSizeX; x++) {
            for (int y = 0; y < worldSizeY; y++) {
                for (int z = 0; z < worldSizeZ; z++) {
                    GameObject go = new GameObject($"Chunk_{x}_{y}_{z}");
                    go.transform.SetParent(transform, false);
                    go.transform.localPosition = new Vector3(x * chunkWorldSize, y * chunkWorldSize, z * chunkWorldSize);
                    go.hideFlags = HideFlags.DontSave;

                    // === MATERIAL ANTES DE OnEnable ===
                    go.AddComponent<MeshFilter>();
                    MeshRenderer mr = go.AddComponent<MeshRenderer>();
                    if (terrainMaterial != null) mr.sharedMaterial = terrainMaterial;
                    go.AddComponent<MeshCollider>();

                    TerrainChunk chunk = go.AddComponent<TerrainChunk>();
                    chunk.GenerateBasicTerrain();
                    chunk.UpdateMesh();
                    chunks[x, y, z] = chunk;
                }
            }
        }
    }

    // === DESTRUCCIÓN SEGURA ===
    private void DestroyChunks() {
        if (chunks != null) {
            for (int x = 0; x < chunks.GetLength(0); x++)
                for (int y = 0; y < chunks.GetLength(1); y++)
                    for (int z = 0; z < chunks.GetLength(2); z++)
                        if (chunks[x, y, z] != null)
                            DestroyImmediate(chunks[x, y, z].gameObject);

            chunks = null;
        }

        // === LIMPIEZA DE HIJOS HUÉRFANOS ===
        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);
    }

    // === APLICAR PINCEL ===
    public void ApplyBrush(Vector3 worldHitPoint) {
        if (!isEditing || chunks == null) return;

        for (int x = 0; x < chunks.GetLength(0); x++) {
            for (int y = 0; y < chunks.GetLength(1); y++) {
                for (int z = 0; z < chunks.GetLength(2); z++) {
                    TerrainChunk chunk = chunks[x, y, z];
                    if (chunk == null) return;
                    if (!chunk.Densities.IsCreated) continue;

                    Vector3 localHit = worldHitPoint - chunk.transform.position;

                    int3 gridSize = new int3(chunkGridSize, chunkGridSize, chunkGridSize);
                    float chunkWorldSize = chunkGridSize * voxelSize;

                    bool inRange =
                        localHit.x >= -BrushRadius && localHit.x <= chunkWorldSize + BrushRadius &&
                        localHit.y >= -BrushRadius && localHit.y <= chunkWorldSize + BrushRadius &&
                        localHit.z >= -BrushRadius && localHit.z <= chunkWorldSize + BrushRadius;

                    if (!inRange) continue;

                    TerrainSculptor.Apply(
                        chunk.Densities,
                        chunk.Metadata,
                        gridSize,
                        voxelSize,
                        localHit,
                        BrushRadius,
                        BrushStrength,
                        CurrentBrushShape,
                        CurrentBrushType,
                        SelectedMaterialID,
                        IsVerticalBrush
                    );

                    chunk.UpdateMesh();
                }
            }
        }
    }

    // === RAYCAST VOXEL ===
    public bool RaycastVoxel(Ray ray, out Vector3 hitPoint, out TerrainChunk hitChunk) {
        hitPoint = Vector3.zero;
        hitChunk = null;

        if (!Physics.Raycast(ray, out RaycastHit hit)) return false;  

        Collider col = hit.collider;
        TerrainChunk chunk = col.GetComponent<TerrainChunk>();
        if (chunk == null) return false;

        hitPoint = hit.point;
        hitChunk = chunk;
        return true;
    }

    // === SNAPSHOT EDITOR -> JUEGO ===
    private void CaptureEditorSnapshot() {
        if (chunks == null) return;

        int total = worldSizeX * worldSizeY * worldSizeZ;
        editorToPlaySnapshot = new ChunkSnapshot[total];

        int idx = 0;
        for (int x = 0; x < worldSizeX; x++) {
            for (int y = 0; y < worldSizeY; y++) {
                for (int z = 0; z < worldSizeZ; z++) {
                    ChunkSnapshot snap = new ChunkSnapshot { x = x, y = y, z = z };
                    TerrainChunk chunk = chunks[x, y, z];

                    if (chunk != null)
                        chunk.TryExportTerrainData(out snap.densities, out snap.metadata);

                    editorToPlaySnapshot[idx++] = snap;
                }
            }
        }
    }

    private void ApplyEditorSnapshotToChunks() {
        if (chunks == null || editorToPlaySnapshot == null || editorToPlaySnapshot.Length == 0) return;

        for (int i = 0; i < editorToPlaySnapshot.Length; i++) {
            ChunkSnapshot snap = editorToPlaySnapshot[i];
            if (snap.x < 0 || snap.y < 0 || snap.z < 0) continue;
            if (snap.x >= worldSizeX || snap.y >= worldSizeY || snap.z >= worldSizeZ) continue;

            TerrainChunk chunk = chunks[snap.x, snap.y, snap.z];
            if (chunk == null) continue;
            if (!chunk.TryImportTerrainData(snap.densities, snap.metadata)) continue;

            chunk.UpdateMesh();
        }
    }
}
