// Forward pass of LoomRoom/Pixel Lit, shared with its per-object low-res twin
// (Hidden/LoomRoom/Pixel Lit Object, compiled with PIXEL_OBJECT_PASS).
#ifndef LOOMROOM_PIXEL_LIT_FORWARD
#define LOOMROOM_PIXEL_LIT_FORWARD

#include "PixelLitInput.hlsl"

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float3 normalWS : TEXCOORD1;
    float2 uv : TEXCOORD2;
    float3 positionOS : TEXCOORD3;
    float3 normalOS : TEXCOORD4;
    half fogFactor : TEXCOORD5;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

Varyings Vert(Attributes input)
{
    Varyings o = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, o);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
    o.positionCS = pos.positionCS;
    o.positionWS = pos.positionWS;
    o.normalWS = TransformObjectToWorldNormal(input.normalOS);
    o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
    o.positionOS = input.positionOS.xyz;
    o.normalOS = input.normalOS;
    o.fogFactor = ComputeFogFactor(pos.positionCS.z);
    return o;
}

half3 Diffuse(Light light, float3 n, half3 shade)
{
    half ndl = saturate(dot(n, light.direction));
    return light.color * (light.distanceAttenuation * light.shadowAttenuation * ndl);
}

half3 Gloss(Light light, float3 n, float3 v)
{
    if (_Gloss <= 0) return 0;
    float3 h = normalize(light.direction + v);
    half hit = step(1.0 - _GlossSize, saturate(dot(n, h)));
    return light.color * (light.distanceAttenuation * light.shadowAttenuation * hit * _Gloss);
}

float4 _PixelObjectTargetSize; // xy size, zw 1/size (set by PixelObjectFeature)

half4 Frag(Varyings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    float3 n = normalize(input.normalWS);
    float3 litPos = input.positionWS;
    float2 cell = floor(input.positionCS.xy);
    float axisSeed = 0;
    half detail = 0.5;
    half4 base;

    if (_SnapMode == 1)
    {
        float texels = _BaseTexels > 0 ? _BaseTexels : _BaseMap_TexelSize.z;
        float2 snappedUv;
        litPos = SnapToUvTexel(input.positionWS, input.uv, texels, cell, snappedUv);
        litPos -= n * dot(litPos - input.positionWS, n);
        base = SampleBaseUv(snappedUv, _BaseTexels);
    }
    else
    {
        base = SAMPLE_TEXTURE2D(_BaseMap, sampler_PointRepeat, input.uv);
        if (_SnapMode >= 2)
        {
            float density = TexelDensity();
            bool objectSpace = _SnapMode == 3;
            float3 p = input.positionWS, gridN = n;
            if (objectSpace)
            {
                // Object grid: texels stay glued to moving and animated models.
                float scale = length(float3(UNITY_MATRIX_M[0].x, UNITY_MATRIX_M[1].x, UNITY_MATRIX_M[2].x));
                density *= scale;
                p = input.positionOS;
                gridN = normalize(input.normalOS);
            }
            float3 snapped = SnapToGrid(p, gridN, density, cell, axisSeed);
            litPos = objectSpace ? TransformObjectToWorld(snapped) : snapped;

            if (_DetailStrength > 0)
            {
                float seed;
                float2 duv = PlanarCoords(p * density, gridN, seed) * _DetailMap_TexelSize.xy;
                detail = SAMPLE_TEXTURE2D_GRAD(_DetailMap, sampler_PointRepeat, duv, ddx(duv), ddy(duv)).r;
            }
        }
    }

    half3 albedo = base.rgb * _BaseColor.rgb;
    half alpha = base.a * _BaseColor.a;
    #if defined(_ALPHATEST_ON)
        clip(alpha - _Cutoff);
    #endif
    if (_DitherAlpha > 0) clip(alpha - Bayer4(floor(input.positionCS.xy)));

    albedo *= lerp(1.0, detail * 2.0, _DetailStrength);
    albedo *= 1.0 + (PixelHash(cell + axisSeed) - 0.5) * 2.0 * _TexelNoise;

    // Lighting, evaluated at the texel centre so light and shadow edges follow the texels.
    InputData inputData = (InputData)0;
    inputData.positionWS = litPos;
    inputData.normalWS = n;
    #if defined(PIXEL_OBJECT_PASS)
    // Drawn into the low-res object buffer: screen UV from that buffer's size, not the camera's.
    inputData.normalizedScreenSpaceUV = input.positionCS.xy * _PixelObjectTargetSize.zw;
    #else
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
    #endif
    half4 shadowMask = half4(1, 1, 1, 1);
    float3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);

    half3 indirectAO = 1, directAO = 1;
    #if defined(_SCREEN_SPACE_OCCLUSION)
        AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(inputData.normalizedScreenSpaceUV);
        indirectAO = ao.indirectAmbientOcclusion;
        directAO = ao.directAmbientOcclusion;
    #endif

    half3 ambient = SampleSH(n) * _AmbientStrength * indirectAO;
    half3 light = 0;
    half3 gloss = 0;
    uint meshLayers = GetMeshRenderingLayer();

    Light mainLight = GetMainLight(TransformWorldToShadowCoord(litPos), litPos, shadowMask);
    #ifdef _LIGHT_LAYERS
    if (IsMatchingLightLayer(mainLight.layerMask, meshLayers))
    #endif
    {
        light += Diffuse(mainLight, n, 1) * directAO;
        gloss += Gloss(mainLight, n, v);
    }

    #if defined(_ADDITIONAL_LIGHTS)
    uint lightCount = GetAdditionalLightsCount();
    #if USE_CLUSTER_LIGHT_LOOP
    [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
        Light l = GetAdditionalLight(lightIndex, litPos, shadowMask);
        #ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(l.layerMask, meshLayers))
        #endif
        { light += Diffuse(l, n, 1); gloss += Gloss(l, n, v); }
    }
    #endif
    LIGHT_LOOP_BEGIN(lightCount)
        Light l = GetAdditionalLight(lightIndex, litPos, shadowMask);
        #ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(l.layerMask, meshLayers))
        #endif
        { light += Diffuse(l, n, 1); gloss += Gloss(l, n, v); }
    LIGHT_LOOP_END
    #endif

    // Step the direct light into bands; band edges break up in an ordered dither, one texel
    // at a time. Ambient stays smooth so shadowed areas never crush to black.
    if (_LightSteps >= 1)
    {
        half lum = max(light.r, max(light.g, light.b));
        half x = lum / (1.0 + lum);
        half offset = 0.5 + (Bayer4(cell) - 0.5) * _BandDither;
        half xq = floor(x * _LightSteps + offset) / _LightSteps;
        // The faint outer edge of a light fades out smoothly instead of ending in a hard, flat disc.
        if (x < 1.0 / _LightSteps) xq = x;
        xq = min(xq, 0.97);
        half lq = xq / (1.0 - xq);
        light *= lq / max(lum, 1e-4);
    }
    light += ambient;
    // Darker areas lean towards the shade tint.
    half shadeAmount = saturate(1.0 - max(light.r, max(light.g, light.b)));
    light *= lerp(half3(1, 1, 1), _ShadeColor.rgb, shadeAmount);
    light *= _PixelLookBrightness > 0 ? _PixelLookBrightness : 1.0;

    half3 color = albedo * light + gloss;
    color += _EmissionColor.rgb * SAMPLE_TEXTURE2D(_EmissionMap, sampler_PointRepeat, input.uv).rgb;
    color = MixFog(color, input.fogFactor);
    return half4(color, 1);
}

#endif
