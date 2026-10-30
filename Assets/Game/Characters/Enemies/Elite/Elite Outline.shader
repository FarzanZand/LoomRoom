// The tinted outline round an elite enemy (EliteEnemy): its body mesh drawn again on the same bones, pushed out along
// smoothed normals by _Width (metres), back faces only, one flat colour, unlit. Pushed back in depth by _DepthPush (a
// share of the distance) so it never lies in front of the body itself: only the rim past the silhouette shows, not the
// edges of armour plates and bones layered on one another inside it.
Shader "LoomRoom/Elite Outline"
{
    Properties
    {
        _Color ("Colour", Color) = (1, 0.72, 0.3, 1)
        _Width ("Width (metres)", Float) = 0.016
        _DepthPush ("Depth push (share of distance)", Float) = 0.04
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+5" }
        Pass
        {
            Cull Front
            ZWrite On
            ZTest LEqual
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color;
            float _Width, _DepthPush;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            float4 vert(appdata v) : SV_POSITION
            {
                float3 world = mul(unity_ObjectToWorld, v.vertex).xyz;
                world += normalize(UnityObjectToWorldNormal(v.normal)) * _Width;
                // further from the camera by a share of its distance
                float3 toCam = _WorldSpaceCameraPos - world;
                world -= toCam * _DepthPush;
                return mul(UNITY_MATRIX_VP, float4(world, 1));
            }
            fixed4 frag() : SV_Target { return _Color; }
            ENDCG
        }
    }
}
