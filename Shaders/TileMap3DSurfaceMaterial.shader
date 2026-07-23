Shader "TileMap3D/SurfaceMaterial"
{
    Properties
    {
        [NoScaleOffset] _CellData("Cell Data", 2DArray) = "" {}
        [NoScaleOffset] _TransformData("Transform Data", 2DArray) = "" {}
        [NoScaleOffset] _ColorData("Color Data", 2DArray) = "" {}
        [NoScaleOffset] _SpriteLookup("Sprite Lookup", 2D) = "black" {}
        [NoScaleOffset] _TileTexture0("Tile Texture 0", 2D) = "white" {}
        [NoScaleOffset] _TileTexture1("Tile Texture 1", 2D) = "white" {}
        [NoScaleOffset] _TileTexture2("Tile Texture 2", 2D) = "white" {}
        [NoScaleOffset] _TileTexture3("Tile Texture 3", 2D) = "white" {}
        [NoScaleOffset] _TileTexture4("Tile Texture 4", 2D) = "white" {}
        [NoScaleOffset] _TileTexture5("Tile Texture 5", 2D) = "white" {}
        [NoScaleOffset] _TileTexture6("Tile Texture 6", 2D) = "white" {}
        [NoScaleOffset] _TileTexture7("Tile Texture 7", 2D) = "white" {}
        _Tint("Tint", Color) = (1, 1, 1, 1)
        [HideInInspector] _SurfaceRect("Surface Rect", Vector) = (0, 0, 0, 0)
        [HideInInspector] _SurfaceNormalWS("Surface Normal WS", Vector) = (0, 1, 0, 0)
        [HideInInspector] _CellDimensions("Cell Dimensions", Vector) = (1, 1, 0, 1)
        [HideInInspector] _PlaneTolerance("Plane Tolerance", Float) = 0.001
        [HideInInspector] _NormalThreshold("Normal Threshold", Float) = 0.9
        [HideInInspector] _SpriteCount("Sprite Count", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "TileMap3DSurfaceMaterial"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Back
            ZWrite Off
            ZTest LEqual
            Offset -1, -1

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex TileMap3DVertex
            #pragma fragment TileMap3DFragment
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D_ARRAY(_CellData);
            SAMPLER(sampler_CellData);
            TEXTURE2D_ARRAY(_TransformData);
            SAMPLER(sampler_TransformData);
            TEXTURE2D_ARRAY(_ColorData);
            SAMPLER(sampler_ColorData);
            TEXTURE2D(_SpriteLookup);
            SAMPLER(sampler_SpriteLookup);
            TEXTURE2D(_TileTexture0);
            SAMPLER(sampler_TileTexture0);
            TEXTURE2D(_TileTexture1);
            SAMPLER(sampler_TileTexture1);
            TEXTURE2D(_TileTexture2);
            SAMPLER(sampler_TileTexture2);
            TEXTURE2D(_TileTexture3);
            SAMPLER(sampler_TileTexture3);
            TEXTURE2D(_TileTexture4);
            SAMPLER(sampler_TileTexture4);
            TEXTURE2D(_TileTexture5);
            SAMPLER(sampler_TileTexture5);
            TEXTURE2D(_TileTexture6);
            SAMPLER(sampler_TileTexture6);
            TEXTURE2D(_TileTexture7);
            SAMPLER(sampler_TileTexture7);

            CBUFFER_START(UnityPerMaterial)
                float4x4 _SurfaceWorldToLocal;
                float4 _SurfaceRect;
                float4 _SurfaceNormalWS;
                float4 _CellDimensions;
                float4 _Tint;
                float _PlaneTolerance;
                float _NormalThreshold;
                float _SpriteCount;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
            };

            Varyings TileMap3DVertex(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 SampleTileTexture(int textureSlot, float2 uv)
            {
                if (textureSlot == 0)
                {
                    return SAMPLE_TEXTURE2D(_TileTexture0, sampler_TileTexture0, uv);
                }

                if (textureSlot == 1)
                {
                    return SAMPLE_TEXTURE2D(_TileTexture1, sampler_TileTexture1, uv);
                }

                if (textureSlot == 2)
                {
                    return SAMPLE_TEXTURE2D(_TileTexture2, sampler_TileTexture2, uv);
                }

                if (textureSlot == 3)
                {
                    return SAMPLE_TEXTURE2D(_TileTexture3, sampler_TileTexture3, uv);
                }

                if (textureSlot == 4)
                {
                    return SAMPLE_TEXTURE2D(_TileTexture4, sampler_TileTexture4, uv);
                }

                if (textureSlot == 5)
                {
                    return SAMPLE_TEXTURE2D(_TileTexture5, sampler_TileTexture5, uv);
                }

                if (textureSlot == 6)
                {
                    return SAMPLE_TEXTURE2D(_TileTexture6, sampler_TileTexture6, uv);
                }

                if (textureSlot == 7)
                {
                    return SAMPLE_TEXTURE2D(_TileTexture7, sampler_TileTexture7, uv);
                }

                return half4(0, 0, 0, 0);
            }

            half4 TileMap3DFragment(Varyings input) : SV_Target
            {
                float3 surfaceLocal = mul(
                    _SurfaceWorldToLocal,
                    float4(input.positionWS, 1.0)).xyz;
                clip(_PlaneTolerance - abs(surfaceLocal.y));
                clip(dot(normalize(input.normalWS), normalize(_SurfaceNormalWS.xyz))
                    - _NormalThreshold);
                clip(surfaceLocal.x - _SurfaceRect.x);
                clip(_SurfaceRect.z - surfaceLocal.x);
                clip(surfaceLocal.z - _SurfaceRect.y);
                clip(_SurfaceRect.w - surfaceLocal.z);

                float2 cellPosition = (surfaceLocal.xz - _SurfaceRect.xy) / _CellDimensions.w;
                int2 cell = (int2)floor(cellPosition);
                if (cell.x < 0 || cell.y < 0
                    || cell.x >= (int)_CellDimensions.x
                    || cell.y >= (int)_CellDimensions.y)
                {
                    discard;
                }

                float2 tileUv = frac(cellPosition);
                half4 result = half4(0, 0, 0, 0);
                [loop]
                for (int layer = 0; layer < (int)_CellDimensions.z; layer++)
                {
                    float2 dataUv = float2(
                        (cell.x + 0.5) / _CellDimensions.x,
                        (cell.y + 0.5) / _CellDimensions.y);
                    float4 cellData = SAMPLE_TEXTURE2D_ARRAY_LOD(
                        _CellData,
                        sampler_CellData,
                        dataUv,
                        layer,
                        0);
                    if (cellData.a < 0.5)
                    {
                        continue;
                    }

                    int spriteIndex = (int)round(cellData.r * 255.0)
                        + ((int)round(cellData.g * 255.0) << 8);
                    if (spriteIndex <= 0 || spriteIndex >= (int)_SpriteCount)
                    {
                        continue;
                    }

                    float4 encodedTransform = SAMPLE_TEXTURE2D_ARRAY_LOD(
                        _TransformData,
                        sampler_TransformData,
                        dataUv,
                        layer,
                        0);
                    float4 tileTransform = (encodedTransform * 255.0 - 128.0) / 127.0;
                    float2 centeredUv = tileUv - 0.5;
                    float2 transformedUv = float2(
                        dot(tileTransform.xy, centeredUv),
                        dot(tileTransform.zw, centeredUv)) + 0.5;

                    float lookupX = (spriteIndex + 0.5) / _SpriteCount;
                    float4 spriteRect = SAMPLE_TEXTURE2D_LOD(
                        _SpriteLookup,
                        sampler_SpriteLookup,
                        float2(lookupX, 0.25),
                        0);
                    float4 spriteMeta = SAMPLE_TEXTURE2D_LOD(
                        _SpriteLookup,
                        sampler_SpriteLookup,
                        float2(lookupX, 0.75),
                        0);
                    float2 spriteUv = lerp(spriteRect.xy, spriteRect.zw, transformedUv);
                    half4 tileColor = SampleTileTexture((int)round(spriteMeta.x), spriteUv);
                    tileColor *= SAMPLE_TEXTURE2D_ARRAY_LOD(
                        _ColorData,
                        sampler_ColorData,
                        dataUv,
                        layer,
                        0);
                    tileColor *= _Tint;
                    result.rgb = tileColor.rgb * tileColor.a
                        + result.rgb * (1.0h - tileColor.a);
                    result.a = tileColor.a + result.a * (1.0h - tileColor.a);
                }

                clip(result.a - 0.001h);
                // 多层合成在这里保存的是预乘颜色；输出前还原为普通透明颜色，
                // 才能与目标 Mesh 的 SrcAlpha 混合保持半透明 Tile 的亮度。
                result.rgb = MixFog(result.rgb / max(result.a, 0.001h), input.fogFactor);
                return result;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
