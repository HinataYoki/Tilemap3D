#ifndef TILEMAP3D_SURFACE_COMMON_INCLUDED
#define TILEMAP3D_SURFACE_COMMON_INCLUDED

// TileMap3D 原生 Tilemap 图层共享的受光管线：
// 结构体、顶点函数、Forward / Forward+ 兼容的光照累加与共用片元逻辑。
// 使用方在 include 前可定义：
//   TILEMAP3D_ALPHA_CLIP      —— 启用 _Cutoff Alpha 裁剪（Base 图层）。
//   _SURFACE_TYPE_TRANSPARENT —— 透明表面：走级联阴影采样并跳过 SSAO。

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

TEXTURE2D(_MainTex);
SAMPLER(sampler_MainTex);

// Cutout 与 Transparent 两个 Shader 共用同一 CBUFFER 布局；
// _Cutoff 仅在 TILEMAP3D_ALPHA_CLIP 下被消费，Transparent 中保持默认值即可。
CBUFFER_START(UnityPerMaterial)
    half4 _Color;
    half _Cutoff;
    half _TileMap3DReceiveShadows;
CBUFFER_END

struct TileMap3DAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 uv : TEXCOORD0;
    half4 color : COLOR;
};

struct TileMap3DVaryings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    half4 color : COLOR;
    half fogFactor : TEXCOORD1;
    float3 positionWS : TEXCOORD2;
    half3 normalWS : TEXCOORD3;
    half3 vertexLighting : TEXCOORD4;
};

TileMap3DVaryings TileMap3DVertex(TileMap3DAttributes input)
{
    TileMap3DVaryings output;
    VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
    output.positionCS = positionInputs.positionCS;
    output.uv = input.uv;
    output.color = input.color * _Color;
    output.fogFactor = ComputeFogFactor(output.positionCS.z);
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

// 主光 + 附加光的 Lambert 累加，兼容传统 Forward 与 Forward+（Cluster）光照循环。
half3 TileMap3DComputeLighting(TileMap3DVaryings input, half3 normalWS, float2 screenUV)
{
    half indirectOcclusion = 1.0h;
    half directOcclusion = 1.0h;
    #if defined(_SCREEN_SPACE_OCCLUSION) && !defined(_SURFACE_TYPE_TRANSPARENT)
    AmbientOcclusionFactor aoFactor = GetScreenSpaceAmbientOcclusion(screenUV);
    indirectOcclusion = aoFactor.indirectAmbientOcclusion;
    directOcclusion = aoFactor.directAmbientOcclusion;
    #endif

    half3 lighting = (SampleSH(normalWS) + input.vertexLighting) * indirectOcclusion;

    // 屏幕空间阴影贴图按屏幕 UV 采样，不能传级联阴影图集坐标。
    #if defined(_MAIN_LIGHT_SHADOWS_SCREEN) && !defined(_SURFACE_TYPE_TRANSPARENT)
    float4 shadowCoord = float4(screenUV, 0.0, 1.0);
    #else
    float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
    #endif

    Light mainLight = GetMainLight(shadowCoord);
    // TilemapRenderer 的 Chunk Pass 会把方向光的距离衰减置零；方向光不应发生距离衰减。
    half mainLightDistanceAttenuation = _MainLightPosition.w == 0.0
        ? 1.0h
        : mainLight.distanceAttenuation;
    lighting += mainLight.color
        * saturate(dot(normalWS, mainLight.direction))
        * mainLightDistanceAttenuation
        * lerp(1.0h, mainLight.shadowAttenuation, _TileMap3DReceiveShadows)
        * directOcclusion;

    #if defined(_ADDITIONAL_LIGHTS)
    // LIGHT_LOOP_BEGIN 在 Forward+ 下按名引用局部 inputData 完成分簇光源查询。
    InputData inputData = (InputData)0;
    inputData.positionWS = input.positionWS;
    inputData.normalizedScreenSpaceUV = screenUV;
    half4 shadowMask = half4(1.0h, 1.0h, 1.0h, 1.0h);
    uint pixelLightCount = GetAdditionalLightsCount();
    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = GetAdditionalLight(lightIndex, input.positionWS, shadowMask);
        lighting += light.color
            * saturate(dot(normalWS, light.direction))
            * light.distanceAttenuation
            * lerp(1.0h, light.shadowAttenuation, _TileMap3DReceiveShadows)
            * directOcclusion;
    LIGHT_LOOP_END
    #endif

    return lighting;
}

half4 TileMap3DFragmentShared(TileMap3DVaryings input, FRONT_FACE_TYPE frontFace)
{
    half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * input.color;
    #if defined(TILEMAP3D_ALPHA_CLIP)
    clip(color.a - _Cutoff);
    #else
    // 透明混合下全透明像素无贡献，提前裁剪省去光照与混合开销。
    clip(color.a - 0.004h);
    #endif

    // Cull Off 双面渲染：背面使用翻转法线计算光照。
    half3 normalWS = NormalizeNormalPerPixel(input.normalWS)
        * IS_FRONT_VFACE(frontFace, 1.0h, -1.0h);
    float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
    half3 lighting = TileMap3DComputeLighting(input, normalWS, screenUV);
    half4 result = half4(color.rgb * lighting, color.a);
    result.rgb = MixFog(result.rgb, input.fogFactor);
    return result;
}

// —— DepthOnly / DepthNormals 共用结构（仅 Cutout 图层需要） ——

struct TileMap3DDepthAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 uv : TEXCOORD0;
    half4 color : COLOR;
};

struct TileMap3DDepthVaryings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    half alpha : TEXCOORD1;
    half3 normalWS : TEXCOORD2;
};

TileMap3DDepthVaryings TileMap3DDepthVertex(TileMap3DDepthAttributes input)
{
    TileMap3DDepthVaryings output;
    output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
    output.uv = input.uv;
    output.alpha = input.color.a * _Color.a;
    output.normalWS = TransformObjectToWorldNormal(input.normalOS);
    return output;
}

void TileMap3DClipDepthAlpha(TileMap3DDepthVaryings input)
{
    half alpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a * input.alpha;
    clip(alpha - _Cutoff);
}

half TileMap3DDepthOnlyFragment(TileMap3DDepthVaryings input) : SV_Target
{
    TileMap3DClipDepthAlpha(input);
    return input.positionCS.z;
}

half4 TileMap3DDepthNormalsFragment(
    TileMap3DDepthVaryings input,
    FRONT_FACE_TYPE frontFace : FRONT_FACE_SEMANTIC) : SV_Target
{
    TileMap3DClipDepthAlpha(input);
    half3 normalWS = NormalizeNormalPerPixel(input.normalWS)
        * IS_FRONT_VFACE(frontFace, 1.0h, -1.0h);
    return half4(normalWS, 0.0);
}

#endif
