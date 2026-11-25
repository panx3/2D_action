Shader "IronBallGirl/Slash Mantis Dissolve"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        [PerRendererData] _Dissolve ("Dissolve", Range(0, 1)) = 0
        _Color ("Tint", Color) = (1,1,1,1)
        [MaterialToggle] PixelSnap ("Pixel snap", Float) = 0
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
        [PerRendererData] _AlphaTex ("External Alpha", 2D) = "white" {}
        [PerRendererData] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "CanUseSpriteAtlas"="True" }
        Cull Off Lighting Off ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex SpriteVert
            #pragma fragment DissolveFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ PIXELSNAP_ON
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #include "UnitySprites.cginc"

            float4 _MainTex_TexelSize;
            float _Dissolve;

            fixed4 DissolveFrag(v2f input) : SV_Target
            {
                fixed4 color = SampleSpriteTexture(input.texcoord) * input.color;
                // Break the silhouette into horizontal clusters of source pixels.
                float2 cell = floor(input.texcoord * _MainTex_TexelSize.zw / float2(13.0, 8.0));
                float noise = frac(sin(dot(cell, float2(12.9898, 78.233))) * 43758.5453);
                float threshold = lerp(0.12, 0.94, noise);
                float eroded = smoothstep(threshold - 0.05, threshold + 0.09, _Dissolve);
                float fringe = smoothstep(threshold - 0.12, threshold, _Dissolve);
                color.rgb = lerp(color.rgb, fixed3(0.26, 0.91, 1.0), fringe * 0.65);
                color.a *= 1.0 - eroded;
                color.rgb *= color.a;
                return color;
            }
            ENDCG
        }
    }
}
