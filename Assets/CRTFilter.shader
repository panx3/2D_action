Shader "Hidden/IronBallGirl/CRTFilter"
{
    Properties { _MainTex ("Source", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex;
        float4 _MainTex_TexelSize;
        float _MasterStrength, _ScanlineStrength, _NoiseStrength, _VignetteStrength;
        float _ChromaticStrength, _ContrastStrength, _CRTTime;
        float _OverlayActive, _OverlayGlass, _OverlayBlend;
        float4 _OverlayShape; // curvature, vignette, acquisition pulse, screen scale
        float4 _OverlaySignal; // scanlines, noise, RGB pixels, transition jitter pixels
        float _BackdropBlur, _BackdropBrightness;
        float Random(float2 p) { return frac(sin(dot(p, float2(12.9898,78.233))) * 43758.5453); }
        float StaticNoise(float2 cell, float frame)
        {
            float3 seed = frac(float3(cell,frame)*float3(0.1031,0.1030,0.0973));
            seed += dot(seed,seed.yxz+33.33);
            return frac((seed.x+seed.y)*seed.z);
        }
        float RoundedScreen(float2 p, float2 bounds, float radius)
        {
            float2 q = abs(p) - bounds + radius;
            return length(max(q,0)) + min(max(q.x,q.y),0) - radius;
        }
        ENDCG
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            fixed4 frag(v2f_img input) : SV_Target
            {
                float2 uv = input.uv;
                float2 p = uv * 2 - 1;
                float mask = 1;
                float bezel = 0;
                float pulse = _OverlayShape.z * _OverlayActive;
                float blend = _OverlayBlend * _OverlayActive;
                if (_OverlayActive > 0.5)
                {
                    p /= lerp(1,max(_OverlayShape.w,0.85),blend);
                    p *= 1 + _OverlayShape.x * blend * dot(p,p);
                    uv = p * 0.5 + 0.5;
                    float sd = RoundedScreen(p, float2(0.975,0.955),0.15);
                    mask = lerp(1,1-smoothstep(-0.004,0.004,sd),blend);
                    bezel = (1-smoothstep(0.008,0.065,abs(sd))) * 0.055;
                    // Fine row chatter and wider horizontal bands share the short burst envelope.
                    float renderRow = floor(input.uv.y*abs(_MainTex_TexelSize.w));
                    float signalFrame = floor(_CRTTime*60);
                    float rowJitter = round(StaticNoise(float2(0,floor(renderRow*0.5)),signalFrame)*2-1);
                    float bandJitter = round(StaticNoise(float2(7,floor(renderRow/12)),signalFrame)*2-1);
                    uv.x += (rowJitter*0.4+bandJitter*0.6)*abs(_MainTex_TexelSize.x)*pulse*_OverlaySignal.w;
                }
                float2 centered = uv - 0.5;
                float edge = saturate(dot(centered,centered)*2.4);
                float rgb = lerp(3*_MasterStrength*_ChromaticStrength*edge,
                    _OverlaySignal.z*(0.25+edge), blend);
                float2 offset = float2(abs(_MainTex_TexelSize.x)*rgb,0);
                fixed4 source = tex2D(_MainTex,uv);
                float3 color = float3(tex2D(_MainTex,uv+offset).r,source.g,tex2D(_MainTex,uv-offset).b);
                float contrast = 1+0.25*_MasterStrength*_ContrastStrength;
                color = (color-0.5)*contrast+0.5;
                float row = uv.y/max(abs(_MainTex_TexelSize.y),0.000001);
                float scan = 0.5+0.5*sin(row*UNITY_PI);
                float scanStrength = lerp(0.12*_MasterStrength*_ScanlineStrength,_OverlaySignal.x,blend);
                color *= 1-scan*scanStrength;
                float2 cell = floor(uv/max(abs(_MainTex_TexelSize.xy),float2(0.000001,0.000001)));
                float noise = Random(cell+floor(_CRTTime*24)*float2(17,31))-0.5;
                color += noise*lerp(0.06*_MasterStrength*_NoiseStrength,_OverlaySignal.y,blend);
                float aspect = abs(_MainTex_TexelSize.y/_MainTex_TexelSize.x);
                float normalVignette = smoothstep(0.38,0.78,length(centered*float2(aspect,1)));
                float monitorVignette = smoothstep(0.25,1.25,dot(p,p));
                color *= 1-lerp(normalVignette*0.35*_MasterStrength*_VignetteStrength,
                    monitorVignette*_OverlayShape.y,blend);
                if (_OverlayActive > 0.5)
                {
                    float reflection = pow(saturate(1-abs(uv.x+uv.y*0.7-0.23-sin(_CRTTime*0.3)*0.015)),12);
                    float glassEdge = pow(saturate(1-abs(uv.y-0.91)*14),4)*0.2;
                    color += (reflection+glassEdge)*_OverlayGlass*blend*float3(0.7,0.78,1);
                    float roll = pow(saturate(1-abs(uv.y-frac(_CRTTime*0.065))*18),8);
                    color += roll*0.018*blend*float3(1,0.5,0.8);
                    color = color*mask + (1-mask)*float3(bezel*0.8,bezel*0.65,bezel);
                }
                return fixed4(saturate(color),1);
            }
            ENDCG
        }
        // Only runs when opening a screen: soften/dim the frozen game, not its UI text.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment backdrop
            #pragma target 3.0
            fixed4 backdrop(v2f_img i) : SV_Target
            {
                float2 d = abs(_MainTex_TexelSize.xy)*_BackdropBlur;
                float3 c = tex2D(_MainTex,i.uv).rgb*0.2;
                c += tex2D(_MainTex,i.uv+float2(d.x,0)).rgb*0.12;
                c += tex2D(_MainTex,i.uv-float2(d.x,0)).rgb*0.12;
                c += tex2D(_MainTex,i.uv+float2(0,d.y)).rgb*0.12;
                c += tex2D(_MainTex,i.uv-float2(0,d.y)).rgb*0.12;
                c += tex2D(_MainTex,i.uv+d).rgb*0.08;
                c += tex2D(_MainTex,i.uv-d).rgb*0.08;
                c += tex2D(_MainTex,i.uv+float2(d.x,-d.y)).rgb*0.08;
                c += tex2D(_MainTex,i.uv+float2(-d.x,d.y)).rgb*0.08;
                return fixed4(c*_BackdropBrightness*float3(0.94,0.88,1),1);
            }
            ENDCG
        }
    }
    Fallback Off
}
