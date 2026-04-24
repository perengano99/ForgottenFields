using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

// === GENERATE TERRAIN MESH JOB ===
[BurstCompile(CompileSynchronously = true)]
public struct GenerateTerrainMeshJob : IJobParallelFor
{
    // === INPUT ===
    [ReadOnly] public NativeArray<CellData> cells;
    public int chunkSize;

    // === OUTPUT ===
    [WriteOnly] public NativeList<Vector3>.ParallelWriter vertices;
    [WriteOnly] public NativeList<int>.ParallelWriter triangles;

    // === TRIANGLE LUT ===
    private static readonly int[] SolidTriangles =
    {
        0, 2, 1, 0, 3, 2,
        4, 5, 6, 4, 6, 7,
        0, 1, 5, 0, 5, 4,
        1, 2, 6, 1, 6, 5,
        2, 3, 7, 2, 7, 6,
        3, 0, 4, 3, 4, 7
    };

    private static readonly int[] SlopeNTriangles =
    {
        0, 2, 1,
        0, 3, 2,
        0, 1, 5,
        0, 5, 4,
        1, 2, 5,
        2, 3, 4,
        2, 4, 5,
        3, 0, 4
    };

    private static readonly int[] SlopeSTriangles =
    {
        0, 2, 1,
        0, 3, 2,
        0, 1, 5,
        0, 5, 4,
        1, 2, 5,
        2, 3, 4,
        2, 4, 5,
        3, 0, 4
    };

    private static readonly int[] SlopeETriangles =
    {
        0, 2, 1,
        0, 3, 2,
        0, 1, 5,
        0, 5, 4,
        1, 2, 5,
        2, 3, 4,
        2, 4, 5,
        3, 0, 4
    };

    private static readonly int[] SlopeWTriangles =
    {
        0, 2, 1,
        0, 3, 2,
        0, 1, 5,
        0, 5, 4,
        1, 2, 5,
        2, 3, 4,
        2, 4, 5,
        3, 0, 4
    };

    // === EXECUTE ===
    public void Execute(int index)
    {
        int x = index % chunkSize;
        int z = index / chunkSize;

        CellData cell = cells[index];
        if (cell.topologyID == (byte)BlockTopology.Air)
        {
            return;
        }

        Vector3[] localVertices = TopologyLUT.VerticesByTopology[cell.topologyID];
        int[] localTriangles = GetTriangles(cell.topologyID);

        float normalizedHeight = (float)cell.heightLevel / TerrainTopologyData.HEIGHT_STEPS;
        Vector3 chunkOffset = new Vector3(x, 0f, z);

        int baseVertex = index * 8;

        for (int i = 0; i < localVertices.Length; i++)
        {
            Vector3 v = localVertices[i];
            if (v.y > 0f)
            {
                v.y *= normalizedHeight;
            }

            vertices.AddNoResize(v + chunkOffset);
        }

        for (int i = 0; i < localTriangles.Length; i++)
        {
            triangles.AddNoResize(baseVertex + localTriangles[i]);
        }
    }

    // === HELPERS ===
    private static int[] GetTriangles(byte topologyID)
    {
        switch ((BlockTopology)topologyID)
        {
            case BlockTopology.Solid:
                return SolidTriangles;
            case BlockTopology.SlopeN:
                return SlopeNTriangles;
            case BlockTopology.SlopeS:
                return SlopeSTriangles;
            case BlockTopology.SlopeE:
                return SlopeETriangles;
            case BlockTopology.SlopeW:
                return SlopeWTriangles;
            default:
                return SolidTriangles;
        }
    }
}
