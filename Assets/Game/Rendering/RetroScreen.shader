// Full-screen pass for the room's low-res look: snaps the image to a coarse pixel grid and
// reduces each colour channel with an ordered (Bayer) dither. Driven by globals from ScreenManager.
// With a camera filter on (ScreenManager > Camera filter, a ScreenFilterLook) the same pass also bends the screen,
// grades or replaces the colours, and lays scanlines, a shadow mask, tape bleed, a vignette and grain over it.
// Standard (no filter) takes exactly the old path.
Shader "Hidden/LoomRoom/Retro Screen"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Blend Off Cull Off

        Pass
        {
            Name "Retro Screen"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _RetroPixelLines;  // vertical resolution of the pixel grid, 0 = full resolution
            float _RetroColorLevels; // levels per channel, below 2 = no banding
            float _RetroDither;      // 0..1
            float _RetroStrength;    // 0..1; pixels shrink back to full resolution and banding fades out

            // Camera filter (all zero = none)
            float _RetroFilterOn;
            float _RetroBayer;        // 2, 4 or 8
            float _RetroDitherScale;  // grid pixels per dither cell
            float _RetroBrightDither; // 1 = dither the brightness only (a dark checker over the colour, no colour speckle)
            float _RetroLift;         // Mono and Palette: brightness curve, below 1 lifts the darks (0 = 1)
            float4 _RetroShade;       // dither band (1 = whole step, less = only the middle of each step), black lift, glow grid, sharpen
            float _RetroColourMode;   // 0 keep, 1 grade, 2 mono, 3 palette
            float4 _RetroGrade;       // saturation, contrast, crush, keep red
            float4 _RetroShadowTint;  // rgb (sRGB, normalised to grey) + amount
            float4 _RetroLightTint;
            float4 _RetroMonoDark, _RetroMonoLight;                          // sRGB
            float4 _RetroPalette0, _RetroPalette1, _RetroPalette2, _RetroPalette3; // sRGB, dark to light
            float4 _RetroScreenFx;    // curvature, scanlines, mask, scanline count
            float4 _RetroTape;        // colour bleed (grid pixels), wobble, vignette, grain

            static const float Bayer4[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
            static const float Bayer8[64] = {
                 0, 32,  8, 40,  2, 34, 10, 42,  48, 16, 56, 24, 50, 18, 58, 26,
                12, 44,  4, 36, 14, 46,  6, 38,  60, 28, 52, 20, 62, 30, 54, 22,
                 3, 35, 11, 43,  1, 33,  9, 41,  51, 19, 59, 27, 49, 17, 57, 25,
                15, 47,  7, 39, 13, 45,  5, 37,  63, 31, 55, 23, 61, 29, 53, 21 };
            static const float3 Luma = float3(0.299, 0.587, 0.114);

            // -0.5..0.5 threshold of an ordered dither of the given size at a cell.
            float Threshold(int2 p, float size)
            {
                if (size < 3) { int2 b = p & 1; return (((b.x ^ b.y) * 2 + b.y) + 0.5) / 4.0 - 0.5; }
                if (size < 6) { int2 b = p & 3; return (Bayer4[b.y * 4 + b.x] + 0.5) / 16.0 - 0.5; }
                int2 b = p & 7; return (Bayer8[b.y * 8 + b.x] + 0.5) / 64.0 - 0.5;
            }

            float Hash(float2 p) { return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453); }

            // v in shade units to a whole shade. With a band below 1 only the middle of each step is dithered and the
            // rest is a solid fill of the nearer shade; band 1 is the plain ordered dither.
            float StepShade(float v, float threshold, float band)
            {
                float b = floor(v), f = v - b;
                float t = band > 0.001 ? saturate((f - 0.5) / band + 0.5) : step(0.5, f);
                return b + step(0.5 - threshold, t);
            }
            float3 StepShade3(float3 v, float threshold, float band)
            {
                return float3(StepShade(v.r, threshold, band), StepShade(v.g, threshold, band), StepShade(v.b, threshold, band));
            }

            half4 Sample(float2 cell, float block, float2 size)
            {
                float2 uv = clamp((cell + 0.5) * block / size, 0.0, 1.0);
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 size = _ScreenParams.xy;
                float fullBlock = _RetroPixelLines > 0 ? max(1.0, size.y / _RetroPixelLines) : 1.0;
                float block = max(1.0, round(lerp(1.0, fullBlock, _RetroStrength)));

                if (_RetroFilterOn < 0.5)
                {
                    float2 cell = floor(input.texcoord * size / block);
                    float2 uv = min((cell + 0.5) * block / size, 1.0);
                    half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv);

                    if (_RetroColorLevels >= 2)
                    {
                        int2 b = int2(cell) & 3;
                        float threshold = (Bayer4[b.y * 4 + b.x] + 0.5) / 16.0 - 0.5;
                        float steps = _RetroColorLevels - 1;
                        float3 c = LinearToSRGB(saturate(color.rgb));
                        c = floor(c * steps + 0.5 + threshold * _RetroDither) / steps;
                        color.rgb = lerp(color.rgb, SRGBToLinear(saturate(c)), _RetroStrength);
                    }
                    return color;
                }

                // ── Camera filter ───────────────────────────────────────────
                float2 uv0 = input.texcoord;
                float curve = _RetroScreenFx.x;
                if (curve > 0)
                {
                    float2 c = uv0 * 2.0 - 1.0;
                    c *= 1.0 + curve * dot(c, c);
                    uv0 = c * 0.5 + 0.5;
                    if (any(uv0 < 0.0) || any(uv0 > 1.0)) return half4(0, 0, 0, 1);
                }
                if (_RetroTape.y > 0)
                {
                    // Now and then (about one moment in six) a thin band slips sideways.
                    float slot = floor(_Time.y * 1.5);
                    float on = step(0.83, Hash(float2(slot, 7.0)));
                    float band = saturate(1.0 - abs(uv0.y - Hash(float2(slot, 3.0))) / 0.05) * on;
                    uv0.x += band * _RetroTape.y * 0.02 * sin(uv0.y * 140.0 + _Time.y * 40.0);
                }

                float2 cell = floor(uv0 * size / block);
                half4 color = Sample(cell, block, size);
                float3 s = LinearToSRGB(saturate(color.rgb));
                if (_RetroShade.w > 0)
                {
                    // Crisper detail: the picture pushed away from the average of its four neighbours.
                    float3 n = LinearToSRGB(saturate(Sample(cell + float2(1, 0), block, size).rgb)) + LinearToSRGB(saturate(Sample(cell - float2(1, 0), block, size).rgb))
                             + LinearToSRGB(saturate(Sample(cell + float2(0, 1), block, size).rgb)) + LinearToSRGB(saturate(Sample(cell - float2(0, 1), block, size).rgb));
                    s = saturate(s + (s - n * 0.25) * _RetroShade.w);
                }

                if (_RetroTape.x > 0)
                {
                    // Tape chroma: the colour is smeared sideways, the brightness stays sharp.
                    float3 l = LinearToSRGB(saturate(Sample(cell - float2(_RetroTape.x, 0), block, size).rgb));
                    float3 r = LinearToSRGB(saturate(Sample(cell + float2(_RetroTape.x * 0.5, 0), block, size).rgb));
                    float3 avg = (l + s + r) / 3.0;
                    s = dot(s, Luma) + (avg - dot(avg, Luma));
                }

                int2 ditherCell = int2(floor(cell / max(1.0, _RetroDitherScale)));
                float threshold = Threshold(ditherCell, _RetroBayer) * _RetroDither;
                float levels = _RetroColorLevels;
                float lum = dot(s, Luma);
                float crush = _RetroGrade.z;
                float lumC = saturate((lum - crush) / max(1e-3, 1.0 - crush));
                int mode = (int)round(_RetroColourMode);
                float band = _RetroShade.x > 0 ? _RetroShade.x : 1.0;
                float blackLift = _RetroShade.y;
                if (mode >= 2)
                {
                    if (_RetroLift > 0) lumC = pow(lumC, _RetroLift);
                    lumC = saturate((lumC - 0.5) * (1.0 + _RetroGrade.y) + 0.5);
                    lumC = lumC * (1.0 - blackLift) + blackLift;
                }

                if (mode == 1)
                {
                    s = lerp(lum.xxx, s, _RetroGrade.x);
                    s = (s - 0.5) * (1.0 + _RetroGrade.y) + 0.5;
                    s *= lum > 1e-3 ? lumC / lum : 0.0;
                    float l2 = saturate(dot(s, Luma));
                    s *= lerp(1.0, _RetroShadowTint.rgb, _RetroShadowTint.a * (1.0 - l2) * (1.0 - l2));
                    s *= lerp(1.0, _RetroLightTint.rgb, _RetroLightTint.a * l2);
                }
                else if (mode == 2)
                {
                    float g = lumC;
                    if (levels >= 2) { float st = levels - 1; g = saturate(StepShade(g * st, threshold, band) / st); }
                    float3 tone = lerp(_RetroMonoDark.rgb, _RetroMonoLight.rgb, g);
                    float red = saturate((s.r - 2.0 * max(s.g, s.b) - 0.05) * 6.0) * _RetroGrade.w;   // deep reds only (blood), not skin
                    s = lerp(tone, s, red);
                }
                else if (mode == 3)
                {
                    int i = (int)clamp(StepShade(lumC * 3.0, threshold, band), 0.0, 3.0);
                    s = i == 0 ? _RetroPalette0.rgb : i == 1 ? _RetroPalette1.rgb : i == 2 ? _RetroPalette2.rgb : _RetroPalette3.rgb;
                }

                if (mode < 2)
                {
                    s = saturate(s) * (1.0 - blackLift) + blackLift;   // blacks lifted into a dark haze the dither can break up
                    if (levels >= 2)
                    {
                        float st = levels - 1;
                        if (_RetroBrightDither > 0.5)
                        {
                            // Only the brightness is stepped: the colour keeps its hue (scaled as a whole, never clipped per channel).
                            float l = dot(s, Luma);
                            float q = saturate(StepShade(l * st, threshold, band) / st);
                            s = l > 1e-3 ? s * (q / l) : q.xxx;
                            float top = max(s.r, max(s.g, s.b));
                            if (top > 1.0) s /= top;
                        }
                        else s = StepShade3(s * st, threshold, band) / st;
                    }
                    if (_RetroShade.z > 0)
                    {
                        // Bright light broken into a grid: dark lines between the dither cells over the glows.
                        float cellPx = block * max(1.0, _RetroDitherScale);
                        float2 inCell = fmod(floor(uv0 * size), cellPx);
                        float width = max(1.0, round(cellPx * 0.3));
                        float edge = (inCell.x < width || inCell.y < width) ? 1.0 : 0.0;
                        float glow = smoothstep(0.5, 0.85, dot(saturate(s), Luma)) * _RetroShade.z;
                        s *= edge > 0.5 ? 1.0 - glow * 0.7 : 1.0 + glow * 0.12;
                    }
                }

                // Screen overlays
                float scan = _RetroScreenFx.y;
                if (scan > 0)
                {
                    float wave = 0.5 + 0.5 * cos(uv0.y * _RetroScreenFx.w * 6.2831853);
                    s *= lerp(1.0, 0.6 + 0.4 * wave, scan);
                }
                if (_RetroScreenFx.z > 0)
                {
                    int column = (int)fmod(floor(input.texcoord.x * size.x), 3.0);
                    float3 m = column == 0 ? float3(1.0, 0.75, 0.75) : column == 1 ? float3(0.75, 1.0, 0.75) : float3(0.75, 0.75, 1.0);
                    s *= lerp(1.0, m * 1.2, _RetroScreenFx.z);
                }
                if (_RetroTape.z > 0)
                {
                    float d = length((uv0 - 0.5) * float2(size.x / size.y, 1.0));
                    s *= 1.0 - _RetroTape.z * smoothstep(0.35, 0.95, d);
                }
                if (_RetroTape.w > 0)
                    s += (Hash(cell + frac(_Time.y * 7.0) * 113.0) - 0.5) * _RetroTape.w * 0.18;

                color.rgb = SRGBToLinear(saturate(s));
                return color;
            }
            ENDHLSL
        }
    }
}
