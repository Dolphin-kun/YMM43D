#ifndef YMM43D_LIGHTING_HLSLI
#define YMM43D_LIGHTING_HLSLI

float3 ApplyLight(float3 color, float3 normal, float3 world)
{
    if (Unlit > 0.5 || dot(normal, normal) < 1e-8)
        return color;

    float3 n = normalize(normal);
    float3 toEye = CameraPosition.xyz - world;

    if (dot(n, toEye) < 0.0)
        n = -n;

    float3 sum = Ambient.rgb;

    [unroll]
    for (int i = 0; i < 4; i++)
    {
        float3 toLight = Lights[i].Vector.xyz;
        float fade = 1.0;

        if (Lights[i].Vector.w > 0.5)
        {
            float3 offset = Lights[i].Vector.xyz - world;
            float distance = length(offset);
            toLight = distance > 1e-6 ? offset / distance : float3(0.0, 0.0, 1.0);
            fade = saturate(1.0 - distance / max(Lights[i].Color.a, 1e-6));
            fade *= fade;
        }

        sum += Lights[i].Color.rgb * saturate(dot(n, toLight)) * fade;
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
