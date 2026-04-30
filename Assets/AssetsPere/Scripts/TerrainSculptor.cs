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
        byte materialID,
        bool isVertical = false) {

        if (!densities.IsCreated || !metadata.IsCreated || radius <= 0f || voxelSize <= 0f) return;

        float dt = Time.deltaTime > 0f ? Time.deltaTime : (1f / 60f);

        SculptJob job = new SculptJob {
            densities = densities,
            metadata = metadata,
            gridSize = gridSize,
            voxelSize = voxelSize,
            hitPoint = new float3(localHitPoint.x, localHitPoint.y, localHitPoint.z),
            radius = radius,
            strength = strength,
            brushShape = shape,
            brushType = type,
            materialID = materialID,
            isVertical = isVertical,
            deltaTime = dt,
            lowPolyStepFactor = 3.5f,
            noiseScale = 0.18f,
            noiseAmplitude = 1.0f
        };

        job.Schedule(densities.Length, 64).Complete();
    }

    // === PINTURA DE MATERIALES ===
    public static JobHandle SchedulePaintJob(
        NativeArray<byte> metadata,
        int3 gridSize,
        float voxelSize,
        Vector3 localHitPoint,
        float radius,
        byte targetMaterialID,
        JobHandle dependsOn = default) {

        if (!metadata.IsCreated || radius <= 0f || voxelSize <= 0f) return dependsOn;

        PaintTerrainJob job = new PaintTerrainJob {
            Metadata = metadata,
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

            Metadata[index] = targetMaterialID;
        }
    }

    [BurstCompile]
    private struct SculptJob : IJobParallelFor {
        public NativeArray<float> densities;
        public NativeArray<byte> metadata;

        public int3 gridSize;
        public float voxelSize;

        public float3 hitPoint;
        public float radius;
        public float strength;

        public BrushShape brushShape;
        public BrushType brushType;
        public byte materialID;
        public bool isVertical;

        public float deltaTime;
        public float lowPolyStepFactor;
        public float noiseScale;
        public float noiseAmplitude;

        private float FixedStepAmount => math.max(0.0005f, deltaTime * lowPolyStepFactor);

        public void Execute(int index) {
            int pointsX = gridSize.x + 1;
            int pointsY = gridSize.y + 1;
            int planeSize = pointsX * pointsY;

            int z = index / planeSize;
            int rem = index - z * planeSize;
            int y = rem / pointsX;
            int x = rem - y * pointsX;

            if (x > gridSize.x || y > gridSize.y || z > gridSize.z) return;

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

            if (!affects) return;

            ApplyQuantizedDensity(index, voxelPos, shapeFactor);

            if (densities[index] < 0f)
                metadata[index] = materialID;
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
        private void ApplyQuantizedDensity(int index, float3 voxelPos, float shapeFactor) {
            float step = FixedStepAmount * math.max(0.01f, math.abs(strength)) * math.max(0.01f, shapeFactor);
            float current = densities[index];

            switch (brushType) {
                case BrushType.SphereAdd: {
                    float deltaDensity = -math.sign(strength == 0f ? 1f : strength) * step;
                    densities[index] = current + deltaDensity;
                    break;
                }
                case BrushType.SphereSubtract: {
                    float deltaDensity = math.sign(strength == 0f ? 1f : strength) * step;
                    densities[index] = current + deltaDensity;
                    break;
                }
                case BrushType.Flatten: {
                    float target = voxelPos.y - hitPoint.y;
                    float dir = math.sign(target - current);
                    float deltaDensity = dir * step;
                    float next = current + deltaDensity;

                    if ((dir > 0f && next > target) || (dir < 0f && next < target))
                        next = target;

                    densities[index] = next;
                    break;
                }
            }
        }
    }
}
