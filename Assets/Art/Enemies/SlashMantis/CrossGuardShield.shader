Shader "IronBallGirl/Slash Mantis Guard Shield"
{
    Properties
    {
        [PerRendererData] _Impact ("Impact", Range(0, 1)) = 0
        [PerRendererData] _Contact ("Contact UV", Vector) = (0.5, 0.5, 0, 0)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            float _Impact;
            float4 _Contact;

            struct ShieldVertex
            {
                float4 position : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct ShieldFragment
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            ShieldFragment Vert(ShieldVertex input)
            {
                ShieldFragment output;
                output.position = UnityObjectToClipPos(input.position);
                output.uv = input.uv;
                return output;
            }

            fixed4 Frag(ShieldFragment input) : SV_Target
            {
                float2 panel = input.uv - 0.5;
                panel.x -= panel.y * 0.10;
                float2 corner = abs(panel);
                float edgeDistance = max(max(corner.x - 0.37, corner.y - 0.45),
                    dot(corner, float2(0.92, 0.65)) - 0.51);
                float surface = 1.0 - smoothstep(-0.01, 0.018, edgeDistance);
                float rim = 1.0 - smoothstep(0.008, 0.023, abs(edgeDistance));
                float innerRim = 1.0 - smoothstep(0.003, 0.012, abs(edgeDistance + 0.050));
                float halo = 1.0 - smoothstep(0.008, 0.086, abs(edgeDistance));
                float front = 1.0 - smoothstep(0.20, 0.88, input.uv.x);
                float scanLine = 1.0 - smoothstep(0.09, 0.24, abs(frac(input.uv.y * 18.0 - _Time.y * 1.8) - 0.5));

                // Move a blue wave away from the point where the ball struck the panel.
                float contactDistance = length((input.uv - _Contact.xy) * float2(0.57, 1.0));
                float pulseRadius = (1.0 - _Impact) * 0.69;
                float wave = _Impact * (1.0 - smoothstep(0.015, 0.064, abs(contactDistance - pulseRadius)));
                float contactFlash = _Impact * _Impact * (1.0 - smoothstep(0.015, 0.22, contactDistance));
                float light = saturate(rim * (0.65 + 0.35 * front) + wave + contactFlash);
                float3 color = lerp(float3(0.075, 0.51, 1.0), float3(0.69, 0.95, 1.0), light);
                float alpha = saturate(halo * (0.12 + wave * 0.20)
                    + surface * (0.16 + scanLine * 0.08 + wave * 0.46 + contactFlash * 0.65)
                    + innerRim * 0.38 + rim * (0.49 + 0.41 * front));
                return fixed4(color * alpha, alpha);
            }
            ENDCG
        }
    }
}
