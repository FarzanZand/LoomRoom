// Asset Studio's paint picking: writes each visible fragment's texture coordinate and which part and material it
// belongs to, so a click paints exactly the texel that is on screen under the mouse.
Shader "Hidden/LoomRoom/Studio Pick"
{
    Properties
    {
        _Id ("Part and material", Float) = 0
        _Cull ("Cull", Float) = 2
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
            float _Id;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata_base v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord.xy; return o; }
            float4 frag(v2f i) : SV_Target { return float4(i.uv, _Id, 1); }
            ENDCG
        }
    }
}
