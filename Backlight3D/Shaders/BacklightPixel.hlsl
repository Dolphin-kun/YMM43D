#include "Backlight.hlsli"

Texture2D<float> SceneDepth : register(t4);

static const float VolumeBias = 0.0015;

static const float Bayer[64] =
{
     0.0 / 64.0, 32.0 / 64.0,  8.0 / 64.0, 40.0 / 64.0,  2.0 / 64.0, 34.0 / 64.0, 10.0 / 64.0, 42.0 / 64.0,
    48.0 / 64.0, 16.0 / 64.0, 56.0 / 64.0, 24.0 / 64.0, 50.0 / 64.0, 18.0 / 64.0, 58.0 / 64.0, 26.0 / 64.0,
    12.0 / 64.0, 44.0 / 64.0,  4.0 / 64.0, 36.0 / 64.0, 14.0 / 64.0, 46.0 / 64.0,  6.0 / 64.0, 38.0 / 64.0,
    60.0 / 64.0, 28.0 / 64.0, 52.0 / 64.0, 20.0 / 64.0, 62.0 / 64.0, 30.0 / 64.0, 54.0 / 64.0, 22.0 / 64.0,
     3.0 / 64.0, 35.0 / 64.0, 11.0 / 64.0, 43.0 / 64.0,  1.0 / 64.0, 33.0 / 64.0,  9.0 / 64.0, 41.0 / 64.0,
    51.0 / 64.0, 19.0 / 64.0, 59.0 / 64.0, 27.0 / 64.0, 49.0 / 64.0, 17.0 / 64.0, 57.0 / 64.0, 25.0 / 64.0,
    15.0 / 64.0, 47.0 / 64.0,  7.0 / 64.0, 39.0 / 64.0, 13.0 / 64.0, 45.0 / 64.0,  5.0 / 64.0, 37.0 / 64.0,
    63.0 / 64.0, 31.0 / 64.0, 55.0 / 64.0, 23.0 / 64.0, 61.0 / 64.0, 29.0 / 64.0, 53.0 / 64.0, 21.0 / 64.0,
};

float Dither(float2 pixel)
{
    int2 at = int2(pixel) & 7;

    return Bayer[at.y * 8 + at.x];
}

float3 WorldAt(float2 screen, float depth)
{
    float4 placed = mul(float4(screen, depth, 1.0), Unproject);

    return placed.xyz / (abs(placed.w) > 1e-6 ? placed.w : 1e-6);
}

bool Crosses(float3 from, float3 along, out float enter, out float leave)
{
    float3 start = mul(float4(from, 1.0), BoxInverse).xyz;
    float3 step = mul(float4(along, 0.0), BoxInverse).xyz;

    enter = 0.0;
    leave = 1e9;

    for (int i = 0; i < 3; i++)
    {
        float origin = start[i];
        float speed = step[i];

        if (abs(speed) < 1e-9)
        {
            if (abs(origin) > 0.5)
                return false;

            continue;
        }

        float first = (-0.5 - origin) / speed;
        float second = (0.5 - origin) / speed;

        enter = max(enter, min(first, second));
        leave = min(leave, max(first, second));
    }

    return leave > enter;
}

float Softness(float3 world)
{
    float3 away = abs(mul(float4(world, 1.0), BoxInverse).xyz) * 2.0;

    if (any(away > 1.0))
        return 0.0;

    float soft = max(EdgeBlur, 1e-4);
    float3 fade = saturate((1.0 - away) / soft);

    return fade.x * fade.y * fade.z;
}

float Reaches(Light light, float3 world)
{
    int slice = (int)light.Edge.y;

    if (slice < 0)
        return 1.0;

    int face = IsPointLight(light) ? PointFaceOf(light, world) : 0;
    float3 at = ProjectShadow(light, world, face);

    if (!IsOnShadowMap(light, world, at))
        return IsPointLight(light) ? 0.0 : 1.0;

    return ShadowMaps.SampleCmpLevelZero(
        ShadowSampler, float3(at.xy, slice + face), at.z - VolumeBias);
}

float Scatter(float aligned)
{
    float g = clamp(Forward, 0.0, 0.92);
    float gg = g * g;
    float under = 1.0 + gg - 2.0 * g * aligned;

    return (1.0 - gg) / (4.0 * 3.14159265 * pow(max(under, 1e-4), 1.5));
}

float4 main(PS_INPUT input) : SV_Target
{
    float3 eye = CameraPosition.xyz;
    float3 far = WorldAt(input.Screen, 1.0);
    float3 look = normalize(far - eye);

    float limit = 1e9;

    if (Walk.z > 0.5)
    {
        float depth = SceneDepth.Load(int3(input.Position.xy * Walk.w, 0));

        if (depth < 1.0)
            limit = dot(WorldAt(input.Screen, depth) - eye, look);
    }

    float enter;
    float leave;

    if (!Crosses(eye, look, enter, leave))
        discard;

    float from = max(enter, 0.0);
    float to = min(leave, limit);

    if (to <= from)
        discard;

    int count = (int)LightCount;
    int steps = max((int)Steps, 4);
    float span = (to - from) / steps;
    float start = from + span * Dither(input.Position.xy);

    float3 gathered = float3(0.0, 0.0, 0.0);

    for (int step = 0; step < steps; step++)
    {
        float3 world = eye + look * (start + span * step);
        float held = Softness(world);

        if (held <= 0.002)
            continue;

        for (int i = 0; i < count; i++)
        {
            Light light = Lights[i];

            float3 toLight = light.Vector.xyz;
            float fade = 1.0;

            if (light.Vector.w > 0.5)
                fade = LightFalloff(light, world, toLight);

            if (fade <= 0.0)
                continue;

            float lit = Reaches(light, world);

            if (lit <= 0.0)
                continue;

            float share = fade * lit * held;

            gathered += light.Color.rgb * share * Scatter(dot(-look, -toLight));

            if (Bloom > 0.0 && light.Vector.w > 0.5)
            {
                float away = length(light.Vector.xyz - world);

                gathered += light.Color.rgb * exp(-away / max(light.Color.a * 0.25, 1e-4)) * share * Bloom;
            }
        }
    }

    float3 color = gathered * span * Thickness * Tint.rgb * Fade;
    float strength = saturate(max(color.r, max(color.g, color.b)));

    if (strength <= 0.0)
        discard;

    return float4(color, strength);
}
