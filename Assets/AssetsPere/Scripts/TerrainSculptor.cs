using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public enum BrushShape { Sphere, Cube, Cylinder, Cone, Noise }

public enum BrushMode {
    Add,
    Subtract,
    Smooth,
    Flatten,
    Paint
}

public static class TerrainSculptor {
    // === ENTRADA PRINCIPAL ===
    public static void Apply(
        NativeArray<float> densities,
        NativeArray<byte> metadata,
        int3 gridSize,
        float voxelSize,
        Vector3 localHitPoint,
        float radius,
        float strength,
        BrushShape shape,
        BrushType type,
        byte materialID,
        bool isVertical = false) {

        float3 hitPoint = new float3(localHitPoint.x, localHitPoint.y, localHitPoint.z);

        switch (shape) {
            case BrushShape.Sphere:
                new SphereJob {
                    densities = densities,
                    metadata = metadata,
                    gridSize = gridSize,
                    voxelSize = voxelSize,
                    hitPoint = hitPoint,
                    radius = radius,
                    strength = strength,
                    brushType = type,
                    materialID = materialID
                }.Schedule(densities.Length, 64).Complete();
                break;

            case BrushShape.Cube:
                new CubeJob {
                    densities = densities,
                    metadata = metadata,
                    gridSize = gridSize,
                    voxelSize = voxelSize,
                    hitPoint = hitPoint,
                    radius = radius,
                    strength = strength,
                    brushType = type,
                    materialID = materialID,
                    isVertical = isVertical
                }.Schedule(densities.Length, 64).Complete();
                break;

            case BrushShape.Cylinder:
                new CylinderJob {
                    densities = densities,
                    metadata = metadata,
                    gridSize = gridSize,
                    voxelSize = voxelSize,
                    hitPoint = hitPoint,
                    radius = radius,
                    strength = strength,
                    brushType = type,
                    materialID = materialID,
                    isVertical = isVertical
                }.Schedule(densities.Length, 64).Complete();
                break;

            case BrushShape.Cone:
                new ConeJob {
                    densities = densities,
                    metadata = metadata,
                    gridSize = gridSize,
                    voxelSize = voxelSize,
                    hitPoint = hitPoint,
                    radius = radius,
                    strength = strength,
                    brushType = type,
                    materialID = materialID
                }.Schedule(densities.Length, 64).Complete();
                break;

            case BrushShape.Noise:
                new NoiseJob {
                    densities = densities,
                    metadata = metadata,
                    gridSize = gridSize,
                    voxelSize = voxelSize,
                    hitPoint = hitPoint,
                    radius = radius,
                    strength = strength,
                    brushType = type,
                    materialID = materialID
                }.Schedule(densities.Length, 64).Complete();
                break;
        }
    }

    // === UTILIDAD COMPARTIDA ===
    private static void DecomposeIndex(int index, int3 gridSize, out int x, out int y, out int z) {
        int pointsX = gridSize.x + 1;
        int pointsY = gridSize.y + 1;
        int planeSize = pointsX * pointsY;
        z = index / planeSize;
        int rem = index - z * planeSize;
        y = rem / pointsX;
        x = rem - y * pointsX;
    }

    private static void ApplyBrush(NativeArray<float> densities, NativeArray<byte> metadata, int index, float falloff, float strength, BrushType brushType, byte materialID, float3 nodePos, float3 hitPoint, float voxelSize) {
        switch (brushType) {
            case BrushType.SphereAdd:
                densities[index] -= strength * falloff;
                if (densities[index] < 0f) metadata[index] = materialID;
                break;
            case BrushType.SphereSubtract:
                densities[index] += strength * falloff;
                break;
            case BrushType.Flatten:
                float dy = nodePos.y - hitPoint.y;
                densities[index] = math.lerp(densities[index], dy, strength * falloff);
                if (densities[index] < 0f) metadata[index] = materialID;
                break;
        }
    }

    // === SPHERE JOB ===
    [BurstCompile]
    private struct SphereJob : IJobParallelFor {
        public NativeArray<float> densities;
        public NativeArray<byte> metadata;
        public int3 gridSize;
        public float voxelSize;
        public float3 hitPoint;
        public float radius;
        public float strength;
        public BrushType brushType;
        public byte materialID;

        public void Execute(int index) {
            int pointsX = gridSize.x + 1;
            int pointsY = gridSize.y + 1;
            int planeSize = pointsX * pointsY;
            int z = index / planeSize;
            int rem = index - z * planeSize;
            int y = rem / pointsX;
            int x = rem - y * pointsX;
            if (x > gridSize.x || y > gridSize.y || z > gridSize.z) return;

            float3 nodePos = new float3(x, y, z) * voxelSize;
            float dist = math.distance(nodePos, hitPoint);
            if (dist > radius) return;

            float falloff = math.smoothstep(radius, 0f, dist);
            ApplyBrush(densities, metadata, index, falloff, strength, brushType, materialID, nodePos, hitPoint, voxelSize);
        }
    }

    // === CUBE JOB ===
    [BurstCompile]
    private struct CubeJob : IJobParallelFor {
        public NativeArray<float> densities;
        public NativeArray<byte> metadata;
        public int3 gridSize;
        public float voxelSize;
        public float3 hitPoint;
        public float radius;
        public float strength;
        public BrushType brushType;
        public byte materialID;
        public bool isVertical;

