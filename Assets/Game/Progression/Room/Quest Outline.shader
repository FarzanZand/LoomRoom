// The quest outline (QuestGlow, QuestOutlinePass): a soft glow round an object's silhouette, the way games mark what to
// go to. Three passes, run by QuestOutlinePass on the game camera before post-processing:
//   Mask       the object's own pieces drawn flat in the glow's colour into a screen-sized texture, only where they
//              are not hidden behind something (tested against the scene's depth), so the glow follows what is seen.
//   Blur       that texture blurred at half size (run across and down, twice).
//   Composite  the blur added to the picture outside the object only, strongest at its edge and fading to nothing,
//              and a little of the colour on the object itself.
// It took the place of a shell pushed out round each mesh, which gave a hard bright line of one width in the world
// (thick close up, with lines inside the shape wherever one piece stood in front of another; the user, 2026-11-16:
// "You can do an outline, just don't do it as sharp and ugly"). Kept narrow: at 26 pixels wide it was a halo, and on
// the character sheet a blot ("The glow is too much, just do an outline on the sheet"); at 9 it is a line with a soft edge.
Shader "LoomRoom/Quest Outline"
{
    Properties
    {
        [HDR] _Tint ("Colour", Color) = (.45, .8, 1, 1)
        _Spread ("Glow width (pixels on a 1080 high screen)", Float) = 9
        _Strength ("Glow strength at the edge", Float) = .8
        _Falloff ("Falloff (higher: tighter round the edge)", Float) = 1.3
        _Inner ("Colour on the object itself", Range(0, .5)) = .03
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "Mask"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                float _Spread, _Strength, _Falloff, _Inner;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // Hidden behind something nearer: not part of what is outlined.
                float2 uv = GetNormalizedScreenSpaceUV(i.positionCS);
                float scene = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float mine = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                clip(scene * 1.004 + 0.002 - mine);
                return half4(_Tint.rgb, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Blur"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _QuestStep;   // xy: one tap's step across the texture

            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = i.texcoord;
                half4 sum = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv) * 0.2270270;
                sum += (SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + _QuestStep.xy) + SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - _QuestStep.xy)) * 0.1945946;
                sum += (SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + _QuestStep.xy * 2) + SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - _QuestStep.xy * 2)) * 0.1216216;
                sum += (SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + _QuestStep.xy * 3) + SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - _QuestStep.xy * 3)) * 0.0540541;
                sum += (SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + _QuestStep.xy * 4) + SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - _QuestStep.xy * 4)) * 0.0162162;
                return sum;
            }
            ENDHLSL
        }

        Pass
        {
            Name "Composite"
            Blend One One
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D_X(_QuestMask);
            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                float _Spread, _Strength, _Falloff, _Inner;
            CBUFFER_END

            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = i.texcoord;
                half4 glow = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                half4 mask = SAMPLE_TEXTURE2D_X(_QuestMask, sampler_LinearClamp, uv);
                // The blur holds colour times cover: its own colour back, then its cover shaped into the glow. Half
                // cover is the object's edge, so the glow is full there and gone where the blur ends.
                half3 colour = glow.rgb / max(glow.a, 1e-4);
                half edge = pow(saturate(glow.a * 2), _Falloff);
                half3 outside = colour * edge * _Strength * (1 - mask.a);
                return half4(outside + mask.rgb * _Inner, 1);
            }
            ENDHLSL
        }
    }
}
