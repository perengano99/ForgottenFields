using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public enum EditMode { Sculpt, Paint }
public enum SculptMode { Add, Subtract, Flatten, Smooth }

// -[] (REFACTORIZAR Y ACOMODAR EN CLASE AISLADA TODO EL SISTEMA DE EDICION) El sistema vertical no funciona como se espera. El objetivo es que esculpa de manera vertical, sin importar la orentacion de la vista o el punto de impacto. Tampoco debe funcionar en un radio de esfera, sino como un plano desde el punto vertical.
// -[I] El esculpido en general sigue siendo muy suave y no genera terreno estilo low poly.

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
    private Dictionary<Vector3Int, TerrainChunk> activeChunks = new Dictionary<Vector3Int, TerrainChunk>();

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
        if (!RaycastVoxel(ray, out Vector3 hitPoint, out TerrainChunk hitChunk)) return;

        float brushRadius = BrushRadius;
        List<TerrainChunk> affectedChunks = GetChunksInRadius(hitPoint, brushRadius);
        if (affectedChunks.Count == 0) return;

        if (currentEditMode == EditMode.Paint) {
            ApplyPaintBrush(hitPoint, affectedChunks);
            return;
        }

        SculptMode mode;
        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) mode = SculptMode.Smooth;
        else if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) mode = SculptMode.Flatten;
        else if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) mode = SculptMode.Subtract;
        else mode = SculptMode.Add;

        byte hitMaterial = PickRayHitMat(hitChunk, hitPoint);
        ApplySculptBrush(hitPoint, mode, hitMaterial, affectedChunks);
    }

    private byte PickRayHitMat(TerrainChunk chunk, Vector3 hitPoint) {
        if (chunk == null || !chunk.Metadata.IsCreated) return 0;

        Vector3 localHit = hitPoint - chunk.transform.position;
        int3 gridSize = new int3(chunkGridSize + 1, chunkGridSize + 1, chunkGridSize + 1);

        int x = Mathf.RoundToInt(localHit.x / voxelSize);
        int y = Mathf.RoundToInt(localHit.y / voxelSize);
        int z = Mathf.RoundToInt(localHit.z / voxelSize);

        if (x < 0 || y < 0 || z < 0 || x >= gridSize.x || y >= gridSize.y || z >= gridSize.z) return 0;

        int hitIndex = x + (y * gridSize.x) + (z * gridSize.x * gridSize.y);
        if (hitIndex < 0 || hitIndex >= chunk.Metadata.Length) return 0;

        return chunk.Metadata[hitIndex];
    }

    // === CONSTRUCCIÓN DE CHUNKS ===
    private void RebuildChunks() {
        DestroyChunks();

        chunks = new TerrainChunk[worldSizeX, worldSizeY, worldSizeZ];
        activeChunks.Clear();

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
                    activeChunks[new Vector3Int(x, y, z)] = chunk;
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

        activeChunks.Clear();

        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);
    }

    private struct SculptBatchEntry {
        public TerrainChunk chunk;
        public Vector3 localHit;
        public float[] preDensities;
        public byte[] preMetadata;
        public byte impactMaterialID;
        public bool hasJob;
        public NativeArray<float> sourceDensities;
    }

    // === ESCULPIDO ===
    public void ApplySculptBrush(Vector3 worldHitPoint, SculptMode mode, byte hitMaterial, List<TerrainChunk> affectedChunks) {
        if (!isEditing || chunks == null) return;
        if (affectedChunks == null || affectedChunks.Count == 0) return;

        List<SculptBatchEntry> entries = new List<SculptBatchEntry>(affectedChunks.Count);
        NativeList<JobHandle> jobHandles = new NativeList<JobHandle>(Allocator.Temp);

        for (int i = 0; i < affectedChunks.Count; i++) {
            TerrainChunk chunk = affectedChunks[i];
            if (chunk == null) continue;
            if (!chunk.Densities.IsCreated || !chunk.Metadata.IsCreated) continue;

            Vector3 localHit = chunk.transform.InverseTransformPoint(worldHitPoint);

            float[] preDensities;
            byte[] preMetadata;
            CaptureChunkSnapshot(chunk, out preDensities, out preMetadata);

            byte impactMaterialID = hitMaterial != 0 ? hitMaterial : PickImpactMat(localHit, preDensities, preMetadata);

            SculptBatchEntry entry = new SculptBatchEntry {
                chunk = chunk,
                localHit = localHit,
                preDensities = preDensities,
                preMetadata = preMetadata,
                impactMaterialID = impactMaterialID,
                hasJob = false,
                sourceDensities = default
            };

            if (mode == SculptMode.Smooth) {
                ApplySmoothToChunk(chunk, localHit);
                jobHandles.Add(default);
                entries.Add(entry);
                continue;
            }

            BrushType brushType = mode == SculptMode.Add ? BrushType.SphereAdd : mode == SculptMode.Subtract ? BrushType.SphereSubtract : BrushType.Flatten;

            NativeArray<float> sourceDensities;
            JobHandle handle = TerrainSculptor.ScheduleSculptJob(
                chunk.Densities,
                chunk.Metadata,
                new int3(chunkGridSize, chunkGridSize, chunkGridSize),
                voxelSize,
                worldHitPoint,
                chunk.transform.position,
                BrushRadius,
                BrushStrength,
                CurrentBrushShape,
                brushType,
                impactMaterialID,
                IsVerticalBrush,
                out sourceDensities
            );

            entry.hasJob = true;
            entry.sourceDensities = sourceDensities;
            entries.Add(entry);
            jobHandles.Add(handle);
        }

        if (jobHandles.Length > 0)
            JobHandle.CompleteAll(jobHandles.AsArray());

        SyncAffectedChunkBorders(affectedChunks);

        for (int i = 0; i < entries.Count; i++) {
            SculptBatchEntry entry = entries[i];

            if (mode != SculptMode.Smooth)
                ReapplyMaterialsAfterSculpt(entry.chunk, entry.localHit, entry.impactMaterialID, mode, entry.preDensities, entry.preMetadata);

            if (entry.hasJob && entry.sourceDensities.IsCreated)
                entry.sourceDensities.Dispose();
        }

        for (int i = 0; i < affectedChunks.Count; i++) {
            TerrainChunk chunk = affectedChunks[i];
            if (chunk == null) continue;
            if (!chunk.Densities.IsCreated || !chunk.Metadata.IsCreated) continue;
            chunk.UpdateMesh();
        }

        jobHandles.Dispose();
    }

    private void CaptureChunkSnapshot(TerrainChunk chunk, out float[] preDensities, out byte[] preMetadata) {
        var densities = chunk.Densities;
        var metadata = chunk.Metadata;

        preDensities = new float[densities.Length];
        preMetadata = new byte[metadata.Length];

        for (int i = 0; i < densities.Length; i++) {
            preDensities[i] = densities[i];
            preMetadata[i] = metadata[i];
        }
    }

    private byte PickImpactMat(Vector3 localHit, float[] preDensities, byte[] preMetadata) {
        int pointsX = chunkGridSize + 1;
        int pointsY = chunkGridSize + 1;
        int planeSize = pointsX * pointsY;

        int hx = Mathf.Clamp(Mathf.RoundToInt(localHit.x / voxelSize), 0, chunkGridSize);
        int hy = Mathf.Clamp(Mathf.RoundToInt(localHit.y / voxelSize), 0, chunkGridSize);
        int hz = Mathf.Clamp(Mathf.RoundToInt(localHit.z / voxelSize), 0, chunkGridSize);

        int index = hx + hy * pointsX + hz * planeSize;
        if (preDensities[index] <= 0f && preMetadata[index] != 0) return preMetadata[index];

        byte fallback = 0;
        float bestSolid = float.MaxValue;

        TryPickLocalMat(hx + 1, hy, hz, preDensities, preMetadata, pointsX, planeSize, ref bestSolid, ref fallback);
        TryPickLocalMat(hx - 1, hy, hz, preDensities, preMetadata, pointsX, planeSize, ref bestSolid, ref fallback);
        TryPickLocalMat(hx, hy + 1, hz, preDensities, preMetadata, pointsX, planeSize, ref bestSolid, ref fallback);
        TryPickLocalMat(hx, hy - 1, hz, preDensities, preMetadata, pointsX, planeSize, ref bestSolid, ref fallback);
        TryPickLocalMat(hx, hy, hz + 1, preDensities, preMetadata, pointsX, planeSize, ref bestSolid, ref fallback);
        TryPickLocalMat(hx, hy, hz - 1, preDensities, preMetadata, pointsX, planeSize, ref bestSolid, ref fallback);

        return fallback;
    }

    private void TryPickLocalMat(
        int x,
        int y,
        int z,
        float[] preDensities,
        byte[] preMetadata,
        int pointsX,
        int planeSize,
        ref float bestSolid,
        ref byte mat) {
        if (x < 0 || y < 0 || z < 0 || x > chunkGridSize || y > chunkGridSize || z > chunkGridSize) return;

        int i = x + y * pointsX + z * planeSize;
        if (preDensities[i] > 0f) return;
        if (preMetadata[i] == 0) return;

        if (preDensities[i] < bestSolid) {
            bestSolid = preDensities[i];
            mat = preMetadata[i];
        }
    }

    private void ReapplyMaterialsAfterSculpt(TerrainChunk chunk, Vector3 localHit, byte impactMaterialID, SculptMode mode, float[] preDensities, byte[] preMetadata) {
        var densities = chunk.Densities;
        var metadata = chunk.Metadata;

        int pointsX = chunkGridSize + 1;
        int pointsY = chunkGridSize + 1;
        int planeSize = pointsX * pointsY;

        float processRadius = BrushRadius + voxelSize * 1.5f;
        float impactFallbackRadius = math.max(voxelSize, BrushRadius * 0.6f);

        // === PASO 1: CONSERVAR MATERIAL PREVIO Y ASIGNAR SOLO EN SÓLIDO NUEVO ===
        for (int index = 0; index < densities.Length; index++) {
            int z = index / planeSize;
            int rem = index - z * planeSize;
            int y = rem / pointsX;
            int x = rem - y * pointsX;

            float3 nodePos = new float3(x, y, z) * voxelSize;
            float dist = math.distance(nodePos, (float3)localHit);
            if (dist > processRadius) continue;

            bool wasSolid = preDensities[index] <= 0f;
            bool isSolidNow = densities[index] <= 0f;
            if (!isSolidNow) continue;

            byte preMat = preMetadata[index];
            if (wasSolid) {
                if (preMat != 0)
                    metadata[index] = preMat;
                continue;
            }

            byte nearestMat = ResolveNearestMat(x, y, z, metadata, densities, pointsX, planeSize, 1);
            if (nearestMat == 0)
                nearestMat = ResolveNearestMat(x, y, z, preMetadata, preDensities, pointsX, planeSize, 2);
            if (nearestMat == 0 && mode == SculptMode.Add && impactMaterialID != 0 && dist <= impactFallbackRadius)
                nearestMat = impactMaterialID;

            if (nearestMat != 0)
                metadata[index] = nearestMat;
        }

        // === PASO 2: RELLENO LOCAL EN PASADAS PARA PROPAGACIÓN GRADUAL ===
        int fillPasses = mode == SculptMode.Add ? 4 : 1;
        for (int pass = 0; pass < fillPasses; pass++) {
            bool changed = false;

            for (int index = 0; index < densities.Length; index++) {
                if (densities[index] > 0f) continue;
                if (metadata[index] != 0) continue;

                int z = index / planeSize;
                int rem = index - z * planeSize;
                int y = rem / pointsX;
                int x = rem - y * pointsX;

                float3 nodePos = new float3(x, y, z) * voxelSize;
                float dist = math.distance(nodePos, (float3)localHit);
                if (dist > processRadius) continue;

                byte nearestMat = ResolveNearestMat(x, y, z, metadata, densities, pointsX, planeSize, 1);
                if (nearestMat == 0)
                    nearestMat = ResolveNearestMat(x, y, z, preMetadata, preDensities, pointsX, planeSize, 1);
                if (nearestMat == 0 && mode == SculptMode.Add && impactMaterialID != 0 && dist <= impactFallbackRadius)
                    nearestMat = impactMaterialID;

                if (nearestMat == 0) continue;

                metadata[index] = nearestMat;
                changed = true;
            }

            if (!changed) break;
        }
    }

    private byte ResolveNearestMat(int x, int y, int z, NativeArray<byte> metadata, NativeArray<float> densities, int pointsX, int planeSize, int cellRadius) {
        byte best = 0;
        float bestDistance = float.MaxValue;
        float bestSolid = float.MaxValue;

        for (int dz = -cellRadius; dz <= cellRadius; dz++) {
            for (int dy = -cellRadius; dy <= cellRadius; dy++) {
                for (int dx = -cellRadius; dx <= cellRadius; dx++) {
                    int nx = x + dx;
                    int ny = y + dy;
                    int nz = z + dz;

                    if (nx < 0 || ny < 0 || nz < 0 || nx > chunkGridSize || ny > chunkGridSize || nz > chunkGridSize) continue;

                    int ni = nx + ny * pointsX + nz * planeSize;
                    if (densities[ni] > 0f) continue;

                    byte m = metadata[ni];
                    if (m == 0) continue;

                    float d = dx * dx + dy * dy + dz * dz;
                    float solid = densities[ni];

                    if (d < bestDistance || (math.abs(d - bestDistance) < 0.001f && solid < bestSolid)) {
                        bestDistance = d;
                        bestSolid = solid;
                        best = m;
                    }
                }
            }
        }

        return best;
    }

    private byte ResolveNearestMat(int x, int y, int z, byte[] metadata, float[] densities, int pointsX, int planeSize, int cellRadius) {
        byte best = 0;
        float bestDistance = float.MaxValue;
        float bestSolid = float.MaxValue;

        for (int dz = -cellRadius; dz <= cellRadius; dz++) {
            for (int dy = -cellRadius; dy <= cellRadius; dy++) {
                for (int dx = -cellRadius; dx <= cellRadius; dx++) {
                    int nx = x + dx;
                    int ny = y + dy;
                    int nz = z + dz;

                    if (nx < 0 || ny < 0 || nz < 0 || nx > chunkGridSize || ny > chunkGridSize || nz > chunkGridSize) continue;

                    int ni = nx + ny * pointsX + nz * planeSize;
                    if (densities[ni] > 0f) continue;

                    byte m = metadata[ni];
                    if (m == 0) continue;

                    float d = dx * dx + dy * dy + dz * dz;
                    float solid = densities[ni];

                    if (d < bestDistance || (math.abs(d - bestDistance) < 0.001f && solid < bestSolid)) {
                        bestDistance = d;
                        bestSolid = solid;
                        best = m;
                    }
                }
            }
        }

        return best;
    }

    // === PINTURA ===
    public void ApplyPaintBrush(Vector3 worldHitPoint, List<TerrainChunk> affectedChunks) {
        if (!isEditing || chunks == null) return;
        if (affectedChunks == null || affectedChunks.Count == 0) return;

        NativeList<JobHandle> jobHandles = new NativeList<JobHandle>(Allocator.Temp);

        for (int i = 0; i < affectedChunks.Count; i++) {
            TerrainChunk chunk = affectedChunks[i];
            if (chunk == null) continue;
            if (!chunk.Densities.IsCreated || !chunk.Metadata.IsCreated) continue;

            Vector3 localHit = chunk.transform.InverseTransformPoint(worldHitPoint);
            JobHandle handle = TerrainSculptor.SchedulePaintJob(
                chunk.Metadata,
                chunk.Densities,
                new int3(chunkGridSize, chunkGridSize, chunkGridSize),
                voxelSize,
                worldHitPoint,
                chunk.transform.position,
                BrushRadius,
                SelectedMaterialID
            );

            jobHandles.Add(handle);
        }

        if (jobHandles.Length > 0)
            JobHandle.CompleteAll(jobHandles.AsArray());

        SyncAffectedChunkBorders(affectedChunks);

        for (int i = 0; i < affectedChunks.Count; i++) {
            TerrainChunk chunk = affectedChunks[i];
            if (chunk == null) continue;
            if (!chunk.Densities.IsCreated || !chunk.Metadata.IsCreated) continue;
            chunk.UpdateMesh();
        }

        jobHandles.Dispose();
    }

    private void SyncAffectedChunkBorders(List<TerrainChunk> affectedChunks) {
        if (affectedChunks == null || affectedChunks.Count == 0) return;
        if (activeChunks == null || activeChunks.Count == 0) return;

        for (int i = 0; i < affectedChunks.Count; i++) {
            TerrainChunk chunk = affectedChunks[i];
            if (chunk == null) continue;
            if (!TryGetChunkCoord(chunk, out Vector3Int coord)) continue;

            Vector3Int rightCoord = coord + Vector3Int.right;
            if (activeChunks.TryGetValue(rightCoord, out TerrainChunk rightNeighbor) && rightNeighbor != null)
                chunk.SyncBordersWith(rightNeighbor, Vector3Int.right);

            Vector3Int leftCoord = coord + Vector3Int.left;
            if (activeChunks.TryGetValue(leftCoord, out TerrainChunk leftNeighbor) && leftNeighbor != null)
                chunk.SyncBordersWith(leftNeighbor, Vector3Int.left);

            Vector3Int upCoord = coord + Vector3Int.up;
            if (activeChunks.TryGetValue(upCoord, out TerrainChunk upNeighbor) && upNeighbor != null)
                chunk.SyncBordersWith(upNeighbor, Vector3Int.up);

            Vector3Int downCoord = coord + Vector3Int.down;
            if (activeChunks.TryGetValue(downCoord, out TerrainChunk downNeighbor) && downNeighbor != null)
                chunk.SyncBordersWith(downNeighbor, Vector3Int.down);

            Vector3Int forwardCoord = coord + Vector3Int.forward;
            if (activeChunks.TryGetValue(forwardCoord, out TerrainChunk forwardNeighbor) && forwardNeighbor != null)
                chunk.SyncBordersWith(forwardNeighbor, Vector3Int.forward);

            Vector3Int backCoord = coord + Vector3Int.back;
            if (activeChunks.TryGetValue(backCoord, out TerrainChunk backNeighbor) && backNeighbor != null)
                chunk.SyncBordersWith(backNeighbor, Vector3Int.back);
        }
    }

    private bool TryGetChunkCoord(TerrainChunk chunk, out Vector3Int coord) {
        foreach (var kvp in activeChunks) {
            if (kvp.Value != chunk) continue;

            coord = kvp.Key;
            return true;
        }

        coord = default;
        return false;
    }

    private List<TerrainChunk> GetChunksInRadius(Vector3 hitPoint, float radius) {
        List<TerrainChunk> affectedChunks = new List<TerrainChunk>();
        if (activeChunks == null || activeChunks.Count == 0) return affectedChunks;

        float expandedRadius = radius + voxelSize * 2f;
        Bounds brushBounds = new Bounds(hitPoint, Vector3.one * (expandedRadius * 2f));

        float chunkSize = chunkGridSize * voxelSize;
        Vector3 chunkExtents = Vector3.one * (chunkSize * 0.5f);

        foreach (var kvp in activeChunks) {
            TerrainChunk chunk = kvp.Value;
            if (chunk == null) continue;

            Vector3 chunkCenter = chunk.transform.position + chunkExtents;
            Bounds chunkBounds = new Bounds(chunkCenter, Vector3.one * chunkSize);
            if (!chunkBounds.Intersects(brushBounds)) continue;

            affectedChunks.Add(chunk);
        }

        return affectedChunks;
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