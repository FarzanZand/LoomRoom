// Bakes the charred copy of a body texture for BurnCoat (an enemy set alight by a Fireball), texel for texel at the
// texture's own size. Pass 0, the colour: each texel keeps its value and takes its colour from soot (darkest) to ash
// (lightest) by _Char, a little darker in all, with soot in ragged clusters; ember texels a dark burnt orange. Pass 1,
// the glow: the ember texels (ragged clusters of _Embers of the darker texels: creases, outlines, grime), brightest in
// their middle, black everywhere else; BurnCoat puts it in the copies' emission. Worked in gamma so the ramp follows
// the value as the eye sees it. Run once per texture through Graphics.Blit; point filtered like the original.
Shader "Hidden/LoomRoom/Burn Bake"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Char ("Charred", Float) = 0.6
        _Embers ("Embers", Float) = 0.08
        _Soot ("Soot", Color) = (0.05, 0.04, 0.035, 1)
        _Ash ("Ash", Color) = (0.5, 0.46, 0.42, 1)
    }
    CGINCLUDE
    #include "UnityCG.cginc"
    sampler2D _MainTex; float4 _MainTex_TexelSize;
    float _Char, _Embers; float4 _Soot, _Ash;

    float hash(float2 p)
    {
        p = frac(p * float2(123.34, 456.21));
        p += dot(p, p + 45.32);
        return frac(p.x * p.y);
    }

    // Clusters two or three texels wide, each row shifted by a ragged amount, denser in some patches than in others.
    float clusters(float2 t, float cover, float salt)
    {
        float row = floor(t.y / 2);
        float shift = floor(hash(float2(row, 7.1 + salt)) * 3);
        float h = hash(floor(float2((t.x + shift) / 3, row)) + 13.7 + salt);
        float patch = hash(floor(float2((t.x + shift * 2) / 9, floor(t.y / 6))) + 91.3 + salt);
        return h < cover * (.2 + 1.6 * patch) ? 1 : 0;
    }

    float value(float2 t)
    {
        float3 c = pow(tex2Dlod(_MainTex, float4((t + .5) * _MainTex_TexelSize.xy, 0, 0)).rgb, 1 / 2.2);
        return dot(c, float3(.3, .59, .11));
    }

    // An ember: a darker texel in an ember cluster.
    float ember(float2 t)
    {
        // only in a crease: a dark texel with a lighter one beside it (grime between plates, a seam, an outline)
        float v = value(t);
        if (v > .3 || clusters(t, _Embers, 5) < 1) return 0;
        float lighter = max(max(value(t + float2(1, 0)), value(t - float2(1, 0))), max(value(t + float2(0, 1)), value(t - float2(0, 1))));
        return lighter > v + .08 ? 1 : 0;
    }
    ENDCG

    SubShader
    {
        ZTest Always Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            fixed4 frag(v2f_img i) : SV_Target
            {
                float2 t = floor(i.uv * _MainTex_TexelSize.zw);
                fixed4 src = tex2Dlod(_MainTex, float4((t + .5) * _MainTex_TexelSize.xy, 0, 0));
                float3 c = pow(src.rgb, 1 / 2.2);
                float v = dot(c, float3(.3, .59, .11));
                float3 soot = pow(_Soot.rgb, 1 / 2.2), ash = pow(_Ash.rgb, 1 / 2.2);
                float3 col = lerp(c * .85, lerp(soot, ash, saturate(v * 1.1)), _Char);
                if (clusters(t, _Char * .35, 2) > 0) col = lerp(col, soot, .7 * saturate(_Char / .6));
                if (ember(t) > 0) col = lerp(col, float3(.45, .14, .03), saturate(_Char / .6));
                return fixed4(pow(col, 2.2), src.a);
            }
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            fixed4 frag(v2f_img i) : SV_Target
            {
                float2 t = floor(i.uv * _MainTex_TexelSize.zw);
                if (ember(t) < .5) return fixed4(0, 0, 0, 1);
                // brighter where the cluster goes on to both sides, dimmer at its ragged ends
                float n = ember(t + float2(1, 0)) + ember(t - float2(1, 0)) + ember(t + float2(0, 1)) + ember(t - float2(0, 1));
                float k = .45 + .15 * n + .2 * hash(t + 1.7);
                return fixed4(k, k, k, 1);
            }
            ENDCG
        }
    }
}
