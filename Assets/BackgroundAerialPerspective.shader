Shader "IronBallGirl/Background Aerial Perspective"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _AtmosphereColor ("青白い霞", Color) = (0.70,0.83,0.96,1)
        _AtmosphereStrength ("霞の強さ", Range(0,1)) = 0.3
        _Saturation ("彩度", Range(0,1)) = 0.9
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
            #pragma fragment AtmosphereFrag
            #pragma target 2.0
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ PIXELSNAP_ON
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #include "UnitySprites.cginc"
            fixed4 _AtmosphereColor;
            half _AtmosphereStrength;
            half _Saturation;
            fixed4 AtmosphereFrag(v2f input) : SV_Target
            {
                fixed4 c = SampleSpriteTexture(input.texcoord) * input.color;
                half luminance = dot(c.rgb, half3(0.2126, 0.7152, 0.0722));
                c.rgb = lerp(luminance.xxx, c.rgb, _Saturation);
                c.rgb = lerp(c.rgb, _AtmosphereColor.rgb, _AtmosphereStrength);
                c.rgb *= c.a;
                return c;
            }
            ENDCG
        }
    }
}
