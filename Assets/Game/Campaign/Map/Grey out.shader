// A texture made grey: the campaign map's beaten places draw their props with greyed copies of their textures
// (CampaignMap.Greyed, through Graphics.Blit). Amount 1 is fully grey; Darken scales the result.
Shader "Hidden/LoomRoom/Grey out"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Amount ("Amount", Range(0, 1)) = .85
        _Darken ("Darken", Range(0, 1)) = .8
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float _Amount, _Darken;
            fixed4 frag(v2f_img i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv);
                float grey = dot(c.rgb, float3(.299, .587, .114));
                c.rgb = lerp(c.rgb, grey.xxx, _Amount) * _Darken;
                return c;
            }
            ENDCG
        }
    }
}
