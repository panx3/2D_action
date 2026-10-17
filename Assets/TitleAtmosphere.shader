Shader "UI/Title Atmosphere"
{
    Properties
    {
        [PerRendererData] _MainTex ("Background", 2D) = "white" {}
        _TitleTime ("Unscaled clock", Float) = 0
        _ViewAspect ("View aspect", Float) = 1.7777778
        _Motion ("Motion strength", Range(0,2)) = 1
        _Scanlines ("Scanlines", Range(0,0.1)) = 0.025
        _Grain ("Grain", Range(0,0.04)) = 0.009
        _Response ("Confirm", Range(0,1)) = 0
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
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _TitleTime, _ViewAspect, _Motion, _Scanlines, _Grain, _Response;
            v2f vert(appdata v) { v2f o; o.vertex=UnityObjectToClipPos(v.vertex); o.uv=v.uv; o.color=v.color; return o; }
            fixed4 frag(v2f i):SV_Target
            {
                float2 screen=i.uv;
                float aspect=_MainTex_TexelSize.z/_MainTex_TexelSize.w;
                float2 crop=float2(min(1,_ViewAspect/aspect),min(1,aspect/_ViewAspect));
                float2 uv=(screen-.5)*crop*.992+.5;
                float t=_TitleTime;
                float farMask=smoothstep(.38,.72,uv.x);
                float sky=smoothstep(.48,.82,uv.y);
                float2 movement=float2(sin(t*6.283185/18),cos(t*6.283185/20));
                uv+=movement*float2(.0012,.00065)*_Motion*(.18+farMask);
                // Soft spatial masks keep the original painting intact: subpixel weight, no cutout edges.
                float ball=1-smoothstep(.15,.29,length((uv-float2(.25,.53))*float2(1,0.72)));
                float2 radial=uv-float2(.24,.48);
                uv+=float2(-radial.y,radial.x)*sin(t*6.283185/16)*.0018*ball*_Motion;
                uv.x+=sin(t*6.283185/17+uv.y*4)*.0017*sky*farMask*_Motion;
                fixed3 c=tex2D(_MainTex,uv).rgb;
                // Broad, faint moving cloud light, not a second opaque copy of the scenery.
                float haze=sin(uv.x*9+uv.y*4+t*6.283185/20)*.5+.5;
                c=lerp(c,float3(.88,.94,1),haze*.025*farMask*sky);
                float cloud=pow(.5+.5*sin(uv.x*10-uv.y*3-t*6.283185/20),5);
                float cloudHeight=exp(-pow((uv.y-.73)/.16,2));
                c=lerp(c,float3(.91,.96,1),cloud*cloudHeight*.13*farMask);
                float lowMist=pow(.5+.5*sin(uv.x*7+uv.y*5+t*6.283185/18),4);
                c=lerp(c,float3(.84,.91,1),lowMist*(1-smoothstep(.1,.4,uv.y))*.075);
                c*=1+sin(t*6.283185/15)*.009;
                c+=ball*pow(saturate(c.r+c.g+c.b-1.3),2)*(.008+.008*sin(t*6.283185/18));
                // A feathered indigo veil gives small type stable contrast over the white ruins.
                float veil=exp(-pow((screen.x-.745)/.235,4)-pow((screen.y-.31)/.235,4));
                c=lerp(c,float3(.018,.044,.082),veil*.88);
                float vignette=smoothstep(.32,.78,length((screen-.5)*float2(.87,1)));
                c*=1-vignette*.15;
                c+=_Response*.018;
                float scan=.5+.5*cos(i.vertex.y*3.14159265);
                float noise=frac(sin(dot(floor(i.vertex.xy),float2(12.9898,78.233))+floor(t*12))*43758.5453)-.5;
                c=c*(1-scan*_Scanlines)+noise*_Grain;
                return fixed4(c,i.color.a);
            }
            ENDCG
        }
    }
}
