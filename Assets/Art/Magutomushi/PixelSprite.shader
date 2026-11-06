Shader "IronBallGirl/Magutomushi Pixel Sprite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [MaterialToggle] PixelSnap ("Pixel snap", Float) = 0
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
        [PerRendererData] _AlphaTex ("External Alpha", 2D) = "white" {}
        [PerRendererData] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Cull Off Lighting Off ZWrite Off
        Blend One OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex SpriteVert
            #pragma fragment PixelFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ PIXELSNAP_ON
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #include "UnitySprites.cginc"
            float4 _MainTex_TexelSize;
            fixed4 PixelFrag(v2f input) : SV_Target
            {
                // The supplied art uses roughly four source pixels per game pixel.
                float2 size = _MainTex_TexelSize.zw;
                float2 uv = (floor(input.texcoord * size / 4.0) * 4.0 + 2.0) / size;
                fixed4 texel = SampleSpriteTexture(uv);
                clip(texel.a - 0.5);
                fixed4 result = fixed4(texel.rgb * input.color.rgb, input.color.a);
                result.rgb *= result.a;
                return result;
            }
            ENDCG
        }
    }
}
