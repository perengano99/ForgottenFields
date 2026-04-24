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
    [ReadOnly] public NativeArray<ComplexCell> palette;
    public int chunkSize;

    public NativeList<Vector3> vertices;
    public NativeList<int> triangles;
    public NativeList<Vector3> uvs;

    public void Execute()
    {
        for (int i = 0; i < cells.Length; i++)
        {
            ProcessCell(i);
        }
    }

    private void ProcessCell(int index)
    {
        ushort pIndex = cells[index].paletteIndex;
        ComplexCell cData = palette[pIndex];

        int x = index % chunkSize;
        int z = index / chunkSize;
        Vector3 offset = new Vector3(x, 0, z);

        int blockCount = cData.activeCount;
        if (blockCount > 4) blockCount = 4;

        for (int i = 0; i < blockCount; i++)
        {
            SubBlockData subBlock;
            switch (i)
            {
                case 0: subBlock = cData.block0; break;
                case 1: subBlock = cData.block1; break;
                case 2: subBlock = cData.block2; break;
                default: subBlock = cData.block3; break;
            }

            if (subBlock.topologyID == (byte)BlockTopology.Air) continue;

            float h = (float)subBlock.heightLevel / 12f;
            int baseV = vertices.Length;

            // Note: Lógica unmanaged Burst-Compatible (Sin arreglos dinámicos)
            switch ((BlockTopology)subBlock.topologyID)
            {
                case BlockTopology.Solid:
                    AddVert(new Vector3(0, 0, 0), offset, h, false, subBlock.materialID);
                    AddVert(new Vector3(1, 0, 0), offset, h, false, subBlock.materialID);
                    AddVert(new Vector3(1, 0, 1), offset, h, false, subBlock.materialID);
                    AddVert(new Vector3(0, 0, 1), offset, h, false, subBlock.materialID);
                    AddVert(new Vector3(0, 1, 0), offset, h, true, subBlock.materialID);
                    AddVert(new Vector3(1, 1, 0), offset, h, true, subBlock.materialID);
                    AddVert(new Vector3(1, 1, 1), offset, h, true, subBlock.materialID);
                    AddVert(new Vector3(0, 1, 1), offset, h, true, subBlock.materialID);

                    AddTri(baseV, new int3(0, 1, 5)); AddTri(baseV, new int3(0, 5, 4));
                    AddTri(baseV, new int3(3, 7, 6)); AddTri(baseV, new int3(3, 6, 2));
                    AddTri(baseV, new int3(0, 3, 2)); AddTri(baseV, new int3(0, 2, 1));
                    AddTri(baseV, new int3(4, 6, 7)); AddTri(baseV, new int3(4, 5, 6));
                    AddTri(baseV, new int3(0, 4, 7)); AddTri(baseV, new int3(0, 7, 3));
                    AddTri(baseV, new int3(1, 2, 6)); AddTri(baseV, new int3(1, 6, 5));
                    break;

                case BlockTopology.SlopeN:
                    AddVert(new Vector3(0, 0, 0), offset, h, false, subBlock.materialID);
                    AddVert(new Vector3(1, 0, 0), offset, h, false, subBlock.materialID);
                    AddVert(new Vector3(1, 0, 1), offset, h, false, subBlock.materialID);
                    AddVert(new Vector3(0, 0, 1), offset, h, false, subBlock.materialID);
                    AddVert(new Vector3(0, 1, 1), offset, h, true, subBlock.materialID);
                    AddVert(new Vector3(1, 1, 1), offset, h, true, subBlock.materialID);

                    AddTri(baseV, new int3(1, 2, 0)); AddTri(baseV, new int3(2, 3, 0));
                    AddTri(baseV, new int3(5, 1, 0)); AddTri(baseV, new int3(4, 5, 0));
                    AddTri(baseV, new int3(5, 2, 1)); AddTri(baseV, new int3(4, 3, 2));
                    AddTri(baseV, new int3(5, 4, 2)); AddTri(baseV, new int3(4, 0, 3));
                    break;

                case BlockTopology.SlopeS:
                    AddVert(new Vector3(0, 0, 1), offset, h, false, subBlock.materialID);
                    AddVert(new Vector3(1, 0, 1), offset, h, false, subBlock.materialID);
                    AddVert(new Vector3(1, 0, 0), offset, h, false, subBlock.materialID);
                    AddVert(new Vector3(0, 0, 0), offset, h, false, subBlock.materialID);
                    AddVert(new Vector3(0, 1, 0), offset, h, true, subBlock.materialID);
                    AddVert(new Vector3(1, 1, 0), offset, h, true, subBlock.materialID);

                    AddTri(baseV, new int3(1, 2, 0)); AddTri(baseV, new int3(2, 3, 0));
                    AddTri(baseV, new int3(5, 1, 0)); AddTri(baseV, new int3(4, 5, 0));
                    AddTri(baseV, new int3(5, 2, 1)); AddTri(baseV, new int3(4, 3, 2));
                    AddTri(baseV, new int3(5, 4, 2)); AddTri(baseV, new int3(4, 0, 3));
                    break;

                case BlockTopology.SlopeE:
                    AddVert(new Vector3(0, 0, 0), offset, h, false, subBlock.materialID);
                    AddVert(new Vector3(0, 0, 1), offset, h, false, subBlock.materialID);
                    AddVert(new Vector3(1, 0, 1), offset, h, false, subBlock.materialID);
                    AddVert(new Vector3(1, 0, 0), offset, h, false, subBlock.materialID);
                    AddVert(new Vector3(1, 1, 0), offset, h, true, subBlock.materialID);
                    AddVert(new Vector3(1, 1, 1), offset, h, true, subBlock.materialID);

                    AddTri(baseV, new int3(1, 2, 0)); AddTri(baseV, new int3(2, 3, 0));
                    AddTri(baseV, new int3(5, 1, 0)); AddTri(baseV, new int3(4, 5, 0));
                    AddTri(baseV, new int3(5, 2, 1)); AddTri(baseV, new int3(4, 3, 2));
                    AddTri(baseV, new int3(5, 4, 2)); AddTri(baseV, new int3(4, 0, 3));
                    break;

                case BlockTopology.SlopeW:
                    AddVert(new Vector3(1, 0, 0), offset, h, false, subBlock.materialID);
                    AddVert(new Vector3(1, 0, 1), offset, h, false, subBlock.materialID);
                    AddVert(new Vector3(0, 0, 1), offset, h, false, subBlock.materialID);
                    AddVert(new Vector3(0, 0, 0), offset, h, false, subBlock.materialID);
                    AddVert(new Vector3(0, 1, 0), offset, h, true, subBlock.materialID);
                    AddVert(new Vector3(0, 1, 1), offset, h, true, subBlock.materialID);

                    AddTri(baseV, new int3(1, 2, 0)); AddTri(baseV, new int3(2, 3, 0));
                    AddTri(baseV, new int3(5, 1, 0)); AddTri(baseV, new int3(4, 5, 0));
                    AddTri(baseV, new int3(5, 2, 1)); AddTri(baseV, new int3(4, 3, 2));
                    AddTri(baseV, new int3(5, 4, 2)); AddTri(baseV, new int3(4, 0, 3));
                    break;
            }
        }
    }

    private void AddVert(Vector3 localPos, Vector3 chunkOffset, float heightMod, bool applyHeight, byte matID)
    {
        if (applyHeight)
        {
            localPos.y *= heightMod;
        }
        vertices.Add(localPos + chunkOffset);
        uvs.Add(new Vector3(0f, 0f, matID));
    }

    private void AddTri(int baseIndex, int3 indices)
    {
        triangles.Add(baseIndex + indices.z);
        triangles.Add(baseIndex + indices.y);
        triangles.Add(baseIndex + indices.x);
    }
}