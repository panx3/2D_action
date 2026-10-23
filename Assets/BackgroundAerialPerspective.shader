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
            float4 _SV_Tint, _SV_Tone, _SV_Blur, _SV_Fog, _SV_Atmosphere, _SV_FogColor;
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
                    c.rgb*=ScreenVisualLight(input.world,_SV_Layer,_SV_Tone.w);
                }
                c.rgb *= c.a;
                return c;
            }
            ENDCG
        }
    }
}
