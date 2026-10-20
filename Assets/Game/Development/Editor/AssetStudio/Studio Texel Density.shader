// Asset Studio's texel density view: the model's own texture with every texel shown as a checker square, tinted by how
// many texels a metre it gets there against the target: green on target, blue too few (blurry, big pixels), red too
// many (finer than its neighbours). One step of colour is a factor of two.
Shader "Hidden/LoomRoom/Studio Texel Density"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Target ("Texels per metre", Float) = 128
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _Target;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 world : TEXCOORD1; };
            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float2 tc = i.uv * _MainTex_TexelSize.zw;
                float2 dx = ddx(tc), dy = ddy(tc);
                float texelArea = abs(dx.x * dy.y - dx.y * dy.x);
                float worldArea = length(cross(ddx(i.world), ddy(i.world)));
                float perMetre = sqrt(texelArea / max(worldArea, 1e-12));
                float l = clamp(log2(max(perMetre, 1e-4) / _Target), -2, 2) / 2;
                float3 good = float3(0.25, 0.85, 0.3);
                float3 heat = l < 0 ? lerp(good, float3(0.2, 0.4, 1), -l) : lerp(good, float3(1, 0.25, 0.2), l);
                float2 cell = floor(tc);
                float check = fmod(abs(cell.x + cell.y), 2);
                // Mixed as seen on screen (the project renders in linear colour, where the tint would drown dark art).
                float3 tex = pow(saturate(tex2D(_MainTex, i.uv).rgb), 1 / 2.2);
                float3 col = lerp(tex, heat, 0.38) * (check > 0.5 ? 1 : 0.8);
                return fixed4(pow(col, 2.2), 1);
            }
            ENDCG
        }
    }
}
