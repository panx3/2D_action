Shader "IronBallGirl/PinkTreeWind"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _SwayPixels ("Canopy Sway (Texture Pixels)", Range(0,4)) = 3.2
        _RustlePixels ("Leaf Rustle (Texture Pixels)", Range(0,2)) = 0.75
        _WindSpeed ("Wind Speed", Range(0,3)) = 1.05
        _WindPhase ("Wind Phase", Float) = 0
        _TrunkHeight ("Stationary Trunk Height", Range(0,0.8)) = 0.36
        [HideInInspector] _PivotX ("Sprite Pivot X", Float) = 0.63
        [MaterialToggle] PixelSnap ("Pixel snap", Float) = 0
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
        [PerRendererData] _AlphaTex ("External Alpha", 2D) = "white" {}
        [PerRendererData] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
    }

    SubShader
    {
        // The UV wind uses the whole standalone texture, so do not pack this sprite.
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="False" }
        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex TreeVert
            #pragma fragment TreeFrag
            #pragma target 2.0
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ PIXELSNAP_ON
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #include "UnitySprites.cginc"

            float4 _MainTex_TexelSize;
            float _SwayPixels, _RustlePixels, _WindSpeed, _WindPhase, _TrunkHeight, _PivotX;

            v2f TreeVert(appdata_t input)
            {
                // Expand both geometry and UVs together: the original sprite keeps
                // its size, with transparent room for wind at the left/right tips.
                float padding = (_SwayPixels + _RustlePixels + 1) * _MainTex_TexelSize.x;
                input.vertex.x *= 1 + 2 * padding;
                input.texcoord.x = (input.texcoord.x - _PivotX) * (1 + 2 * padding) + _PivotX;
                return SpriteVert(input);
            }

            fixed4 TreeFrag(v2f input) : SV_Target
            {
                float2 uv = input.texcoord;
                float height = smoothstep(_TrunkHeight, 0.92, uv.y);
                float time = _Time.y * _WindSpeed + _WindPhase;
                float gust = 0.72 + 0.28 * sin(time * 0.37);
                float sway = sin(time + uv.y * 1.6) * _SwayPixels * gust;
                float rustle = sin(time * 2.7 + uv.y * 24 + uv.x * 9) * _RustlePixels;
                // Whole texture-pixel steps retain the sprite's pixel clusters.
                uv.x -= floor((sway + rustle) * height + 0.5) * _MainTex_TexelSize.x;
                clip(uv);
                clip(1 - uv);
                fixed4 color = SampleSpriteTexture(uv);
                // Remove semitransparent generated fringe without softening the art.
                clip(color.a - 0.5);
                color.a = input.color.a;
                color.rgb *= input.color.rgb * color.a;
                return color;
            }
            ENDCG
        }
    }
}
