// The campaign map burning away like parchment (CampaignManager > Burn): while it burns, every map material is drawn with
// a copy using this shader (same texture and colour, lit by the same lights: main, the room's and the map's own). A
// ragged front eats in from the map's edges to its middle (_BurnFront 0..1 over the distance from the nearest edge);
// just behind it the surface chars dark, at it a thin band of embers glows, and past it nothing is left. Height counts a
// little, so a prop burns up from its foot as the front reaches it. The front's place comes from globals set each frame
// (CampaignMap.Burn): the map's centre, its right and forward axes, its half sizes.
Shader "LoomRoom/Map Burn"
{
    Properties
    {
        _BaseMap ("Texture", 2D) = "white" {}
        _BaseColor ("Colour", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
            CBUFFER_END
            float _MapBurnFront;        // 0: nothing burnt .. 1: all gone
            float4 _MapBurnCentre;      // xyz world centre of the map
            float4 _MapBurnRight;       // xyz the map's right (local x) axis
            float4 _MapBurnForward;     // xyz the map's forward (local z) axis
            float4 _MapBurnSize;        // x half width, y half depth, z height weight, w ragged amount
            half4 _MapBurnGlow;         // ember colour (HDR)

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float2 uv : TEXCOORD2; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x), u.y);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                // How far in from the map's nearest edge (0 at the edge, 1 in the middle), roughened, and a little later
                // the higher up (a prop burns from its foot up).
                float3 d = i.positionWS - _MapBurnCentre.xyz;
                float2 local = float2(dot(d, _MapBurnRight.xyz), dot(d, _MapBurnForward.xyz));
                float2 size = max(_MapBurnSize.xy, 0.001);
                float inward = saturate(min(1.0 - abs(local.x) / size.x, 1.0 - abs(local.y) / size.y));
                float rag = (Noise(local * 0.35) * 0.65 + Noise(local * 1.3) * 0.35) * _MapBurnSize.w;
                float at = inward + rag + max(0.0, d.y) * _MapBurnSize.z;
                // 0: the very edge is just catching; 1: the last of the middle (and the tallest peak) is gone.
                float front = _MapBurnFront * (1.0 + _MapBurnSize.w + 0.12);
                float behind = front - at;          // > 0: already burnt
                clip(0.03 - behind);
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                float3 n = normalize(i.normalWS);
                Light main = GetMainLight();
                half3 lit = SampleSH(n) + main.color * saturate(dot(n, main.direction)) * main.distanceAttenuation;
            #if defined(_ADDITIONAL_LIGHTS)
                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                uint meshRenderingLayers = GetMeshRenderingLayer();
                #if USE_CLUSTER_LIGHT_LOOP
                [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
                {
                    Light l = GetAdditionalLight(lightIndex, i.positionWS);
                    #ifdef _LIGHT_LAYERS
                    if (IsMatchingLightLayer(l.layerMask, meshRenderingLayers))
                    #endif
                        lit += l.color * saturate(dot(n, l.direction)) * l.distanceAttenuation;
                }
                #endif
                uint count = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(count)
                    Light l = GetAdditionalLight(lightIndex, i.positionWS);
                    #ifdef _LIGHT_LAYERS
                    if (IsMatchingLightLayer(l.layerMask, meshRenderingLayers))
                    #endif
                        lit += l.color * saturate(dot(n, l.direction)) * l.distanceAttenuation;
                LIGHT_LOOP_END
            #endif
                half3 colour = tex.rgb * lit;
                // Ahead of the front the surface browns, then chars black; at it a thin line of embers; just behind it
                // a sliver of ash with sparks in it, then nothing.
                float scorch = saturate((behind + 0.09) / 0.09);
                colour = lerp(colour, colour * half3(0.55, 0.38, 0.25), saturate(scorch * 2.0));
                colour = lerp(colour, half3(0.015, 0.01, 0.008), saturate(scorch * 2.0 - 1.0));
                float flicker = 0.55 + 0.45 * Noise(local * 6.0 + _MapBurnFront * 40.0);
                float ember = saturate(1.0 - abs(behind + 0.004) / 0.008) * flicker;
                float ash = saturate(behind / 0.03) * step(0.0, behind);
                colour += _MapBurnGlow.rgb * (ember + ash * step(0.82, Noise(local * 9.0)) * 0.6);
                return half4(colour, 1);
            }
            ENDHLSL
        }
    }
}
