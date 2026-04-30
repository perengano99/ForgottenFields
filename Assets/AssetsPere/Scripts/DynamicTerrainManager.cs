using Unity.Mathematics;
using UnityEngine;

public enum EditMode { Sculpt, Paint }
public enum SculptMode { Add, Subtract, Flatten, Smooth }

// TODO: El modo sculpt no debe asignar material, pero el paint sí. Separar lógica de ambos modos en métodos distintos para evitar confusiones y errores futuros.
// El sistema vertical no funciona como se espera. El objetivo es que esculpa de manera vertical, sin importar la orentacion de la vista o el punto de impacto. Tampoco debe funcionar en un radio de esfera, sino como un plano desde el punto vertical.
// El esculpido en general sigue siendo muy suave y no genera terreno estilo low poly.

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
    public EditMode currentEditMode = EditMode.Sculpt;
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
        if (Application.isEditor && !Application.isPlaying) CaptureEditorSnapshot();
        DestroyChunks();
    }

    private void Update() {
        if (Application.isEditor) {
            if (!wasPlaying && Application.isPlaying) {
                pendingApplyEditorSnapshotInPlay = true;
                RebuildChunks();
                ApplyEditorSnapshotToChunks();
                pendingApplyEditorSnapshotInPlay = false;
            }

            if (wasPlaying && !Application.isPlaying)
                RebuildChunks();

            wasPlaying = Application.isPlaying;
        }

        if (!Application.isPlaying || !isEditing) return;
        if (!Input.GetMouseButton(0)) return;
        if (Camera.main == null) return;

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (!RaycastVoxel(ray, out Vector3 hitPoint, out _)) return;

        if (currentEditMode == EditMode.Paint) {
            ApplyPaintBrush(hitPoint);
            return;
        }

        SculptMode mode;
        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) mode = SculptMode.Smooth;
        else if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) mode = SculptMode.Flatten;
        else if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) mode = SculptMode.Subtract;
        else mode = SculptMode.Add;

        ApplySculptBrush(hitPoint, mode);
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

        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);
    }

    // === ESCULPIDO ===
    public void ApplySculptBrush(Vector3 worldHitPoint, SculptMode mode) {
        if (!isEditing || chunks == null) return;

        for (int x = 0; x < chunks.GetLength(0); x++) {
            for (int y = 0; y < chunks.GetLength(1); y++) {
                for (int z = 0; z < chunks.GetLength(2); z++) {
                    TerrainChunk chunk = chunks[x, y, z];
                    if (chunk == null) continue;
                    if (!chunk.Densities.IsCreated || !chunk.Metadata.IsCreated) continue;

                    Vector3 localHit = worldHitPoint - chunk.transform.position;

                    float chunkWorldSize = chunkGridSize * voxelSize;
                    bool inRange =
                        localHit.x >= -BrushRadius && localHit.x <= chunkWorldSize + BrushRadius &&
                        localHit.y >= -BrushRadius && localHit.y <= chunkWorldSize + BrushRadius &&
                        localHit.z >= -BrushRadius && localHit.z <= chunkWorldSize + BrushRadius;

                    if (!inRange) continue;

                    if (mode == SculptMode.Smooth)
                        ApplySmoothToChunk(chunk, localHit);
                    else {
                        BrushType brushType = mode == SculptMode.Add ? BrushType.SphereAdd : mode == SculptMode.Subtract ? BrushType.SphereSubtract : BrushType.Flatten;
                        TerrainSculptor.Apply(
                            chunk.Densities,
                            chunk.Metadata,
                            new int3(chunkGridSize, chunkGridSize, chunkGridSize),
                            voxelSize,
                            localHit,
                            BrushRadius,
                            BrushStrength,
                            CurrentBrushShape,
                            brushType,
                            SelectedMaterialID,
                            IsVerticalBrush
                        );
                    }

                    chunk.UpdateMesh();
                }
            }
        }
    }

    // === PINTURA ===
    public void ApplyPaintBrush(Vector3 worldHitPoint) {
        if (!isEditing || chunks == null) return;

        for (int x = 0; x < chunks.GetLength(0); x++) {
            for (int y = 0; y < chunks.GetLength(1); y++) {
                for (int z = 0; z < chunks.GetLength(2); z++) {
                    TerrainChunk chunk = chunks[x, y, z];
                    if (chunk == null) continue;
                    if (!chunk.Densities.IsCreated || !chunk.Metadata.IsCreated) continue;

                    Vector3 localHit = worldHitPoint - chunk.transform.position;

                    float chunkWorldSize = chunkGridSize * voxelSize;
                    bool inRange =
                        localHit.x >= -BrushRadius && localHit.x <= chunkWorldSize + BrushRadius &&
                        localHit.y >= -BrushRadius && localHit.y <= chunkWorldSize + BrushRadius &&
                        localHit.z >= -BrushRadius && localHit.z <= chunkWorldSize + BrushRadius;

                    if (!inRange) continue;
                    if (!PaintChunk(chunk, localHit)) continue;

                    chunk.UpdateMesh();
                }
            }
        }
    }

    private bool PaintChunk(TerrainChunk chunk, Vector3 localHit) {
        var densities = chunk.Densities;
        var metadata = chunk.Metadata;

        int pointsX = chunkGridSize + 1;
        int pointsY = chunkGridSize + 1;
        int planeSize = pointsX * pointsY;

        bool modified = false;

        for (int index = 0; index < densities.Length; index++) {
            int z = index / planeSize;
            int rem = index - z * planeSize;
            int y = rem / pointsX;
            int x = rem - y * pointsX;

            float3 nodePos = new float3(x, y, z) * voxelSize;
            float dist = math.distance(nodePos, (float3)localHit);
            if (dist > BrushRadius) continue;
            if (densities[index] >= 0f) continue;
            if (metadata[index] == SelectedMaterialID) continue;

            metadata[index] = SelectedMaterialID;
            modified = true;
        }

        return modified;
    }

    private void ApplySmoothToChunk(TerrainChunk chunk, Vector3 localHit) {
        var densities = chunk.Densities;
        int pointsX = chunkGridSize + 1;
        int planeSize = pointsX * (chunkGridSize + 1);

        float[] copy = new float[densities.Length];
        for (int i = 0; i < densities.Length; i++) copy[i] = densities[i];

        for (int index = 0; index < densities.Length; index++) {
            int z = index / planeSize;
            int rem = index - z * planeSize;
            int y = rem / pointsX;
            int x = rem - y * pointsX;

            float3 nodePos = new float3(x, y, z) * voxelSize;
            float dist = math.distance(nodePos, (float3)localHit);
            if (dist > BrushRadius) continue;

            float sum = copy[index];
            int count = 1;

            if (x > 0) { sum += copy[index - 1]; count++; }
            if (x < chunkGridSize) { sum += copy[index + 1]; count++; }
            if (y > 0) { sum += copy[index - pointsX]; count++; }
            if (y < chunkGridSize) { sum += copy[index + pointsX]; count++; }
            if (z > 0) { sum += copy[index - planeSize]; count++; }
            if (z < chunkGridSize) { sum += copy[index + planeSize]; count++; }

            float target = sum / count;
            densities[index] = math.lerp(copy[index], target, BrushStrength * 0.5f);
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
