#ifndef TERRAIN_SPLATTING_INCLUDED
#define TERRAIN_SPLATTING_INCLUDED

float hash3D(float3 p){
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float simpleNoise(float3 p){
    float3 i = floor(p); float3 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(lerp(hash3D(i+float3(0,0,0)), hash3D(i+float3(1,0,0)), f.x),
                     lerp(hash3D(i+float3(0,1,0)), hash3D(i+float3(1,1,0)), f.x), f.y),
                lerp(lerp(hash3D(i+float3(0,0,1)), hash3D(i+float3(1,0,1)), f.x),
                     lerp(hash3D(i+float3(0,1,1)), hash3D(i+float3(1,1,1)), f.x), f.y), f.z);
}

void OrganicTriplanarBlend_float(UnityTexture2DArray TexArray, float3 WorldPos, float3 WorldNormal, float4 Indices, float4 Weights, float Sharpness, float NoiseScale, float NoiseStrength, out float3 OutColor)
{
    // 1. Ruido orgánico sobre los pesos espaciales exactos
    float noise = simpleNoise(WorldPos * NoiseScale) * 2.0 - 1.0;
    float4 w = Weights + (noise * NoiseStrength);
    w = max(w, 0.0);
    float sum = dot(w, (float4) 1.0);
    if (sum > 0.0001)
        w /= sum;
    else
        w = float4(1, 0, 0, 0);

    // 2. Preparación Triplanar
    float3 blend = pow(abs(WorldNormal), Sharpness);
    blend /= dot(blend, (float3) 1.0);
    float2 uvX = WorldPos.zy;
    float2 uvY = WorldPos.xz;
    float2 uvZ = WorldPos.xy;
    float3 finalCol = float3(0, 0, 0);

    // 3. Muestreo de los 4 materiales
    if (w.x > 0.001 && Indices.x >= 0.0)
    {
        float3 c = float3(0, 0, 0);
        if (blend.x > 0.001)
            c += SAMPLE_TEXTURE2D_ARRAY(TexArray.tex, TexArray.samplerstate, uvX, Indices.x).rgb * blend.x;
        if (blend.y > 0.001)
            c += SAMPLE_TEXTURE2D_ARRAY(TexArray.tex, TexArray.samplerstate, uvY, Indices.x).rgb * blend.y;
        if (blend.z > 0.001)
            c += SAMPLE_TEXTURE2D_ARRAY(TexArray.tex, TexArray.samplerstate, uvZ, Indices.x).rgb * blend.z;
        finalCol += c * w.x;
    }
    if (w.y > 0.001 && Indices.y >= 0.0)
    {
        float3 c = float3(0, 0, 0);
        if (blend.x > 0.001)
            c += SAMPLE_TEXTURE2D_ARRAY(TexArray.tex, TexArray.samplerstate, uvX, Indices.y).rgb * blend.x;
        if (blend.y > 0.001)
            c += SAMPLE_TEXTURE2D_ARRAY(TexArray.tex, TexArray.samplerstate, uvY, Indices.y).rgb * blend.y;
        if (blend.z > 0.001)
            c += SAMPLE_TEXTURE2D_ARRAY(TexArray.tex, TexArray.samplerstate, uvZ, Indices.y).rgb * blend.z;
        finalCol += c * w.y;
    }
    if (w.z > 0.001 && Indices.z >= 0.0)
    {
        float3 c = float3(0, 0, 0);
        if (blend.x > 0.001)
            c += SAMPLE_TEXTURE2D_ARRAY(TexArray.tex, TexArray.samplerstate, uvX, Indices.z).rgb * blend.x;
        if (blend.y > 0.001)
            c += SAMPLE_TEXTURE2D_ARRAY(TexArray.tex, TexArray.samplerstate, uvY, Indices.z).rgb * blend.y;
        if (blend.z > 0.001)
            c += SAMPLE_TEXTURE2D_ARRAY(TexArray.tex, TexArray.samplerstate, uvZ, Indices.z).rgb * blend.z;
        finalCol += c * w.z;
    }
    if (w.w > 0.001 && Indices.w >= 0.0)
    {
        float3 c = float3(0, 0, 0);
        if (blend.x > 0.001)
            c += SAMPLE_TEXTURE2D_ARRAY(TexArray.tex, TexArray.samplerstate, uvX, Indices.w).rgb * blend.x;
        if (blend.y > 0.001)
            c += SAMPLE_TEXTURE2D_ARRAY(TexArray.tex, TexArray.samplerstate, uvY, Indices.w).rgb * blend.y;
        if (blend.z > 0.001)
            c += SAMPLE_TEXTURE2D_ARRAY(TexArray.tex, TexArray.samplerstate, uvZ, Indices.w).rgb * blend.z;
        finalCol += c * w.w;
    }
    OutColor = finalCol;
}

#endif
