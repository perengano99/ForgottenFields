// === URP TRIPLANAR TOON SHADER ===
Shader "Custom/Terrain/TriplanarArray"
{
    Properties
    {
        _MainTexArray ("Texture Array", 2DArray) = "white" {}
        _TileScale ("Tile Scale", Float) = 1.0
        _ToonThreshold ("Toon Threshold", Range(0, 1)) = 0.1
        _ShadowIntensity ("Shadow Intensity", Range(0, 1)) = 0.4
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // Note: Dependencia de URP Core
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float3 uv : TEXCOORD0; // Z contiene el MaterialID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD2;
                float materialID : TEXCOORD1;
            };

            TEXTURE2D_ARRAY(_MainTexArray);
            SAMPLER(sampler_MainTexArray);
            
            CBUFFER_START(UnityPerMaterial)
                float _TileScale;
                float _ToonThreshold;
                float _ShadowIntensity;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.materialID = input.uv.z;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 pos = input.positionWS * _TileScale;
                float3 normal = normalize(input.normalWS);
                
                // Note: Cálculo de pesos triplanares estritos (Sharp blending)
                float3 blend = abs(normal);
                blend /= (blend.x + blend.y + blend.z);
                
                float matID = input.materialID;

                // Note: Muestreo Triplanar desde Texture2DArray
                half4 texX = SAMPLE_TEXTURE2D_ARRAY(_MainTexArray, sampler_MainTexArray, float2(pos.z, pos.y), matID);
                half4 texY = SAMPLE_TEXTURE2D_ARRAY(_MainTexArray, sampler_MainTexArray, float2(pos.x, pos.z), matID);
                half4 texZ = SAMPLE_TEXTURE2D_ARRAY(_MainTexArray, sampler_MainTexArray, float2(pos.x, pos.y), matID);

                half4 albedo = texX * blend.x + texY * blend.y + texZ * blend.z;

                // Note: Iluminación Toon Básica (1 Paso)
                Light mainLight = GetMainLight();
                half NdotL = saturate(dot(normal, mainLight.direction));
                half shadowAttenuation = mainLight.shadowAttenuation;
                
                half toonStep = step(_ToonThreshold, NdotL * shadowAttenuation);
                half3 lighting = lerp(mainLight.color * _ShadowIntensity, mainLight.color, toonStep);

                // Note: Ambiental / Luz de Cielo
                half3 ambient = SampleSH(normal);

                return half4(albedo.rgb * (lighting + ambient), albedo.a);
            }
            ENDHLSL
        }
        
        // Note: Requerido para proyectar sombras sobre otros objetos.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }

            ColorMask 0
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformWorldToHClip(TransformObjectToWorld(input.positionOS.xyz));
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}