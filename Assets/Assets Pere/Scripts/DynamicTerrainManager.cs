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

    // === ARREGLO TRIDIMENSIONAL DE CHUNKS ===
    private TerrainChunk[,,] chunks;

    // === ACCESO EXTERNO ===
    public TerrainChunk[,,] Chunks => chunks;
    public int WorldSizeX => worldSizeX;
    public int WorldSizeY => worldSizeY;
    public int WorldSizeZ => worldSizeZ;

    private void OnEnable() => RebuildChunks();

    private void OnDisable() => DestroyChunks();

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

                    TerrainChunk chunk = go.AddComponent<TerrainChunk>();
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
}
