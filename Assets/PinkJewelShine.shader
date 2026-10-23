Shader "Tekkyu/Pink Jewel Pixel Shine"
{
    Properties
    {
        [PerRendererData] _MainTex ("Original sprite mask", 2D) = "white" {}
        _ShineColor ("Reflection color", Color) = (1,0.93,0.98,1)
        _ShinePosition ("Reflection position", Float) = -0.25
        _ShineStrength ("Reflection strength", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "CanUseSpriteAtlas"="False" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha One
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            fixed4 _ShineColor;
            float _ShinePosition, _ShineStrength;
            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float2 pixelUV = (floor(i.uv * _MainTex_TexelSize.zw) + 0.5) * _MainTex_TexelSize.xy;
                fixed4 sprite = tex2D(_MainTex, pixelUV);
                float distanceToBand = abs(pixelUV.x + (pixelUV.y - 0.5) * 0.32 - _ShinePosition);
                float band = 1 - smoothstep(0.025, 0.115, distanceToBand);
                float luminance = dot(sprite.rgb, float3(0.299, 0.587, 0.114));
                // Keep dark metal/outline detail readable, and never light transparent pixels.
                float surface = lerp(0.15, 1, smoothstep(0.08, 0.65, luminance));
                return fixed4(_ShineColor.rgb * i.color.rgb,
                    sprite.a * band * surface * _ShineStrength * i.color.a * _ShineColor.a);
            }
            ENDCG
        }
    }
}
