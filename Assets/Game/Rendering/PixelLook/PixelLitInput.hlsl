// Shared input for LoomRoom/Pixel Lit: material properties (one CBUFFER for every pass, so the
// SRP batcher keeps working) and the texel snapping that gives the "3D pixel art" look.
#ifndef LOOMROOM_PIXEL_LIT_INPUT
#define LOOMROOM_PIXEL_LIT_INPUT

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    float4 _BaseMap_TexelSize;
    float4 _DetailMap_TexelSize;
    half4 _BaseColor;
    half4 _EmissionColor;
    half4 _ShadeColor;
    half _Cutoff;
    float _SnapMode;        // 0 none, 1 texture texels (UVs), 2 world grid, 3 object grid
    float _TexelsPerUnit;   // grid modes: texels per metre
    float _BaseTexels;      // UV mode: texels across the base map (0 = the texture's own size)
    half _DetailStrength;
    half _TexelNoise;
    half _LightSteps;       // 0 = smooth light
    half _BandDither;
    half _Gloss;
    half _GlossSize;
    half _AmbientStrength;
CBUFFER_END

TEXTURE2D(_BaseMap);
TEXTURE2D(_DetailMap);
TEXTURE2D(_EmissionMap);

// Set by PixelLook. Multiplies every material's texel density; 1 = as authored.
float _PixelLookTexelScale;

float PixelHash(float2 p)
{
    p = frac(p * float2(0.1031, 0.1030));
    p += dot(p, p.yx + 33.33);
    return frac((p.x + p.y) * p.x);
}

float Bayer4(float2 cell)
{
    uint2 b = (uint2)(int2)floor(cell) & 3u;
    const float m[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
    return (m[b.y * 4 + b.x] + 0.5) / 16.0;
}

float TexelDensity()
{
    float scale = _PixelLookTexelScale > 0 ? _PixelLookTexelScale : 1.0;
    return max(_TexelsPerUnit * scale, 0.01);
}

// Picks the two axes of the plane a surface faces most.
float2 PlanarCoords(float3 p, float3 n, out float axisSeed)
{
    float3 a = abs(n);
    if (a.x >= a.y && a.x >= a.z) { axisSeed = 17.0; return p.zy; }
    if (a.y >= a.z)               { axisSeed = 53.0; return p.xz; }
    axisSeed = 91.0; return p.xy;
}

// Moves a point onto the centre of its texel on a 3D grid, keeping it on the surface plane.
float3 SnapToGrid(float3 p, float3 n, float density, out float2 cell, out float axisSeed)
{
    float3 q = p * density;
    float3 c = (floor(q) + 0.5) / density;
    float3 a = abs(n);
    if (a.x >= a.y && a.x >= a.z) c.x = p.x;
    else if (a.y >= a.z)          c.y = p.y;
    else                          c.z = p.z;
    c -= n * dot(c - p, n);
    cell = floor(PlanarCoords(q, n, axisSeed));
    return c;
}

// Moves a world position to the centre of the base map texel it shows, using screen derivatives.
float3 SnapToUvTexel(float3 positionWS, float2 uv, float texels, out float2 cell, out float2 snappedUv)
{
    float2 t = uv * texels;
    cell = floor(t);
    snappedUv = (cell + 0.5) / texels;
    float2 duv = snappedUv - uv;
    float2 dx = ddx(uv), dy = ddy(uv);
    float det = dx.x * dy.y - dx.y * dy.x;
    if (abs(det) < 1e-12) return positionWS;
    float a = clamp((duv.x * dy.y - duv.y * dy.x) / det, -64.0, 64.0);
    float b = clamp((dx.x * duv.y - dx.y * duv.x) / det, -64.0, 64.0);
    return positionWS + a * ddx(positionWS) + b * ddy(positionWS);
}

half4 SampleBaseUv(float2 uv, float texels)
{
    // Point sample at the mip that matches the target texel count, never sharper than the screen allows.
    float size = _BaseMap_TexelSize.z;
    float fixedLod = texels > 0 ? max(0.0, log2(size / texels)) : 0.0;
    float2 dx = ddx(uv) * _BaseMap_TexelSize.zw, dy = ddy(uv) * _BaseMap_TexelSize.zw;
    float screenLod = 0.5 * log2(max(dot(dx, dx), dot(dy, dy)));
    return SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_PointRepeat, uv, max(fixedLod, screenLod));
}

half AlphaOf(float2 uv)
{
    return SAMPLE_TEXTURE2D(_BaseMap, sampler_PointRepeat, uv).a * _BaseColor.a;
}

#endif
