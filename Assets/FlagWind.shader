Shader "IronBallGirl/FlagWind"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _SwayPixels ("Cloth Sway (Texture Pixels)", Range(0,5)) = 3
        _FlutterPixels ("Cloth Flutter (Texture Pixels)", Range(0,2)) = 0.75
        _WindSpeed ("Wind Speed", Range(0,3)) = 1.15
        _WindPhase ("Wind Phase", Float) = 0
        _ClothStart ("Fixed Hoist X", Range(0,1)) = 0.27
        _ClothBottom ("Cloth Bottom", Range(0,1)) = 0.48
        _ClothTop ("Cloth Top", Range(0,1)) = 0.94
        [HideInInspector] _SpriteUVRect ("Sprite UV Rect", Vector) = (0,0,1,1)
        [HideInInspector] _PivotY ("Sprite Pivot Y", Float) = 0.5
        [HideInInspector] _SV_Layer ("Layer", Float) = 0
        [MaterialToggle] PixelSnap ("Pixel snap", Float) = 0
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
        [PerRendererData] _AlphaTex ("External Alpha", 2D) = "white" {}
        [PerRendererData] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="False" }
        Cull Off Lighting Off ZWrite Off
        Blend One OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex FlagVert
            #pragma fragment FlagFrag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ PIXELSNAP_ON
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #include "UnitySprites.cginc"
            #include "ScreenVisualLighting.cginc"
            float4 _MainTex_TexelSize, _SpriteUVRect;
            float _SwayPixels, _FlutterPixels, _WindSpeed, _WindPhase;
            float _ClothStart, _ClothBottom, _ClothTop, _PivotY, _SV_Layer;
            struct FlagOutput { float4 vertex:SV_POSITION; fixed4 color:COLOR; float2 uv:TEXCOORD0; float2 world:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            FlagOutput FlagVert(appdata_t input)
            {
                // Expand geometry and UVs together so the tips have transparent room to move.
                float padding = (_SwayPixels + _FlutterPixels + 1) * _MainTex_TexelSize.y / _SpriteUVRect.w;
                float pivot = _SpriteUVRect.y + _SpriteUVRect.w * _PivotY;
                input.vertex.y *= 1 + 2 * padding;
                input.texcoord.y = (input.texcoord.y - pivot) * (1 + 2 * padding) + pivot;
                v2f source = SpriteVert(input);
                FlagOutput output; UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.vertex = source.vertex; output.color = source.color; output.uv = source.texcoord;
                output.world = mul(unity_ObjectToWorld, UnityFlipSprite(input.vertex, _Flip)).xy;
                return output;
            }
            fixed4 FlagFrag(FlagOutput input) : SV_Target
            {
                float2 uv = input.uv;
                float2 local = (uv - _SpriteUVRect.xy) / _SpriteUVRect.zw;
                float freeEdge = smoothstep(_ClothStart, 1, local.x);
                float cloth = smoothstep(_ClothBottom, _ClothBottom + 0.07, local.y)
                    * (1 - smoothstep(_ClothTop - 0.04, _ClothTop, local.y));
                float time = _Time.y * _WindSpeed + _WindPhase + unity_ObjectToWorld._m03 * 0.21;
                float gust = 0.75 + 0.25 * sin(time * 0.43);
                float wave = sin(time * 2 - local.x * 6.5) * _SwayPixels * gust;
                wave += sin(time * 4.3 - local.x * 13 + local.y * 3) * _FlutterPixels;
                uv.y -= floor(wave * freeEdge * cloth + 0.5) * _MainTex_TexelSize.y;
                float2 sampled = (uv - _SpriteUVRect.xy) / _SpriteUVRect.zw;
                clip(sampled); clip(1 - sampled);
                fixed4 color = SampleSpriteTexture(uv) * input.color;
                color.rgb *= ScreenVisualLight(input.world, _SV_Layer, 1) * color.a;
                return color;
            }
            ENDCG
        }
    }
}
