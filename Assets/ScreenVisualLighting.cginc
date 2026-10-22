#ifndef SCREEN_VISUAL_LIGHTING_INCLUDED
#define SCREEN_VISUAL_LIGHTING_INCLUDED
float4 _SV_Ambient;
float4 _SV_LightPositions[8];
float4 _SV_LightColors[8];
float4 _SV_LightMasks[8];
int _SV_LightCount;
float _SV_LightingEnabled;
float3 ScreenVisualLight(float2 world, float layer, float influence)
{
    if (_SV_LightingEnabled < 0.5 || influence <= 0) return float3(1,1,1);
    float3 light = _SV_Ambient.rgb;
    [loop] for (int i = 0; i < _SV_LightCount; i++)
    {
        float mask = layer < 16 ? _SV_LightMasks[i].x : _SV_LightMasks[i].y;
        float included = fmod(floor(mask / exp2(fmod(layer,16))), 2);
        float d = saturate(1 - distance(world, _SV_LightPositions[i].xy) / max(0.01, _SV_LightPositions[i].z));
        light += _SV_LightColors[i].rgb * pow(d, _SV_LightPositions[i].w) * included;
    }
    return lerp(float3(1,1,1), clamp(light, 0.5, 1.65), influence);
}
#endif
