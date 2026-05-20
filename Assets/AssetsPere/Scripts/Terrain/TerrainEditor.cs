using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace FF.Terrain.Edit {
    public static class TerrainEditor {

        // Note: Retorna referencias a los Chunks cuya área de influencia colisiona con el AABB de la brocha.
        private static List<DCTerrainChunk> GetAffectedChunks(TerrainMap map, Vector3 pos, float radius) {
            List<DCTerrainChunk> affected = new List<DCTerrainChunk>();
            int3 minChunk = new int3(Mathf.FloorToInt((pos.x - radius) / DCTerrainChunk.logicalSize)) - 1;
            int3 maxChunk = new int3(Mathf.FloorToInt((pos.x + radius) / DCTerrainChunk.logicalSize)) + 1;

            for (int x = minChunk.x; x <= maxChunk.x; x++) {
                for (int y = minChunk.y; y <= maxChunk.y; y++) {
                    for (int z = minChunk.z; z <= maxChunk.z; z++) {
                        if (map.chunks.TryGetValue(new int3(x, y, z), out DCTerrainChunk chunk)) {
                            affected.Add(chunk);
                        }
                    }
                }
            }
            return affected;
        }

        public static void ModifySDF(TerrainMap map, Vector3 worldPos, float radius, CSGOperation op, BrushShape shape) {
            var chunks = GetAffectedChunks(map, worldPos, radius);
            if (chunks.Count == 0) return;

            NativeArray<JobHandle> handles = new NativeArray<JobHandle>(chunks.Count, Allocator.Temp);
            for (int i = 0; i < chunks.Count; i++) {
                CSGJob job = new CSGJob {
                    voxels = chunks[i].voxels,
                    chunkCoord = chunks[i].chunkCoordinate,
                    logicalSize = DCTerrainChunk.logicalSize,
                    padding = DCTerrainChunk.padding,
                    brushPos = worldPos,
                    radius = radius,
                    operation = op,
                    shape = shape
                };
                handles[i] = job.Schedule(chunks[i].voxels.Length, 64);
            }

            JobHandle.CompleteAll(handles);
            handles.Dispose();

            foreach (var chunk in chunks) chunk.UpdateMesh();
        }

        public static void PaintMaterial(TerrainMap map, Vector3 worldPos, float radius, BrushShape shape, uint materialID) {
            var chunks = GetAffectedChunks(map, worldPos, radius);
            if (chunks.Count == 0) return;

            NativeArray<JobHandle> handles = new NativeArray<JobHandle>(chunks.Count, Allocator.Temp);
            for (int i = 0; i < chunks.Count; i++) {
                PaintJob job = new PaintJob {
                    voxels = chunks[i].voxels,
                    chunkCoord = chunks[i].chunkCoordinate,
                    logicalSize = DCTerrainChunk.logicalSize,
                    padding = DCTerrainChunk.padding,
                    brushPos = worldPos,
                    radius = radius,
                    shape = shape,
                    materialID = materialID
                };
                handles[i] = job.Schedule(chunks[i].voxels.Length, 64);
            }

            JobHandle.CompleteAll(handles);
            handles.Dispose();

            foreach (var chunk in chunks) chunk.UpdateMesh();
        }
    }
}