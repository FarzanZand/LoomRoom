// Asset Studio's paint picking: writes each visible fragment's texture coordinate and which part and material it
// belongs to, so a click paints exactly the texel that is on screen under the mouse.
Shader "Hidden/LoomRoom/Studio Pick"
{
    Properties
    {
        _Id ("Part and material", Float) = 0
        _Cull ("Cull", Float) = 2
        _MainTex ("Texture (for alpha clip)", 2D) = "white" {}
        _Cutoff ("Alpha clip (0: none)", Float) = 0
    }
    SubShader
    {
        Pass
        {
            Cull [_Cull]
            ZTest LEqual
            ZWrite On
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float _Id, _Cutoff;
            sampler2D _MainTex;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata_base v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord.xy; return o; }
            // A texel its material clips away isn't picked: the click goes through to what is drawn behind it.
            float4 frag(v2f i) : SV_Target { if (_Cutoff > 0) clip(tex2D(_MainTex, i.uv).a - _Cutoff); return float4(i.uv, _Id, 1); }
            ENDCG
        }
    }
}
