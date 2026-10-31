// The Dice Portal's vortex: an unlit, alpha-blended flipbook frame (Flick steps the frames through _BaseMap's offset), so
// its dark middle covers what lies behind it; light parts above 1 in the tint feed bloom.
Shader "LoomRoom/Portal Vortex"
{
    Properties
    {
        [MainTexture] _BaseMap ("Texture", 2D) = "white" {}
        [MainColor][HDR] _BaseColor ("Tint", Color) = (1,1,1,1)
        _Glow ("Glow of the light parts", Range(0, 4)) = 1.5
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Glow;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                half light = max(t.r, max(t.g, t.b));
                half3 c = t.rgb * _BaseColor.rgb * (1.0h + _Glow * light * light);
                return half4(c, t.a * _BaseColor.a);
            }
            ENDHLSL
        }
    }
}
