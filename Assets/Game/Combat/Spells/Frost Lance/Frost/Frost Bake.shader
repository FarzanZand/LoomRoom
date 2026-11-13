// Bakes the frozen copy of a body texture for FrostCoat (an enemy chilled by a Frost Lance), texel for texel at the
// texture's own size. Each texel keeps its own value (so the body's creases, edges and grime still read) but takes its
// colour from a cold ramp, _Deep in the darkest texels through _Mid to _Ice in the lightest, by _Cold. Rime settles in
// small ragged clusters only on the lighter texels (the raised, lit parts of the paint), _Cover of them, a step paler than
// the ramp with a lighter top row, and a few single bright ice texels. Worked in gamma so the ramp follows the value as
// the eye sees it. Run once per texture through Graphics.Blit; the result is point filtered like the original.
Shader "Hidden/LoomRoom/Frost Bake"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Cold ("Cold", Float) = 0.7
        _Cover ("Cover", Float) = 0.2
        _Deep ("Deep", Color) = (0.1, 0.14, 0.24, 1)
        _Mid ("Mid", Color) = (0.38, 0.5, 0.64, 1)
        _Ice ("Ice", Color) = (0.8, 0.88, 0.96, 1)
    }
    SubShader
    {
        ZTest Always Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex; float4 _MainTex_TexelSize;
            float _Cold, _Cover; float4 _Deep, _Mid, _Ice;

            float hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            // Clusters three texels wide and two high, each row of clusters shifted by a ragged amount, denser in some
            // 9 x 6 patches than others (never one texel against the next at random: a checker speckle).
            float rime(float2 t)
            {
                float row = floor(t.y / 2);
                float shift = floor(hash(float2(row, 7.1)) * 3);
                float h = hash(floor(float2((t.x + shift) / 3, row)) + 13.7);
                float patch = hash(floor(float2((t.x + shift * 2) / 9, floor(t.y / 6))) + 91.3);
                return h < _Cover * (.2 + 1.6 * patch) ? 1 : 0;
            }

            float3 ramp(float v)
            {
                // the colour of the ramp in gamma; Color properties arrive linear
                float3 d = pow(_Deep.rgb, 1 / 2.2), m = pow(_Mid.rgb, 1 / 2.2), i = pow(_Ice.rgb, 1 / 2.2);
                return v < .45 ? lerp(d, m, v / .45) : lerp(m, i, saturate((v - .45) / .45));
            }

            float value(float2 t)
            {
                float3 c = pow(tex2Dlod(_MainTex, float4((t + .5) * _MainTex_TexelSize.xy, 0, 0)).rgb, 1 / 2.2);
                return dot(c, float3(.3, .59, .11));
            }

            fixed4 frag(v2f_img i) : SV_Target
            {
                float2 t = floor(i.uv * _MainTex_TexelSize.zw);
                fixed4 src = tex2Dlod(_MainTex, float4((t + .5) * _MainTex_TexelSize.xy, 0, 0));
                float3 c = pow(src.rgb, 1 / 2.2);
                float v = dot(c, float3(.3, .59, .11));
                float3 col = lerp(c, ramp(v), _Cold);
                if (v > .32 && rime(t) > 0)
                {
                    float3 frost = ramp(saturate(v + .22));
                    if (rime(t + float2(0, 1)) < .5 || value(t + float2(0, 1)) <= .32) frost = ramp(saturate(v + .32));
                    if (hash(t + 3.3) < .04) frost = pow(float3(.9, .95, 1), 1 / 2.2);
                    col = lerp(col, frost, saturate(_Cold / .7));
                }
                return fixed4(pow(col, 2.2), src.a);
            }
            ENDCG
        }
    }
}
