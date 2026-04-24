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

    // === UNITY EVENTS ===
    private void Awake()
    {
        cells = new NativeArray<CellData>(chunkSize * chunkSize, Allocator.Persistent);

        for (int z = 0; z < chunkSize; z++)
        {
            for (int x = 0; x < chunkSize; x++)
            {
                int index = z * chunkSize + x;
                bool isSolid = Random.value > 0.35f;

                cells[index] = new CellData
                {
                    topologyID = isSolid ? (byte)BlockTopology.Solid : (byte)BlockTopology.Air,
                    heightLevel = isSolid ? (byte)12 : (byte)0
                };
            }
        }

        UpdateMesh();
    }

    private void OnDestroy()
    {
        if (cells.IsCreated)
        {
            cells.Dispose();
        }
    }

    // === MESH GENERATION ===
    public void UpdateMesh()
    {
        NativeList<Vector3> vertices = new NativeList<Vector3>(chunkSize * chunkSize * 8, Allocator.TempJob);
        NativeList<int> triangles = new NativeList<int>(chunkSize * chunkSize * 36, Allocator.TempJob);

        GenerateTerrainMeshJob job = new GenerateTerrainMeshJob
        {
            cells = cells,
            chunkSize = chunkSize,
            vertices = vertices.AsParallelWriter(),
            triangles = triangles.AsParallelWriter()
        };

        var handle = job.Schedule(cells.Length, 64);
        handle.Complete();

        Mesh mesh = new Mesh();
        mesh.vertices = vertices.AsArray().ToArray();
        mesh.triangles = triangles.AsArray().ToArray();
        mesh.RecalculateNormals();

        GetComponent<MeshFilter>().sharedMesh = mesh;

        vertices.Dispose();
        triangles.Dispose();
    }
}
