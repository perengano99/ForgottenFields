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
    float4 colX = SAMPLE_TEXTURE2D_ARRAY(TexArray.tex, TexArray.samplerstate, uvX, Index);
    float4 colY = SAMPLE_TEXTURE2D_ARRAY(TexArray.tex, TexArray.samplerstate, uvY, Index);
    float4 colZ = SAMPLE_TEXTURE2D_ARRAY(TexArray.tex, TexArray.samplerstate, uvZ, Index);

    // Mezcla final
    OutColor = colX * blend.x + colY * blend.y + colZ * blend.z;
}

#endif