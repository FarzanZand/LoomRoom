// Pastes the low-res per-object buffer (PixelObjectFeature) over the camera image: point-sampled
// colour, and the buffer's own depth written and tested against the scene, so walls still hide
// pixelated characters and later transparents sort against them.
Shader "Hidden/LoomRoom/Pixel Object Composite"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite On ZTest LEqual Cull Off Blend Off

        Pass
        {
            Name "Pixel Object Composite"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D_FLOAT(_PixelObjectDepth);
            float4 _PixelObjectTargetSize; // xy size, zw 1/size

            float Nearer(float a, float b)
            {
                #if UNITY_REVERSED_Z
                    return max(a, b);
                #else
                    return min(a, b);
                #endif
            }

            half4 Frag(Varyings input, out float depth : SV_Depth) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                int2 size = int2(_PixelObjectTargetSize.xy);
                int2 texel = min(int2(input.texcoord * _PixelObjectTargetSize.xy), size - 1);
                half4 color = LOAD_TEXTURE2D_X(_BlitTexture, texel);
                clip(color.a - 0.5);

                // One low-res pixel covers several screen pixels, and its single depth would fight a
                // surface it lies on (a rug on the floor) in stripes. Use the nearest depth of its
                // neighbours that belong to the same object layer, then pull it a little towards the camera.
                float raw = LOAD_TEXTURE2D(_PixelObjectDepth, texel).r;
                for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    int2 t = clamp(texel + int2(x, y), 0, size - 1);
                    if (LOAD_TEXTURE2D_X(_BlitTexture, t).a > 0.5)
                        raw = Nearer(raw, LOAD_TEXTURE2D(_PixelObjectDepth, t).r);
                }
                float eye = LinearEyeDepth(raw, _ZBufferParams);
                eye = max(eye - (0.02 + eye * 0.01), _ProjectionParams.y);
                depth = (1.0 / eye - _ZBufferParams.w) / _ZBufferParams.z;
                return half4(color.rgb, 1);
            }
            ENDHLSL
        }
    }
}
