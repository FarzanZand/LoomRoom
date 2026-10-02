// Per-object pixelation twin of LoomRoom/Pixel Lit (same properties, same lighting). Its colour pass
// is tagged LoomPixelObject, so the normal render skips it and PixelObjectFeature draws it into a
// low-res buffer that is composited back with depth. No depth passes, so no full-res silhouette.
// Materials are made at runtime by PixelLook; generated from PixelLit.shader, keep them in step.
Shader "Hidden/LoomRoom/Pixel Lit Object"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        [Toggle(_ALPHATEST_ON)] _AlphaClip("Alpha Clip", Float) = 0
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
        [Toggle] _DitherAlpha("Dither Alpha (transparency as screen-door)", Float) = 0

        [Header(Texels)]
        [Enum(None, 0, Texture Texels, 1, World Grid, 2, Object Grid, 3)] _SnapMode("Snap Mode", Float) = 2
        _TexelsPerUnit("Texels Per Unit (grid modes)", Float) = 16
        _BaseTexels("Base Texels (texture mode, 0 = native)", Float) = 0

        [Header(Surface)]
        [NoScaleOffset] _DetailMap("Detail Pattern (grey, 0.5 = no change)", 2D) = "grey" {}
        _DetailStrength("Detail Strength", Range(0, 1)) = 0
        _TexelNoise("Texel Noise", Range(0, 0.3)) = 0.04

        [Header(Light)]
        _LightSteps("Light Steps (0 = smooth)", Range(0, 16)) = 5
        _BandDither("Band Dither", Range(0, 1)) = 0.6
        _ShadeColor("Shade Tint", Color) = (1, 1, 1, 1)
        _AmbientStrength("Ambient Strength", Range(0, 2)) = 1
        _Gloss("Gloss", Range(0, 2)) = 0
        _GlossSize("Gloss Size", Range(0.001, 0.3)) = 0.04

        [Header(Emission)]
        [HDR] _EmissionColor("Emission", Color) = (0, 0, 0, 1)
        [NoScaleOffset] _EmissionMap("Emission Map", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "LoomPixelObject" }
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local_fragment _ALPHATEST_ON

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_instancing

            #define PIXEL_OBJECT_PASS
            #include "PixelLitForward.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing

            #include "PixelLitInput.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                #if defined(_ALPHATEST_ON)
                    clip(AlphaOf(input.uv) - _Cutoff);
                #endif
                if (_DitherAlpha > 0) clip(-1); // see-through things cast no shadow
                return 0;
            }
            ENDHLSL
        }


    }
    FallBack Off
}
