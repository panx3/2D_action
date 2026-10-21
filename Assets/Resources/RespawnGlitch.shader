Shader "Game/RespawnGlitch"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
        _Progress ("Dissolve", Range(0,1)) = 0
        _Phase ("Glitch phase", Float) = 0
        _Flash ("Flash", Range(0,1)) = 0
        _UVRect ("Sprite UV", Vector) = (0,0,1,1)
        _Pixels ("Sprite pixels", Vector) = (64,64,0,0)
        _Fall ("Vertical glitch", Float) = 0
        _Rebuild ("Cyan reconstruction edge", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "CanUseSpriteAtlas"="True" }
        Cull Off Lighting Off ZWrite Off
        Blend One OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex SpriteVert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnitySprites.cginc"
            float4 _UVRect, _Pixels;
            float _Progress, _Phase, _Flash, _Fall, _Rebuild;
            float hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7))) * 43758.5453); }
            fixed4 frag(v2f i):SV_Target
            {
                float2 local = (i.texcoord - _UVRect.xy) / _UVRect.zw;
                float2 pixel = floor(local * _Pixels.xy);
                float band = floor(pixel.y / 3);
                float shift = (hash(float2(band, floor(_Phase * 45))) - .5);
                shift = round(shift * 8) * step(.62, hash(float2(band, floor(_Phase * 30)))) * (1 - _Flash * .6);
                shift *= saturate(_Progress * 5 + _Flash);
                float2 uv = i.texcoord;
                uv.x += shift / _Pixels.x * _UVRect.z;
                uv.y += _Fall * round(sin(band + _Phase * 30) * 2) / _Pixels.y * _UVRect.w;
                float2 inside = (uv - _UVRect.xy) / _UVRect.zw;
                clip(min(min(inside.x,inside.y), min(1-inside.x,1-inside.y)));
                fixed4 c = SampleSpriteTexture(uv) * i.color;
                clip(hash(floor(pixel / 2)) + .001 - _Progress);
                fixed3 electronic = lerp(fixed3(1,.18,.62), fixed3(.22,1,1), step(.5,hash(float2(band,0))));
                c.rgb = lerp(c.rgb, electronic, .6 * _Progress);
                c.rgb = lerp(c.rgb, fixed3(1,.92,1), _Flash * .9);
                float2 texel = _UVRect.zw / _Pixels.xy;
                float edge = max(max(SampleSpriteTexture(uv + float2(texel.x,0)).a,
                    SampleSpriteTexture(uv - float2(texel.x,0)).a),
                    max(SampleSpriteTexture(uv + float2(0,texel.y)).a, SampleSpriteTexture(uv - float2(0,texel.y)).a));
                float outline = saturate(edge - c.a) * _Rebuild * saturate(1 - _Progress * 2);
                c.rgb = lerp(c.rgb, fixed3(.2,.94,1), outline);
                c.a = max(c.a, outline);
                c.rgb *= c.a;
                return c;
            }
            ENDCG
        }
    }
}
