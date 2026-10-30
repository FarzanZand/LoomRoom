// The moonlight's shaft (Props/Effects/Moonlight and the forest's Moonbeams): additive light in a tube round the object's
// y axis, as bright as the length of beam the eye looks through, so it is strongest down its middle and fades to nothing
// at its sides (it read as a flat pale box with one colour on every face). Only the far wall of the tube is drawn (the
// mesh, Moon shaft.fbx, faces inward: front faces are the far wall from outside and every wall from inside), so each
// pixel is lit once, from outside and from inside alike; only the stretch of beam in front of what the eye sees counts
// (the depth texture), so a wall hides it and a figure standing in it hides what is behind. It fades in from the top and
// out toward the floor, is fainter while the eye stands in it, and slow faint streaks drift down it. Works in the
// object's own space, so a scaled shaft stays right. The tube's radius at each height is _RadiusTop at _Height and _RadiusBottom at 0.
Shader "LoomRoom/Moon Shaft"
{
    Properties
    {
        [MainColor][HDR] _BaseColor ("Colour", Color) = (0.16, 0.21, 0.34, 1)
        _Intensity ("Intensity", Float) = 1.6
        _Height ("Height", Float) = 6
        _RadiusTop ("Radius at top", Float) = 0.75
        _RadiusBottom ("Radius at foot", Float) = 1.05
        _TopFade ("Top fade (share of height)", Range(0, 1)) = 0.2
        _FootFade ("Foot fade (metres)", Float) = 0.9
        _SoftFade ("Fade into what stands in it (metres)", Float) = 0.35
        _Streaks ("Streaks", Range(0, 1)) = 0.12
        _StreakSpeed ("Streak drift", Float) = 0.12
        _Bands ("Brightness steps (0 = smooth)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        ZTest Always
        Cull Back
        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Intensity;
                float _Height, _RadiusTop, _RadiusBottom, _TopFade, _FootFade, _SoftFade, _Streaks, _StreakSpeed, _Bands;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionOS : TEXCOORD0; float4 screen : TEXCOORD1; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionOS = i.positionOS.xyz;
                o.screen = ComputeScreenPos(p.positionCS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 eye = TransformWorldToObject(GetCameraPositionWS());
                float3 ray = i.positionOS - eye;
                float rayLen = max(length(ray), 1e-5);
                float3 dir = ray / rayLen;
                // Where the line of sight passes the axis, and the stretch of it that lies inside the tube there.
                float2 flat = dir.xz;
                float l2 = max(dot(flat, flat), 1e-6);
                float l = sqrt(l2);
                float offset = dot(eye.xz, float2(flat.y, -flat.x)) / l; // signed distance of the sight line from the axis
                float s0 = -dot(eye.xz, flat) / l2;                       // how far along it the sight line passes closest
                float y = clamp(eye.y + dir.y * max(s0, 0), 0, _Height);
                float h = y / max(_Height, 1e-3);
                float radius = lerp(_RadiusBottom, _RadiusTop, h);
                float across = offset / radius;
                float inside = saturate(1 - across * across);
                float half_ = sqrt(inside) * radius / l;
                // Only the part of that stretch in front of what the eye sees counts: a wall in front hides it all, a
                // figure standing in the light hides what is behind it. The depth texture is in world metres.
                float2 uv = i.screen.xy / i.screen.w;
                float sceneEye = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float3 forwardWS = -UNITY_MATRIX_V[2].xyz;
                float3 dirWS = normalize(TransformObjectToWorldDir(dir, false));
                float scale = length(TransformObjectToWorldDir(dir, false));
                float sceneOS = sceneEye / max(dot(dirWS, forwardWS), 1e-3) / max(scale, 1e-5);
                float enter = max(s0 - half_, 0), leave = min(s0 + half_, sceneOS);
                float seen = saturate((leave - enter) / max(2 * half_, 1e-5));
                // Softly into the floor or a table top rather than cut by it.
                seen *= saturate((sceneOS - enter) * scale / max(_SoftFade, 1e-3));
                float glow = sqrt(inside) * seen;

                // Fades in from the top and out toward the floor.
                float top = saturate((_Height - y) / max(_Height * _TopFade, 1e-3));
                float foot = saturate(y / max(_FootFade, 1e-3));
                glow *= top * (0.5 + 0.5 * foot) * lerp(0.7, 1, h);
                // Standing in it, it is a haze over everything seen: kept fainter than the column seen from outside.
                float eyeRadius = lerp(_RadiusBottom, _RadiusTop, saturate(eye.y / max(_Height, 1e-3)));
                glow *= lerp(0.3, 1, saturate(length(eye.xz) / eyeRadius));

                // Faint streaks running down it, fixed to the beam (by where across it the eye looks), drifting slowly.
                float streak = sin(across * 9.0 + y * 0.6 - _Time.y * _StreakSpeed * 6.0) * 0.5 + 0.5;
                streak *= sin(across * 23.0 - y * 1.3 + _Time.y * _StreakSpeed * 4.0) * 0.5 + 0.5;
                glow *= 1 - _Streaks + _Streaks * 2 * streak;

                if (_Bands > 0.5) glow = floor(glow * _Bands + 0.5) / _Bands;
                return half4(_BaseColor.rgb * _Intensity * glow, 1);
            }
            ENDHLSL
        }
    }
}
