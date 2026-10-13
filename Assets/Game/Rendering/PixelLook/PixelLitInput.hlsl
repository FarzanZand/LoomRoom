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
    half _DitherAlpha;      // 1 = transparency as an ordered screen-door dither (opaque pass)
CBUFFER_END

TEXTURE2D(_BaseMap);
TEXTURE2D(_DetailMap);
TEXTURE2D(_EmissionMap);

// Set by PixelLook. Multiplies every material's texel density; 1 = as authored. The room is built at a
// much larger scale than the table's levels, so each world keeps its own: surfaces on the Room rendering
// layer (LightingManager.RoomLayer, bit 8) use the room scale, everything else the table scale, whoever
// is looking. _PixelLookTexelScale is the fallback before those are set.
float _PixelLookTexelScale;
float _PixelLookRoomTexelScale;
float _PixelLookTableTexelScale;
// Set by PixelLook. Multiplies all light on Pixel Lit materials; 0 (never set) reads as 1.
float _PixelLookBrightness;

// Set by LightingManager on dungeon floors (the light radius): ambient fades from full inside xyz's radius w to
// _LoomLightRadiusEdge.y over _LoomLightRadiusEdge.x metres; lamps and torches are direct light and keep lighting
// their surroundings. w = 0 turns it off. Set by ScreenManager (colour split): ambient times _LoomShadowTint.rgb and
// direct light times _LoomLampTint.rgb, each by its a. Room surfaces (rendering layer Room, value 8) are never touched.
float4 _LoomLightRadius;
float4 _LoomLightRadiusEdge;
// Set by MultiplayerManager: a dead co-op player watching from the room sees the dungeon cut away above height x
// (y = 1: on), so the others can be seen. Room surfaces (rendering layer Room, value 8) are never cut.
float4 _LoomCutaway;
void LoomCutaway(float3 positionWS)
{
    if (_LoomCutaway.y > 0 && positionWS.y > _LoomCutaway.x && (GetMeshRenderingLayer() & 8u) == 0u) discard;
}
float4 _LoomShadowTint;
float4 _LoomLampTint;
// Set by ScreenManager (Pixel lighting > Pixel size). Texels grow this many times; 0 (never set) reads as 1.
float _PixelLookPixelSize;

float PixelSize() { return max(_PixelLookPixelSize, 1.0); }

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
    bool room = (asuint(unity_RenderingLayer.x) & 8u) != 0;
    float scale = room ? _PixelLookRoomTexelScale : _PixelLookTableTexelScale;
    if (scale <= 0) scale = _PixelLookTexelScale > 0 ? _PixelLookTexelScale : 1.0;
    return max(_TexelsPerUnit * scale / PixelSize(), 0.01);
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

// Moves a position to the centre of the base map texel it shows, using screen derivatives. texels is per axis, so
// a non-square texture keeps every row. Pass a camera-relative position: far from the origin a world position has
// too little float precision, and the extrapolation turned that into speckle inside whole texels.
float3 SnapToUvTexel(float3 position, float2 uv, float2 texels, out float2 cell, out float2 snappedUv)
{
    float2 t = uv * texels;
    cell = floor(t);
    snappedUv = (cell + 0.5) / texels;
    float2 duv = snappedUv - uv;
    float2 dx = ddx(uv), dy = ddy(uv);
    float3 dpx = ddx(position), dpy = ddy(position);
    float det = dx.x * dy.y - dx.y * dy.x;
    // Collapsed or nearly edge-on UVs (det tiny next to the UV steps themselves, at any texel size) keep the pixel's
    // own position; anything else may reach a texel centre many screen pixels away (big wall blocks).
    if (abs(det) <= 1e-4 * (abs(dx.x * dy.y) + abs(dx.y * dy.x)) + 1e-20) return position;
    float a = clamp((duv.x * dy.y - duv.y * dy.x) / det, -4096.0, 4096.0);
    float b = clamp((dx.x * duv.y - dx.y * duv.x) / det, -4096.0, 4096.0);
    return position + a * dpx + b * dpy;
}

half4 SampleBaseUv(float2 uv, float2 lodUv)
{
    // Point sample at the mip that matches the texel count, never sharper than the screen allows. A material drawn
    // coarser than its texture (_BaseTexels, set by Even Out Texel Sizes) averages only a quarter of each block, as
    // its texture is a patchwork of UV islands that full averaging would mix; Pixel size averages whole texels.
    // The screen mip comes from lodUv, the smooth UV: the snapped uv jumps a whole texel wherever a 2x2 pixel quad
    // straddles a texel edge, and its derivative there picked a blurrier mip, a thin light line along those edges.
    float size = _BaseMap_TexelSize.z;
    float fixedLod = (_BaseTexels > 0 ? max(0.0, log2(size / _BaseTexels) - 2.0) : 0.0) + log2(PixelSize());
    float2 dx = ddx(lodUv) * _BaseMap_TexelSize.zw, dy = ddy(lodUv) * _BaseMap_TexelSize.zw;
    float screenLod = 0.5 * log2(max(dot(dx, dx), dot(dy, dy)));
    // The sample point is fixed per block, so the screen only takes over once a whole block is smaller than a
    // pixel; until then an Even Out material keeps its quarter-block average at every distance.
    // Ramped over the level after that, so there is no ring where the screen takes over.
    float blockLod = log2(size * PixelSize() / (_BaseTexels > 0 ? _BaseTexels : size));
    float lod = lerp(fixedLod, max(screenLod, fixedLod), saturate(screenLod - blockLod));
    return SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_PointRepeat, uv, lod);
}

half AlphaOf(float2 uv)
{
    return SAMPLE_TEXTURE2D(_BaseMap, sampler_PointRepeat, uv).a * _BaseColor.a;
}

#endif
