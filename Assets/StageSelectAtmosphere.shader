Shader "UI/IronBallGirl/StageSelectAtmosphere"
{
    Properties
    {
        [PerRendererData] _MainTex ("Artwork", 2D) = "white" {}
        _Mode ("0 reflection / 1 vignette / 2 energy / 3 label / 4 console reflection / 5 sheen / 6 room / 7 shadow / 8 cable / 9 assembly", Float) = 0
        _PixelGrid ("Room pixel grid", Vector) = (320,180,0,0)
        _Exposure ("Room exposure (linear)", Range(0,1)) = .32
        _Progress ("Progress", Range(0,1)) = 0
        _Intensity ("Intensity", Range(0,2)) = 1
        _Accent ("Light colour", Color) = (1,.35,.65,1)
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct appdata { float4 vertex:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
            struct v2f { float4 vertex:SV_POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; float4 world:TEXCOORD1; };
            sampler2D _MainTex;
            float4 _MainTex_TexelSize, _Accent, _ClipRect, _PixelGrid;
            float _Mode, _Progress, _Intensity, _Exposure;
            float Hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            v2f vert(appdata v) { v2f o; o.world=v.vertex; o.vertex=UnityObjectToClipPos(v.vertex); o.uv=v.uv; o.color=v.color; return o; }
            float4 SoftSample(float2 uv,float blur)
            {
                float2 d=abs(_MainTex_TexelSize.xy)*blur;
                return tex2D(_MainTex,uv)*.28
                    +(tex2D(_MainTex,uv+float2(d.x,0))+tex2D(_MainTex,uv-float2(d.x,0)))*.18
                    +(tex2D(_MainTex,uv+float2(0,d.y))+tex2D(_MainTex,uv-float2(0,d.y)))*.1
                    +(tex2D(_MainTex,uv+d)+tex2D(_MainTex,uv-d))*.08;
            }
            float4 frag(v2f i):SV_Target
            {
                float2 uv=i.uv;
                float4 c=1;
                if(_Mode<.5 || (_Mode>3.5 && _Mode<4.5))
                {
                    // A reflected image, with roughness increasing away from its contact plane.
                    float depth=1-uv.y;
                    float2 sampleUV=float2((uv.x-.5)*(1-depth*.16)+.5,.02+depth*.94);
                    sampleUV.x+=(Hash(floor(uv*float2(320,52)))-.5)*.016*depth;
                    // Begin at the visible bottom of the sprite, past its transparent margin.
                    if(_Mode>3.5) sampleUV.y=depth*.59+.13;
                    c=SoftSample(sampleUV,2+depth*9);
                    float broken=lerp(.42,1,Hash(floor(uv*float2(320,80))));
                    c.rgb*=float3(.68,.76,.94);
                    c.a*=pow(saturate(uv.y),1.35)*smoothstep(0,.12,uv.x)*smoothstep(0,.12,1-uv.x)*broken*.62;
                }
                else if(_Mode<1.5)
                {
                    float2 p=(uv-.5)*float2(1.22,1);
                    float edge=smoothstep(.16,.73,length(p));
                    c=float4(.006,.008,.018,edge*.72);
                }
                else if(_Mode<2.5)
                {
                    // Quantised light clusters sit alongside pixel sprites; only their halo is soft.
                    float2 p=(floor(uv*float2(360,220))+.5)/float2(360,220)-.5;
                    p.y*=1.35;
                    float radius=length(p), angle=atan2(p.y,p.x);
                    float ringRadius=lerp(.07,.58,_Progress);
                    float ring=exp(-abs(radius-ringRadius)*160)*pow(1-_Progress,1.5);
                    float rays=pow(saturate(sin(angle*11+radius*12)),18)*exp(-radius*7)*.3;
                    float core=exp(-radius*radius*42)*.38;
                    c=float4(lerp(_Accent.rgb,float3(1,.91,.83),ring*.65),saturate(ring+rays+core));
                }
                else if(_Mode<3.5)
                {
                    c=tex2D(_MainTex,float2(uv.x,(uv.y-.5)*.47+.57));
                    float edge=smoothstep(0,.1,min(uv.x,1-uv.x))*smoothstep(0,.16,min(uv.y,1-uv.y));
                    c.rgb=lerp(c.rgb*.55,c.rgb*.82,edge);
                    c.rgb*=lerp(float3(.76,.82,.96),float3(1,.95,.88),uv.y);
                    // A printed-paper weave, without blurring the label's pixel illustration.
                    c.rgb*=.97+.03*Hash(floor(uv*float2(336,100)));
                    c.a=1;
                }
                else if(_Mode<5.5)
                {
                    float streak=exp(-pow((uv.x+uv.y*.32-lerp(-.25,1.55,_Progress))*22,2));
                    c=float4(1,.88,.77,streak*.26);
                }
                else if(_Mode<6.5)
                {
                    // Sample actual source clusters at a low, fixed art resolution.
                    float2 grid=max(_PixelGrid.xy,1);
                    c=tex2D(_MainTex,(floor(uv*grid)+.5)/grid);
                    float grey=dot(c.rgb,float3(.2126,.7152,.0722));
                    c.rgb=lerp(grey.xxx,c.rgb,.72)*_Exposure;
                    #ifndef UNITY_COLORSPACE_GAMMA
                        c.rgb=LinearToGammaSpace(c.rgb);
                    #endif
                    c.rgb=floor(c.rgb*31+.5)/31;
                    #ifndef UNITY_COLORSPACE_GAMMA
                        c.rgb=GammaToLinearSpace(c.rgb);
                    #endif
                }
                else if(_Mode<7.5)
                {
                    float2 p=((floor(uv*float2(240,20))+.5)/float2(240,20)-.5)*2;
                    c=float4(0,0,0,pow(saturate(1-dot(p,p)),.7));
                }
                else if(_Mode<8.5)
                {
                    // Pixel-stepped AV cable, with a floor shadow and one narrow reflected edge.
                    float2 p=(floor(uv*float2(130,100))+.5)*2+float2(290,-300);
                    float2 last=float2(285,-228), closest=last;
                    float distance=1000, shadowDistance=1000;
                    [unroll] for(int n=1;n<=20;n++)
                    {
                        float t=n/20.0, s=1-t;
                        float2 next=s*s*s*float2(285,-228)+3*s*s*t*float2(500,-288)
                            +3*s*t*t*float2(460,-132)+t*t*t*float2(377,-144);
                        float2 segment=next-last;
                        float u=saturate(dot(p-last,segment)/max(dot(segment,segment),.001));
                        float2 onCurve=last+segment*u;
                        float d=length(p-onCurve);
                        if(d<distance) { distance=d; closest=onCurve; }
                        float2 shadowPoint=p-float2(3,-4);
                        float v=saturate(dot(shadowPoint-last,segment)/max(dot(segment,segment),.001));
                        shadowDistance=min(shadowDistance,length(shadowPoint-last-segment*v));
                        last=next;
                    }
                    float cable=1-step(3.5,distance);
                    float highlight=step(.65,p.y-closest.y)*(1-step(2.8,distance));
                    float3 color=lerp(float3(.025,.023,.034),float3(.14,.16,.21),highlight);
                    #ifndef UNITY_COLORSPACE_GAMMA
                        color=GammaToLinearSpace(color);
                    #endif
                    c=float4(color,cable+(1-cable)*(1-smoothstep(3,7,shadowDistance))*.45);
                }
                else
                {
                    // Reuse the final crystal's inner facets, building a chipped rectangular core first.
                    float2 grid=float2(168,90);
                    float2 p=(floor(uv*grid)+.5)/grid;
                    c=tex2D(_MainTex,p*.66+.17);
                    float edge=min(min(p.x,1-p.x)*grid.x,min(p.y,1-p.y)*grid.y);
                    float chip=Hash(floor(p*float2(28,15)))*2;
                    c.a*=step(chip,edge);
                }
                c.a*=_Intensity;
                c*=i.color;
                #ifdef UNITY_UI_CLIP_RECT
                    c.a*=UnityGet2DClipping(i.world.xy,_ClipRect);
                #endif
                return c;
            }
            ENDCG
        }
    }
}
