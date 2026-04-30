#ifndef TERRAIN_SPLATTING_INCLUDED
#define TERRAIN_SPLATTING_INCLUDED

// Proyección Triplanar eficiente con arrays de texturas
void TriplanarSplat_float(UnityTexture2DArray TexArray, float3 WorldPos, float3 WorldNormal, float Index, float Sharpness, out float4 OutColor)
{
    // Calcular pesos absolutos de las normales
    float3 blend = pow(abs(WorldNormal), Sharpness);
    blend /= dot(blend, (float3) 1.0); // Normalizar pesos

    // Coordenadas para cada eje
    float2 uvX = WorldPos.zy;
    float2 uvY = WorldPos.xz;
    float2 uvZ = WorldPos.xy;

    // Muestreo del Texture2DArray
    float4 col = float4(0, 0, 0, 0);

    if (blend.x > 0.001)
        col += SAMPLE_TEXTURE2D_ARRAY(TexArray.tex, TexArray.samplerstate, uvX, Index) * blend.x;

    if (blend.y > 0.001)
        col += SAMPLE_TEXTURE2D_ARRAY(TexArray.tex, TexArray.samplerstate, uvY, Index) * blend.y;

    if (blend.z > 0.001)
        col += SAMPLE_TEXTURE2D_ARRAY(TexArray.tex, TexArray.samplerstate, uvZ, Index) * blend.z;

    // Mezcla final
    OutColor = col;
}

#endif