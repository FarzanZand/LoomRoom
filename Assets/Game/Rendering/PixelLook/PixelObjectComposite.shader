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

            half4 Frag(Varyings input, out float depth : SV_Depth) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                int2 texel = min(int2(input.texcoord * _PixelObjectTargetSize.xy), int2(_PixelObjectTargetSize.xy) - 1);
                half4 color = LOAD_TEXTURE2D_X(_BlitTexture, texel);
                clip(color.a - 0.5);
                depth = LOAD_TEXTURE2D(_PixelObjectDepth, texel).r;
                return half4(color.rgb, 1);
            }
            ENDHLSL
        }
    }
}
