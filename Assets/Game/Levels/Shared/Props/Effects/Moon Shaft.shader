// The moonlight's shaft (Props/Effects/Moonlight, the forest's Moonbeams and the boss intro's spotlight): additive light
// in a beam round the object's y axis, marched through along each line of sight, so it is strongest down its middle and
// fades to nothing at its sides, over the top share of its height and in front of whatever the line meets. Drawn on the
// far side of a closed volume (Moon shaft volume.asset: faces inward, capped at both ends), so every line that passes
// through the beam is drawn once, from outside and from inside, looking up into its top or down into its foot (the open
// tube it had showed a dark hollow ellipse at both ends, 2026-11-01). Only the stretch in front of what the eye sees
// counts (the depth texture), so a wall hides it and a figure standing in it hides what is behind. Fainter while the eye
// stands in it; slow faint streaks drift down it. Works in the object's own space, so a scaled shaft stays right. Its
// radius at each height is _RadiusTop at _Height and _RadiusBottom at 0.
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
                float3 dir = ray / max(length(ray), 1e-5);
                // How far the eye sees along this line before something hides it (the depth texture is in world metres).
                float2 uv = i.screen.xy / i.screen.w;
                float sceneEye = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float3 forwardWS = -UNITY_MATRIX_V[2].xyz;
                float3 dirWS = TransformObjectToWorldDir(dir, false);
                float scale = max(length(dirWS), 1e-5);
                float sceneOS = sceneEye / max(dot(dirWS / scale, forwardWS), 1e-3) / scale;

                // The stretch of the line inside the beam's bounds: the cylinder round its widest radius, cut at the
                // floor (y 0) and the top (_Height).
                float rMax = max(_RadiusTop, _RadiusBottom);
                float a = dot(dir.xz, dir.xz), b = dot(eye.xz, dir.xz), c = dot(eye.xz, eye.xz) - rMax * rMax;
                float disc = b * b - a * c;
                if (disc <= 0 || a < 1e-6) return 0;
                float root = sqrt(disc);
                float t0 = (-b - root) / a, t1 = (-b + root) / a;
                float dy = abs(dir.y) > 1e-5 ? dir.y : 1e-5;
                float ya = (0 - eye.y) / dy, yb = (_Height - eye.y) / dy;
                t0 = max(t0, min(ya, yb)); t1 = min(t1, max(ya, yb));
                t0 = max(t0, 0); t1 = min(t1, sceneOS);
                if (t1 <= t0) return 0;

                // Marched through: every step adds the light at its point, strongest on the axis and nothing at the
                // beam's edge, fading out over the top share of its height and dimming toward its foot, and fading in
                // just in front of whatever the line meets (so the floor and a figure standing in it take it softly,
                // with no hard line). A line straight through the middle sideways adds up to 1.
                const int STEPS = 24;
                float dt = (t1 - t0) / STEPS, sum = 0;
                [unroll] for (int k = 0; k < STEPS; k++)
                {
                    float t = t0 + (k + 0.5) * dt;
                    float3 q = eye + dir * t;
                    float h = saturate(q.y / max(_Height, 1e-3));
                    float r = lerp(_RadiusBottom, _RadiusTop, h);
                    float d2 = dot(q.xz, q.xz) / (r * r);
                    float dens = sqrt(saturate(1 - d2));
                    float top = smoothstep(0, 1, saturate((_Height - q.y) / max(_Height * _TopFade, 1e-3)));
                    float foot = lerp(0.5, 1, smoothstep(0, 1, saturate(q.y / max(_FootFade, 1e-3))));
                    float soft = saturate((sceneOS - t) * scale / max(_SoftFade, 1e-3));
                    sum += dens * top * foot * soft * lerp(0.7, 1, h) * dt / (1.5708 * r);
                }
                // Long looks down its length add up to more; eased so they glow brighter, never blow out.
                float glow = (1 - exp(-sum)) / 0.632;

                // Standing in it, it is a haze over everything seen: kept fainter than the column seen from outside.
                float eyeRadius = lerp(_RadiusBottom, _RadiusTop, saturate(eye.y / max(_Height, 1e-3)));
                glow *= lerp(0.3, 1, saturate(length(eye.xz) / eyeRadius));

                // Faint streaks running down it, fixed to the beam (by where across it the line passes), drifting slowly.
                float l = sqrt(a);
                float offset = dot(eye.xz, float2(dir.z, -dir.x)) / l;
                float s0 = -b / a;
                float y = clamp(eye.y + dir.y * max(s0, 0), 0, _Height);
                float across = offset / lerp(_RadiusBottom, _RadiusTop, y / max(_Height, 1e-3));
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
