Shader "IronBallGirl/Screen Visual Particles"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "white" {}
        _AtlasCrop ("Content Rect Within Each Cell", Vector) = (0,0,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off Lighting Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _AtlasCrop;
            struct Input { float4 vertex:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
            struct Output { float4 vertex:SV_POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
            Output vert(Input v) { Output o; o.vertex=UnityObjectToClipPos(v.vertex); o.color=v.color; o.uv=v.uv; return o; }
            fixed4 frag(Output i):SV_Target
            {
                float2 cell=min(floor(i.uv*2),float2(1,1));
                float2 localUV=i.uv*2-cell;
                float2 uv=(cell+lerp(_AtlasCrop.xy,_AtlasCrop.zw,localUV))*0.5;
                return tex2D(_MainTex,uv)*i.color;
            }
            ENDCG
        }
    }
}
