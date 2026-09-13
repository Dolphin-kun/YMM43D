#include "../../YMM43D/Shaders/Lighting.hlsli"

Texture2D SceneTexture : register(t0);
SamplerState SceneSampler : register(s0);

cbuffer GlassConstants : register(b1)
{
    float4 GlassSize;
    float4 GlassCamera;
    float4 GlassOptics;
    float4 GlassTint;
    float4 GlassSurface;
};

#define CornerRadius    GlassSize.w
#define IsSphere        GlassCamera.w
#define RefractiveIndex GlassOptics.x
#define BehindDistance  GlassOptics.y
#define FrostAmount     GlassOptics.z
#define DispersionRate  GlassOptics.w
#define ReflectionRate  GlassSurface.x
#define SceneMipLevels  GlassSurface.y
#define HasScene        GlassSurface.z

static const int MaxSteps = 64;
static const int InsideSteps = 24;
static const float HitDistance = 0.25;

struct VS_IN
{
    float3 Pos : POSITION;
    float4 Col : COLOR;
    float2 Tex : TEXCOORD0;
    float3 Nrm : NORMAL;
};

struct PS_IN
{
    float4 Pos : SV_POSITION;
    float3 Local : TEXCOORD0;
};

struct PS_OUT
{
    float4 Color : SV_Target;
    float Depth : SV_Depth;
};

PS_IN VSMain(VS_IN input)
{
    PS_IN output;

    output.Pos = mul(float4(input.Pos, 1.0), WorldViewProjection);
    output.Local = input.Pos;

    return output;
}

float RoundedBoxDistance(float3 p)
{
    float3 extent = GlassSize.xyz * 0.5;
    float radius = min(CornerRadius, min(extent.x, min(extent.y, extent.z)));
    float3 q = abs(p) - extent + radius;

    return length(max(q, 0.0)) + min(max(q.x, max(q.y, q.z)), 0.0) - radius;
}

float SphereDistance(float3 p)
{
    return length(p) - GlassSize.x * 0.5;
}

float ShapeDistance(float3 p)
{
    return IsSphere > 0.5 ? SphereDistance(p) : RoundedBoxDistance(p);
}

float3 ShapeNormal(float3 p)
{
    const float e = 0.5;
    const float2 k = float2(1.0, -1.0);

    float3 n = k.xyy * ShapeDistance(p + k.xyy * e)
             + k.yyx * ShapeDistance(p + k.yyx * e)
             + k.yxy * ShapeDistance(p + k.yxy * e)
             + k.xxx * ShapeDistance(p + k.xxx * e);

    return dot(n, n) > 1e-12 ? normalize(n) : float3(0.0, 0.0, -1.0);
}

float4 ToClip(float3 pixels)
{
    return mul(float4(pixels / GlassSize.xyz, 1.0), WorldViewProjection);
}

float2 ToSceneUv(float3 pixels)
{
    float4 clip = ToClip(pixels);
    return clip.xy / max(clip.w, 1e-5) * float2(0.5, -0.5) + 0.5;
}

float3 ToWorldNormal(float3 normal)
{
    return normalize(mul(float4(normal * GlassSize.xyz, 0.0), WorldInverse).xyz);
}

bool EnterBox(float3 origin, float3 direction, out float enter, out float leave)
{
    float3 extent = GlassSize.xyz * 0.5;
    float3 inverse = 1.0 / (abs(direction) > 1e-6 ? direction : 1e-6);
    float3 a = (-extent - origin) * inverse;
    float3 b = (extent - origin) * inverse;
    float3 nearest = min(a, b);
    float3 farthest = max(a, b);

    enter = max(max(nearest.x, nearest.y), max(nearest.z, 0.0));
    leave = min(farthest.x, min(farthest.y, farthest.z));

    return leave > enter;
}

float3 Specular(float3 world, float3 normal, float3 toEye)
{
    float3 shine = float3(0.0, 0.0, 0.0);
    int count = (int)LightCount;

    for (int i = 0; i < count; i++)
    {
        Light light = Lights[i];

        float3 toLight = light.Vector.xyz;
        float fade = 1.0;

        if (light.Vector.w > 0.5)
            fade = LightFalloff(light, world, toLight);

        if (fade <= 0.0 || dot(normal, toLight) <= 0.0)
            continue;

        float3 halfway = normalize(toLight + toEye);
        float normalization = (GlossPower + 8.0) / 8.0;

        shine += light.Color.rgb * fade * normalization * pow(saturate(dot(normal, halfway)), GlossPower);
    }

    return shine * Gloss;
}

