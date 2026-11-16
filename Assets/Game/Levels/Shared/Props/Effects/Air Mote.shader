// Dust in the air (DungeonAir): a mote is a small square that shows only the light that reaches it. It adds up the
// lamps and torches at its own place (their colour, how far they are, and their shadows, so a mote behind a pillar is
// dark), whichever way it faces, and is drawn additive. It takes no ambient light, so in an unlit room there are none.
// It fades out close to the eye (a mote at arm's length would be a big square) and far from it.
Shader "LoomRoom/Air Mote"
{
    Properties
    {
        [MainColor] _BaseColor ("Tint", Color) = (1, .93, .82, 1)
        _Strength ("Light strength", Float) = 1
        _Near ("Fades in from (m)", Float) = .7
        _NearFull ("Full from (m)", Float) = 1.6
        _Far ("Gone by (m)", Float) = 11
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Strength;
                half _Near;
                half _NearFull;
                half _Far;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half4 color : COLOR; };

            Varyings Vert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.color = i.color;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half3 light = 0;
                #if defined(_ADDITIONAL_LIGHTS)
                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                half4 shadowMask = half4(1, 1, 1, 1);
                uint meshLayers = GetMeshRenderingLayer();
                uint lightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(lightCount)
                    Light l = GetAdditionalLight(lightIndex, i.positionWS, shadowMask);
                    #ifdef _LIGHT_LAYERS
                    if (IsMatchingLightLayer(l.layerMask, meshLayers))
                    #endif
                    { light += l.color * (l.distanceAttenuation * l.shadowAttenuation); }
                LIGHT_LOOP_END
                #endif
                // A lamp close by doesn't blow a mote out to white: the light is eased toward 1.
                light = light * _Strength;
                light = light / (1.0 + light);
                float d = distance(_WorldSpaceCameraPos, i.positionWS);
                half fade = saturate((d - _Near) / max(.01, _NearFull - _Near)) * saturate((_Far - d) / 3.0);
                return half4(_BaseColor.rgb * i.color.rgb * light, _BaseColor.a * i.color.a * fade);
            }
            ENDHLSL
        }
    }
}
