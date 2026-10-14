// Rolling smoke for the main menu: a sheet of swirling, domain-warped noise drawn on a large quad. Several sheets at
// different depths make the fog bank; each fades out at its edges, toward its top, and where it meets geometry.
Shader "LoomRoom/Menu Fog"
{
    Properties
    {
        _Color ("Colour", Color) = (.45, .55, .55, 1)
        _Density ("Density", Range(0, 2)) = .6
        _Scale ("Noise scale", Vector) = (3, 1.6, 0, 0)
        _Drift ("Drift (uv per second)", Vector) = (.02, .004, 0, 0)
        _Swirl ("Swirl", Range(0, 8)) = 3.5
        _Churn ("Churn speed", Range(0, 1)) = .08
        _Threshold ("Thinness", Range(0, 1)) = .35
        _Softness ("Softness", Range(.01, 1)) = .45
        _EdgeFade ("Edge fade (x, y)", Vector) = (.18, .12, 0, 0)
        _TopFade ("Top fade", Range(0, 1)) = .55
        _DepthFade ("Soft contact distance", Float) = 6
        _Seed ("Seed", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Density, _Swirl, _Churn, _Threshold, _Softness, _TopFade, _DepthFade, _Seed;
                float4 _Scale, _Drift, _EdgeFade;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float4 screenPos : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.screenPos = ComputeScreenPos(o.positionCS);
                return o;
            }

            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3 - 2 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), u.x), lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), u.x), u.y);
            }
            float fbm(float2 p)
            {
                float v = 0, a = .5;
                const float2x2 m = float2x2(1.6, 1.2, -1.2, 1.6);
                for (int i = 0; i < 5; i++) { v += a * noise(p); p = mul(m, p); a *= .5; }
                return v;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y;
                float2 p = i.uv * _Scale.xy + _Drift.xy * t * _Scale.xy + _Seed * 17.31;
                // Warp the noise by itself twice: smoke that curls and rolls rather than slides.
                float2 q = float2(fbm(p + float2(0, t * _Churn)), fbm(p + float2(5.2, 1.3) - t * _Churn * .7));
                float2 r = float2(fbm(p + _Swirl * q + float2(1.7, 9.2) + t * _Churn * 1.3), fbm(p + _Swirl * q + float2(8.3, 2.8) - t * _Churn));
                float f = fbm(p + _Swirl * r);
                float smoke = smoothstep(_Threshold, _Threshold + _Softness, f);
                // Fade at the sheet's edges and toward its top.
                float2 e = min(i.uv, 1 - i.uv);
                float edge = smoothstep(0, _EdgeFade.x, e.x) * smoothstep(0, _EdgeFade.y, e.y);
                float top = lerp(1, 1 - smoothstep(.15, 1, i.uv.y), _TopFade);
                // Soft where the sheet meets geometry.
                float2 suv = i.screenPos.xy / i.screenPos.w;
                float scene = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float self = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                float contact = saturate((scene - self) / max(_DepthFade, 1e-3));
                half a = saturate(smoke * edge * top * contact * _Density);
                // Thicker smoke is a little brighter, as if lit from inside the haze.
                half3 c = _Color.rgb * (.75 + .5 * f);
                return half4(c, a * _Color.a);
            }
            ENDHLSL
        }
    }
}
