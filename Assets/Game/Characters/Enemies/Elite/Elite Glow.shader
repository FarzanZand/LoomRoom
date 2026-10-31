// The soft glow round an elite enemy (EliteEnemy; the user's pick of four looks, 2026-10-31): its body mesh drawn again
// on the same bones, pushed out along smoothed normals by _Width (metres), back faces only, added to what is behind it.
// It is strongest where the shell turns away from the eye (against the body) and fades to nothing at its own edge, in
// _Steps hard steps so it reads as pixel art, not an airbrushed haze. Pushed back in depth by _DepthPush (a share of the
// distance) so it never lies over the body itself, only round it.
Shader "LoomRoom/Elite Glow"
{
    Properties
    {
        _Color ("Colour", Color) = (1, 0.28, 0.2, 1)
        _Width ("Width (metres)", Float) = 0.06
        _Strength ("Strength", Float) = 0.14
        _Falloff ("Falloff", Float) = 1
        _Steps ("Steps", Float) = 3
        _DepthPush ("Depth push (share of distance)", Float) = 0.1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "IgnoreProjector" = "True" }
        Pass
        {
            Cull Front
            ZWrite Off
            ZTest LEqual
            Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color;
            float _Width, _Strength, _Falloff, _Steps, _DepthPush;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float3 normal : TEXCOORD0; float3 toCam : TEXCOORD1; };
            v2f vert(appdata v)
            {
                v2f o;
                float3 world = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 n = normalize(UnityObjectToWorldNormal(v.normal));
                world += n * _Width;
                float3 toCam = _WorldSpaceCameraPos - world;
                o.normal = n; o.toCam = toCam;
                world -= toCam * _DepthPush;
                o.pos = mul(UNITY_MATRIX_VP, float4(world, 1));
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                // back faces: 0 where the shell's edge is seen side on, rising toward the body behind it
                float a = saturate(-dot(normalize(i.normal), normalize(i.toCam)));
                a = pow(a, _Falloff);
                a = floor(a * _Steps + .5) / _Steps;
                return fixed4(_Color.rgb * a * _Strength, 0);
            }
            ENDCG
        }
    }
}
