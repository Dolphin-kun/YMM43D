#include "../../YMM43D/Shaders/Deform.hlsli"

cbuffer FoldConstants : register(b1)
{
    float HalfAngle;
    float FoldCount;
    float AxisRadians;
    float FoldPadding;
};

float2 Turn(float2 v, float angle)
{
    float s = sin(angle);
    float c = cos(angle);

    return float2(v.x * c - v.y * s, v.x * s + v.y * c);
}

float3 Deform(float3 local, float3 piece)
{
    float3 p = float3(Turn(local.xy, -AxisRadians), local.z);

    float count = max(FoldCount, 1.0);

    float span = 1.0 / count;
    float run = span * cos(HalfAngle);
    float rise = span * sin(HalfAngle);

    float along = (p.x + 0.5) * count;
    float index = floor(min(along, count - 1e-4));
    float within = along - index;

    bool up = fmod(index, 2.0) < 1.0;
    float direction = up ? 1.0 : -1.0;

    float x = (index + within) * run - count * run * 0.5;
    float z = (up ? 0.0 : rise) + direction * within * rise - rise * 0.5;

    float3 folded = float3(x, p.y, p.z + z);

    return float3(Turn(folded.xy, AxisRadians), folded.z);
}

float DeformFade(float3 local, float3 piece)
{
    return 1.0;
}
