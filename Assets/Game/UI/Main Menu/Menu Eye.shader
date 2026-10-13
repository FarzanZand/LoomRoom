// A burning eye for the main menu's Dungeon Master, drawn on a camera-facing quad: an angular, frowning slit (a sharp
// outer corner set high, a top edge falling to the nose, a flat bottom, a blunt inner end), pale gold inside, orange at
// its edge, with a short warm glow round it. _Mirror is 1 for the eye on the screen's left and -1 for the right one, so
// the pair mirror each other. Additive.
Shader "LoomRoom/Menu Eye"
{
    Properties
    {
        [HDR] _Core ("Core", Color) = (5, 4.2, 2.2, 1)
        [HDR] _Rim ("Rim", Color) = (4, 1.6, .25, 1)
        [HDR] _Glow ("Glow", Color) = (.8, .28, .04, 1)
        _Slit ("Half width, half height", Vector) = (.55, .24, 0, 0)
        _Tilt ("Tilt (radians)", Float) = 0
        _InnerHeight ("Top edge height at the inner end (0-1)", Range(0, 1)) = .36
        _OuterBottom ("Bottom of the outer edge (0-1 across)", Range(.5, 1)) = .8
        _Softness ("Edge softness", Range(.002, .2)) = .025
        _GlowSize ("Glow reach", Range(.01, 1)) = .14
        _Intensity ("Intensity", Float) = 1
        _Mirror ("Mirror (1 or -1)", Float) = 1
        _DepthPull ("Depth pull toward the camera (units)", Float) = 1.5
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+100" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Core, _Rim, _Glow;
                float4 _Slit;
                float _Tilt, _InnerHeight, _OuterBottom, _Softness, _GlowSize, _Intensity, _Mirror, _DepthPull;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                // Billboard: the quad's centre in view space, its corners laid out on the screen plane.
                float3 centre = TransformWorldToView(TransformObjectToWorld(float3(0, 0, 0)));
                float3 scale = float3(length(UNITY_MATRIX_M._m00_m10_m20), length(UNITY_MATRIX_M._m01_m11_m21), 1);
                centre.xy += v.positionOS.xy * scale.xy;
                o.positionCS = TransformWViewToHClip(centre);
                // Depth-tested as if a little closer to the camera, so the face and hood round the eye don't hide it,
                // while it stays drawn where it is (it sits on the eye from every side).
                float4 pulled = TransformWViewToHClip(centre + float3(0, 0, _DepthPull));
                o.positionCS.z = pulled.z / pulled.w * o.positionCS.w;
                o.uv = v.uv * 2 - 1;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // Local space: +x toward the outer corner (away from the nose), +y up.
                float2 q = float2(-i.uv.x * _Mirror, i.uv.y);
                float s = sin(_Tilt), c = cos(_Tilt);
                q = float2(c * q.x - s * q.y, s * q.x + c * q.y);
                float W = _Slit.x, H = _Slit.y;
                float u = (q.x / W + 1) * .5;   // 0 at the inner end, 1 at the outer corner
                float v = (q.y / H + 1) * .5;   // 0 at the bottom, 1 at the top
                // Signed distances to the four edges, positive inside, in local units.
                float bottomY = -H + 2 * H * .14 * saturate(1 - u / .35);   // rises into the inner tip
                float dBottom = q.y - bottomY;
                float topY = -H + 2 * H * (_InnerHeight + (1 - _InnerHeight) * u);
                float dTop = (topY - q.y) / sqrt(1 + pow((1 - _InnerHeight) * H / W, 2));
                float outerX = -W + 2 * W * (_OuterBottom + (1 - _OuterBottom) * v);
                float dOuter = (outerX - q.x) / sqrt(1 + pow((1 - _OuterBottom) * W / H, 2));
                float dInner = q.x + W;
                // A soft minimum rounds the blunt inner end and the bottom corners a little.
                // A soft minimum of all four edges rounds every corner, the outer one too.
                float k = _Softness * 2;
                float d = -k * log(exp(-dInner / k) + exp(-dBottom / k) + exp(-dTop / k) + exp(-dOuter / k));

                float body = smoothstep(-_Softness * .6, _Softness * .6, d);
                float core = smoothstep(.0, .1, d);
                float glow = exp(-max(-d, 0) / _GlowSize) * (1 - body);
                glow *= saturate((1 - abs(i.uv.x)) * 4) * saturate((1 - abs(i.uv.y)) * 4);   // fade out before the quad's edge
                half3 col = lerp(_Rim.rgb, _Core.rgb, core) * body + _Glow.rgb * glow;
                return half4(col * _Intensity, 1);
            }
            ENDHLSL
        }
    }
}
