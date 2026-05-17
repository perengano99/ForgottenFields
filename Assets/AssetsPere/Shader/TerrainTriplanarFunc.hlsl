// === TERRAIN TRIPLANAR FUNC ===/ === TERRAIN TRIPLANAR FUNC ===/ === TERRAIN TRIPLANAR FUNC ===/ === TERRAIN TRIPLANAR FUNC ===
// ShaderGraph CustomFunction → nombre del nodo: "SampleTerrainLayer"/ ShaderGraph CustomFunction → nombre del nodo: "SampleTerrainLayer"/ ShaderGraph CustomFunction → nombre del nodo: "SampleTerrainLayer"/ ShaderGraph CustomFunction → nombre del nodo: "SampleTerrainLayer"
// MaterialData: Texture2D (1 alto × 256 ancho) = lookup table de materiales/ MaterialData: Texture2D (1 alto × 256 ancho) = lookup table de materiales/ MaterialData: Texture2D (1 alto × 256 ancho) = lookup table de materiales/ MaterialData: Texture2D (1 alto × 256 ancho) = lookup table de materiales

#ifndef TERRAIN_TRIPLANAR_FUNC_INCLUDED
#define TERRAIN_TRIPLANAR_FUNC_INCLUDED

// === HELPERS (internos) ===/ === HELPERS (internos) ===/ === HELPERS (internos) ===/ === HELPERS (internos) ===

float3 _TriplanarWeights(float3 n, float sharpness)
{
    float3 w = abs(n);
    w = pow(w, max(sharpness, 1.0));
    w /= dot(w, 1.0);
    return w;
}

float2 _ScaleUV(float2 uv)
{
    return uv + 0.5;
}

// === ENTRY POINT SHADERGRAPH ===/ === ENTRY POINT SHADERGRAPH ===/ === ENTRY POINT SHADERGRAPH ===/ === ENTRY POINT SHADERGRAPH ===
// Nombre en CustomFunction node → "SampleTerrainLayer"/ Nombre en CustomFunction node → "SampleTerrainLayer"/ Nombre en CustomFunction node → "SampleTerrainLayer"/ Nombre en CustomFunction node → "SampleTerrainLayer"
// MaterialData = Texture2D lookup: 1 fila × 256+ columnas/ MaterialData = Texture2D lookup: 1 fila × 256+ columnas/ MaterialData = Texture2D lookup: 1 fila × 256+ columnas/ MaterialData = Texture2D lookup: 1 fila × 256+ columnas
//   Cada píxel (x=index, y=0) contiene: (Roughness, Metallic, 0, 0)/   Cada píxel (x=index, y=0) contiene: (Roughness, Metallic, 0, 0)/   Cada píxel (x=index, y=0) contiene: (Roughness, Metallic, 0, 0)/   Cada píxel (x=index, y=0) contiene: (Roughness, Metallic, 0, 0)
// Inputs:/ Inputs:/ Inputs:/ Inputs:
//   WorldPos        (Vector3)/   WorldPos        (Vector3)/   WorldPos        (Vector3)/   WorldPos        (Vector3)
//   WorldNormal     (Vector3)/   WorldNormal     (Vector3)/   WorldNormal     (Vector3)/   WorldNormal     (Vector3)
//   AlbedoArray     (Texture2DArray)/   AlbedoArray     (Texture2DArray)/   AlbedoArray     (Texture2DArray)/   AlbedoArray     (Texture2DArray)
//   NormalArray     (Texture2DArray)/   NormalArray     (Texture2DArray)/   NormalArray     (Texture2DArray)/   NormalArray     (Texture2DArray)
//   MaterialData    (Texture2D) — lookup table materiales/   MaterialData    (Texture2D) — lookup table materiales/   MaterialData    (Texture2D) — lookup table materiales/   MaterialData    (Texture2D) — lookup table materiales
//   Sampler         (SamplerState)/   Sampler         (SamplerState)/   Sampler         (SamplerState)/   Sampler         (SamplerState)
//   Index           (Float / int)/   Index           (Float / int)/   Index           (Float / int)/   Index           (Float / int)
//   BaseDensity     (Float) — densidad de referencia, usar 32/   BaseDensity     (Float) — densidad de referencia, usar 32/   BaseDensity     (Float) — densidad de referencia, usar 32/   BaseDensity     (Float) — densidad de referencia, usar 32
//   BlendSharpness  (Float) — nitidez del blend (recomendado: 4-8)/   BlendSharpness  (Float) — nitidez del blend (recomendado: 4-8)/   BlendSharpness  (Float) — nitidez del blend (recomendado: 4-8)/   BlendSharpness  (Float) — nitidez del blend (recomendado: 4-8)
// Outputs:/ Outputs:/ Outputs:/ Outputs:
//   Albedo          (Vector4)/   Albedo          (Vector4)/   Albedo          (Vector4)/   Albedo          (Vector4)
//   Normal          (Vector3)/   Normal          (Vector3)/   Normal          (Vector3)/   Normal          (Vector3)
//   Roughness       (Float)/   Roughness       (Float)/   Roughness       (Float)/   Roughness       (Float)
//   Metallic        (Float)/   Metallic        (Float)/   Metallic        (Float)/   Metallic        (Float)

