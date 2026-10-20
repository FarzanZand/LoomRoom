// The blue outline round a room object the current quest points at (QuestGlow): the object's mesh drawn again, pushed
// out along its smoothed normals by _Width (world units), back faces only, in one flat colour. Lighting doesn't touch it.
Shader "LoomRoom/Quest Outline"
{
    Properties
    {
        _Color ("Colour", Color) = (0.45, 0.8, 1, 1)
        _Width ("Width (world units)", Float) = 0.6
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
            float _Width;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            float4 vert(appdata v) : SV_POSITION
            {
                float3 world = mul(unity_ObjectToWorld, v.vertex).xyz;
                world += normalize(UnityObjectToWorldNormal(v.normal)) * _Width;
                return mul(UNITY_MATRIX_VP, float4(world, 1));
            }
            fixed4 frag() : SV_Target { return _Color; }
            ENDCG
        }
    }
}
