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
        Vector3 localHitPoint,
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
            localHitPoint,
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
        Vector3 localHitPoint,
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
            hitPoint = new float3(localHitPoint.x, localHitPoint.y, localHitPoint.z),
            radius = radius,
            strength = strength,
            brushShape = shape,
            brushType = type,
            isVertical = isVertical,
            deltaTime = dt,
            lowPolyStepFactor = 3.5f,
            noiseScale = 0.18f,
            noiseAmplitude = 1.0f,
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
        Vector3 localHitPoint,
        float radius,
        byte targetMaterialID,
        JobHandle dependsOn = default) {

        if (!metadata.IsCreated || !densities.IsCreated || radius <= 0f || voxelSize <= 0f) return dependsOn;

        PaintTerrainJob job = new PaintTerrainJob {
            Metadata = metadata,
            Densities = densities,
            gridSize = gridSize,
            voxelSize = voxelSize,
            localHitPoint = localHitPoint,
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
        public Vector3 localHitPoint;
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

            Vector3 voxelPos = new Vector3(x, y, z) * voxelSize;
            if (Vector3.Distance(voxelPos, localHitPoint) >= radius) return;
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

        public float3 hitPoint;
        public float radius;
        public float strength;

        public BrushShape brushShape;
        public BrushType brushType;
        public bool isVertical;

        public float deltaTime;
        public float lowPolyStepFactor;
        public float noiseScale;
        public float noiseAmplitude;
        public float isoLevel;
        public byte hitMaterial;

        private float FixedStepAmount => math.max(0.0005f, deltaTime * lowPolyStepFactor);

        public void Execute() {
            int pointsX = gridSize.x + 1;
            int pointsY = gridSize.y + 1;
            int planeSize = pointsX * pointsY;

            float injectRadius = radius * 0.9f;
            float coreRadius = math.max(voxelSize * 1.25f, radius * 0.22f);

            for (int index = 0; index < Densities.Length; index++) {
                int z = index / planeSize;
                int rem = index - z * planeSize;
                int y = rem / pointsX;
                int x = rem - y * pointsX;

                if (x > gridSize.x || y > gridSize.y || z > gridSize.z) continue;

                float3 voxelPos = new float3(x, y, z) * voxelSize;

                bool affects = false;
                float shapeFactor = 1f;

                switch (brushShape) {
                    case BrushShape.Sphere:
                        EvaluateSphere(voxelPos, ref affects, ref shapeFactor);
                        break;

                    case BrushShape.Box:
                        EvaluateBox(voxelPos, ref affects, ref shapeFactor);
                        break;

                    case BrushShape.VerticalPillar:
                        EvaluateVerticalPillar(voxelPos, ref affects, ref shapeFactor);
                        break;

                    case BrushShape.NoiseFeature:
                        EvaluateNoiseFeature(voxelPos, ref affects, ref shapeFactor);
                        break;
                }

                if (!affects) continue;

                float oldDensity = sourceDensities[index];
                bool wasAir = oldDensity > isoLevel;

                float newDensity = ComputeQuantizedDensity(oldDensity, voxelPos, shapeFactor);
                Densities[index] = newDensity;

                bool isNowSolid = newDensity <= isoLevel;
                if (brushType != BrushType.SphereAdd) continue;
                if (!wasAir || !isNowSolid) continue;
                if (hitMaterial == 0) continue;

                float radialDist = math.distance(voxelPos, hitPoint);
                if (radialDist <= coreRadius)
                    Metadata[index] = hitMaterial;
            }

            if (brushType != BrushType.SphereAdd || hitMaterial == 0) return;

            for (int pass = 0; pass < 3; pass++) {
                bool changed = false;

                for (int index = 0; index < Densities.Length; index++) {
                    int z = index / planeSize;
                    int rem = index - z * planeSize;
                    int y = rem / pointsX;
                    int x = rem - y * pointsX;

                    if (x > gridSize.x || y > gridSize.y || z > gridSize.z) continue;

                    float3 voxelPos = new float3(x, y, z) * voxelSize;
                    float radialDist = math.distance(voxelPos, hitPoint);
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

        // === EVALUACIÓN ESPACIAL ===
        private void EvaluateSphere(float3 voxelPos, ref bool affects, ref float factor) {
            float dist = math.distance(voxelPos, hitPoint);
            if (dist > radius) return;

            affects = true;
            factor = math.saturate(1f - (dist / radius));
        }

        private void EvaluateBox(float3 voxelPos, ref bool affects, ref float factor) {
            float3 d = math.abs(voxelPos - hitPoint);
            if (d.x > radius || d.y > radius || d.z > radius) return;

            affects = true;
            factor = 1f;
        }

        private void EvaluateVerticalPillar(float3 voxelPos, ref bool affects, ref float factor) {
            float2 xzDist = new float2(voxelPos.x - hitPoint.x, voxelPos.z - hitPoint.z);
            float radial = math.length(xzDist);
            if (radial > radius) return;

            float lower = hitPoint.y;
            float upper = hitPoint.y;

            if (brushType == BrushType.SphereAdd)
                upper = hitPoint.y + radius * 2f;
            else if (brushType == BrushType.SphereSubtract)
                lower = hitPoint.y - radius * 2f;
            else {
                lower = hitPoint.y - radius;
                upper = hitPoint.y + radius;
            }

            if (voxelPos.y < lower || voxelPos.y > upper) return;

            affects = true;
            factor = math.saturate(1f - (radial / radius));
        }

        private void EvaluateNoiseFeature(float3 voxelPos, ref bool affects, ref float factor) {
            float2 xz = new float2(voxelPos.x, voxelPos.z);
            float2 hitXZ = new float2(hitPoint.x, hitPoint.z);
            float radial = math.length(xz - hitXZ);
            if (radial > radius) return;

            float radialFactor = math.saturate(1f - (radial / radius));
            float n = Unity.Mathematics.noise.cnoise(xz * noiseScale);
            float noiseFactor = 1f + (n * noiseAmplitude);

            affects = true;
            factor = radialFactor * noiseFactor;
        }

        // === APLICACIÓN LOW POLY CUANTIZADA ===
        private float ComputeQuantizedDensity(float current, float3 voxelPos, float shapeFactor) {
            float step = FixedStepAmount * math.max(0.01f, math.abs(strength)) * math.max(0.01f, shapeFactor);

            switch (brushType) {
                case BrushType.SphereAdd: {
                    float deltaDensity = -math.sign(strength == 0f ? 1f : strength) * step;
                    return current + deltaDensity;
                }
                case BrushType.SphereSubtract: {
                    float deltaDensity = math.sign(strength == 0f ? 1f : strength) * step;
                    return current + deltaDensity;
                }
                case BrushType.Flatten: {
                    float target = voxelPos.y - hitPoint.y;
                    float dir = math.sign(target - current);
                    float deltaDensity = dir * step;
                    float next = current + deltaDensity;

                    if ((dir > 0f && next > target) || (dir < 0f && next < target))
                        next = target;

                    return next;
                }
            }

            return current;
        }
    }
}
