using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public enum BrushShape { Sphere, Box, VerticalPillar, NoiseFeature }

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
        Vector3 globalHitPoint,
        Vector3 chunkWorldPosition,
        float radius,
        float strength,
        BrushShape shape,
        BrushType type,
        byte hitMaterial,
        bool isVertical = false) {

        if (!densities.IsCreated || !metadata.IsCreated || radius <= 0f || voxelSize <= 0f) return;

        NativeArray<float> sourceDensities;
        JobHandle handle = ScheduleSculptJob(
            densities,
            metadata,
            gridSize,
            voxelSize,
            globalHitPoint,
            chunkWorldPosition,
            radius,
            strength,
            shape,
            type,
            hitMaterial,
            isVertical,
            out sourceDensities
        );

        handle.Complete();
        if (sourceDensities.IsCreated) sourceDensities.Dispose();
    }

    public static JobHandle ScheduleSculptJob(
        NativeArray<float> densities,
        NativeArray<byte> metadata,
        int3 gridSize,
        float voxelSize,
        Vector3 globalHitPoint,
        Vector3 chunkWorldPosition,
        float radius,
        float strength,
        BrushShape shape,
        BrushType type,
        byte hitMaterial,
        bool isVertical,
        out NativeArray<float> sourceDensities,
        JobHandle dependsOn = default) {

        sourceDensities = default;
        if (!densities.IsCreated || !metadata.IsCreated || radius <= 0f || voxelSize <= 0f) return dependsOn;

        float dt = Time.deltaTime > 0f ? Time.deltaTime : (1f / 60f);
        sourceDensities = new NativeArray<float>(densities, Allocator.TempJob);

        TerrainSculptorJob job = new TerrainSculptorJob {
            Densities = densities,
            Metadata = metadata,
            sourceDensities = sourceDensities,
            gridSize = gridSize,
            voxelSize = voxelSize,
            globalHitPoint = new float3(globalHitPoint.x, globalHitPoint.y, globalHitPoint.z),
            chunkWorldPosition = new float3(chunkWorldPosition.x, chunkWorldPosition.y, chunkWorldPosition.z),
            radius = radius,
            strength = strength,
            fixedStepAmount = math.min(0.05f, math.max(0.0001f, dt)),
            brushShape = shape,
            brushType = type,
            isVertical = isVertical,
            isoLevel = 0f,
            hitMaterial = hitMaterial
        };

        return job.Schedule(dependsOn);
    }

    // === PINTURA DE MATERIALES ===
    public static JobHandle SchedulePaintJob(
        NativeArray<byte> metadata,
        NativeArray<float> densities,
        int3 gridSize,
        float voxelSize,
        Vector3 globalHitPoint,
        Vector3 chunkWorldPosition,
        float radius,
        byte targetMaterialID,
        JobHandle dependsOn = default) {

        if (!metadata.IsCreated || !densities.IsCreated || radius <= 0f || voxelSize <= 0f) return dependsOn;

        PaintTerrainJob job = new PaintTerrainJob {
            Metadata = metadata,
            Densities = densities,
            gridSize = gridSize,
            voxelSize = voxelSize,
            globalHitPoint = globalHitPoint,
            chunkWorldPosition = chunkWorldPosition,
            radius = radius,
            targetMaterialID = targetMaterialID
        };

        return job.Schedule(metadata.Length, 64, dependsOn);
    }

    [BurstCompile]
    public struct PaintTerrainJob : IJobParallelFor {
        public NativeArray<byte> Metadata;
        [ReadOnly] public NativeArray<float> Densities;
        public int3 gridSize;
        public float voxelSize;
        public Vector3 globalHitPoint;
        public Vector3 chunkWorldPosition;
        public float radius;
        public byte targetMaterialID;

        public void Execute(int index) {
            int pointsX = gridSize.x + 1;
            int pointsY = gridSize.y + 1;
            int planeSize = pointsX * pointsY;

            int z = index / planeSize;
            int rem = index - z * planeSize;
            int y = rem / pointsX;
            int x = rem - y * pointsX;

            if (x > gridSize.x || y > gridSize.y || z > gridSize.z) return;

            Vector3 localVoxelPos = new Vector3(x, y, z) * voxelSize;
            Vector3 globalVoxelPos = chunkWorldPosition + localVoxelPos;
            if (Vector3.Distance(globalVoxelPos, globalHitPoint) >= radius) return;
            if (Densities[index] >= 0f) return;

            Metadata[index] = targetMaterialID;
        }
    }

    [BurstCompile]
    private struct TerrainSculptorJob : IJob {
        public NativeArray<float> Densities;
        public NativeArray<byte> Metadata;

        [ReadOnly] public NativeArray<float> sourceDensities;

        public int3 gridSize;
        public float voxelSize;

        public float3 globalHitPoint;
        public float3 chunkWorldPosition;
        public float radius;
        public float strength;
        public float fixedStepAmount;

        public BrushShape brushShape;
        public BrushType brushType;
        public bool isVertical;

        public float isoLevel;
        public byte hitMaterial;

        public void Execute() {
            int pointsX = gridSize.x + 1;
            int pointsY = gridSize.y + 1;
            int planeSize = pointsX * pointsY;

            float injectRadius = radius * 0.9f;
            float coreRadius = math.max(voxelSize * 1.25f, radius * 0.22f);

            bool isAdding = brushType == BrushType.SphereAdd;
            bool isSubtracting = brushType == BrushType.SphereSubtract;
            bool isSmoothing = !isAdding && !isSubtracting;

            for (int index = 0; index < Densities.Length; index++) {
                int z = index / planeSize;
                int rem = index - z * planeSize;
                int y = rem / pointsX;
                int x = rem - y * pointsX;

                if (x > gridSize.x || y > gridSize.y || z > gridSize.z) continue;

                float3 voxelPos = chunkWorldPosition + new float3(x, y, z) * voxelSize;
                float distance = isVertical
                    ? math.distance(voxelPos.xz, globalHitPoint.xz)
                    : math.distance(voxelPos, globalHitPoint);

                if (distance > radius) continue;

                float falloff = math.smoothstep(radius, 0f, distance);
                float delta = strength * falloff * fixedStepAmount;

                float oldDensity = sourceDensities[index];
                float newDensity = oldDensity;

                if (isAdding)
                    newDensity -= delta;
                else if (isSubtracting)
                    newDensity += delta;
                else if (isSmoothing) {
                    float neighborAvg = GetNeighborAverage(x, y, z, pointsX, planeSize, oldDensity);
                    newDensity = math.lerp(oldDensity, neighborAvg, math.saturate(delta));
                }

                newDensity = math.clamp(newDensity, -1f, 1f);
                Densities[index] = newDensity;

                bool wasAir = oldDensity > isoLevel;
                bool isNowSolid = newDensity <= isoLevel;
                if (!isAdding) continue;
                if (!wasAir || !isNowSolid) continue;
                if (hitMaterial == 0) continue;

                if (distance <= coreRadius)
                    Metadata[index] = hitMaterial;
            }

            if (!isAdding || hitMaterial == 0) return;

            for (int pass = 0; pass < 3; pass++) {
                bool changed = false;

                for (int index = 0; index < Densities.Length; index++) {
                    int z = index / planeSize;
                    int rem = index - z * planeSize;
                    int y = rem / pointsX;
                    int x = rem - y * pointsX;

                    if (x > gridSize.x || y > gridSize.y || z > gridSize.z) continue;

                    float3 voxelPos = chunkWorldPosition + new float3(x, y, z) * voxelSize;
                    float radialDist = math.distance(voxelPos, globalHitPoint);
                    if (radialDist > injectRadius) continue;

                    float oldDensity = sourceDensities[index];
                    bool wasAir = oldDensity > isoLevel;
                    if (!wasAir) continue;
                    if (Densities[index] > isoLevel) continue;
                    if (Metadata[index] == hitMaterial) continue;

                    if (!HasNeighborHitMat(x, y, z, pointsX, planeSize)) continue;

                    Metadata[index] = hitMaterial;
                    changed = true;
                }

                if (!changed) break;
            }
        }

        private float GetNeighborAverage(int x, int y, int z, int pointsX, int planeSize, float fallback) {
            float sum = 0f;
            int count = 0;

            TryAccumulate(x + 1, y, z, pointsX, planeSize, ref sum, ref count);
            TryAccumulate(x - 1, y, z, pointsX, planeSize, ref sum, ref count);
            TryAccumulate(x, y + 1, z, pointsX, planeSize, ref sum, ref count);
            TryAccumulate(x, y - 1, z, pointsX, planeSize, ref sum, ref count);
            TryAccumulate(x, y, z + 1, pointsX, planeSize, ref sum, ref count);
            TryAccumulate(x, y, z - 1, pointsX, planeSize, ref sum, ref count);

            if (count == 0) return fallback;
            return sum / count;
        }

        private void TryAccumulate(int x, int y, int z, int pointsX, int planeSize, ref float sum, ref int count) {
            if (x < 0 || y < 0 || z < 0 || x > gridSize.x || y > gridSize.y || z > gridSize.z) return;

            int i = x + y * pointsX + z * planeSize;
            if (i < 0 || i >= sourceDensities.Length) return;

            sum += sourceDensities[i];
            count++;
        }

        private bool HasNeighborHitMat(int x, int y, int z, int pointsX, int planeSize) {
            int3 p = new int3(x, y, z);

            int3 n0 = p + new int3(1, 0, 0);
            if (IsHitMatSolid(n0, pointsX, planeSize)) return true;

            int3 n1 = p + new int3(-1, 0, 0);
            if (IsHitMatSolid(n1, pointsX, planeSize)) return true;

            int3 n2 = p + new int3(0, 1, 0);
            if (IsHitMatSolid(n2, pointsX, planeSize)) return true;

            int3 n3 = p + new int3(0, -1, 0);
            if (IsHitMatSolid(n3, pointsX, planeSize)) return true;

            int3 n4 = p + new int3(0, 0, 1);
            if (IsHitMatSolid(n4, pointsX, planeSize)) return true;

            int3 n5 = p + new int3(0, 0, -1);
            return IsHitMatSolid(n5, pointsX, planeSize);
        }

        private bool IsHitMatSolid(int3 p, int pointsX, int planeSize) {
            if (p.x < 0 || p.y < 0 || p.z < 0 || p.x > gridSize.x || p.y > gridSize.y || p.z > gridSize.z) return false;

            int i = p.x + p.y * pointsX + p.z * planeSize;
            if (i < 0 || i >= Densities.Length) return false;
            if (Densities[i] > isoLevel) return false;
            return Metadata[i] == hitMaterial;
        }
    }
}