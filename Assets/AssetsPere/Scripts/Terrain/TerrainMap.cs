using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace FF.Terrain {
    public class TerrainMap : MonoBehaviour {
        public TerrainMaterialManager materialManager;
        public int3 gridSize = new int3(3, 1, 3); // Temporal, idealmente sera dinamico.

        void Awake() {
            GameObject terrainMesh = new GameObject("TerrainMesh");
            terrainMesh.transform.SetParent(transform, false);

            for (int z = 0; z < gridSize.z; z++)
                for (int y = 0; y < gridSize.y; y++)
                    for (int x = 0; x < gridSize.x; x++) {
                        int3 coordinate = new int3(x, y, z);

                        GameObject chunkObject = new GameObject($"Chunk_{x}_{y}_{z}");
                        chunkObject.transform.SetParent(terrainMesh.transform, false);
                        chunkObject.transform.localPosition = new Vector3(coordinate.x, coordinate.y, coordinate.z) * DCTerrainChunk.logicalSize;

                        DCTerrainChunk chunk = chunkObject.AddComponent<DCTerrainChunk>();
                        chunk.Initialize(materialManager.targetMaterial, coordinate);

                        BasicTerrainSDFJob sdfJob = new BasicTerrainSDFJob {
                            voxels = chunk.voxels,
                            chunkCoord = coordinate,
                            logicalSize = DCTerrainChunk.logicalSize,
                            padding = DCTerrainChunk.padding
                        };

                        JobHandle handle = sdfJob.Schedule(chunk.voxels.Length, 64);
                        handle.Complete();
                        chunk.UpdateMesh();
                    }
        }
    }
}
