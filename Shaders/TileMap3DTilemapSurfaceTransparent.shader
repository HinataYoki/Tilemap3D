Shader "TileMap3D/TilemapSurfaceTransparent"
{
    Properties
    {
        [PerRendererData] _MainTex("Tile Texture", 2D) = "white" {}
        _Color("Tint", Color) = (1, 1, 1, 1)
        [PerRendererData] _TileMap3DReceiveShadows("Receive Shadows", Float) = 0
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
            Name "TileMap3DSurfaceTransparent"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
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
            #pragma multi_compile_fog

            // 透明表面：屏幕空间阴影只覆盖不透明几何，回退级联采样。
            #define _SURFACE_TYPE_TRANSPARENT 1
            #include "TileMap3DSurfaceCommon.hlsl"

            half4 TileMap3DFragment(
                TileMap3DVaryings input,
                FRONT_FACE_TYPE frontFace : FRONT_FACE_SEMANTIC) : SV_Target
            {
                return TileMap3DFragmentShared(input, frontFace);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
