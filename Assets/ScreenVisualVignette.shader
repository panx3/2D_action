Shader "IronBallGirl/Screen Visual Vignette"
{
    Properties
    {
        [PerRendererData] _MainTex ("UI Texture", 2D) = "white" {}
        _Color ("Color", Color) = (0.055,0.07,0.14,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct Input { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct Output { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; };
            float4 _Color;
            float4 _Vignette; // intensity, smoothness, roundness, aspect
            Output vert(Input v) { Output o; o.vertex=UnityObjectToClipPos(v.vertex); o.uv=v.uv; return o; }
            fixed4 frag(Output i):SV_Target
            {
                float2 p=abs(i.uv*2-1);
                float aspect=max(0.1,_Vignette.w);
                float2 circle=p*float2(aspect,1)/sqrt(aspect*aspect+1);
                float d=lerp(pow(pow(p.x,4)+pow(p.y,4),0.25)/1.189207, length(circle), _Vignette.z);
                float edge=smoothstep(1-_Vignette.y,1,d);
                return float4(_Color.rgb,edge*_Vignette.x*_Color.a);
            }
            ENDCG
        }
    }
}
