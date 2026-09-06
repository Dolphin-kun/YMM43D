#ifndef YMM43D_LIGHTING_HLSLI
#define YMM43D_LIGHTING_HLSLI

#include "Scene.hlsli"

// 置いてある光源（点光源とスポット）の、その場所での強さ。
// ついでに、そこから光源へ向かう向きも返す。
float LightFalloff(Light light, float3 world, out float3 toLight)
{
    float3 offset = light.Vector.xyz - world;
    float distance = length(offset);

    toLight = distance > 1e-6 ? offset / distance : float3(0.0, 0.0, 1.0);

    float fade = saturate(1.0 - distance / max(light.Color.a, 1e-6));
    fade *= fade;

    if (light.Vector.w < 1.5)
        return fade;

    // 円錐の外側から内側へ向かって明るくする。
    float aligned = dot(-toLight, light.Cone.xyz);
    float cone = saturate((aligned - light.Cone.w) / max(light.Edge.x - light.Cone.w, 1e-4));

    return fade * cone * cone;
}

float3 ApplyLight(float3 color, float3 normal, float3 world)
{
    if (Unlit > 0.5 || dot(normal, normal) < 1e-8)
        return color;

    float3 n = normalize(normal);
    float3 toEye = CameraPosition.xyz - world;

    if (dot(n, toEye) < 0.0)
        n = -n;

    float3 sum = Ambient.rgb;
    int count = (int)LightCount;

    for (int i = 0; i < count; i++)
    {
        Light light = Lights[i];

        float3 toLight = light.Vector.xyz;
        float fade = 1.0;

        if (light.Vector.w > 0.5)
            fade = LightFalloff(light, world, toLight);

        sum += light.Color.rgb * saturate(dot(n, toLight)) * fade;
    }

    return color * sum;
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
