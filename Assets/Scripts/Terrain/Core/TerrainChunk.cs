using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

// === TERRAIN CHUNK ===
[RequireComponent(typeof(MeshFilter))]
public class TerrainChunk : MonoBehaviour
{
    // === FIELDS ===
    public int chunkSize = 16;
    private NativeArray<CellData> cells;
    private NativeArray<ComplexCell> palette;

    // === UNITY EVENTS ===
    private void Awake()
    {
        cells = new NativeArray<CellData>(chunkSize * chunkSize, Allocator.Persistent);
        palette = new NativeArray<ComplexCell>(2, Allocator.Persistent);

        palette[0] = new ComplexCell
        {
            block0 = new SubBlockData { materialID = 1, topologyID = (byte)BlockTopology.Solid, heightLevel = 12 },
            activeCount = 1
        };

        palette[1] = new ComplexCell
        {
            activeCount = 0
        };

        for (int z = 0; z < chunkSize; z++)
        {
            for (int x = 0; x < chunkSize; x++)
            {
                int index = z * chunkSize + x;
                ushort pIndex = (ushort)(Random.value > 0.35f ? 0 : 1);

                cells[index] = new CellData
                {
                    paletteIndex = pIndex
                };
            }
        }

        UpdateMesh();
    }

    private void Start()
    {
        UpdateMesh();
    }

    private void OnDestroy()
    {
        if (cells.IsCreated)
        {
            cells.Dispose();
        }

        if (palette.IsCreated)
        {
            palette.Dispose();
        }
    }

    // === MESH GENERATION ===
    public void UpdateMesh()
    {
        NativeList<Vector3> vertices = new NativeList<Vector3>(chunkSize * chunkSize * 8, Allocator.TempJob);
        NativeList<int> triangles = new NativeList<int>(chunkSize * chunkSize * 36, Allocator.TempJob);
        NativeList<Vector3> uvs = new NativeList<Vector3>(Allocator.TempJob);

        GenerateTerrainMeshJob job = new GenerateTerrainMeshJob
        {
            cells = cells,
            palette = palette,
            chunkSize = chunkSize,
            vertices = vertices,
            triangles = triangles,
            uvs = uvs
        };

        var handle = job.Schedule();
        handle.Complete();

        Mesh mesh = new Mesh();
        mesh.vertices = vertices.AsArray().ToArray();
        mesh.triangles = triangles.AsArray().ToArray();
        mesh.SetUVs(0, uvs.AsArray());
        mesh.RecalculateNormals();

        GetComponent<MeshFilter>().sharedMesh = mesh;

        vertices.Dispose();
        triangles.Dispose();
        uvs.Dispose();
    }
}