void SampleTerrainLayer_float(
float3 WorldPos,
float3 WorldNormal,
UnityTexture2DArray AlbedoArray,
UnityTexture2DArray NormalArray,
UnityTexture2D MaterialData,
UnitySamplerState SS,
int Index,
float BaseDensity,
float BlendSharpness,
out float4 Albedo,
out float3 Normal,
out float Roughness,
out float Metallic)
{
    // === PREVIEW OVERRIDE ===/ === PREVIEW OVERRIDE ===/ === PREVIEW OVERRIDE ===/ === PREVIEW OVERRIDE ===
    // _PreviewMaterialIndex lo declara ShaderGraph como propiedad del material/ _PreviewMaterialIndex lo declara ShaderGraph como propiedad del material/ _PreviewMaterialIndex lo declara ShaderGraph como propiedad del material/ _PreviewMaterialIndex lo declara ShaderGraph como propiedad del material
    int layerIndex = Index;
    if (_PreviewMaterialIndex >= 0.0) layerIndex = (int)_PreviewMaterialIndex;

    float3 w = _TriplanarWeights(WorldNormal, BlendSharpness);

    // 1. Preparamos las coordenadas float3 (UV.x, UV.y, Capa)/ 1. Preparamos las coordenadas float3 (UV.x, UV.y, Capa)/ 1. Preparamos las coordenadas float3 (UV.x, UV.y, Capa)/ 1. Preparamos las coordenadas float3 (UV.x, UV.y, Capa)
    float3 uvwX = float3(_ScaleUV(WorldPos.zy), (float) layerIndex);
    float3 uvwY = float3(_ScaleUV(WorldPos.xz), (float) layerIndex);
    float3 uvwZ = float3(_ScaleUV(WorldPos.xy), (float) layerIndex);

    // 2. Muestreo usando el miembro .samplerstate de la estructura SS/ 2. Muestreo usando el miembro .samplerstate de la estructura SS/ 2. Muestreo usando el miembro .samplerstate de la estructura SS/ 2. Muestreo usando el miembro .samplerstate de la estructura SS
    // Esto evita el error de "SampleBias" y permite usar el Sampler Point de Shader Graph/ Esto evita el error de "SampleBias" y permite usar el Sampler Point de Shader Graph/ Esto evita el error de "SampleBias" y permite usar el Sampler Point de Shader Graph/ Esto evita el error de "SampleBias" y permite usar el Sampler Point de Shader Graph
    float4 aX = AlbedoArray.tex.Sample(SS.samplerstate, uvwX);
    float4 aY = AlbedoArray.tex.Sample(SS.samplerstate, uvwY);
    float4 aZ = AlbedoArray.tex.Sample(SS.samplerstate, uvwZ);
    Albedo = aX * w.x + aY * w.y + aZ * w.z;

    // 3. Normales/ 3. Normales/ 3. Normales/ 3. Normales
    float3 nX = UnpackNormal(NormalArray.tex.Sample(SS.samplerstate, uvwX));
    float3 nY = UnpackNormal(NormalArray.tex.Sample(SS.samplerstate, uvwY));
    float3 nZ = UnpackNormal(NormalArray.tex.Sample(SS.samplerstate, uvwZ));
    
    nX = float3(nX.xy + WorldNormal.zy, abs(nX.z) * WorldNormal.x);
    nY = float3(nY.xy + WorldNormal.xz, abs(nY.z) * WorldNormal.y);
    nZ = float3(nZ.xy + WorldNormal.xy, abs(nZ.z) * WorldNormal.z);

    Normal = normalize(nX.zxy * w.x + nY.xzy * w.y + nZ.xyz * w.z);

    // === MATERIAL PROPERTIES (desde MaterialData lookup) ===/ === MATERIAL PROPERTIES (desde MaterialData lookup) ===/ === MATERIAL PROPERTIES (desde MaterialData lookup) ===/ === MATERIAL PROPERTIES (desde MaterialData lookup) ===
    float materialUV = (float(layerIndex) + 0.5) / 256.0;
    float4 materialSample = SAMPLE_TEXTURE2D(MaterialData.tex, MaterialData.samplerstate, float2(materialUV, 0.5));
    
    Roughness = materialSample.r;
    Metallic = materialSample.g;
    
    float hasNormal = materialSample.b;// flag azul: 0 = no tiene normal map, 1 = tiene normal map
    Normal = lerp(WorldNormal, Normal, hasNormal);
}

// === HALF VARIANT (mobile) ===/ === HALF VARIANT (mobile) ===/ === HALF VARIANT (mobile) ===/ === HALF VARIANT (mobile) ===
void SampleTerrainLayer_half(
half3 WorldPos,
half3 WorldNormal,
UnityTexture2DArray AlbedoArray,
UnityTexture2DArray NormalArray,
UnityTexture2D MaterialData,
UnitySamplerState SS,
int Index,
half BaseDensity,
half BlendSharpness,
out half4 Albedo,
out half3 Normal,
out half Roughness,
out half Metallic)
{
    float4 a; float3 n; float r; float m;
    SampleTerrainLayer_float(
    WorldPos, WorldNormal,
    AlbedoArray, NormalArray, MaterialData,
    SS, Index, BaseDensity, BlendSharpness,
    a, n, r, m);
    Albedo = a;
    Normal = n;
    Roughness = r;
    Metallic = m;
}

#endif // TERRAIN_TRIPLANAR_FUNC_INCLUDED/ TERRAIN_TRIPLANAR_FUNC_INCLUDED/ TERRAIN_TRIPLANAR_FUNC_INCLUDED/ TERRAIN_TRIPLANAR_FUNC_INCLUDED


