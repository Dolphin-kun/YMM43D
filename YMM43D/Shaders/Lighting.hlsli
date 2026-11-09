#ifndef YMM43D_LIGHTING_HLSLI
#define YMM43D_LIGHTING_HLSLI

#include "Scene.hlsli"

float LightFalloff(Light light, float3 world, out float3 toLight)
{
    float3 offset = light.Vector.xyz - world;
    float distance = length(offset);

    toLight = distance > 1e-6 ? offset / distance : float3(0.0, 0.0, 1.0);

    float fade = saturate(1.0 - distance / max(light.Color.a, 1e-6));
    fade *= fade;

    if (light.Vector.w < 1.5)
        return fade;

    float aligned = dot(-toLight, light.Cone.xyz);
    float cone = saturate((aligned - light.Cone.w) / max(light.Edge.x - light.Cone.w, 1e-4));

    return fade * cone * cone;
}

float3 AmbientOf(float3 normal)
{
    if (Ground.a <= 0.0)
        return Ambient.rgb;

    return lerp(Ground.rgb, Ambient.rgb, saturate(normal.y * 0.5 + 0.5));
}

float Tone(float level)
{
    if (ToonSteps < 2.0)
        return level;

    float steps = ToonSteps - 1.0;
    float soft = max(ToonFeather, 0.002);

    float scaled = saturate(level) * steps;
    float stair = floor(scaled);
    float edge = smoothstep(0.5 - soft, 0.5 + soft, scaled - stair);

    return (stair + edge) / steps;
}

float3 FaceCamera(float3 normal, float3 world, out float3 toEye)
{
    float3 n = normalize(normal);
    float3 offset = CameraPosition.xyz - world;
    float distance = length(offset);

    toEye = distance > 1e-6 ? offset / distance : n;

    return dot(n, toEye) < 0.0 ? -n : n;
}

void GatherLight(
    float3 n,
    float3 world,
    float3 worldDx,
    float3 worldDy,
    float3 toEye,
    out float3 sum,
    out float3 shine)
{
    sum = float3(0.0, 0.0, 0.0);
    shine = float3(0.0, 0.0, 0.0);

    int count = (int)LightCount;

    for (int i = 0; i < count; i++)
    {
        Light light = Lights[i];

        float3 toLight = light.Vector.xyz;
        float fade = 1.0;

        if (light.Vector.w > 0.5)
            fade = LightFalloff(light, world, toLight);

        float lambert = saturate(dot(n, toLight) + ToonShift);

        if (lambert <= 0.0 || fade <= 0.0)
            continue;

        float shaded = Tone(lambert * ShadowAt(light, world, worldDx, worldDy, n, lambert));
        float3 received = light.Color.rgb * fade;

        sum += received * shaded;

        if (Gloss > 0.0)
        {
            float3 halfway = normalize(toLight + toEye);
            float normalization = (GlossPower + 8.0) / 8.0;

            shine += received * shaded * normalization * pow(saturate(dot(n, halfway)), GlossPower);
        }
    }
}

float3 ApplyLight(float3 color, float3 normal, float3 world)
{
    float3 worldDx = ddx(world);
    float3 worldDy = ddy(world);

    if (Unlit > 0.5 || dot(normal, normal) < 1e-8)
        return color;

    float3 toEye;
    float3 n = FaceCamera(normal, world, toEye);

    float3 sum;
    float3 shine;
    GatherLight(n, world, worldDx, worldDy, toEye, sum, shine);

    return color * (AmbientOf(n) + sum) + shine * Gloss;
}

float3 ApplyFog(float3 color, float3 world)
{
    if (FogColor.a <= 0.0)
        return color;

    float distance = length(CameraPosition.xyz - world);
    float amount = saturate((distance - FogStart) / max(FogEnd - FogStart, 1e-6));

    return lerp(color, FogColor.rgb, amount * FogColor.a);
}

#endif
