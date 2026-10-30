Shader "Hidden/IronBallGirl/StageSelectGlass"
{
    Properties { _MainTex ("Preview",2D)="black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "StageSelectCRTShape.cginc"
            sampler2D _MainTex;
            float4 _MainTex_TexelSize, _GlassSize, _FaceShape;
            float _PreviewTime, _Curvature, _Softness, _Brightness, _Scanlines, _Vignette, _Noise, _Glass, _SignalLost, _Disturbance, _Clear;
            float _ScanlineCount, _RGB;
            float Hash(float2 p) { float3 q=frac(float3(p.xyx)*.1031); q+=dot(q,q.yzx+33.33); return frac((q.x+q.y)*q.z); }
            fixed4 frag(v2f_img i):SV_Target
            {
                float2 p=i.uv*2-1;
                float curve=_Curvature*(1-_Clear);
                // Match the pause screen's radial barrel mapping, with bounded overscan so
                // edge pixels stay inside the source instead of smearing the last texture column.
                float2 uv=.5+p*(1+curve*dot(p,p))/(1+curve*1.4)*.5;
                float row=floor(i.uv.y*300), frame=floor(_PreviewTime*30);
                uv.x+=(Hash(float2(floor(row/8),frame))-.5)*_Disturbance*.012;
                float2 d=abs(_MainTex_TexelSize.xy)*_Softness*(1-_Clear);
                float3 c=tex2D(_MainTex,uv).rgb*.5;
                c+=tex2D(_MainTex,uv+float2(d.x,0)).rgb*.125;
                c+=tex2D(_MainTex,uv-float2(d.x,0)).rgb*.125;
                c+=tex2D(_MainTex,uv+float2(0,d.y)).rgb*.125;
                c+=tex2D(_MainTex,uv-float2(0,d.y)).rgb*.125;
                float edge=saturate(dot(p,p)*.5);
                float2 rgb=float2(abs(_MainTex_TexelSize.x)*_RGB*(.25+edge)*(1-_Clear),0);
                c.r=lerp(c.r,tex2D(_MainTex,uv+rgb).r,.45);
                c.b=lerp(c.b,tex2D(_MainTex,uv-rgb).b,.45);
                float snow=Hash(floor(i.uv*float2(620,390))+frame*float2(13,71));
                float band=Hash(float2(floor(i.uv.y*48),frame));
                float level=.09+snow*.42+band*.055;
                float3 snowColor=level*float3(.88,.94,1);
                #ifndef UNITY_COLORSPACE_GAMMA
                    snowColor=GammaToLinearSpace(snowColor);
                #endif
                c=lerp(c*_Brightness,snowColor,_SignalLost);
                // 300 lines on a 600px target sampled every line at cos(pi*n+pi/2)=0.
                // Use a non-Nyquist frequency and warped UVs so visible scanlines follow the glass.
                float scan=.5+.5*cos(uv.y*_ScanlineCount*UNITY_PI*2+.35);
                c*=1-scan*_Scanlines*(1-_Clear);
                c*=1-_Vignette*(1-_Clear)*smoothstep(.25,1.8,dot(p,p));
                c+=(snow-.5)*_Noise*(1-_Clear)*.08;
                // Broad curved highlight follows the convex face instead of outlining a second screen.
                float reflection=pow(saturate(1-abs(uv.x+uv.y*.55-.38)*2.8),12)*.22;
                float upper=pow(saturate(1-abs(p.y-.86)*7),3)*(.45+.55*saturate(1-p.x*p.x));
                float sheen=exp(-dot((uv-float2(.19,.84))*float2(4,6),(uv-float2(.19,.84))*float2(4,6)))*.07;
                c+=(reflection+upper*.08+sheen)*_Glass*(1-_Clear)*float3(.45,.67,1);
                float sd=StageSelectFaceDistance((i.uv-.5)*_GlassSize.xy,_GlassSize.xy*.5-1,_FaceShape.xy,_Clear);
                float aa=max(fwidth(sd),.65);
                float alpha=1-smoothstep(-aa,aa,sd);
                return float4(max(c,0),alpha);
            }
            ENDCG
        }
    }
    Fallback Off
}