        public void Execute(int index) {
            int pointsX = gridSize.x + 1;
            int pointsY = gridSize.y + 1;
            int planeSize = pointsX * pointsY;
            int z = index / planeSize;
            int rem = index - z * planeSize;
            int y = rem / pointsX;
            int x = rem - y * pointsX;
            if (x > gridSize.x || y > gridSize.y || z > gridSize.z) return;

            float3 nodePos = new float3(x, y, z) * voxelSize;
            float3 delta = nodePos - hitPoint;

            bool inX = math.abs(delta.x) <= radius;
            bool inZ = math.abs(delta.z) <= radius;
            bool inY = isVertical
                ? (delta.y >= 0f && delta.y <= radius * 2f)
                : (math.abs(delta.y) <= radius);

            if (!inX || !inY || !inZ) return;

            ApplyBrush(densities, metadata, index, 1f, strength, brushType, materialID, nodePos, hitPoint, voxelSize);
        }
    }

    // === CYLINDER JOB ===
    [BurstCompile]
    private struct CylinderJob : IJobParallelFor {
        public NativeArray<float> densities;
        public NativeArray<byte> metadata;
        public int3 gridSize;
        public float voxelSize;
        public float3 hitPoint;
        public float radius;
        public float strength;
        public BrushType brushType;
        public byte materialID;
        public bool isVertical;

        public void Execute(int index) {
            int pointsX = gridSize.x + 1;
            int pointsY = gridSize.y + 1;
            int planeSize = pointsX * pointsY;
            int z = index / planeSize;
            int rem = index - z * planeSize;
            int y = rem / pointsX;
            int x = rem - y * pointsX;
            if (x > gridSize.x || y > gridSize.y || z > gridSize.z) return;

            float3 nodePos = new float3(x, y, z) * voxelSize;
            float2 xzDelta = new float2(nodePos.x - hitPoint.x, nodePos.z - hitPoint.z);
            float xzDist = math.length(xzDelta);
            if (xzDist > radius) return;

            float yDelta = nodePos.y - hitPoint.y;
            bool inHeight = isVertical
                ? (yDelta >= 0f && yDelta <= radius * 2f)
                : (math.abs(yDelta) <= radius);
            if (!inHeight) return;

            float falloff = math.smoothstep(radius, 0f, xzDist);
            ApplyBrush(densities, metadata, index, falloff, strength, brushType, materialID, nodePos, hitPoint, voxelSize);
        }
    }

    // === CONE JOB ===
    [BurstCompile]
    private struct ConeJob : IJobParallelFor {
        public NativeArray<float> densities;
        public NativeArray<byte> metadata;
        public int3 gridSize;
        public float voxelSize;
        public float3 hitPoint;
        public float radius;
        public float strength;
        public BrushType brushType;
        public byte materialID;

        public void Execute(int index) {
            int pointsX = gridSize.x + 1;
            int pointsY = gridSize.y + 1;
            int planeSize = pointsX * pointsY;
            int z = index / planeSize;
            int rem = index - z * planeSize;
            int y = rem / pointsX;
            int x = rem - y * pointsX;
            if (x > gridSize.x || y > gridSize.y || z > gridSize.z) return;

            float3 nodePos = new float3(x, y, z) * voxelSize;
            float yDelta = nodePos.y - hitPoint.y;
            if (yDelta < 0f || yDelta > radius) return;

            float heightT = 1f - (yDelta / radius);
            float effectiveRadius = radius * heightT;

            float2 xzDelta = new float2(nodePos.x - hitPoint.x, nodePos.z - hitPoint.z);
            float xzDist = math.length(xzDelta);
            if (xzDist > effectiveRadius) return;

            float falloff = math.smoothstep(effectiveRadius, 0f, xzDist) * heightT;
            ApplyBrush(densities, metadata, index, falloff, strength, brushType, materialID, nodePos, hitPoint, voxelSize);
        }
    }

    // === NOISE JOB ===
    [BurstCompile]
    private struct NoiseJob : IJobParallelFor {
        public NativeArray<float> densities;
        public NativeArray<byte> metadata;
        public int3 gridSize;
        public float voxelSize;
        public float3 hitPoint;
        public float radius;
        public float strength;
        public BrushType brushType;
        public byte materialID;

        public void Execute(int index) {
            int pointsX = gridSize.x + 1;
            int pointsY = gridSize.y + 1;
            int planeSize = pointsX * pointsY;
            int z = index / planeSize;
            int rem = index - z * planeSize;
            int y = rem / pointsX;
            int x = rem - y * pointsX;
            if (x > gridSize.x || y > gridSize.y || z > gridSize.z) return;

            float3 nodePos = new float3(x, y, z) * voxelSize;
            float dist = math.distance(nodePos, hitPoint);
            if (dist > radius) return;

            float radialFalloff = math.smoothstep(radius, 0f, dist);
            float noiseVal = math.abs(Unity.Mathematics.noise.cnoise(nodePos * 0.3f));
            float falloff = radialFalloff * noiseVal;

            ApplyBrush(densities, metadata, index, falloff, strength, brushType, materialID, nodePos, hitPoint, voxelSize);
        }
    }
}
