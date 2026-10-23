Shader "IronBallGirl/Ground Tile Seam"
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
        [HideInInspector] _GroundSeamEnabled ("Seam correction", Float) = 0
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
            #include "GroundTileSeam.cginc"
            struct Output { float4 vertex:SV_POSITION; fixed4 color:COLOR; float2 uv:TEXCOORD0; float2 world:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            Output vert(appdata_t v)
            {
                v2f baseVertex = SpriteVert(v);
                Output o;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = baseVertex.vertex; o.color = baseVertex.color; o.uv = baseVertex.texcoord;
                o.world = mul(unity_ObjectToWorld, UnityFlipSprite(v.vertex, _Flip)).xy;
                return o;
            }
            fixed4 frag(Output i):SV_Target
            {
                fixed4 c = ApplyGroundSeam(SampleSpriteTexture(i.uv), i.world) * i.color;
                c.rgb *= c.a;
                return c;
            }
            ENDCG
        }
    }
}
