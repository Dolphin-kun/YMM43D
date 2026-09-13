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

static const float PointFaceScale = 1.05;

static const float MaxSlopeBias = 0.1;

static const int ShadowTapCount = 12;

static const float2 ShadowTaps[ShadowTapCount] =
{
    float2(-0.326, -0.406), float2(-0.840, -0.074), float2(-0.696, 0.457), float2(-0.203, 0.621),
    float2(0.962, -0.195), float2(0.473, -0.480), float2(0.519, 0.767), float2(0.185, -0.893),
    float2(0.507, 0.064), float2(0.896, 0.412), float2(-0.322, -0.933), float2(-0.792, -0.598),
};

bool IsPointLight(Light light)
{
    return light.Vector.w > 0.5 && light.Vector.w < 1.5;
}

int PointFaceOf(Light light, float3 world)
{
    float3 offset = world - light.Vector.xyz;
    float3 span = abs(offset);

    if (span.x >= span.y && span.x >= span.z)
        return offset.x > 0.0 ? 0 : 1;

    if (span.y >= span.z)
        return offset.y > 0.0 ? 2 : 3;

    return offset.z > 0.0 ? 4 : 5;
}

float3 ProjectOnPointFace(Light light, float3 world, int face)
{
    static const float3 Forwards[6] =
    {
        float3(1.0, 0.0, 0.0), float3(-1.0, 0.0, 0.0),
        float3(0.0, 1.0, 0.0), float3(0.0, -1.0, 0.0),
        float3(0.0, 0.0, 1.0), float3(0.0, 0.0, -1.0),
    };

    float3 offset = world - light.Vector.xyz;
    float3 forward = Forwards[face];
    float3 up = abs(forward.y) > 0.5 ? float3(0.0, 0.0, 1.0) : float3(0.0, 1.0, 0.0);
    float3 back = -forward;
    float3 right = normalize(cross(up, back));
    float3 above = cross(back, right);

    float along = max(dot(offset, forward), 1e-6);
    float far = light.Color.a;
    float near = max(far * 0.01, 0.01);

    float2 flat = float2(dot(offset, right), dot(offset, above)) / (along * PointFaceScale);

    return float3(flat * float2(0.5, -0.5) + 0.5, far * (along - near) / ((far - near) * along));
}

float3 ProjectOnShadowMap(Light light, float3 world)
{
    float4 placed = mul(float4(world, 1.0), light.Shadow);
    placed.xyz /= max(placed.w, 1e-6);

    return float3(placed.xy * float2(0.5, -0.5) + 0.5, placed.z);
}

float3 ProjectShadow(Light light, float3 world, int face)
{
    return IsPointLight(light) ? ProjectOnPointFace(light, world, face) : ProjectOnShadowMap(light, world);
}

bool IsOnShadowMap(Light light, float3 world, float3 coordinate)
{
    if (IsPointLight(light))
        return coordinate.z > 0.0 && coordinate.z < 1.0;

    float4 placed = mul(float4(world, 1.0), light.Shadow);

    return placed.w > 0.0
        && coordinate.z > 0.0 && coordinate.z < 1.0
        && all(coordinate.xy >= 0.0) && all(coordinate.xy <= 1.0);
}

float2 DepthSlope(float3 center, float3 alongX, float3 alongY)
{
    float3 dx = alongX - center;
    float3 dy = alongY - center;

    float det = dx.x * dy.y - dx.y * dy.x;

    if (abs(det) < 1e-12)
        return float2(0.0, 0.0);

    return float2(dy.y * dx.z - dx.y * dy.z, dx.x * dy.z - dy.x * dx.z) / det;
}

float ShadowAt(Light light, float3 world, float3 worldDx, float3 worldDy, float3 normal, float lambert)
{
    int slice = (int)light.Edge.y;

    if (slice < 0)
        return 1.0;

    float3 lifted = world + normal * (ShadowLift * (2.0 - lambert));

    int face = IsPointLight(light) ? PointFaceOf(light, lifted) : 0;

    float3 center = ProjectShadow(light, lifted, face);

    if (!IsOnShadowMap(light, lifted, center))
        return 1.0;

    float2 slope = DepthSlope(
        center,
        ProjectShadow(light, lifted + worldDx, face),
        ProjectShadow(light, lifted + worldDy, face));

    float radius = light.Edge.w;
    float turn = frac(sin(dot(world, float3(12.9898, 78.233, 37.719))) * 43758.5453) * 6.2831853;
    float s = sin(turn);
    float c = cos(turn);

    float lit = ShadowMaps.SampleCmpLevelZero(ShadowSampler, float3(center.xy, slice + face), center.z);

    [unroll]
    for (int i = 0; i < ShadowTapCount; i++)
    {
        float2 tap = ShadowTaps[i];
        float2 offset = float2(tap.x * c - tap.y * s, tap.x * s + tap.y * c) * radius;

        float bias = clamp(dot(slope, offset), -MaxSlopeBias, MaxSlopeBias);

        lit += ShadowMaps.SampleCmpLevelZero(
            ShadowSampler, float3(center.xy + offset, slice + face), center.z + bias);
    }

    return lerp(1.0, lit / (ShadowTapCount + 1), saturate(light.Edge.z));
}

#endif
