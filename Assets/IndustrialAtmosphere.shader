Shader "IronBallGirl/Industrial Atmosphere"
{
    Properties
    {
        _MainTex ("Atmosphere Atlas", 2D) = "white" {}
        [HideInInspector] _SV_SunsetContrast ("Sunset contrast", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Cull Off ZWrite Off Lighting Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Stage2Sunset.cginc"
            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _SV_SunsetContrast;
            struct AppData { float4 vertex:POSITION; fixed4 color:COLOR; float2 uv:TEXCOORD0; };
            struct Varyings { float4 vertex:SV_POSITION; fixed4 color:COLOR; float2 uv:TEXCOORD0; float2 world:TEXCOORD1; };
            Varyings vert(AppData v)
            {
                Varyings o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xy;
                return o;
            }
            fixed4 frag(Varyings i) : SV_Target
            {
                fixed4 color = tex2D(_MainTex, i.uv) * i.color;
                if (_SV_SunsetContrast > 0 && i.uv.y < 0.5) // The four steam cells occupy the bottom half of the atlas.
                {
                    float sun = Stage2Sunlight(i.world);
                    float underside = 1 - smoothstep(0.30, 0.74, frac(i.uv.y * 4));
                    float3 shadow = lerp(float3(0.22, 0.16, 0.34), float3(0.36, 0.27, 0.48), underside);
                    float3 lit = lerp(float3(0.70, 0.33, 0.16), float3(1, 0.71, 0.34), underside);
                    color.rgb = lerp(color.rgb, lerp(shadow, lit, sun), _SV_SunsetContrast);
                    color.a *= lerp(1, lerp(0.70, 1.19, sun), _SV_SunsetContrast);
                }
                return color;
            }
            ENDCG
        }
    }
}
