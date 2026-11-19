#ifndef STAGE2_SUNSET_INCLUDED
#define STAGE2_SUNSET_INCLUDED

float SunsetLuminance(float3 color)
{
    return dot(color, float3(0.2126, 0.7152, 0.0722));
}

float Stage2Sunlight(float2 world)
{
    // Light enters from low on the left and reaches the city through three openings.
    float projectedX = world.x - (world.y - 5) * 0.36;
    float3 distanceToOpening = abs(projectedX - float3(-4, 48, 118));
    float3 openings = 1 - smoothstep(float3(3, 3.5, 4), float3(14, 16, 17), distanceToOpening);
    return max(openings.x, max(openings.y, openings.z));
}

float3 Stage2SunlitSurface(float3 color, float sunlight, float contrast)
{
    float peak = max(color.r, max(color.g, color.b));
    // Preserve the baked high points on lamps, furnaces and footholds.
    float amount = contrast * (1 - smoothstep(0.49, 0.96, peak));
    float3 shifted = color * lerp(float3(0.76, 0.92, 1.34), float3(1.13, 1.01, 0.78), sunlight);
    shifted *= max(0.001, SunsetLuminance(color)) / max(0.001, SunsetLuminance(shifted));
    float exposure = 1 + amount * lerp(-0.18, 0.15, sunlight);
    #ifndef UNITY_COLORSPACE_GAMMA
    // Work in approximate display brightness so the shadow limit also holds in Linear projects.
    exposure *= exposure;
    #endif
    return lerp(color, shifted, amount) * exposure;
}

#endif
