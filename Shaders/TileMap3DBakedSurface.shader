Shader "TileMap3D/BakedSurface"
{
    Properties
    {
        [NoScaleOffset] _BaseMap("Baked Tilemap", 2D) = "white" {}
        _BaseColor("Color", Color) = (1, 1, 1, 1)
        _AmbientStrength("Ambient Strength", Range(0, 1)) = 0.6
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }
        LOD 200

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half _AmbientStrength;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            half3 normalWS : TEXCOORD1;
            float2 uv : TEXCOORD2;
            half fogFactor : TEXCOORD3;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        Varyings TileMap3DSurfaceVertex(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
            VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);
            output.positionCS = positionInputs.positionCS;
            output.positionWS = positionInputs.positionWS;
            output.normalWS = normalInputs.normalWS;
            output.uv = input.uv;
            output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
            return output;
        }

        half4 TileMap3DSurfaceFragment(Varyings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _BaseColor.rgb;
            half3 normalWS = normalize(input.normalWS);
            float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
            Light mainLight = GetMainLight(shadowCoord);
            half mainNdotL = saturate(dot(normalWS, mainLight.direction));
            half3 lighting = _AmbientStrength;
            lighting += mainLight.color
                * mainNdotL
                * mainLight.distanceAttenuation
                * mainLight.shadowAttenuation;

            #ifdef _ADDITIONAL_LIGHTS
            uint lightCount = GetAdditionalLightsCount();
            for (uint lightIndex = 0u; lightIndex < lightCount; lightIndex++)
            {
                Light light = GetAdditionalLight(lightIndex, input.positionWS);
                half ndotl = saturate(dot(normalWS, light.direction));
                lighting += light.color
                    * ndotl
                    * light.distanceAttenuation
                    * light.shadowAttenuation;
            }
            #endif

            return half4(MixFog(albedo * lighting, input.fogFactor), 1.0h);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex TileMap3DSurfaceVertex
            #pragma fragment TileMap3DSurfaceFragment
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }

    FallBack Off
}
