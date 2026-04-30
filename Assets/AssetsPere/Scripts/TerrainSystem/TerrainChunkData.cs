using System;
using Unity.Collections;
using UnityEngine;

    public sealed class TerrainChunkData : IDisposable {
        private NativeArray<byte> densityField;
        private NativeArray<byte> materialField;

        public int ChunkSizeX { get; }
        public int ChunkSizeY { get; }
        public int ChunkSizeZ { get; }

        public TerrainChunkData(int chunkSizeX, int chunkSizeY, int chunkSizeZ, Allocator allocator) {
            ChunkSizeX = chunkSizeX;
            ChunkSizeY = chunkSizeY;
            ChunkSizeZ = chunkSizeZ;

            int totalSize = chunkSizeX * chunkSizeY * chunkSizeZ;
            densityField = new NativeArray<byte>(totalSize, allocator);
            materialField = new NativeArray<byte>(totalSize, allocator);
        }

        public void SetDensity(int x, int y, int z, byte density) {
            if (x < 0 || x >= ChunkSizeX || y < 0 || y >= ChunkSizeY || z < 0 || z >= ChunkSizeZ) {
                Debug.LogError($"SetDensity fuera de límites: ({x}, {y}, {z}) para chunk ({ChunkSizeX}, {ChunkSizeY}, {ChunkSizeZ}).");
                return;
            }

            int index = x + y * ChunkSizeX + z * ChunkSizeX * ChunkSizeY;
            densityField[index] = density;
        }

        public byte GetDensity(int x, int y, int z) {
            if (x < 0 || x >= ChunkSizeX || y < 0 || y >= ChunkSizeY || z < 0 || z >= ChunkSizeZ) {
                Debug.LogError($"GetDensity fuera de límites: ({x}, {y}, {z}) para chunk ({ChunkSizeX}, {ChunkSizeY}, {ChunkSizeZ}).");
                return 0;
            }

            int index = x + y * ChunkSizeX + z * ChunkSizeX * ChunkSizeY;
            return densityField[index];
        }

        public void SetMaterial(int x, int y, int z, byte material) {
            if (x < 0 || x >= ChunkSizeX || y < 0 || y >= ChunkSizeY || z < 0 || z >= ChunkSizeZ) {
                Debug.LogError($"SetMaterial fuera de límites: ({x}, {y}, {z}) para chunk ({ChunkSizeX}, {ChunkSizeY}, {ChunkSizeZ}).");
                return;
            }

            int index = x + y * ChunkSizeX + z * ChunkSizeX * ChunkSizeY;
            materialField[index] = material;
        }

        public byte GetMaterial(int x, int y, int z) {
            if (x < 0 || x >= ChunkSizeX || y < 0 || y >= ChunkSizeY || z < 0 || z >= ChunkSizeZ) {
                Debug.LogError($"GetMaterial fuera de límites: ({x}, {y}, {z}) para chunk ({ChunkSizeX}, {ChunkSizeY}, {ChunkSizeZ}).");
                return 0;
            }

            int index = x + y * ChunkSizeX + z * ChunkSizeX * ChunkSizeY;
            return materialField[index];
        }

        public void Dispose() {
            if (densityField.IsCreated) densityField.Dispose();
            if (materialField.IsCreated) materialField.Dispose();
        }
    }
