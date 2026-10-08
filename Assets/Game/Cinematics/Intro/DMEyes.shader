// The Dungeon Master's glowing eyes. Additive and unlit; each vertex is pulled toward the camera by
// Pull (world units) so the eyes sit on the face without the face's own surface hiding them, while
// anything really in front (a wall, the table) still does.
Shader "LoomRoom/DM Eyes"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (1, .78, .4, 1)
        _Pull ("Pull Toward Camera", Float) = 2.5
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+100" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Pull;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 world = TransformObjectToWorld(v.positionOS.xyz);
                world += normalize(GetCameraPositionWS() - world) * _Pull;
                o.positionCS = TransformWorldToHClip(world);
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return _Color; }
            ENDHLSL
        }
    }
}
