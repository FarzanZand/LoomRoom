// A UI image fading out toward each of its edges by its own amount (the class painting on the cinematic versus screen:
// its sides and top melt into the glow behind it, its foot runs on behind the bottom bar). Fade is the share of the
// picture each edge fades over: left, right, bottom, top.
Shader "LoomRoom/UI Edge Fade"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Fade ("Fade (left, right, bottom, top)", Vector) = (0.24, 0.24, 0, 0.2)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" "PreviewType" = "Plane" "CanUseSpriteAtlas" = "True" }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };

            sampler2D _MainTex;
            fixed4 _Color;
            float4 _Fade;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            // 0 at the edge, 1 once a share of `width` in from it (no fade when width is 0).
            float Edge(float distance, float width)
            {
                return width <= 0.0001 ? 1.0 : smoothstep(0.0, 1.0, saturate(distance / width));
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * i.color;
                c.a *= Edge(i.uv.x, _Fade.x) * Edge(1.0 - i.uv.x, _Fade.y) * Edge(i.uv.y, _Fade.z) * Edge(1.0 - i.uv.y, _Fade.w);
                return c;
            }
            ENDCG
        }
    }
}
