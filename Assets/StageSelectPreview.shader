Shader "Hidden/IronBallGirl/StageSelectPreview"
{
    Properties { _MainTex ("Unused", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _FarTex, _MiddleTex, _NearTex;
            float4 _FarRect, _MiddleRect, _NearRect;
            float4 _FarCrop, _MiddleCrop, _NearCrop;
            float4 _FarTint, _MiddleTint, _NearTint, _Background;
            float _PreviewTime;
            fixed4 Layer(sampler2D tex, float2 uv, float4 rect, float4 crop, float4 tint, float phase)
            {
                uv = (uv - .5) * crop.xy * .94 + .5;
                uv.x += sin(_PreviewTime * .055 + phase) * crop.z;
                uv.y += cos(_PreviewTime * .035 + phase) * crop.z * .12;
                return tex2D(tex, rect.xy + saturate(uv) * rect.zw) * tint;
            }
            fixed4 frag(v2f_img i) : SV_Target
            {
                float3 c = _Background.rgb;
                fixed4 far = Layer(_FarTex, i.uv, _FarRect, _FarCrop, _FarTint, 0);
                fixed4 mid = Layer(_MiddleTex, i.uv, _MiddleRect, _MiddleCrop, _MiddleTint, .6);
                fixed4 near = Layer(_NearTex, i.uv, _NearRect, _NearCrop, _NearTint, 1.2);
                c = lerp(c, far.rgb, far.a);
                c = lerp(c, mid.rgb, mid.a);
                return fixed4(lerp(c, near.rgb, near.a), 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
