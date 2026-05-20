using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace FF.Terrain.Edit {
    public enum CSGOperation { Add, Subtract }
    public enum BrushShape { Sphere, Cube }
    // Extensión futura: Esfera, Cubo, Triángulo, Ruido Natural, etc.

    public static class SDFUtility {
        // Note: Evaluador central de funciones matemáticas de distancia. Devuelve < 0.0f si el punto está dentro de la brocha.
        public static float EvaluateBrush(float3 p, float radius, BrushShape shape) {
            switch (shape) {
                case BrushShape.Sphere:
                    return math.length(p) - radius;
                case BrushShape.Cube:
                    float3 d = math.abs(p) - radius;
                    return math.length(math.max(d, 0f)) + math.min(math.max(d.x, math.max(d.y, d.z)), 0f);
                default:
                    return math.length(p) - radius;
            }
        }
    }

    [BurstCompile(CompileSynchronously = true)]
    public struct CSGJob : IJobParallelFor {
        public NativeArray<VoxelData> voxels;
        public int3 chunkCoord;
        public int logicalSize;
        public int padding;
        public float3 brushPos;
        public float radius;
        public CSGOperation operation;
        public BrushShape shape;

        public void Execute(int index) {
            int physicalSize = logicalSize + (padding * 2);
            int x = index % physicalSize;
            int y = (index / physicalSize) % physicalSize;
            int z = index / (physicalSize * physicalSize);

            int3 worldPos = (chunkCoord * logicalSize) + new int3(x, y, z) - padding;
            float3 p = new float3(worldPos) - brushPos;

            float brushSDF = SDFUtility.EvaluateBrush(p, radius, shape);

            // Inversión: CSG requiere densidad positiva para sumar volumen.
            float densityDelta = -brushSDF;
            VoxelData v = voxels[index];

            if (operation == CSGOperation.Add) {
                v.density = math.max(v.density, densityDelta);
            }
            else {
                v.density = math.min(v.density, -densityDelta);
            }

            voxels[index] = v;
        }
    }

    [BurstCompile(CompileSynchronously = true)]
    public struct PaintJob : IJobParallelFor {
        public NativeArray<VoxelData> voxels;
        public int3 chunkCoord;
        public int logicalSize;
        public int padding;
        public float3 brushPos;
        public float radius;
        public BrushShape shape;
        public uint materialID;

        public void Execute(int index) {
            int physicalSize = logicalSize + (padding * 2);
            int x = index % physicalSize;
            int y = (index / physicalSize) % physicalSize;
            int z = index / (physicalSize * physicalSize);

            int3 worldPos = (chunkCoord * logicalSize) + new int3(x, y, z) - padding;
            float3 p = new float3(worldPos) - brushPos;

            float brushSDF = SDFUtility.EvaluateBrush(p, radius, shape);
            VoxelData v = voxels[index];

            // Solo pinta la metadata si intersecta el volumen y el voxel actual es material sólido.
            if (brushSDF <= 0f && v.density > 0f) {
                v.material = materialID;
                voxels[index] = v;
            }
        }
    }
}