#ifndef TERRAIN_LIGHTING_INCLUDED
#define TERRAIN_LIGHTING_INCLUDED

// Aseguramos que las funciones de iluminación de URP estén disponibles
#ifndef UNIVERSAL_LIGHTING_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#endif

void TerrainLighting_float(float3 WorldPos, float3 Normal, float3 Albedo, float ShadowSmoothness, out float3 OutColor)
{
    float3 finalColor = float3(0, 0, 0);

#ifdef SHADERGRAPH_PREVIEW
    // Para la vista previa en la ventanita de Shader Graph
    float NdotL = saturate(dot(Normal, normalize(float3(0.5, 0.5, 0.25))));
    float stepped = smoothstep(0.5 - ShadowSmoothness, 0.5 + ShadowSmoothness, NdotL);
    OutColor = Albedo * (stepped * 0.8 + 0.2);
#else
    // 1. Obtener Coordenadas de Sombra
#if SHADOWS_SCREEN
        float4 clipPos = TransformWorldToHClip(WorldPos);
        float4 shadowCoord = ComputeScreenPos(clipPos);
#else
    float4 shadowCoord = TransformWorldToShadowCoord(WorldPos);
#endif

    // 2. Luz Principal (Sol)
    Light mainLight = GetMainLight(shadowCoord);
    
    // Mapeo del producto punto de -1..1 a 0..1 (Half Lambert) para sombras menos bruscas
    float NdotL = dot(Normal, mainLight.direction);
    float halfLambert = NdotL * 0.5 + 0.5;
    
    // Sombras propias (Form shadow) con bordes ligeramente suaves
    float litFactor = smoothstep(0.5 - ShadowSmoothness, 0.5 + ShadowSmoothness, halfLambert);
    
    // Sombras arrojadas (Cast shadow) con bordes ligeramente suaves
    float shadowFactor = smoothstep(0.1, 0.9, mainLight.shadowAttenuation);
    
    float mainLightIntensity = litFactor * shadowFactor;

    // Tinte ambiental (GI / Ambient Light) - Afectado por tu Skybox o Environment Lighting
    float3 ambient = SampleSH(Normal);
    float3 shadowColor = Albedo * ambient;
    
    // Color de la luz principal multiplicada por el albedo
    float3 mainLightColor = Albedo * mainLight.color;
    
    // Mezcla final de la luz principal y la sombra tintada ambientalmente
    finalColor = lerp(shadowColor, mainLightColor + shadowColor, mainLightIntensity);

    // 3. Luces Dinámicas Adicionales (Point, Spot)
    int pixelLightCount = GetAdditionalLightsCount();
    for (int i = 0; i < pixelLightCount; ++i)
    {
        Light light = GetAdditionalLight(i, WorldPos);
        
        float addNdotL = dot(Normal, light.direction);
        float addHalfLambert = addNdotL * 0.5 + 0.5;
        
        float addLitFactor = smoothstep(0.5 - ShadowSmoothness, 0.5 + ShadowSmoothness, addHalfLambert);
        float addShadowFactor = smoothstep(0.0, ShadowSmoothness * 2.0, light.shadowAttenuation);
        
        float addIntensity = addLitFactor * addShadowFactor * light.distanceAttenuation;
        
        // Sumamos las luces dinámicas, afectadas por su propio color de luz
        finalColor += Albedo * light.color * addIntensity;
    }

    OutColor = finalColor;
#endif
}
#endif