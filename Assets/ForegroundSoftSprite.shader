Shader "IronBallGirl/ForegroundSoftSprite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _BlurPixels ("Near Camera Softness (Texture Pixels)", Range(0,2)) = 1
        [MaterialToggle] PixelSnap ("Pixel snap", Float) = 0
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
        [PerRendererData] _AlphaTex ("External Alpha", 2D) = "white" {}
        [PerRendererData] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex SpriteVert
            #pragma fragment ForegroundFrag
            #pragma target 2.0
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ PIXELSNAP_ON
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #include "UnitySprites.cginc"
            float4 _MainTex_TexelSize;
            float _BlurPixels;

            fixed4 PremultipliedSample(float2 uv)
            {
                fixed4 c = SampleSpriteTexture(uv);
                c.rgb *= c.a;
                return c;
            }

            fixed4 ForegroundFrag(v2f input) : SV_Target
            {
                // 透明部分のRGBを滲ませず、近接する前景だけ1texel程度柔らかくする。
                float2 d = _MainTex_TexelSize.xy * _BlurPixels;
                fixed4 c = PremultipliedSample(input.texcoord) * 4;
                c += (PremultipliedSample(input.texcoord + float2(d.x,0)) +
                      PremultipliedSample(input.texcoord - float2(d.x,0)) +
                      PremultipliedSample(input.texcoord + float2(0,d.y)) +
                      PremultipliedSample(input.texcoord - float2(0,d.y))) * 2;
                c += PremultipliedSample(input.texcoord + d);
                c += PremultipliedSample(input.texcoord - d);
                c += PremultipliedSample(input.texcoord + float2(d.x,-d.y));
                c += PremultipliedSample(input.texcoord + float2(-d.x,d.y));
                c *= 0.0625;
                c.rgb *= input.color.rgb * input.color.a;
                c.a *= input.color.a;
                return c;
            }
            ENDCG
        }
    }
}
