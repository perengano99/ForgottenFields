// === TERRAIN TRIPLANAR FUNC ===
// ShaderGraph CustomFunction → nombre del nodo: "SampleTerrainLayer"
// MaterialData: Texture2D (1 alto × 256 ancho) = lookup table de materiales

#ifndef TERRAIN_TRIPLANAR_FUNC_INCLUDED
#define TERRAIN_TRIPLANAR_FUNC_INCLUDED

// === HELPERS (internos) ===

float3 _TriplanarWeights(float3 n, float sharpness)
{
    float3 w = abs(n);
    w = pow(w, max(sharpness, 1.0));
    w /= dot(w, 1.0);
    return w;
}

float2 _ScaleUV(float2 uv, float texSize, float baseDensity)
{
    return uv * (texSize / max(baseDensity, 0.001));
}

// === ENTRY POINT SHADERGRAPH ===
// Nombre en CustomFunction node → "SampleTerrainLayer"
// MaterialData = Texture2D lookup: 1 fila × 256+ columnas
//   Cada píxel (x=index, y=0) contiene: (Roughness, Metallic, 0, 0)
// Inputs:
//   WorldPos        (Vector3)
//   WorldNormal     (Vector3)
//   AlbedoArray     (Texture2DArray)
//   NormalArray     (Texture2DArray)
//   MaterialData    (Texture2D) — lookup table materiales
//   Sampler         (SamplerState)
//   Index           (Float / int)
//   BaseDensity     (Float) — densidad de referencia, usar 32
//   BlendSharpness  (Float) — nitidez del blend (recomendado: 4-8)
// Outputs:
//   Albedo          (Vector4)
//   Normal          (Vector3)
//   Roughness       (Float)
//   Metallic        (Float)

void SampleTerrainLayer_float(
    float3              WorldPos,
    float3              WorldNormal,
    UnityTexture2DArray AlbedoArray,
    UnityTexture2DArray NormalArray,
    UnityTexture2D      MaterialData,
    int                 Index,
    float               BaseDensity,
    float               BlendSharpness,
    out float4          Albedo,
    out float3          Normal,
    out float           Roughness,
    out float           Metallic)
{
    // === AUTO-DETECT TEXTURE SIZE ===
    uint albedoW, albedoH, albedoE;
    AlbedoArray.tex.GetDimensions(albedoW, albedoH, albedoE);
    float texSize = float(albedoW);

    // === PREVIEW OVERRIDE ===
    // _PreviewMaterialIndex lo declara ShaderGraph como propiedad del material
    int layerIndex = Index;
    if (_PreviewMaterialIndex >= 0.0) layerIndex = (int)_PreviewMaterialIndex;

    float3 w = _TriplanarWeights(WorldNormal, BlendSharpness);

    // === ALBEDO ===
    float4 aX = SAMPLE_TEXTURE2D_ARRAY(AlbedoArray.tex, AlbedoArray.samplerstate, _ScaleUV(WorldPos.zy, texSize, BaseDensity), layerIndex);
    float4 aY = SAMPLE_TEXTURE2D_ARRAY(AlbedoArray.tex, AlbedoArray.samplerstate, _ScaleUV(WorldPos.xz, texSize, BaseDensity), layerIndex);
    float4 aZ = SAMPLE_TEXTURE2D_ARRAY(AlbedoArray.tex, AlbedoArray.samplerstate, _ScaleUV(WorldPos.xy, texSize, BaseDensity), layerIndex);
    Albedo = aX * w.x + aY * w.y + aZ * w.z;

    // === NORMAL (RNM blend → world space) ===
    float3 nX = UnpackNormal(SAMPLE_TEXTURE2D_ARRAY(NormalArray.tex, NormalArray.samplerstate, _ScaleUV(WorldPos.zy, texSize, BaseDensity), layerIndex));
    float3 nY = UnpackNormal(SAMPLE_TEXTURE2D_ARRAY(NormalArray.tex, NormalArray.samplerstate, _ScaleUV(WorldPos.xz, texSize, BaseDensity), layerIndex));
    float3 nZ = UnpackNormal(SAMPLE_TEXTURE2D_ARRAY(NormalArray.tex, NormalArray.samplerstate, _ScaleUV(WorldPos.xy, texSize, BaseDensity), layerIndex));

    nX = float3(nX.xy + WorldNormal.zy, abs(nX.z) * WorldNormal.x);
    nY = float3(nY.xy + WorldNormal.xz, abs(nY.z) * WorldNormal.y);
    nZ = float3(nZ.xy + WorldNormal.xy, abs(nZ.z) * WorldNormal.z);

    Normal = normalize(nX.zxy * w.x + nY.xzy * w.y + nZ.xyz * w.z);

    // === MATERIAL PROPERTIES (desde MaterialData lookup) ===
    float materialUV = (float(layerIndex) + 0.5) / 256.0;
    float4 materialSample = SAMPLE_TEXTURE2D(MaterialData.tex, MaterialData.samplerstate, float2(materialUV, 0.5));

    Roughness = materialSample.r;
    Metallic = materialSample.g;
}

// === HALF VARIANT (mobile) ===
void SampleTerrainLayer_half(
    half3               WorldPos,
    half3               WorldNormal,
    UnityTexture2DArray AlbedoArray,
    UnityTexture2DArray NormalArray,
    UnityTexture2D      MaterialData,
    int                 Index,
    half                BaseDensity,
    half                BlendSharpness,
    out half4           Albedo,
    out half3           Normal,
    out half            Roughness,
    out half            Metallic)
{
    float4 a; float3 n; float r; float m;
    SampleTerrainLayer_float(
        WorldPos, WorldNormal,
        AlbedoArray, NormalArray, MaterialData,
        Index, BaseDensity, BlendSharpness,
        a, n, r, m);
    Albedo = a;
    Normal = n;
    Roughness = r;
    Metallic = m;
}

#endif // TERRAIN_TRIPLANAR_FUNC_INCLUDED
