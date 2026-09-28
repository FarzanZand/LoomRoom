Shader "LoomRoom/UI/Copper Plum"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _PanelColor ("Panel", Color) = (0.204,0.184,0.227,1)
        _PanelShading ("Panel shading", Range(0,1)) = 0.25
        _ShadowColor ("Border shadow", Color) = (0.09,0.075,0.11,1)
        _BorderColor ("Border copper", Color) = (0.64,0.34,0.24,1)
        _HighlightColor ("Border highlight", Color) = (0.88,0.53,0.35,1)
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct appdata { float4 vertex:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex:SV_POSITION; fixed4 color:COLOR; float2 uv:TEXCOORD0; float4 local:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            sampler2D _MainTex;
            fixed4 _Color, _TextureSampleAdd, _PanelColor, _ShadowColor, _BorderColor, _HighlightColor;
            float _PanelShading;
            float4 _ClipRect;
            v2f vert(appdata v)
            {
                v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.local=v.vertex; o.vertex=UnityObjectToClipPos(v.vertex); o.uv=v.uv; o.color=v.color*_Color; return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                fixed4 source=tex2D(_MainTex,i.uv)+_TextureSampleAdd;
                // Kit sprites are greyscale value maps (UIManager sets the colours):
                // black = shadow, dark grey = panel with its shading, light grey to white = frame to highlight.
                float value=source.r;
                #ifndef UNITY_COLORSPACE_GAMMA
                value=LinearToGammaSpace(value);
                #endif
                fixed3 palette=value<0.07 ? _ShadowColor.rgb : value<0.3
                    ? _PanelColor.rgb*lerp(1-0.6*_PanelShading,1+0.4*_PanelShading,saturate((value-0.07)/0.23))
                    : lerp(_BorderColor.rgb,_HighlightColor.rgb,saturate((value-0.3)/0.62));
                fixed4 result=fixed4(palette,source.a)*i.color;
                #ifdef UNITY_UI_CLIP_RECT
                result.a*=UnityGet2DClipping(i.local.xy,_ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(result.a-0.001);
                #endif
                return result;
            }
            ENDCG
        }
    }
}
