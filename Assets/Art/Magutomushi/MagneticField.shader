Shader "IronBallGirl/Magutomushi Magnetic Field"
{
    Properties
    {
        _ChargeColor ("Charge Copper", Color) = (0.91, 0.64, 0.35, 1)
        _FieldColor ("Active Verdigris", Color) = (0.35, 0.81, 0.78, 1)
        _SparkColor ("Magnetic Spark", Color) = (0.83, 1, 0.89, 1)
        [PerRendererData] _Field ("Radius, half extent, elapsed, charge (-1 while active)", Vector) = (1, 1, 0, 0)
        [PerRendererData] _IronBall ("Offset XY, marker radius, attached", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off Lighting Off ZWrite Off
        Blend One OneMinusSrcAlpha

        // Share the background capture when several beetles are visible.
        GrabPass { "_MagutomushiFieldBackground" }

        Pass
        {
            CGPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma target 3.0
            #include "UnityCG.cginc"

            float4 _Field;
            float4 _IronBall;
            half4 _ChargeColor, _FieldColor, _SparkColor;
            sampler2D _MagutomushiFieldBackground;

            struct Varyings
            {
                float4 position : SV_POSITION;
                float2 fieldPosition : TEXCOORD0;
                float3 worldPosition : TEXCOORD1;
            };

            Varyings Vertex(appdata_base input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.fieldPosition = (input.texcoord.xy * 2.0 - 1.0) * _Field.y;
                output.worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
                return output;
            }

            float Ring(float distance, float radius, float halfWidth)
            {
                return step(abs(distance - radius), halfWidth);
            }

            float Arc(float angle, float count, float offset, float halfWidth)
            {
                return step(abs(frac(angle * count + offset) - 0.5), halfWidth);
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                float2 position = input.fieldPosition;
                float distance = length(position);
                float inside = _Field.x - distance;
                clip(inside);

                float elapsed = _Field.z;
                float angle = atan2(position.y, position.x);
                float active = step(_Field.w, -0.5);
                float charge = saturate(_Field.w);
                half3 tone = lerp(_ChargeColor.rgb, _FieldColor.rgb, active);

                // Bend the whole field and soften the refraction at the actual attraction boundary.
                float coverage = smoothstep(0.0, 0.85, inside);
                float edge = smoothstep(0.0, 0.18, inside) * (1.0 - smoothstep(0.27, 0.95, inside));
                float edgeWave = sin(angle * 7.0 + inside * 9.5 - elapsed * 3.1);
                float2 radial = position / max(distance, 0.001);
                float2 tangent = float2(-radial.y, radial.x);
                float twist = 0.24 * sin(angle * 11.0 - elapsed * 2.3);
                float2 fieldWave = float2(
                    sin(position.y * 1.95 + position.x * 0.73 + elapsed * 2.1),
                    sin(position.x * 1.57 - position.y * 1.33 - elapsed * 1.9));
                float fieldStrength = lerp(0.019 + 0.020 * charge, 0.086, active);
                float edgeStrength = lerp(0.027 + 0.013 * charge, 0.073, active);
                float2 shift = fieldWave * (coverage * fieldStrength)
                    + (radial * edgeWave + tangent * twist) * (edge * edgeStrength);
                float refraction = saturate(coverage * lerp(0.57, 0.82, active) + edge * 0.6);
                float haze = edge * (0.52 + 0.48 * edgeWave) * lerp(0.045, 0.075, active);

                // Small cues for the horn and ball keep the same pixel grid as the sprites.
                const float pixel = 0.045;
                float2 p = (floor(position / pixel) + 0.5) * pixel;
                float clock = floor(elapsed * 24.0) / 24.0;
                float pixelDistance = length(p);
                float turn = angle / UNITY_TWO_PI;
                float glint = step(abs(p.x) + abs(p.y), lerp(0.12 + 0.06 * charge, 0.18, active));
                float glow = saturate(1.0 - pixelDistance / 0.52) * lerp(0.09, 0.15, active);
                float hornArc = Ring(pixelDistance, lerp(0.68 - 0.26 * charge, 0.51, active), 0.028)
                    * Arc(turn, 4.0, -clock * 0.9, 0.23);
                float light = max(glint * lerp(0.31 + 0.15 * charge, 0.60, active),
                    hornArc * lerp(0.22, 0.39, active));

                if (_IronBall.z > 0.0)
                {
                    float2 ball = _IronBall.xy;
                    float ballDistance = length(ball);
                    float2 direction = ball / max(ballDistance, 0.001);
                    float along = dot(p, direction);
                    float across = dot(p, float2(-direction.y, direction.x));
                    float fraction = saturate(along / max(ballDistance, 0.001));
                    float curve = 0.18 * sin(fraction * UNITY_PI);
                    float linkSpan = step(0.23, along) * step(along, ballDistance - _IronBall.z * 0.6);
                    float flow = frac(along * 1.35 + clock * 3.9);
                    float trails = step(abs(abs(across) - curve), 0.030) * step(flow, 0.38);
                    glow = max(glow, step(abs(across), 0.032) * linkSpan * 0.12);
                    light = max(light, trails * linkSpan * 0.43);

                    float2 aroundBall = abs(p - ball);
                    float ballAngle = atan2(p.y - ball.y, p.x - ball.x) / UNITY_TWO_PI;
                    float ballRing = Ring(length(aroundBall), _IronBall.z, 0.030) * Arc(ballAngle, 4.0, clock * 0.75, 0.19);
                    light = max(light, ballRing * 0.39);
                }

                float showSparks = 1.0 - saturate(_IronBall.w);
                light *= showSparks;
                float cueAlpha = max(haze, max(glow * showSparks, light));
                float backgroundAlpha = refraction * (1.0 - cueAlpha);
                clip(cueAlpha + backgroundAlpha - 0.001);
                half3 spark = lerp(lerp(_ChargeColor.rgb, 1.0, 0.58), _SparkColor.rgb, active);
                half3 cue = lerp(tone, spark, saturate(light / max(cueAlpha, 0.001)));
                float3 refractedPosition = input.worldPosition + float3(shift, 0.0);
                float4 grabPosition = ComputeGrabScreenPos(UnityWorldToClipPos(refractedPosition));
                half3 background = tex2Dproj(_MagutomushiFieldBackground, UNITY_PROJ_COORD(grabPosition)).rgb;
                return half4(background * backgroundAlpha + cue * cueAlpha, backgroundAlpha + cueAlpha);
            }
            ENDCG
        }
    }
}
