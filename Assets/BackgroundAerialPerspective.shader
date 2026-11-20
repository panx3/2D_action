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
        [HideInInspector] _SV_Sunset ("Sunset strength, sky mask, contrast", Vector) = (0,0,0,0)
        [HideInInspector] _SV_Sun ("Sun position, radius, opening", Vector) = (0,0,0,0)
        [HideInInspector] _SV_PaintedSmoke ("Painted smoke count and brightness threshold", Vector) = (0,0,0,0)
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
            #include "Stage2Sunset.cginc"
            float4 _MainTex_TexelSize;
            float _SV_Active, _SV_Layer, _SV_Time;
            float4 _SV_Tint, _SV_Tone, _SV_Blur, _SV_Fog, _SV_Atmosphere, _SV_FogColor, _SV_Sunset;
            float4 _SV_Sun;
            float4 _SV_PaintedSmoke;
            float4 _SV_PaintedSmokeRects[12];
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
            float3 ApplySunset(float3 color, float2 uv, float2 world, float noise, float sourceLuminance)
            {
                float strength = _SV_Sunset.x;
                float contrast = _SV_Sunset.z;
                float sunlight = Stage2Sunlight(world);
                float luminance = SunsetLuminance(color);
                float horizon = smoothstep(0.06, 0.21, uv.y) * (1 - smoothstep(0.31, 0.75, uv.y));
                float litCloud = smoothstep(0.055, 0.38, luminance);
                if (_SV_Sunset.y < 0.5)
                {
                    // The warm mist picks up roof highlights and leaves the dark profiles readable.
                    color *= lerp(1, float3(1.07, 0.98, 0.64), strength);
                    float scatter = lerp(0.20, 1, litCloud) * lerp(0.49, 1, horizon) * lerp(0.76, 1, noise);
                    float3 shadow = color * float3(0.60, 0.57, 0.50);
                    float3 glow = color + (1 - color) * SunsetColor(float3(0.89, 0.58, 0.23)) * scatter * 0.33;
                    return lerp(color, lerp(shadow, glow, sunlight), contrast * lerp(0.62, 1, saturate(strength * 1.8)));
                }

                float3 copper = SunsetColor(float3(0.55, 0.234, 0.112));
                float3 orange = SunsetColor(float3(1.0, 0.56, 0.21));
                float3 warmSky = color * float3(1.18, 1.02, 0.30) + copper * 0.20;
                warmSky *= 1 - smoothstep(0.47, 0.94, uv.y) * (1 - litCloud) * contrast * 0.27;
                color = lerp(color, warmSky, strength);

                float radius = max(_SV_Sun.z, 0.01);
                float2 ray = (world - _SV_Sun.xy) / radius;
                float band = _SV_Sun.z > 0 ? 1 - smoothstep(0.25, 4.6, abs(ray.y + 0.74)) : horizon;
                float below = SunsetLuminance(SampleSpriteTexture(uv - float2(0, _MainTex_TexelSize.y * 6)).rgb);
                float rim = smoothstep(0.009, 0.09, below - sourceLuminance);
                float litEdge = rim * lerp(0.32, 0.90, sunlight) * (0.19 + 0.36 * band);
                float horizonFill = (0.09 * horizon + band * lerp(0.11, 0.21, sunlight)) * lerp(0.55, 1, litCloud);
                color += (1 - color) * orange * (horizonFill + litEdge) * strength;
                if (_SV_Sun.w <= 0) return saturate(color);

                float distanceToSun = length(ray);
                float halo = exp2(-distanceToSun * distanceToSun * 0.26);
                float wideRay = 1 - smoothstep(0.14, 1.56, abs(ray.x - ray.y * 0.45));
                float fineRay = 1 - smoothstep(0.06, 0.63, abs(ray.x - ray.y * 0.94));
                float reach = smoothstep(0.55, 1.5, distanceToSun) * (1 - smoothstep(2.4, 5.7, distanceToSun));
                float rayBrightness = (wideRay * 0.18 + fineRay * 0.13) * reach * (0.94 + 0.06 * sin(_SV_Time * 0.63));
                float3 glowingSky = color + (1 - color) * orange * (halo * 0.78 + rayBrightness);

                // A single sun in world space keeps the mirrored copies of the sky in phase.
                float2 solarPixel = floor(ray * 29 + 0.5) / 29;
                float diskDistance = length(solarPixel);
                float disk = 1 - smoothstep(0.98, 1.02, diskDistance);
                float3 edge = SunsetColor(float3(1.0, 0.71, 0.29));
                float3 core = SunsetColor(float3(1.0, 0.963, 0.80));
                float3 sun = lerp(edge, core, 1 - smoothstep(0.43, 1, diskDistance));
                // Underlit cloud rows can cut a soft band into the outer disk.
                float cloud = smoothstep(0.012, 0.145, below - sourceLuminance);
                float sunExposure = _SV_Sun.w * strength;
                color = lerp(color, glowingSky, sunExposure);
                return saturate(lerp(color, sun, disk * sunExposure * (1 - 0.27 * cloud)));
            }
            fixed4 _AtmosphereColor;
            half _AtmosphereStrength;
            half _Saturation;
            fixed4 AtmosphereFrag(VisualOutput input) : SV_Target
            {
                fixed4 c = SampleSpriteTexture(input.uv);
                // Stage2 cuts out the smoke drawn over the chimneys before drawing its moving plumes.
                if (_SV_PaintedSmoke.x > 0 && c.a > 0 && c.r >= _SV_PaintedSmoke.y)
                {
                    for (int i = 0; i < (int)_SV_PaintedSmoke.x; i++)
                    {
                        float4 smoke = _SV_PaintedSmokeRects[i];
                        if (all(input.uv >= smoke.xy) && all(input.uv <= smoke.zw)) return 0;
                    }
                }
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
                float sourceLuminance = SunsetLuminance(c.rgb);
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
                    if (_SV_Sunset.x > 0.001) c.rgb = ApplySunset(c.rgb, input.uv, input.world, n, sourceLuminance);
                    c.rgb*=ScreenVisualLight(input.world,_SV_Layer,_SV_Tone.w);
                }
                c.rgb *= c.a;
                return c;
            }
            ENDCG
        }
    }
}
