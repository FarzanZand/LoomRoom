// A see-through particle (texture x colour x the particle's own colour, alpha blended, like URP's Particles/Unlit)
// drawn after the camera filter (the Retro Screen pass), so noir, VHS or any other look leaves its colours alone: the
// Pianist's notes. Its one pass is tagged LoomAfterFilter, which the renderer's own passes never draw; AfterFilterPass
// draws it once Retro Screen is done. The world still hides it: what lies behind the scene's depth (walls, props,
// enemies) is cut away here, and the first-person arms by the depth they left in the buffer.
Shader "LoomRoom/After Filter Particle"
{
    Properties
    {
        [MainTexture] _BaseMap ("Texture", 2D) = "white" {}
        [MainColor] _BaseColor ("Colour", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }

        Pass
        {
            Name "After Filter"
            Tags { "LightMode" = "LoomAfterFilter" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off ZTest LEqual Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; float eye : TEXCOORD1; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 world = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(world);
                o.eye = -TransformWorldToView(world).z;
                o.color = v.color;
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                // Behind the world (the depth the scene left before the arms were drawn): hidden.
                float2 screen = GetNormalizedScreenSpaceUV(i.positionCS);
                float scene = LinearEyeDepth(SampleSceneDepth(screen), _ZBufferParams);
                clip(scene - i.eye);
                half4 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor * i.color;
                clip(c.a - 0.004);
                return c;
            }
            ENDHLSL
        }
    }
}
