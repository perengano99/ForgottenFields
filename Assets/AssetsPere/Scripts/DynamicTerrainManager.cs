using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public enum EditMode { Sculpt, Paint }
public enum SculptMode { Add, Subtract, Flatten, Smooth }

// -[] El sistema vertical no funciona como se espera. El objetivo es que esculpa de manera vertical, sin importar la orentacion de la vista o el punto de impacto. Tampoco debe funcionar en un radio de esfera, sino como un plano desde el punto vertical.
// -[] El esculpido en general sigue siendo muy suave y no genera terreno estilo low poly.

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
        if (!RaycastVoxel(ray, out Vector3 hitPoint, out TerrainChunk hitChunk)) return;

        if (currentEditMode == EditMode.Paint) {
            ApplyPaintBrush(hitPoint);
            return;
        }

        SculptMode mode;
        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) mode = SculptMode.Smooth;
        else if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) mode = SculptMode.Flatten;
        else if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) mode = SculptMode.Subtract;
        else mode = SculptMode.Add;

        byte hitMaterial = PickRayHitMat(hitChunk, hitPoint);
        ApplySculptBrush(hitPoint, mode, hitMaterial);
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
    public void ApplySculptBrush(Vector3 worldHitPoint, SculptMode mode, byte hitMaterial = 0) {
        if (!isEditing || chunks == null) return;

        List<TerrainChunk> chunksInRadius = GetChunksInRadius(worldHitPoint, BrushRadius);
        if (chunksInRadius.Count == 0) return;

        List<SculptBatchEntry> entries = new List<SculptBatchEntry>(chunksInRadius.Count);
        List<JobHandle> handles = new List<JobHandle>(chunksInRadius.Count);

        for (int i = 0; i < chunksInRadius.Count; i++) {
            TerrainChunk chunk = chunksInRadius[i];
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
                localHit,
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
            handles.Add(handle);
        }

        if (handles.Count > 0) {
            NativeArray<JobHandle> handleArray = new NativeArray<JobHandle>(handles.Count, Allocator.Temp);
            for (int i = 0; i < handles.Count; i++)
                handleArray[i] = handles[i];

            JobHandle.CompleteAll(handleArray);
            handleArray.Dispose();
        }

        for (int i = 0; i < entries.Count; i++) {
            SculptBatchEntry entry = entries[i];

            if (mode != SculptMode.Smooth)
                ReapplyMaterialsAfterSculpt(entry.chunk, entry.localHit, entry.impactMaterialID, mode, entry.preDensities, entry.preMetadata);

            if (entry.hasJob && entry.sourceDensities.IsCreated)
                entry.sourceDensities.Dispose();
        }

        for (int i = 0; i < entries.Count; i++)
            entries[i].chunk.UpdateMesh();
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
    public void ApplyPaintBrush(Vector3 worldHitPoint) {
        if (!isEditing || chunks == null) return;

        List<TerrainChunk> chunksInRadius = GetChunksInRadius(worldHitPoint, BrushRadius);
        if (chunksInRadius.Count == 0) return;

        List<TerrainChunk> affectedChunks = new List<TerrainChunk>(chunksInRadius.Count);
        List<JobHandle> handles = new List<JobHandle>(chunksInRadius.Count);

        for (int i = 0; i < chunksInRadius.Count; i++) {
            TerrainChunk chunk = chunksInRadius[i];
            if (chunk == null) continue;
            if (!chunk.Densities.IsCreated || !chunk.Metadata.IsCreated) continue;

            Vector3 localHit = chunk.transform.InverseTransformPoint(worldHitPoint);
            JobHandle handle = TerrainSculptor.SchedulePaintJob(
                chunk.Metadata,
                chunk.Densities,
                new int3(chunkGridSize, chunkGridSize, chunkGridSize),
                voxelSize,
                localHit,
                BrushRadius,
                SelectedMaterialID
            );

            handles.Add(handle);
            affectedChunks.Add(chunk);
        }

        if (handles.Count > 0) {
            NativeArray<JobHandle> handleArray = new NativeArray<JobHandle>(handles.Count, Allocator.Temp);
            for (int i = 0; i < handles.Count; i++)
                handleArray[i] = handles[i];

            JobHandle.CompleteAll(handleArray);
            handleArray.Dispose();
        }

        for (int i = 0; i < affectedChunks.Count; i++)
            affectedChunks[i].UpdateMesh();
    }

    private List<TerrainChunk> GetChunksInRadius(Vector3 hitPoint, float radius) {
        List<TerrainChunk> result = new List<TerrainChunk>();
        if (chunks == null) return result;

        float chunkSize = chunkGridSize * voxelSize;

        Vector3 minBounds = hitPoint - new Vector3(radius, radius, radius);
        Vector3 maxBounds = hitPoint + new Vector3(radius, radius, radius);

        Vector3 localMin = minBounds - transform.position;
        Vector3 localMax = maxBounds - transform.position;

        int minChunkX = Mathf.FloorToInt(localMin.x / chunkSize);
        int minChunkY = Mathf.FloorToInt(localMin.y / chunkSize);
        int minChunkZ = Mathf.FloorToInt(localMin.z / chunkSize);

        int maxChunkX = Mathf.FloorToInt(localMax.x / chunkSize);
        int maxChunkY = Mathf.FloorToInt(localMax.y / chunkSize);
        int maxChunkZ = Mathf.FloorToInt(localMax.z / chunkSize);

        minChunkX = Mathf.Clamp(minChunkX, 0, worldSizeX - 1);
        minChunkY = Mathf.Clamp(minChunkY, 0, worldSizeY - 1);
        minChunkZ = Mathf.Clamp(minChunkZ, 0, worldSizeZ - 1);

        maxChunkX = Mathf.Clamp(maxChunkX, 0, worldSizeX - 1);
        maxChunkY = Mathf.Clamp(maxChunkY, 0, worldSizeY - 1);
        maxChunkZ = Mathf.Clamp(maxChunkZ, 0, worldSizeZ - 1);

        for (int x = minChunkX; x <= maxChunkX; x++)
            for (int y = minChunkY; y <= maxChunkY; y++)
                for (int z = minChunkZ; z <= maxChunkZ; z++) {
                    TerrainChunk chunk = chunks[x, y, z];
                    if (chunk != null)
                        result.Add(chunk);
                }

        return result;
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
