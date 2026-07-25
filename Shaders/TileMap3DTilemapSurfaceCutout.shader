Shader "TileMap3D/TilemapSurfaceCutout"
{
    Properties
    {
        [PerRendererData] _MainTex("Tile Texture", 2D) = "white" {}
        _Color("Tint", Color) = (1, 1, 1, 1)
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.1
        [PerRendererData] _TileMap3DReceiveShadows("Receive Shadows", Float) = 0
        [HideInInspector] _TileMap3DPreviewOutside("Preview Outside", Float) = 0
        [HideInInspector] _TileMap3DPreviewRect("Preview Rect", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
        }

        Pass
        {
            Name "TileMap3DSurfaceCutout"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite On
            ZTest LEqual
            Offset -1, -1

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex TileMap3DVertex
            #pragma fragment TileMap3DFragment
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Cutoff;
                half _TileMap3DReceiveShadows;
                half _TileMap3DPreviewOutside;
                float4 _TileMap3DPreviewRect;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                half fogFactor : TEXCOORD1;
                float2 positionOS : TEXCOORD2;
                float3 positionWS : TEXCOORD3;
                half3 normalWS : TEXCOORD4;
                half3 vertexLighting : TEXCOORD5;
            };

            Varyings TileMap3DVertex(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.uv = input.uv;
                output.color = input.color * _Color;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                output.positionOS = input.positionOS.xy;
                output.positionWS = positionInputs.positionWS;
                half3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.normalWS = normalWS;
                #if defined(_ADDITIONAL_LIGHTS_VERTEX)
                output.vertexLighting = VertexLighting(positionInputs.positionWS, normalWS);
                #else
                output.vertexLighting = half3(0.0h, 0.0h, 0.0h);
                #endif
                return output;
            }

            half4 ShadeTile(half4 color, Varyings input)
            {
                half3 normalWS = NormalizeNormalPerPixel(input.normalWS);
                half3 lighting = SampleSH(normalWS) + input.vertexLighting;
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                // TilemapRenderer 的 Chunk Pass 会把方向光的距离衰减置零；方向光不应发生距离衰减。
                half mainLightDistanceAttenuation = _MainLightPosition.w == 0.0
                    ? 1.0h
                    : mainLight.distanceAttenuation;
                lighting += mainLight.color
                    * saturate(dot(normalWS, mainLight.direction))
                    * mainLightDistanceAttenuation
                    * lerp(1.0h, mainLight.shadowAttenuation, _TileMap3DReceiveShadows);

                #if defined(_ADDITIONAL_LIGHTS)
                uint lightCount = GetAdditionalLightsCount();
                for (uint lightIndex = 0u; lightIndex < lightCount; lightIndex++)
                {
                    Light light = GetAdditionalLight(lightIndex, input.positionWS);
                    lighting += light.color
                        * saturate(dot(normalWS, light.direction))
                        * light.distanceAttenuation
                        * lerp(1.0h, light.shadowAttenuation, _TileMap3DReceiveShadows);
                }
                #endif

                half4 result = half4(color.rgb * lighting, color.a);
                result.rgb = MixFog(result.rgb, input.fogFactor);
                return result;
            }

            half4 TileMap3DFragment(Varyings input) : SV_Target
            {
                if (_TileMap3DPreviewOutside > 0.5h
                    && input.positionOS.x >= _TileMap3DPreviewRect.x
                    && input.positionOS.y >= _TileMap3DPreviewRect.y
                    && input.positionOS.x <= _TileMap3DPreviewRect.z
                    && input.positionOS.y <= _TileMap3DPreviewRect.w)
                {
                    clip(-1.0h);
                }

                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * input.color;
                clip(color.a - _Cutoff);
                return ShadeTile(color, input);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
