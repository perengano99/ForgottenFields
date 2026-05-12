using Unity.Mathematics;

public static class TerrainCSG {
    // === SDF Shapes ===
    public static float SDFSphere(float3 p, float radius) {
        return math.length(p) - radius;
    }

    public static float SDFBox(float3 p, float3 bounds) {
        float3 q = math.abs(p) - bounds;
        return math.length(math.max(q, 0.0f)) + math.min(math.max(q.x, math.max(q.y, q.z)), 0.0f);
    }

    public static float SDFVerticalPillar(float2 pXZ, float radius) {
        return math.length(pXZ) - radius;
    }

    // === CSG Operations ===
    public static float Union(float d1, float d2) {
        return math.min(d1, d2);
    }

    public static float Difference(float baseSDF, float subtractSDF) {
        return math.max(baseSDF, -subtractSDF);
    }

    public static float SmoothUnion(float d1, float d2, float k) {
        if (k <= 0f) return math.min(d1, d2);

        float h = math.saturate(0.5f + 0.5f * (d2 - d1) / k);
        return math.lerp(d2, d1, h) - k * h * (1f - h);
    }
}