// === GENERATE TERRAIN MESH JOB ===
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

// Note: Convertido a IJob. El paralelismo estricto requiere pre-calcular offsets si se omiten celdas (Aire).
[BurstCompile(CompileSynchronously = true)]
public struct GenerateTerrainMeshJob : IJob
{
    [ReadOnly] public NativeArray<CellData> cells;
    public int chunkSize;

    public NativeList<Vector3> vertices;
    public NativeList<int> triangles;

    public void Execute()
    {
        for (int i = 0; i < cells.Length; i++)
        {
            ProcessCell(i);
        }
    }

    private void ProcessCell(int index)
    {
        CellData cell = cells[index];
        if (cell.topologyID == (byte)BlockTopology.Air) return;

        int x = index % chunkSize;
        int z = index / chunkSize;
        Vector3 offset = new Vector3(x, 0, z);

        // Asume 12 divisiones como definido en TerrainTopologyData
        float h = (float)cell.heightLevel / 12f;
        int baseV = vertices.Length;

        // Note: Lógica unmanaged Burst-Compatible (Sin arreglos dinámicos)
        switch ((BlockTopology)cell.topologyID)
        {
            case BlockTopology.Solid:
                AddVert(new Vector3(0, 0, 0), offset, h, false);
                AddVert(new Vector3(1, 0, 0), offset, h, false);
                AddVert(new Vector3(1, 0, 1), offset, h, false);
                AddVert(new Vector3(0, 0, 1), offset, h, false);
                AddVert(new Vector3(0, 1, 0), offset, h, true);
                AddVert(new Vector3(1, 1, 0), offset, h, true);
                AddVert(new Vector3(1, 1, 1), offset, h, true);
                AddVert(new Vector3(0, 1, 1), offset, h, true);

                AddTri(baseV, new int3(0, 2, 1)); AddTri(baseV, new int3(0, 3, 2));
                AddTri(baseV, new int3(4, 5, 6)); AddTri(baseV, new int3(4, 6, 7));
                AddTri(baseV, new int3(0, 1, 5)); AddTri(baseV, new int3(0, 5, 4));
                AddTri(baseV, new int3(1, 2, 6)); AddTri(baseV, new int3(1, 6, 5));
                AddTri(baseV, new int3(2, 3, 7)); AddTri(baseV, new int3(2, 7, 6));
                AddTri(baseV, new int3(3, 0, 4)); AddTri(baseV, new int3(3, 4, 7));
                break;

            case BlockTopology.SlopeN:
                AddVert(new Vector3(0, 0, 0), offset, h, false);
                AddVert(new Vector3(1, 0, 0), offset, h, false);
                AddVert(new Vector3(1, 0, 1), offset, h, false);
                AddVert(new Vector3(0, 0, 1), offset, h, false);
                AddVert(new Vector3(0, 1, 1), offset, h, true);
                AddVert(new Vector3(1, 1, 1), offset, h, true);

                AddTri(baseV, new int3(0, 2, 1)); AddTri(baseV, new int3(0, 3, 2));
                AddTri(baseV, new int3(0, 1, 5)); AddTri(baseV, new int3(0, 5, 4));
                AddTri(baseV, new int3(1, 2, 5)); AddTri(baseV, new int3(2, 3, 4));
                AddTri(baseV, new int3(2, 4, 5)); AddTri(baseV, new int3(3, 0, 4));
                break;

            case BlockTopology.SlopeS:
                AddVert(new Vector3(0, 0, 1), offset, h, false);
                AddVert(new Vector3(1, 0, 1), offset, h, false);
                AddVert(new Vector3(1, 0, 0), offset, h, false);
                AddVert(new Vector3(0, 0, 0), offset, h, false);
                AddVert(new Vector3(0, 1, 0), offset, h, true);
                AddVert(new Vector3(1, 1, 0), offset, h, true);

                AddTri(baseV, new int3(0, 2, 1)); AddTri(baseV, new int3(0, 3, 2));
                AddTri(baseV, new int3(0, 1, 5)); AddTri(baseV, new int3(0, 5, 4));
                AddTri(baseV, new int3(1, 2, 5)); AddTri(baseV, new int3(2, 3, 4));
                AddTri(baseV, new int3(2, 4, 5)); AddTri(baseV, new int3(3, 0, 4));
                break;

            case BlockTopology.SlopeE:
                AddVert(new Vector3(0, 0, 0), offset, h, false);
                AddVert(new Vector3(0, 0, 1), offset, h, false);
                AddVert(new Vector3(1, 0, 1), offset, h, false);
                AddVert(new Vector3(1, 0, 0), offset, h, false);
                AddVert(new Vector3(1, 1, 0), offset, h, true);
                AddVert(new Vector3(1, 1, 1), offset, h, true);

                AddTri(baseV, new int3(0, 2, 1)); AddTri(baseV, new int3(0, 3, 2));
                AddTri(baseV, new int3(0, 1, 5)); AddTri(baseV, new int3(0, 5, 4));
                AddTri(baseV, new int3(1, 2, 5)); AddTri(baseV, new int3(2, 3, 4));
                AddTri(baseV, new int3(2, 4, 5)); AddTri(baseV, new int3(3, 0, 4));
                break;

            case BlockTopology.SlopeW:
                AddVert(new Vector3(1, 0, 0), offset, h, false);
                AddVert(new Vector3(1, 0, 1), offset, h, false);
                AddVert(new Vector3(0, 0, 1), offset, h, false);
                AddVert(new Vector3(0, 0, 0), offset, h, false);
                AddVert(new Vector3(0, 1, 0), offset, h, true);
                AddVert(new Vector3(0, 1, 1), offset, h, true);

                AddTri(baseV, new int3(0, 2, 1)); AddTri(baseV, new int3(0, 3, 2));
                AddTri(baseV, new int3(0, 1, 5)); AddTri(baseV, new int3(0, 5, 4));
                AddTri(baseV, new int3(1, 2, 5)); AddTri(baseV, new int3(2, 3, 4));
                AddTri(baseV, new int3(2, 4, 5)); AddTri(baseV, new int3(3, 0, 4));
                break;
        }
    }

    private void AddVert(Vector3 localPos, Vector3 chunkOffset, float heightMod, bool applyHeight)
    {
        if (applyHeight)
        {
            localPos.y *= heightMod;
        }
        vertices.Add(localPos + chunkOffset);
    }

    private void AddTri(int baseIndex, int3 indices)
    {
        triangles.Add(baseIndex + indices.x);
        triangles.Add(baseIndex + indices.y);
        triangles.Add(baseIndex + indices.z);
    }
}