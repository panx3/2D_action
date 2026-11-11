Shader "IronBallGirl/Press Player Squash"
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
        [HideInInspector] _SV_Layer ("Layer", Float) = 0
        [PerRendererData] _PressSquash ("Scale and foot pivot", Vector) = (1,1,0,0)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "CanUseSpriteAtlas"="True" "DisableBatching"="True" }
        Cull Off Lighting Off ZWrite Off
        Blend One OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ PIXELSNAP_ON
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #include "UnitySprites.cginc"
            #include "../ScreenVisualLighting.cginc"
            float4 _PressSquash, _SV_WorldTone;
            float _SV_Layer, _SV_WorldSaturation;
            struct Output { float4 vertex:SV_POSITION; fixed4 color:COLOR; float2 uv:TEXCOORD0; float2 world:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            Output vert(appdata_t v)
            {
                v.vertex.xy = (v.vertex.xy - _PressSquash.zw) * _PressSquash.xy + _PressSquash.zw;
                v2f basic = SpriteVert(v);
                Output o; UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex=basic.vertex; o.color=basic.color; o.uv=basic.texcoord;
                o.world=mul(unity_ObjectToWorld,UnityFlipSprite(v.vertex,_Flip)).xy;
                return o;
            }
            fixed4 frag(Output i):SV_Target
            {
                fixed4 c=SampleSpriteTexture(i.uv)*i.color;
                float3 light=ScreenVisualLight(i.world,_SV_Layer,1);
                c.rgb*=light;
                if (_SV_LightingEnabled > 0.5 && (_SV_WorldTone.a > 0.001 || _SV_WorldSaturation < 0.999))
                {
                    half lum=dot(c.rgb,half3(0.2126,0.7152,0.0722));
                    c.rgb=lerp(lum.xxx,c.rgb,_SV_WorldSaturation);
                    c.rgb=lerp(c.rgb,lum*_SV_WorldTone.rgb,_SV_WorldTone.a);
                }
                c.rgb*=c.a; return c;
            }
            ENDCG
        }
    }
}
