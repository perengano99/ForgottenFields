// === ToonTerrainLighting.hlsl ===
#ifndef TOON_TERRAIN_LIGHTING_INCLUDED
#define TOON_TERRAIN_LIGHTING_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// === CORE ===
float3 ToonTerrainLightingCore(
    float3 WorldPos,
    float3 SmoothWorldNormal,
    float3 BaseColor,
    float ShadowThreshold,
    float ShadowSmoothness,
    float3 ShadowColor)
{
    float3 N = normalize(SmoothWorldNormal);
    float width = max(ShadowSmoothness, 0.0001);
    float thMin = ShadowThreshold - width;
    float thMax = ShadowThreshold + width;

    #ifdef SHADERGRAPH_PREVIEW
        float3 L = normalize(float3(0.35, 0.8, 0.25));
        float ndl01 = saturate(dot(N, L) * 0.5 + 0.5);
        float band = smoothstep(thMin, thMax, ndl01);
        float3 ramp = lerp(ShadowColor, 1.0.xxx, band);
        return BaseColor * ramp;
    #else
        // === MAIN LIGHT ===
        float4 shadowCoord = TransformWorldToShadowCoord(WorldPos);
        Light mainLight = GetMainLight(shadowCoord);

        float attenuation = saturate(mainLight.shadowAttenuation * mainLight.distanceAttenuation);
        float ndlMain01 = saturate(dot(N, normalize(mainLight.direction)) * 0.5 + 0.5);
        float mainBand = smoothstep(thMin, thMax, ndlMain01 * attenuation);

        float3 mainRamp = lerp(ShadowColor, 1.0.xxx, mainBand);
        float3 color = BaseColor * mainRamp * mainLight.color;

        // === ADDITIONAL LIGHTS ===
        uint addCount = GetAdditionalLightsCount();
        for (uint i = 0u; i < addCount; i++) {
            Light addLight = GetAdditionalLight(i, WorldPos);

            float ndlAdd01 = saturate(dot(N, normalize(addLight.direction)) * 0.5 + 0.5);
            float addDistHard = step(0.001, addLight.distanceAttenuation);
            float addAtten = addDistHard * addLight.shadowAttenuation;
            float addBand = smoothstep(thMin, thMax, ndlAdd01 * addAtten);

            color += BaseColor * addLight.color * addBand;
        }

        return color;
    #endif
}

// === CUSTOM FUNCTION (FLOAT PRECISION) ===
void ToonTerrainLighting_float(
    float3 WorldPos,
    float3 SmoothWorldNormal,
    float3 BaseColor,
    float ShadowThreshold,
    float ShadowSmoothness,
    float3 ShadowColor,
    out float3 OutColor)
{
    OutColor = ToonTerrainLightingCore(
        WorldPos,
        SmoothWorldNormal,
        BaseColor,
        ShadowThreshold,
        ShadowSmoothness,
        ShadowColor);
}

// === CUSTOM FUNCTION (HALF PRECISION) ===
void ToonTerrainLighting_half(
    half3 WorldPos,
    half3 SmoothWorldNormal,
    half3 BaseColor,
    half ShadowThreshold,
    half ShadowSmoothness,
    half3 ShadowColor,
    out half3 OutColor)
{
    float3 result = ToonTerrainLightingCore(
        (float3)WorldPos,
        (float3)SmoothWorldNormal,
        (float3)BaseColor,
        (float)ShadowThreshold,
        (float)ShadowSmoothness,
        (float3)ShadowColor);

    OutColor = (half3)result;
}

void ToonShading_float(
    float3 WorldPos,
    float3 SmoothWorldNormal,
    float3 BaseColor,
    float ShadowThreshold,
    float ShadowSmoothness,
    float3 ShadowColor,
    out float3 OutColor)
{
    ToonTerrainLighting_float(
        WorldPos,
        SmoothWorldNormal,
        BaseColor,
        ShadowThreshold,
        ShadowSmoothness,
        ShadowColor,
        OutColor);
}

void ToonShading_half(
    half3 WorldPos,
    half3 SmoothWorldNormal,
    half3 BaseColor,
    half ShadowThreshold,
    half ShadowSmoothness,
    half3 ShadowColor,
    out half3 OutColor)
{
    ToonTerrainLighting_half(
        WorldPos,
        SmoothWorldNormal,
        BaseColor,
        ShadowThreshold,
        ShadowSmoothness,
        ShadowColor,
        OutColor);
}

#endif