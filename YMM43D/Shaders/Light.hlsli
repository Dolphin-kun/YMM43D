#ifndef YMM43D_LIGHT_HLSLI
#define YMM43D_LIGHT_HLSLI

struct Light
{
    float4 Vector;

    float4 Color;

    float4 Cone;

    float4 Edge;

    matrix Shadow;
};

StructuredBuffer<Light> Lights : register(t1);

Texture2DArray ShadowMaps : register(t2);
SamplerComparisonState ShadowSampler : register(s1);

static const float ShadowLift = 0.02;

float ShadowAt(Light light, float3 world, float3 normal, float lambert)
{
    int slice = (int)light.Edge.y;

    if (slice < 0)
        return 1.0;

    float3 lifted = world + normal * (ShadowLift * (2.0 - lambert));

    float4 placed = mul(float4(lifted, 1.0), light.Shadow);

    if (placed.w <= 0.0)
        return 1.0;

    placed.xyz /= placed.w;

    if (placed.z <= 0.0 || placed.z >= 1.0)
        return 1.0;

    float2 uv = placed.xy * float2(0.5, -0.5) + 0.5;

    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
        return 1.0;

    float texel = light.Edge.w;
    float lit = 0.0;

    [unroll]
    for (int y = -1; y <= 1; y++)
    {
        [unroll]
        for (int x = -1; x <= 1; x++)
        {
            float2 at = uv + float2(x, y) * texel;

            lit += ShadowMaps.SampleCmpLevelZero(ShadowSampler, float3(at, slice), placed.z);
        }
    }

    return lerp(1.0, lit / 9.0, saturate(light.Edge.z));
}

#endif
