Shader "IronBallGirl/Screen Visual Petals"
{
    Properties
    {
        _MainTex ("Petal Atlas", 2D) = "white" {}
        _PetalPixels ("Pixels Per Petal (Lower = Coarser)", Range(8,32)) = 16
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
            float _PetalPixels;
            struct Input { float4 vertex:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
            struct Output { float4 vertex:SV_POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
            Output vert(Input v) { Output o; o.vertex=UnityObjectToClipPos(v.vertex); o.color=v.color; o.uv=v.uv; return o; }
            fixed4 frag(Output i):SV_Target
            {
                // Two cells per atlas axis. Sample one texel for each coarse
                // block so the grid follows each petal's turn and silhouette.
                float atlasPixels = floor(clamp(_PetalPixels, 8, 32) + 0.5) * 2;
                float2 pixel = min(floor(saturate(i.uv) * atlasPixels), atlasPixels - 1);
                fixed4 petal = tex2D(_MainTex, (pixel + 0.5) / atlasPixels);
                // Cut only the source edge; retain the layer opacity and fades.
                petal.a = step(0.5, petal.a);
                return petal * i.color;
            }
            ENDCG
        }
    }
}
