Shader "IronBallGirl/Stage2 Sun Flare"
{
    Properties
    {
        [HideInInspector] _Source ("Viewport position, strength, aspect", Vector) = (0,0,0,1)
        [HideInInspector] _Band ("Viewport bottom, height", Vector) = (0,1,0,0)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Cull Off ZWrite Off Lighting Off
        Blend One One
        ColorMask RGB
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Stage2Sunset.cginc"
            float4 _Source, _Band;

            struct Varyings { float4 vertex:SV_POSITION; float2 view:TEXCOORD0; };

            Varyings vert(appdata_base v)
            {
                Varyings o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.view = float2(v.texcoord.x, _Band.x + v.texcoord.y * _Band.y);
                return o;
            }

            float Ghost(float2 ray, float radius)
            {
                float2 pixel = floor(ray * 384 + 0.5) / 384;
                float r = length(pixel) / radius;
                float edge = smoothstep(0.63, 0.89, r) * (1 - smoothstep(0.89, 1.06, r));
                return edge * 0.74 + (1 - smoothstep(0.15, 1, r)) * 0.26;
            }

            fixed4 frag(Varyings i) : SV_Target
            {
                float2 aspect = float2(_Source.w, 1);
                float2 ray = (i.view - _Source.xy) * aspect;
                float streak = (1 - smoothstep(0.017, 0.205, abs(ray.x))) * (1 - smoothstep(0.001, 0.010, abs(ray.y)));
                float glow = 1 - smoothstep(0.018, 0.115, length(ray));
                float2 first = lerp(_Source.xy, float2(0.5, 0.5), 0.53);
                float2 second = lerp(_Source.xy, float2(0.5, 0.5), 1.51);
                float nearGhost = Ghost((i.view - first) * aspect, 0.023);
                float farGhost = Ghost((i.view - second) * aspect, 0.034);
                float3 gold = SunsetColor(float3(1, 0.84, 0.51));
                float3 copper = SunsetColor(float3(1, 0.53, 0.18));
                float3 color = gold * (0.48 * streak + 0.15 * glow) + copper * (0.19 * nearGhost + 0.13 * farGhost);
                return float4(color * _Source.z, 0);
            }
            ENDCG
        }
    }
}
