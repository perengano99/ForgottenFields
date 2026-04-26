using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class DynamicTerrainGenerator : MonoBehaviour {
    // === GRID SETTINGS ===
    [SerializeField] private int width = 10;
    [SerializeField] private int length = 10;
    [SerializeField] private float cellSize = 1f;

    // === BRUSH SETTINGS ===
    [SerializeField, HideInInspector] private float brushRadius = 2f;
    [SerializeField, HideInInspector] private float brushStrength = 0.5f;

    // === MODELO DE DATOS (PERSISTENCIA) ===
    [SerializeField, HideInInspector] private float[] heightMap;
    [SerializeField, HideInInspector] private int lastAppliedWidth;
    [SerializeField, HideInInspector] private int lastAppliedLength;

    // === RUNTIME DATA ===
    private Mesh terrainMesh;
    private int cellCountX;
    private int cellCountZ;

    private void OnEnable() {
        if (terrainMesh == null) {
            terrainMesh = new Mesh {
                name = "TerrainMesh",
                hideFlags = HideFlags.DontSave
            };
        }

        GetComponent<MeshFilter>().sharedMesh = terrainMesh;

        GenerateGridMesh();
    }

    private void OnValidate() {
        if (!isActiveAndEnabled) return;
    }

    private void OnDisable() {
        if (terrainMesh != null) {
            DestroyImmediate(terrainMesh);
            terrainMesh = null;
        }
    }

    private void OnDestroy() {
        if (terrainMesh != null) {
            DestroyImmediate(terrainMesh);
            terrainMesh = null;
        }
    }

    // === PUBLIC API ===
    public void ApplySettings() {
        if (terrainMesh == null) {
            terrainMesh = new Mesh {
                name = "TerrainMesh",
                hideFlags = HideFlags.DontSave
            };

            GetComponent<MeshFilter>().sharedMesh = terrainMesh;
        }

        GenerateGridMesh();
    }

    // === PUBLIC API ===
    public void RegenerateMesh() {
        if (terrainMesh == null) {
            terrainMesh = new Mesh {
                name = "TerrainMesh",
                hideFlags = HideFlags.DontSave
            };

            GetComponent<MeshFilter>().sharedMesh = terrainMesh;
        }

        if (width <= 0 || length <= 0 || cellSize <= 0f) return;

        int regenerateCountX = Mathf.Max(1, Mathf.RoundToInt(width / cellSize));
        int regenerateCountZ = Mathf.Max(1, Mathf.RoundToInt(length / cellSize));
        heightMap = new float[(regenerateCountX + 1) * (regenerateCountZ + 1)];

        GenerateGridMesh();
    }

    // === PUBLIC API ===
    public float GetBrushRadius() {
        return brushRadius;
    }

    // === PUBLIC API ===
    public void SmoothTerrain(Vector3 worldHitPoint) {
        if (heightMap == null || terrainMesh == null) return;
        if (cellCountX <= 0 || cellCountZ <= 0) return;

        Vector3 localHit = transform.InverseTransformPoint(worldHitPoint);
        float stepX = (float)width / cellCountX;
        float stepZ = (float)length / cellCountZ;

        int minX = Mathf.Max(0, Mathf.FloorToInt((localHit.x - brushRadius) / stepX));
        int maxX = Mathf.Min(cellCountX, Mathf.CeilToInt((localHit.x + brushRadius) / stepX));
        int minZ = Mathf.Max(0, Mathf.FloorToInt((localHit.z - brushRadius) / stepZ));
        int maxZ = Mathf.Min(cellCountZ, Mathf.CeilToInt((localHit.z + brushRadius) / stepZ));

        for (int z = minZ; z <= maxZ; z++) {
            for (int x = minX; x <= maxX; x++) {
                int hmIndex = z * (cellCountX + 1) + x;

                float dx = (x * stepX) - localHit.x;
                float dz = (z * stepZ) - localHit.z;
                float distance = Mathf.Sqrt(dx * dx + dz * dz);

                if (distance >= brushRadius) continue;

                float distanceRatio = Mathf.Clamp01(distance / brushRadius);
                float falloff = Mathf.SmoothStep(1f, 0f, distanceRatio);
                float smoothFactor = brushStrength * falloff * 0.05f;

                float neighborSum = 0f;
                int neighborCount = 0;

                for (int nz = z - 1; nz <= z + 1; nz++) {
                    if (nz < 0 || nz > cellCountZ) continue;

                    for (int nx = x - 1; nx <= x + 1; nx++) {
                        if (nx < 0 || nx > cellCountX) continue;

                        neighborSum += heightMap[nz * (cellCountX + 1) + nx];
                        neighborCount++;
                    }
                }

                if (neighborCount <= 0) continue;

                float averageHeight = neighborSum / neighborCount;
                heightMap[hmIndex] = Mathf.Lerp(heightMap[hmIndex], averageHeight, smoothFactor);
            }
        }

        GenerateGridMesh();
    }

    // === MESH GENERATION ===
    private void GenerateGridMesh() {
        if (terrainMesh == null || width <= 0 || length <= 0 || cellSize <= 0f) return;

        int previousCountX = cellCountX;
        int previousCountZ = cellCountZ;
        float[] previousHeightMap = heightMap;
        int previousWidth = lastAppliedWidth;
        int previousLength = lastAppliedLength;

        // === cellSize define densidad, width/length definen tamaño final ===
        cellCountX = Mathf.Max(1, Mathf.RoundToInt(width / cellSize));
        cellCountZ = Mathf.Max(1, Mathf.RoundToInt(length / cellSize));

        float stepX = (float)width / cellCountX;
        float stepZ = (float)length / cellCountZ;

        // === VALIDAR HEIGHTMAP ===
        int expectedSize = (cellCountX + 1) * (cellCountZ + 1);
        if (heightMap == null || heightMap.Length != expectedSize) {
            float[] resizedHeightMap = new float[expectedSize];

            // === PERSISTENCIA POR MUNDO: mantiene escala al cambiar cellSize y recorta al cambiar W/L ===
            if (previousHeightMap != null && previousHeightMap.Length > 0 && previousCountX > 0 && previousCountZ > 0 && previousWidth > 0 && previousLength > 0) {
                float previousStepX = (float)previousWidth / previousCountX;
                float previousStepZ = (float)previousLength / previousCountZ;

                for (int z = 0; z <= cellCountZ; z++) {
                    float worldZ = z * stepZ;
                    if (worldZ > previousLength) continue;

                    float sourceZ = worldZ / previousStepZ;
                    int z0 = Mathf.Clamp(Mathf.FloorToInt(sourceZ), 0, previousCountZ);
                    int z1 = Mathf.Min(z0 + 1, previousCountZ);
                    float tz = sourceZ - z0;

                    for (int x = 0; x <= cellCountX; x++) {
                        float worldX = x * stepX;
                        if (worldX > previousWidth) continue;

                        float sourceX = worldX / previousStepX;
                        int x0 = Mathf.Clamp(Mathf.FloorToInt(sourceX), 0, previousCountX);
                        int x1 = Mathf.Min(x0 + 1, previousCountX);
                        float tx = sourceX - x0;

                        float h00 = previousHeightMap[z0 * (previousCountX + 1) + x0];
                        float h10 = previousHeightMap[z0 * (previousCountX + 1) + x1];
                        float h01 = previousHeightMap[z1 * (previousCountX + 1) + x0];
                        float h11 = previousHeightMap[z1 * (previousCountX + 1) + x1];

                        float h0 = Mathf.Lerp(h00, h10, tx);
                        float h1 = Mathf.Lerp(h01, h11, tx);
                        resizedHeightMap[z * (cellCountX + 1) + x] = Mathf.Lerp(h0, h1, tz);
                    }
                }
            }

            heightMap = resizedHeightMap;
        }

        // === CONSTRUCCIÓN TOPOLÓGICA (HARD EDGES - CELDAS DESCONECTADAS) ===
        int cellCount = cellCountX * cellCountZ;

        Vector3[] vertices = new Vector3[cellCount * 4];
        Vector2[] uvs = new Vector2[cellCount * 4];
        int[] triangles = new int[cellCount * 6];

        int vertIndex = 0;
        int triIndex = 0;

        for (int z = 0; z < cellCountZ; z++) {
            for (int x = 0; x < cellCountX; x++) {
                // === ÍNDICES DEL HEIGHTMAP (4 esquinas de la celda) ===
                int hmBL = z * (cellCountX + 1) + x;       // Bottom-Left
                int hmBR = z * (cellCountX + 1) + (x + 1); // Bottom-Right
                int hmTL = (z + 1) * (cellCountX + 1) + x;       // Top-Left
                int hmTR = (z + 1) * (cellCountX + 1) + (x + 1); // Top-Right

                // === VÉRTICES DE LA CELDA (LÉEN ALTURA DEL HEIGHTMAP) ===
                Vector3 posBL = new Vector3(x * stepX, heightMap[hmBL], z * stepZ);
                Vector3 posBR = new Vector3((x + 1) * stepX, heightMap[hmBR], z * stepZ);
                Vector3 posTL = new Vector3(x * stepX, heightMap[hmTL], (z + 1) * stepZ);
                Vector3 posTR = new Vector3((x + 1) * stepX, heightMap[hmTR], (z + 1) * stepZ);

                vertices[vertIndex] = posBL;
                vertices[vertIndex + 1] = posBR;
                vertices[vertIndex + 2] = posTL;
                vertices[vertIndex + 3] = posTR;

                // === UVS: CADA CELDA DE (0,0) A (1,1) ===
                uvs[vertIndex] = new Vector2(0f, 0f);
                uvs[vertIndex + 1] = new Vector2(1f, 0f);
                uvs[vertIndex + 2] = new Vector2(0f, 1f);
                uvs[vertIndex + 3] = new Vector2(1f, 1f);

                // === TRIÁNGULOS ===
                int i = vertIndex;
                triangles[triIndex] = i;
                triangles[triIndex + 1] = i + 2;
                triangles[triIndex + 2] = i + 1;

                triangles[triIndex + 3] = i + 1;
                triangles[triIndex + 4] = i + 2;
                triangles[triIndex + 5] = i + 3;

                vertIndex += 4;
                triIndex += 6;
            }
        }

        // === APPLY TO MESH ===
        terrainMesh.Clear();
        terrainMesh.vertices = vertices;
        terrainMesh.uv = uvs;
        terrainMesh.triangles = triangles;

        terrainMesh.RecalculateNormals();
        terrainMesh.RecalculateBounds();

        lastAppliedWidth = width;
        lastAppliedLength = length;
    }

    // === TERRAIN DEFORMATION ===
    public void ModifyTerrain(Vector3 worldHitPoint, bool isElevating) {
        if (heightMap == null || terrainMesh == null) return;
        if (cellCountX <= 0 || cellCountZ <= 0) return;

        Vector3 localHit = transform.InverseTransformPoint(worldHitPoint);
        float stepX = (float)width / cellCountX;
        float stepZ = (float)length / cellCountZ;

        // === SPATIAL OPTIMIZATION: solo índices afectados por el brush ===
        int minX = Mathf.Max(0, Mathf.FloorToInt((localHit.x - brushRadius) / stepX));
        int maxX = Mathf.Min(cellCountX, Mathf.CeilToInt((localHit.x + brushRadius) / stepX));
        int minZ = Mathf.Max(0, Mathf.FloorToInt((localHit.z - brushRadius) / stepZ));
        int maxZ = Mathf.Min(cellCountZ, Mathf.CeilToInt((localHit.z + brushRadius) / stepZ));

        for (int z = minZ; z <= maxZ; z++) {
            for (int x = minX; x <= maxX; x++) {
                int hmIndex = z * (cellCountX + 1) + x;

                float dx = (x * stepX) - localHit.x;
                float dz = (z * stepZ) - localHit.z;
                float distance = Mathf.Sqrt(dx * dx + dz * dz);

                if (distance >= brushRadius) continue;

                // === FALLOFF: SmoothStep basado en distancia normalizada ===
                float distanceRatio = Mathf.Clamp01(distance / brushRadius);
                float falloff = Mathf.SmoothStep(1f, 0f, distanceRatio);

                float force = brushStrength * falloff * 0.05f;

                heightMap[hmIndex] += isElevating ? force : -force;
            }
        }

        GenerateGridMesh();
    }
}
