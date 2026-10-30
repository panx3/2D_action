#ifndef IRON_BALL_GIRL_STAGE_SELECT_CRT_SHAPE
#define IRON_BALL_GIRL_STAGE_SELECT_CRT_SHAPE

// A nearly rectangular glass face with gently bowed edges and a separate, modest corner radius.
float StageSelectFaceDistance(float2 facePosition, float2 halfSize, float2 shape, float clearBlend)
{
    float flatten = saturate(clearBlend * 1.7);
    float2 p = facePosition / max(halfSize, 1);
    p *= 1 + shape.x * (1 - flatten) * p.yx * p.yx;
    float radius = lerp(shape.y, 1, flatten);
    float2 q = abs(p * halfSize) - halfSize + radius;
    return length(max(q, 0)) + min(max(q.x, q.y), 0) - radius;
}

#endif
