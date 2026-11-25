Shader "Sprites/Beam Scorpion Energy"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor ("Renderer Color", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
        [HideInInspector] _Shape ("Shape", Float) = 0
        [HideInInspector] _Progress ("Progress", Float) = 1
        [HideInInspector] _Span ("Beam Span", Float) = 1
        [HideInInspector] _Height ("Beam Height", Float) = 0.84
        [HideInInspector] _Contact ("Beam Contact", Float) = 0
        [HideInInspector] _Arc ("Sweep Angles", Vector) = (0,1,0.5,0)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "CanUseSpriteAtlas"="False" }
        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex SpriteVert
            #pragma fragment Shade
            #pragma target 3.0
            #include "UnitySprites.cginc"

            float _Shape, _Progress, _Span, _Height, _Contact;
            float4 _Arc;
            static const half3 Ivory = half3(1.0, 0.95, 0.79);
            static const half3 Amber = half3(1.0, 0.59, 0.30);

            float Noise(float2 cell)
            {
                return frac(52.9829189 * frac(dot(cell, float2(0.06711056, 0.00583715))));
            }

            float Inside(float distanceFromCenter, float radius)
            {
                return step(abs(distanceFromCenter), radius);
            }

            float Octagon(float2 cell)
            {
                float2 a = abs(cell);
                return max(a.x, a.y) + min(a.x, a.y) * 0.42;
            }

            half3 Shadow(half3 tint) { return tint * 0.28 + half3(0.09, 0.025, 0.035); }
            half3 Flame(half3 tint) { return lerp(tint, Amber, 0.62); }

            void Paint(inout half4 pixel, float mask, half3 color)
            {
                pixel = lerp(pixel, half4(color, 1.0), mask);
            }

            half4 ResolvePixel(half4 pixel, float opacity, float2 cell)
            {
                // 輪郭のぼかしは使わず、同じ格子の粒を間引いて消す。
                float dither = (floor(Noise(cell) * 8.0) + 0.5) / 8.0;
                return pixel * step(dither, saturate(opacity));
            }

            half4 Beam(float2 uv, half4 tint)
            {
                // 64 PPU、表示スケール2の本体と同じワールド寸法のドット。
                float2 grid = max(1.0, floor(float2(_Span, _Height) * 32.0 + 0.5));
                float2 cell = floor(uv * grid);
                float tick = floor(_Time.y * 20.0);
                float fromEnd = grid.x - 1.0 - cell.x;
                float taper = lerp(saturate(fromEnd / max(1.0, min(38.0, grid.x * 0.66))), 1.0, _Contact);
                float lane = floor((cell.x - tick * 4.0) / 5.0);
                float chip = Noise(float2(lane, 3.0));
                float jag = floor(chip * 3.0) - 1.0;
                float y = cell.y - floor(grid.y * 0.5);
                float d = abs(y - jag * step(0.22, taper));
                float mouth = floor(max(0.0, 3.4 - cell.x * 0.28) * (0.55 + (1.0 - _Progress)));
                float radius = floor(grid.y * 0.22 * taper + 0.5) + mouth;
                float ribs = step(0.42, frac((cell.x - tick * 5.0) / 19.0));
                float hotRadius = floor(radius * 0.34);
                float edgeChip = step(0.61, chip) * step(0.26, Noise(float2(cell.x, lane)));
                float rail = Inside(d - radius - 2.0 - floor(chip * 2.0), 0.0) * edgeChip * step(0.52, taper);

                half4 pixel = 0;
                Paint(pixel, rail, Flame(tint.rgb));
                Paint(pixel, step(d, radius + 1.0 + edgeChip) * step(1.0, radius), Shadow(tint.rgb));
                Paint(pixel, step(d, radius), tint.rgb);
                Paint(pixel, step(d, radius - 1.0 - ribs), Flame(tint.rgb));
                Paint(pixel, step(d, hotRadius), Ivory);
                float weld = _Contact * step(fromEnd * 0.62 + abs(y) * 0.78, 5.8);
                Paint(pixel, weld, Flame(tint.rgb));
                Paint(pixel, weld * step(fromEnd + abs(y), 3.0), Ivory);
                return ResolvePixel(pixel, tint.a * saturate(taper * 1.45), cell);
            }

            half4 Charge(float2 cell, float grid, half4 tint, float t)
            {
                float2 a = abs(cell);
                float d = Octagon(cell);
                float radius = floor(lerp(grid * 0.39, grid * 0.19, t));
                float ring = Inside(d - radius, 0.8) * step(0.24, Noise(floor(cell / 3.0)));
                float nucleus = floor(lerp(2.0, 4.8, t));
                float cross = step(min(a.x, a.y) + max(a.x, a.y) * 0.26, lerp(1.6, 3.4, t));
                half4 pixel = 0;
                Paint(pixel, Inside(d - radius, 1.7), Shadow(tint.rgb));
                Paint(pixel, ring, Flame(tint.rgb));
                Paint(pixel, step(d, nucleus + 2.0), Shadow(tint.rgb));
                Paint(pixel, step(d, nucleus + 0.5), tint.rgb);
                Paint(pixel, cross, Ivory);
                return ResolvePixel(pixel, tint.a * lerp(0.78, 1.0, t), cell);
            }

            half4 Discharge(float2 cell, float grid, half4 tint, float t, float impact)
            {
                float2 a = abs(cell);
                float d = Octagon(cell * float2(lerp(0.86, 1.0, impact), lerp(1.14, 0.8, impact)));
                float expand = 1.0 - (1.0 - t) * (1.0 - t);
                float radius = floor(lerp(grid * 0.12, grid * 0.36, expand));
                float broken = step(0.28 + t * 0.35, Noise(floor(cell / 4.0)));
                float ring = Inside(d - radius, 1.0) * broken;
                float cross = min(a.x, a.y) * 1.35 + max(a.x, a.y) * 0.26;
                float flare = floor(grid * lerp(0.11, 0.025, t));
                float jet = (1.0 - impact) * step(a.y + abs(cell.x - grid * 0.10) * 0.21, flare);
                float spill = step(-2.0 - a.y * 0.38, cell.x);
                half4 pixel = 0;
                Paint(pixel, Inside(d - radius, 2.1) * broken, Shadow(tint.rgb));
                Paint(pixel, ring, tint.rgb);
                Paint(pixel, max(step(cross, flare + 1.0), jet), Flame(tint.rgb));
                Paint(pixel, step(cross, flare * 0.67), Ivory);
                return ResolvePixel(pixel, tint.a * (1.0 - t) * lerp(1.0, 0.36 + spill * 0.64, impact), cell);
            }

            half4 ClawArc(float2 cell, float grid, half4 tint, float t)
            {
                float front = lerp(_Arc.x, _Arc.y, saturate(t / _Arc.z));
                float angle = atan2(cell.y, cell.x);
                float tail = front - angle;
                float arc = step(_Arc.x - 0.06, angle) * step(-0.045, tail) * step(tail, 1.04);
                float distanceToEdge = length(cell) - floor(grid * 0.40);
                float width = floor(lerp(6.0, 1.0, saturate(tail)));
                float breakup = step(tail * 0.24, Noise(floor(cell / 2.0)));
                half4 pixel = 0;
                Paint(pixel, step(-width - 1.0, distanceToEdge) * step(distanceToEdge, 1.4), Shadow(tint.rgb));
                Paint(pixel, step(-width, distanceToEdge) * step(distanceToEdge, 0.4) * breakup, tint.rgb);
                Paint(pixel, Inside(distanceToEdge + 0.65, 0.95) * step(tail, 0.79), Flame(tint.rgb));
                Paint(pixel, Inside(distanceToEdge + 0.7, 0.72) * step(tail, 0.36), Ivory);
                return ResolvePixel(pixel * arc, tint.a * (1.0 - saturate((t - _Arc.z) / (1.0 - _Arc.z))), cell);
            }

            half4 Shade(v2f input) : SV_Target
            {
                float2 uv = input.texcoord;
                half4 tint = input.color;
                if (_Shape < 0.5) return Beam(uv, tint);
                float grid = max(1.0, floor(_Span * 32.0 + 0.5));
                float2 cell = floor((uv - 0.5) * grid + 0.5);
                if (_Shape < 1.5) return Charge(cell, grid, tint, floor(_Progress * 10.0) / 10.0);
                if (_Shape < 3.5) return Discharge(cell, grid, tint, floor(_Progress * 9.0) / 9.0, step(2.5, _Shape));
                return ClawArc(cell, grid, tint, _Progress);
            }
            ENDCG
        }
    }
}
