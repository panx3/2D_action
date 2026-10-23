#ifndef GROUND_TILE_SEAM_INCLUDED
#define GROUND_TILE_SEAM_INCLUDED
sampler2D _GroundSeamLookup, _GroundSeamAtlas;
float4 _GroundSeamBounds, _GroundSeamCellSize;
float4 _GroundSeamAtlas_TexelSize;
float4x4 _GroundSeamWorldToLocal;
float _GroundSeamEnabled;

fixed4 ApplyGroundSeam(fixed4 source, float2 world)
{
    if (_GroundSeamEnabled < 0.5) return source;
    float2 local = mul(_GroundSeamWorldToLocal, float4(world, 0, 1)).xy / _GroundSeamCellSize.xy;
    float2 cell = floor(local);
    float2 index = cell - _GroundSeamBounds.xy;
    if (any(index < 0) || any(index >= _GroundSeamBounds.zw)) return source;
    float4 patch = tex2D(_GroundSeamLookup, (index + 0.5) / _GroundSeamBounds.zw);
    if (patch.z <= 0 || patch.w <= 0) return source;
    float2 pixels = round(patch.zw / _GroundSeamAtlas_TexelSize.xy);
    float2 uv = patch.xy + (floor(saturate(frac(local)) * pixels) + 0.5) * _GroundSeamAtlas_TexelSize.xy;
    fixed4 corrected = tex2D(_GroundSeamAtlas, uv);
    source.rgb = lerp(source.rgb, corrected.rgb, corrected.a);
    return source; // Keep original alpha and original mesh/silhouette.
}
#endif
