Shader "Hidden/IronBallGirl/CRTFilter"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
    }

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _MasterStrength;
            float _ScanlineStrength;
            float _NoiseStrength;
            float _VignetteStrength;
            float _ChromaticStrength;
            float _ContrastStrength;
            float _CRTTime;

            float Random(float2 value)
            {
                return frac(sin(dot(value, float2(12.9898, 78.233))) * 43758.5453);
            }

            fixed4 frag(v2f_img input) : SV_Target
            {
                float2 uv = input.uv;

                // 色収差は画面端ほど僅かに強くし、中央の視認性を維持する。
                float2 centered = uv - 0.5;
                float edge = saturate(dot(centered, centered) * 2.4);
                float chromaticPixels = 3.0 * _MasterStrength * _ChromaticStrength * edge;
                float2 chromaticOffset = float2(_MainTex_TexelSize.x * chromaticPixels, 0.0);

                fixed4 source = tex2D(_MainTex, uv);
                fixed3 color;
                color.r = tex2D(_MainTex, uv + chromaticOffset).r;
                color.g = source.g;
                color.b = tex2D(_MainTex, uv - chromaticOffset).b;

                // コントラストは最大値でも控えめな範囲に制限する。
                float contrast = 1.0 + (0.25 * _MasterStrength * _ContrastStrength);
                color = (color - 0.5) * contrast + 0.5;

                // 1レンダーテクセル単位の薄い走査線。解像度変更時も密度が破綻しない。
                float pixelRow = uv.y / max(_MainTex_TexelSize.y, 0.000001);
                float scanline = 0.5 + 0.5 * sin(pixelRow * UNITY_PI);
                float scanlineDarkening = scanline * 0.12 * _MasterStrength * _ScanlineStrength;
                color *= 1.0 - scanlineDarkening;

                // 低振幅のモノクロノイズ。色の判別を阻害しない。
                float2 noiseCell = floor(uv / max(_MainTex_TexelSize.xy, float2(0.000001, 0.000001)));
                float noiseFrame = floor(_CRTTime * 12.0);
                float noise = Random(noiseCell + noiseFrame * float2(17.0, 31.0)) - 0.5;
                color += noise * (0.06 * _MasterStrength * _NoiseStrength);

                // 四隅だけを軽く落とすビネット。
                float aspect = _MainTex_TexelSize.y / max(_MainTex_TexelSize.x, 0.000001);
                float2 vignetteUv = centered * float2(aspect, 1.0);
                float vignette = smoothstep(0.38, 0.78, length(vignetteUv));
                color *= 1.0 - vignette * (0.35 * _MasterStrength * _VignetteStrength);

                return fixed4(saturate(color), source.a);
            }
            ENDCG
        }
    }

    Fallback Off
}
