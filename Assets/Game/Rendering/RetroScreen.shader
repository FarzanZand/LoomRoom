// Full-screen pass for the room's low-res look: snaps the image to a coarse pixel grid and
// reduces each colour channel with an ordered (Bayer) dither. Driven by globals from ScreenManager.
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

            static const float Bayer4[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 size = _ScreenParams.xy;
                float fullBlock = _RetroPixelLines > 0 ? max(1.0, size.y / _RetroPixelLines) : 1.0;
                float block = max(1.0, round(lerp(1.0, fullBlock, _RetroStrength)));
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
            ENDHLSL
        }
    }
}
