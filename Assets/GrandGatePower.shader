Shader "IronBallGirl/Grand Gate Power"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _PowerMask ("Electrical regions", 2D) = "black" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [PerRendererData] _GatePower ("Charge", Range(0,1)) = 0
        [PerRendererData] _GatePulse ("Electrical pulse", Float) = 0
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
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ PIXELSNAP_ON
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #include "UnitySprites.cginc"
            #include "ScreenVisualLighting.cginc"
            sampler2D _PowerMask;
            float _GatePower, _GatePulse;
            struct Output { float4 vertex:SV_POSITION; fixed4 color:COLOR; float2 uv:TEXCOORD0; float2 world:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            Output vert(appdata_t v)
            {
                v2f b = SpriteVert(v);
                Output o;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex=b.vertex; o.color=b.color; o.uv=b.texcoord;
                o.world=mul(unity_ObjectToWorld,UnityFlipSprite(v.vertex,_Flip)).xy;
                return o;
            }
            fixed4 frag(Output i):SV_Target
            {
                fixed4 s=SampleSpriteTexture(i.uv);
                float mask=tex2D(_PowerMask,i.uv).r;
                float charge=saturate(_GatePower);
                s.rgb*=lerp(1.0,lerp(0.10,1.0,charge)+_GatePulse*charge,mask);
                fixed4 c=s*i.color;
                float3 light=ScreenVisualLight(i.world,0,1);
                c.rgb*=lerp(light,float3(1,1,1),mask*charge*0.65)*c.a;
                return c;
            }
            ENDCG
        }
    }
}
