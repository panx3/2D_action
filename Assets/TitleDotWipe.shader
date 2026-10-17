Shader "UI/Title Dot Wipe"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _BackdropTex ("Ruins in mist", 2D) = "black" {}
        _Progress ("Progress", Range(0,1)) = 0
        _Revealing ("Reveal", Float) = 0
        _RightToLeft ("Right to left", Float) = 0
        _PixelSize ("Dot size at 1080p", Float) = 8
        _Scatter ("Edge scatter", Range(.02,.2)) = .085
        _PatternStrength ("Interlude atmosphere", Range(0,1)) = .85
        _TransitionTime ("Unscaled clock", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; };
            struct v2f { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; };
            sampler2D _BackdropTex;
            float4 _BackdropTex_TexelSize;
            float _Progress, _Revealing, _RightToLeft, _PixelSize, _Scatter, _PatternStrength, _TransitionTime;
            float Hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            v2f vert(appdata v) { v2f o; o.vertex=UnityObjectToClipPos(v.vertex); o.uv=v.uv; o.color=v.color; return o; }
            fixed4 frag(v2f i):SV_Target
            {
                float2 grid=float2(_ScreenParams.x/_ScreenParams.y,1)*1080/max(3,_PixelSize);
                float2 cell=floor(i.uv*grid);
                float x=(cell.x+.5)/grid.x;
                if (_RightToLeft>.5) x=1-x;
                // Each cell has one stable arrival time: no temporal noise or glitch.
                float order=x+(Hash(cell)-.5)*_Scatter+(Hash(float2(2,cell.y))-.5)*_Scatter*.25;
                float threshold=(order+_Scatter*.7)/(1+_Scatter*1.4);
                float appeared=step(threshold,_Progress);
                if (_Progress<=0) appeared=0;
                if (_Progress>=1) appeared=1;
                float mask=lerp(appeared,1-appeared,step(.5,_Revealing));
                float aspect=_ScreenParams.x/_ScreenParams.y;
                float artAspect=_BackdropTex_TexelSize.z/max(1,_BackdropTex_TexelSize.w);
                float2 crop=float2(min(1,aspect/artAspect),min(1,artAspect/aspect));
                float2 uv=(i.uv-.5)*crop*.994+.5;
                uv.x+=sin(_TransitionTime*.17)*.0015;
                float3 art=tex2D(_BackdropTex,uv).rgb;
                float gray=dot(art,float3(.299,.587,.114));
                float fog=pow(.5+.5*sin(i.uv.x*7+i.uv.y*5-_TransitionTime*.24),4);
                float3 color=lerp(float3(.004,.005,.007),gray*.9,_PatternStrength);
                color+=fog*(1-smoothstep(.1,.65,i.uv.y))*.006*_PatternStrength;
                return fixed4(color,mask*i.color.a);
            }
            ENDCG
        }
    }
}
