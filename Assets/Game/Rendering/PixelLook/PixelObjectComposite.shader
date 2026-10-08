// Pass 0 pastes the low-res per-object buffer (PixelObjectFeature) over the camera image: point-sampled
// colour, and the buffer's own depth written and tested against the scene, so walls still hide
// pixelated characters and later transparents sort against them.
Shader "Hidden/LoomRoom/Pixel Object Composite"
{
    Properties
    {
        // Set by PixelObjectFeature: Always when the buffer was seeded with the scene's depth (hidden
        // texels are already culled, and a per-pixel test would only fight surfaces the object lies on
        // in stripes), LessEqual when it was not.
        [HideInInspector] _PixelObjectZTest("ZTest", Float) = 4
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite On ZTest [_PixelObjectZTest] Cull Off Blend Off

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
                // neighbours on the same surface, then pull it a little towards the camera. Neighbours much
                // nearer belong to another object in front: borrowing their depth would paint whatever lies
                // far behind (the room under the dungeon) as a ring around that object.
                float own = LOAD_TEXTURE2D(_PixelObjectDepth, texel).r;
                float ownEye = LinearEyeDepth(own, _ZBufferParams);
                float reach = 0.3 + ownEye * 0.08;
                float raw = own;
                for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    int2 t = clamp(texel + int2(x, y), 0, size - 1);
                    if (LOAD_TEXTURE2D_X(_BlitTexture, t).a <= 0.5) continue;
                    float n = LOAD_TEXTURE2D(_PixelObjectDepth, t).r;
                    if (ownEye - LinearEyeDepth(n, _ZBufferParams) < reach) raw = Nearer(raw, n);
                }
                float eye = LinearEyeDepth(raw, _ZBufferParams);
                eye = max(eye - (0.02 + eye * 0.01), _ProjectionParams.y);
                depth = (1.0 / eye - _ZBufferParams.w) / _ZBufferParams.z;
                return half4(color.rgb, 1);
            }
            ENDHLSL
        }

        // Pass 1: seeds the low-res depth buffer with the scene's depth before the objects are drawn,
        // the farthest of each block, so anything hidden behind scene geometry (the room under the
        // dungeon floor) is never drawn into the buffer and cannot show at the objects' edges.
        Pass
        {
            Name "Pixel Object Depth Seed"
            ZWrite On ZTest Always ColorMask 0 Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D_FLOAT(_PixelObjectSceneDepth);
            float4 _PixelObjectSceneSize; // xy scene depth size, z screen pixels per object pixel

            float Farther(float a, float b)
            {
                #if UNITY_REVERSED_Z
                    return min(a, b);
                #else
                    return max(a, b);
                #endif
            }

            float Frag(Varyings input) : SV_Depth
            {
                int n = max(1, (int)_PixelObjectSceneSize.z);
                int2 size = int2(_PixelObjectSceneSize.xy);
                int2 origin = int2(floor(input.positionCS.xy)) * n;
                #if UNITY_REVERSED_Z
                    float depth = 1;
                #else
                    float depth = 0;
                #endif
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    depth = Farther(depth, LOAD_TEXTURE2D(_PixelObjectSceneDepth, min(origin + int2(x, y), size - 1)).r);
                return depth;
            }
            ENDHLSL
        }
    }
}
