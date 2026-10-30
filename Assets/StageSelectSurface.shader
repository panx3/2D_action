Shader "UI/IronBallGirl/StageSelectSurface"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Mode ("0 Hardware / 1 Glow / 2 Void", Float) = 0
        _PixelGrid ("Pixel Grid", Vector) = (400,240,0,0)
        _Tone ("Surface Tone", Color) = (1,1,1,1)
        _DstBlend ("Destination Blend", Float) = 10
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
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha [_DstBlend]
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
            struct appdata { float4 vertex:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
            struct v2f { float4 vertex:SV_POSITION; fixed4 color:COLOR; float2 uv:TEXCOORD0; float4 world:TEXCOORD1; };
            sampler2D _MainTex;
            float4 _PixelGrid, _Tone, _ClipRect;
            float _Mode;
            float Hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            v2f vert(appdata v)
            {
                v2f o; o.world=v.vertex; o.vertex=UnityObjectToClipPos(v.vertex); o.uv=v.uv; o.color=v.color; return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                float2 grid=max(_PixelGrid.xy,1);
                float2 uv=(floor(i.uv*grid)+.5)/grid;
                fixed4 c;
                if (_Mode < .5)
                {
                    c=tex2D(_MainTex,uv)*_Tone;
                    c.a *= step(.26,c.a);
                }
                else if (_Mode < 1.5)
                {
                    float2 p=(uv-.5)*2;
                    float glow=pow(saturate(1-dot(p,p)),2.6);
                    c=float4(_Tone.rgb,glow*_Tone.a);
                }
                else
                {
                    float2 p=uv-.5;
                    float vignette=saturate(1-dot(p*float2(1.15,.85),p*float2(1.15,.85))*2.8);
                    float dust=Hash(floor(uv*float2(320,180)))*.007;
                    float ground=exp(-dot((uv-float2(.5,.11))*float2(3.2,13),(uv-float2(.5,.11))*float2(3.2,13)));
                    float3 col=lerp(float3(.018,.018,.032),float3(.043,.041,.065),vignette);
                    col += ground*float3(.034,.018,.047)+dust;
                    // This palette is authored as display colors; hardware textures are already decoded by the sampler.
                    #ifndef UNITY_COLORSPACE_GAMMA
                        col=GammaToLinearSpace(col);
                    #endif
                    c=float4(col,1);
                }
                c *= i.color;
                #ifdef UNITY_UI_CLIP_RECT
                    c.a *= UnityGet2DClipping(i.world.xy,_ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                    clip(c.a-.001);
                #endif
                return c;
            }
            ENDCG
        }
    }
}
