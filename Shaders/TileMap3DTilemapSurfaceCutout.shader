Shader "TileMap3D/TilemapSurfaceCutout"
{
    Properties
    {
        [PerRendererData] _MainTex("Tile Texture", 2D) = "white" {}
        _Color("Tint", Color) = (1, 1, 1, 1)
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5
        [PerRendererData] _TileMap3DReceiveShadows("Receive Shadows", Float) = 0
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
            #pragma target 3.5
            #pragma vertex TileMap3DVertex
            #pragma fragment TileMap3DFragment
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _FORWARD_PLUS _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog

            #define TILEMAP3D_ALPHA_CLIP 1
            #include "TileMap3DSurfaceCommon.hlsl"

            half4 TileMap3DFragment(
                TileMap3DVaryings input,
                FRONT_FACE_TYPE frontFace : FRONT_FACE_SEMANTIC) : SV_Target
            {
                return TileMap3DFragmentShared(input, frontFace);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Off
            Offset -1, -1

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex TileMap3DDepthVertex
            #pragma fragment TileMap3DDepthOnlyFragment

            #define TILEMAP3D_ALPHA_CLIP 1
            #include "TileMap3DSurfaceCommon.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Off
            Offset -1, -1

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex TileMap3DDepthVertex
            #pragma fragment TileMap3DDepthNormalsFragment

            #define TILEMAP3D_ALPHA_CLIP 1
            #include "TileMap3DSurfaceCommon.hlsl"
            ENDHLSL
        }
    }

    FallBack Off
}
