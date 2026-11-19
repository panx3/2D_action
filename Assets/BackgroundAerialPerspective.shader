Shader "IronBallGirl/Background Aerial Perspective"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _AtmosphereColor ("青白い霞", Color) = (0.70,0.83,0.96,1)
        _AtmosphereStrength ("霞の強さ", Range(0,1)) = 0.3
        _Saturation ("彩度", Range(0,1)) = 0.9
        [HideInInspector] _SV_Active ("Visual active", Float) = 0
        [HideInInspector] _SV_Tint ("Visual tint", Color) = (1,1,1,1)
        [HideInInspector] _SV_Tone ("Contrast, saturation, distance, lighting", Vector) = (1,1,0,0)
        [HideInInspector] _SV_Blur ("Strength, radius, peripheral, range", Vector) = (0,0,0,0.6)
        [HideInInspector] _SV_Fog ("Opacity, variation, speedX, speedY", Vector) = (0,0,0,0)
        [HideInInspector] _SV_Atmosphere ("Atmosphere", Color) = (0.7,0.83,0.96,1)
        [HideInInspector] _SV_FogColor ("Fog", Color) = (0.7,0.83,0.96,1)
        [HideInInspector] _SV_Sunset ("Sunset strength, sky mask", Vector) = (0,0,0,0)
        [HideInInspector] _SV_Layer ("Layer", Float) = 0
        [MaterialToggle] PixelSnap ("Pixel snap", Float) = 0
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
        [PerRendererData] _AlphaTex ("External Alpha", 2D) = "white" {}
        [PerRendererData] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex VisualVert
            #pragma fragment AtmosphereFrag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ PIXELSNAP_ON
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #include "UnitySprites.cginc"
            #include "ScreenVisualLighting.cginc"
            float4 _MainTex_TexelSize;
            float _SV_Active, _SV_Layer, _SV_Time;
            float4 _SV_Tint, _SV_Tone, _SV_Blur, _SV_Fog, _SV_Atmosphere, _SV_FogColor, _SV_Sunset;
            struct VisualOutput { float4 vertex:SV_POSITION; fixed4 color:COLOR; float2 uv:TEXCOORD0; float2 world:TEXCOORD1; float4 screen:TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };
            VisualOutput VisualVert(appdata_t v)
            {
                v2f b=SpriteVert(v);
                VisualOutput o; UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex=b.vertex; o.color=b.color; o.uv=b.texcoord;
                o.world=mul(unity_ObjectToWorld,UnityFlipSprite(v.vertex,_Flip)).xy;
                o.screen=ComputeScreenPos(o.vertex); return o;
            }
            float FogNoise(float2 p)
            {
                // Smooth periodic waves: no texture edges or repeating sprite seams.
                return saturate(0.5+0.23*sin(p.x+sin(p.y*0.7))+0.17*sin(p.x*0.63-p.y*1.4));
            }
            float3 ApplySunset(float3 color, float2 uv, float noise)
            {
                float strength = _SV_Sunset.x;
                float luminance = dot(color, float3(0.2126, 0.7152, 0.0722));
                float horizon = smoothstep(0.07, 0.19, uv.y) * (1 - smoothstep(0.24, 0.66, uv.y));
                float litCloud = smoothstep(0.1, 0.54, luminance);
                if (_SV_Sunset.y < 0.5)
                {
                    // Scatter warm light into the lighter recesses of the distant factories.
                    float mist = horizon * lerp(0.55, 1, noise) * litCloud;
                    return lerp(color, float3(0.94, 0.57, 0.35), mist * strength * 0.38);
                }

                float horizonFocus = 1 - smoothstep(0.03, 0.36, abs(uv.x - 0.47));
                float amber = horizon * lerp(0.65, 1, horizonFocus) * lerp(0.48, 1, litCloud);
                float upperShadow = smoothstep(0.4, 0.94, uv.y) * (1 - litCloud);
                color = lerp(color, color * float3(0.9, 0.94, 1.12) + float3(0.008, 0.012, 0.035),
                    upperShadow * strength);
                color += float3(0.28, 0.34, 0.13) * amber * strength;

                // Two broad shafts spread from the light already painted into the sky.
                float2 sky = floor(uv * float2(544, 181)) / float2(544, 181);
                float rise = max(0, sky.y - 0.12);
                float spread = 0.012 + rise * 0.092;
                float leftRay = 1 - smoothstep(0.002, spread, abs(sky.x - 0.46 + rise * 0.20));
                float rightRay = 1 - smoothstep(0.004, spread, abs(sky.x - 0.49 - rise * 0.28));
                float reach = smoothstep(0.035, 0.19, rise) * (1 - smoothstep(0.36, 0.64, rise));
                float rays = leftRay * (0.043 + sin(_SV_Time * 0.63) * 0.010)
                    + rightRay * (0.049 + sin(_SV_Time * 0.51 + 2.1) * 0.010);
                return saturate(color + float3(1, 0.74, 0.42) * rays * reach * strength);
            }
            fixed4 _AtmosphereColor;
            half _AtmosphereStrength;
            half _Saturation;
            fixed4 AtmosphereFrag(VisualOutput input) : SV_Target
            {
                fixed4 c = SampleSpriteTexture(input.uv);
                if (_SV_Active > 0.5)
                {
                    float2 edge=abs(input.screen.xy/input.screen.w*2-1);
                    float peripheral=smoothstep(_SV_Blur.w,1.414214,length(edge));
                    float amount=saturate(_SV_Blur.x+peripheral*_SV_Blur.z);
                    if (amount > 0.001 && _SV_Blur.y > 0)
                    {
                        float2 r=_MainTex_TexelSize.xy*_SV_Blur.y;
                        fixed4 soft=(c*4+SampleSpriteTexture(input.uv+float2(r.x,0))+SampleSpriteTexture(input.uv-float2(r.x,0))+SampleSpriteTexture(input.uv+float2(0,r.y))+SampleSpriteTexture(input.uv-float2(0,r.y)))*0.125;
                        c=lerp(c,soft,amount);
                    }
                }
                c *= input.color;
                half luminance = dot(c.rgb, half3(0.2126, 0.7152, 0.0722));
                c.rgb = lerp(luminance.xxx, c.rgb, _Saturation);
                c.rgb = lerp(c.rgb, _AtmosphereColor.rgb, _AtmosphereStrength);
                if (_SV_Active > 0.5)
                {
                    half l=dot(c.rgb,half3(0.2126,0.7152,0.0722));
                    c.rgb=lerp(l.xxx,c.rgb,_SV_Tone.y);
                    c.rgb=saturate((c.rgb-0.5)*_SV_Tone.x+0.5)*_SV_Tint.rgb;
                    c.rgb=lerp(c.rgb,_SV_Atmosphere.rgb,saturate(_SV_Tone.z));
                    float n=FogNoise(input.world*float2(0.14,0.35)-_SV_Time*_SV_Fog.zw);
                    float fog=_SV_Fog.x*lerp(0.65,n,_SV_Fog.y);
                    c.rgb=lerp(c.rgb,_SV_FogColor.rgb,fog);
                    if (_SV_Sunset.x > 0.001) c.rgb = ApplySunset(c.rgb, input.uv, n);
                    c.rgb*=ScreenVisualLight(input.world,_SV_Layer,_SV_Tone.w);
                }
                c.rgb *= c.a;
                return c;
            }
            ENDCG
        }
    }
}
