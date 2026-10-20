// Asset Studio's wireframe: plain lines in one colour, pulled a little toward the camera so they sit on the surface.
Shader "Hidden/LoomRoom/Studio Wire"
{
    Properties { _Color ("Color", Color) = (1, 0.7, 0.3, 1) }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+10" }
        Pass
        {
            ZWrite On
            ZTest LEqual
            Offset -1, -4
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color;
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag() : SV_Target { return _Color; }
            ENDCG
        }
    }
}