float4 SampleScene(float2 uv, float lod)
{
    return SceneTexture.SampleLevel(SceneSampler, uv, lod);
}

PS_OUT PSMain(PS_IN input)
{
    float3 origin = GlassCamera.xyz * GlassSize.xyz;
    float3 direction = normalize(input.Local * GlassSize.xyz - origin);

    float footprint = max(length(ddx(direction)), length(ddy(direction)));

    float enter;
    float leave;

    if (!EnterBox(origin, direction, enter, leave))
        discard;

    float t = enter;
    float closest = 1e9;
    float closestT = t;
    bool hit = false;

    [loop]
    for (int i = 0; i < MaxSteps; i++)
    {
        float d = ShapeDistance(origin + direction * t);

        if (d < closest)
        {
            closest = d;
            closestT = t;
        }

        if (d < HitDistance)
        {
            hit = true;
            break;
        }

        t += d;

        if (t > leave)
            break;
    }

    float coverage = hit ? 1.0 : saturate(1.0 - closest / max(closestT * footprint, 1e-3));

    if (coverage <= 0.0)
        discard;

    float3 surface = origin + direction * (hit ? t : closestT);
    float3 normal = ShapeNormal(surface);

    float4 surfaceClip = ToClip(surface);

    float ior = RefractiveIndex;
    float3 inside = refract(direction, normal, 1.0 / ior);

    if (dot(inside, inside) < 1e-6)
        inside = reflect(direction, normal);

    float3 exitPoint = surface + inside * 1.0;

    [loop]
    for (int j = 0; j < InsideSteps; j++)
    {
        float remaining = -ShapeDistance(exitPoint);

        if (remaining < HitDistance)
            break;

        exitPoint += inside * max(remaining, 1.0);
    }

    float3 exitNormal = ShapeNormal(exitPoint);
    float3 outside = refract(inside, -exitNormal, ior);

    if (dot(outside, outside) < 1e-6)
        outside = reflect(inside, -exitNormal);

    float2 bent = ToSceneUv(exitPoint + outside * BehindDistance);
    float2 straight = ToSceneUv(exitPoint + direction * BehindDistance);
    float2 shift = bent - straight;

    float lod = FrostAmount * max(SceneMipLevels - 1.0, 0.0) * 0.6;

    float4 scene = float4(0.0, 0.0, 0.0, 0.0);

    if (HasScene > 0.5)
    {
        float4 green = SampleScene(bent, lod);
        float red = SampleScene(straight + shift * (1.0 + DispersionRate), lod).r;
        float blue = SampleScene(straight + shift * max(1.0 - DispersionRate, 0.0), lod).b;

        scene = float4(red, green.g, blue, green.a);
    }

    float3 worldSurface = mul(float4(surface / GlassSize.xyz, 1.0), World).xyz;
    float3 worldNormal = ToWorldNormal(normal);
    float3 toEye = normalize(CameraPosition.xyz - worldSurface);

    if (dot(worldNormal, toEye) < 0.0)
        worldNormal = -worldNormal;

    float cosine = saturate(dot(worldNormal, toEye));
    float f0 = pow((ior - 1.0) / (ior + 1.0), 2.0);
    float fresnel = (f0 + (1.0 - f0) * pow(1.0 - cosine, 5.0)) * ReflectionRate;

    float3 environment = lerp(Ambient.rgb, float3(1.0, 1.0, 1.0), 0.55 + 0.45 * worldNormal.y);
    float3 shine = Unlit > 0.5 ? float3(0.0, 0.0, 0.0) : Specular(worldSurface, worldNormal, toEye);

    float3 absorbed = scene.rgb * lerp(float3(1.0, 1.0, 1.0), GlassTint.rgb, GlassTint.a);
    float3 premultiplied = absorbed * (1.0 - fresnel) + environment * fresnel + shine
                         + GlassTint.rgb * GlassTint.a * 0.08;

    float alpha = saturate(scene.a * (1.0 - fresnel) + fresnel + max(shine.r, max(shine.g, shine.b)) + 0.06 + GlassTint.a * 0.2);
    alpha *= coverage * Opacity;

    PS_OUT output;
    output.Color = float4(saturate(premultiplied / max(alpha / max(coverage * Opacity, 1e-4), 1e-4)), alpha);
    output.Depth = surfaceClip.z / max(surfaceClip.w, 1e-5);

    return output;
}
