Shader "UI/IronBallGirl/StageSelectSurface"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Mode ("0 Hardware / 1 Glow / 2 Void", Float) = 0
        _PixelGrid ("Pixel Grid", Vector) = (400,240,0,0)
        _Tone ("Surface Tone", Color) = (1,1,1,1)
        _SurfaceClipY ("Hardware front lip clip", Float) = 1
        _SpriteUVRect ("Sprite UV rectangle", Vector) = (0,0,1,1)
        _Window ("CRT source window", Vector) = (.16,.20,.84,.84)
        _DisplayWindow ("CRT display window", Vector) = (.08,.12,.92,.92)
        _HousingSize ("Housing / glass dimensions", Vector) = (840,572,720,460)
        _FaceShape ("CRT edge bow / corner radius", Vector) = (.06,30,0,0)
        _FaceOffset ("CRT glass center in housing", Vector) = (0,50,0,0)
        _FaceClear ("CRT entry flattening", Range(0,1)) = 0
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
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #include "StageSelectCRTShape.cginc"
            struct appdata { float4 vertex:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
            struct v2f { float4 vertex:SV_POSITION; fixed4 color:COLOR; float2 uv:TEXCOORD0; float4 world:TEXCOORD1; };
            sampler2D _MainTex;
            float4 _PixelGrid, _Tone, _ClipRect, _Window, _DisplayWindow, _HousingSize, _SpriteUVRect, _FaceShape, _FaceOffset;
            float _Mode, _SurfaceClipY, _FaceClear;
            float Hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float RoundBox(float2 p,float2 b,float r) { float2 q=abs(p)-b+r; return length(max(q,0))+min(max(q.x,q.y),0)-r; }
            float RemapWindow(float x, float a, float b, float u, float v)
            { return x<a ? x/a*u : x>b ? v+(x-b)/(1-b)*(1-v) : u+(x-a)/(b-a)*(v-u); }
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
                    float localY=(i.uv.y-_SpriteUVRect.y)/max(_SpriteUVRect.w,.0001);
                    c.a *= step(.26,c.a)*step(localY,_SurfaceClipY);
                }
                else if (_Mode < 1.5)
                {
                    float2 p=(uv-.5)*2;
                    float glow=pow(saturate(1-dot(p,p)),2.6);
                    c=float4(_Tone.rgb,glow*_Tone.a);
                }
                else if (_Mode < 2.5)
                {
                    float2 p=uv-.5;
                    float vignette=saturate(1-dot(p*float2(1.15,.85),p*float2(1.15,.85))*2.8);
                    float dust=Hash(floor(uv*float2(320,180)))*.007;
                    float ground=exp(-dot((uv-float2(.5,.11))*float2(3.2,13),(uv-float2(.5,.11))*float2(3.2,13)));
                    float3 col=lerp(float3(.018,.018,.032),float3(.043,.041,.065),vignette);
                    col += ground*float3(.034,.018,.047)+dust;
                    float floorMask=1-smoothstep(.24,.31,uv.y);
                    float paving=Hash(floor(float2(uv.x*94,uv.y*42)));
                    col += floorMask*(paving-.45)*.018;
                    col += floorMask*exp(-abs(uv.x-.29)*22)*float3(.022,.005,.015)*(1-uv.y*2);
                    col += floorMask*exp(-abs(uv.x-.71)*22)*float3(.006,.014,.027)*(1-uv.y*2);
                    // This palette is authored as display colors; hardware textures are already decoded by the sampler.
                    #ifndef UNITY_COLORSPACE_GAMMA
                        col=GammaToLinearSpace(col);
                    #endif
                    c=float4(col,1);
                }
                else if (_Mode < 3.5)
                {
                    float2 p=float2(RemapWindow(i.uv.x,_DisplayWindow.x,_DisplayWindow.z,_Window.x,_Window.z),
                        RemapWindow(i.uv.y,_DisplayWindow.y,_DisplayWindow.w,_Window.y,_Window.w));
                    p=(floor(p*grid)+.5)/grid;
                    c=tex2D(_MainTex,p)*_Tone;
                    float inWindow=step(_Window.x,p.x)*step(p.x,_Window.z)*step(_Window.y,p.y)*step(p.y,_Window.w);
                    c.a*=1-inWindow*(1-smoothstep(.004,.022,max(c.r,max(c.g,c.b))));
                }
                else
                {
                    // Stepped, faceted hardware around a gently bowed glass opening, matching the reference CRT.
                    float2 p=(i.uv-.5)*_HousingSize.xy;
                    float outer=RoundBox(p,_HousingSize.xy*.5-2,3);
                    float inner=StageSelectFaceDistance(p-_FaceOffset.xy,_HousingSize.zw*.5-1,_FaceShape.xy,_FaceClear);
                    float2 pixel=(floor(p/2)+.5)*2;
                    float depth=-RoundBox(pixel,_HousingSize.xy*.5-2,3);
                    float top=saturate(pixel.y/_HousingSize.y+.5);
                    float left=saturate(.5-pixel.x/_HousingSize.x);
                    float3 col=lerp(float3(.044,.036,.055),float3(.095,.068,.083),top);
                    float3 edgeLight=float3(.025,.035,.055)*top+float3(.042,.009,.019)*left;
                    if(depth<5) col=float3(.025,.021,.034);
                    else if(depth<12) col=float3(.052,.042,.057)+edgeLight;
                    else if(depth<17) col=float3(.021,.02,.03);
                    else if(depth<23) col=float3(.055,.043,.057)+edgeLight*.7;
                    float rim=floor(max(inner,0)/2)*2;
                    if(rim<3) col=float3(.012,.015,.023);
                    else if(rim<6) col=float3(.025,.045,.071);
                    else if(rim<11) col=float3(.057,.045,.063)+edgeLight*.45;
                    else if(rim<15) col=float3(.02,.023,.034);
                    else if(rim<20) col=float3(.053,.041,.059)+edgeLight;
                    col+=exp(-abs(inner-8)*.18)*float3(.012,.026,.048);
                    col+=exp(-abs(pixel.y-(_HousingSize.y*.5-19))*.7)*float3(.014,.025,.048);
                    col+=(Hash(floor(pixel))-.5)*.012;
                    #ifndef UNITY_COLORSPACE_GAMMA
                        col=GammaToLinearSpace(col);
                    #endif
                    float aa=max(fwidth(outer),.55), innerAA=max(fwidth(inner),.55);
                    c=float4(col,(1-smoothstep(-aa,aa,outer))*smoothstep(-innerAA,innerAA,inner));
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
