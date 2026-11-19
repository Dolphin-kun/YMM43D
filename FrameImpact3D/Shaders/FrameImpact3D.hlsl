#include "../../YMM43D/Shaders/Scene.hlsli"

cbuffer ImpactConstants : register(b1)
{
    float4 Center;
    float4 AxisU;
    float4 AxisV;
    float4 AxisW;
    float4 Right;
    float4 Up;
    float4 Tint;
    float4 Aim;
};
#define Progress  Center.w
#define Reach     AxisU.w
#define Width     AxisV.w
#define Extra     AxisW.w
#define Twist     Up.w
#define SeesDepth AxisV.w
#define AimAngle  Aim.x
#define AimSpread Aim.y
#define Stretch   Aim.z
#define LinesLeave Aim.w

Texture2D<float> SceneDepth : register(t12);

static const float Tau = 6.2831853;
static const float LineStartSpan = 0.2;
static const float LineDelaySpan = 0.3;
static const float SparkTrail = 0.18;
static const float ArcSoftness = 0.3;
static const float2 DepthTaps[5] = { float2(0.0, 0.0), float2(4.0, 0.0), float2(-4.0, 0.0), float2(0.0, 4.0), float2(0.0, -4.0) };

struct VS_IN
{
    float3 Dir : POSITION;
    float4 Seed : TEXCOORD0;
    float2 Corner : TEXCOORD1;
};

struct PS_IN
{
    float4 Pos : SV_POSITION;
    float2 Corner : TEXCOORD0;
    float4 Data : TEXCOORD1;
};

float EaseOut(float t)
{
    float rest = 1.0 - t;
    return 1.0 - rest * rest * rest;
}

float3 InPlane(float angle)
{
    float s, c;
    sincos(angle, s, c);
    return normalize(c * AxisU.xyz + s * AxisV.xyz);
}

float3 PlaneDirection(float fraction)
{
    return InPlane(AimAngle + (fraction - 0.5) * AimSpread);
}

float3 ConeDirection(float2 random)
{
    float3 forward = InPlane(AimAngle);
    float3 across = normalize(cross(AxisW.xyz, forward));
    float lowest = cos(AimSpread * 0.5);
    float tilt = 1.0 - random.x * (1.0 - lowest);
    float lean = sqrt(saturate(1.0 - tilt * tilt));

    float s, c;
    sincos(random.y * Tau, s, c);

    return normalize(forward * tilt + (across * c + AxisW.xyz * s) * lean);
}

float ArcMask(float2 local)
{
    if (AimSpread >= Tau - 1e-3)
        return 1.0;

    float delta = atan2(local.y, local.x) - AimAngle;
    delta = delta - Tau * floor((delta + 3.14159265) / Tau);

    return saturate((AimSpread * 0.5 - abs(delta)) / ArcSoftness + 0.5);
}

float3 Side(float3 axis, float3 at)
{
    float3 side = cross(axis, CameraPosition.xyz - at);
    float size = length(side);
    return size > 1e-6 ? side / size : Right.xyz;
}

PS_IN Emit(float3 world, float2 corner, float4 data)
{
    PS_IN output;

    output.Pos = mul(float4(world, 1.0), WorldViewProjection);
    output.Corner = corner;
    output.Data = data;

    return output;
}

float4 Glow(float3 color, float amount)
{
    float strength = max(amount, 0.0) * Tint.a * Opacity;
    return float4(color * strength, saturate(strength));
}

PS_IN VSRing(VS_IN input)
{
    float delay = input.Seed.x;
    float started = Progress > delay ? 1.0 : 0.0;
    float age = saturate((Progress - delay) / max(1.0 - delay, 1e-3));
    float radius = Reach * EaseOut(age);
    float thickness = max(Width * (0.3 + 0.7 * (1.0 - age)) * 0.5, 1e-4);
    float extent = (radius + thickness * 3.0) * started;
    float3 world = Center.xyz + (AxisU.xyz * input.Corner.x + AxisV.xyz * input.Corner.y) * extent;

    return Emit(world, input.Corner * extent, float4(radius, thickness, pow(1.0 - age, 1.5), 0.0));
}

float4 PSRing(PS_IN input) : SV_TARGET
{
    float distance = length(input.Corner);
    float radius = input.Data.x;
    float offset = (distance - radius) / input.Data.y;
    float band = exp(-offset * offset * 2.0);
    float inner = distance < radius ? pow(distance / max(radius, 1e-4), 6.0) * 0.35 : 0.0;
    float3 color = lerp(Tint.rgb, 1.0, band * band * 0.6);

    return Glow(color, (band + inner) * input.Data.z * ArcMask(input.Corner));
}

PS_IN VSLines(VS_IN input)
{
    float delay = input.Seed.y * LineDelaySpan;
    float started = Progress >= delay ? 1.0 : 0.0;
    float age = saturate((Progress - delay) / (1.0 - LineDelaySpan));
    float3 axis = LinesLeave > 0.5 ? ConeDirection(input.Dir.yz) : PlaneDirection(input.Dir.x);
    float start = Reach * LineStartSpan * (0.25 + 0.75 * input.Seed.w);
    float tail = start + Reach * EaseOut(age);
    float head = tail + Extra * (0.4 + 0.6 * input.Seed.x);
    float along = input.Corner.x * 0.5 + 0.5;
    float3 at = Center.xyz + axis * lerp(tail, head, along);
    float width = Width * (0.5 + 0.5 * input.Seed.z) * (1.0 - age) * started;
    float3 world = at + Side(axis, at) * input.Corner.y * width * 0.5;

    return Emit(world, float2(along, input.Corner.y), float4((1.0 - age * age) * started, 0.0, 0.0, 0.0));
}

float4 PSLines(PS_IN input) : SV_TARGET
{
    float along = input.Corner.x;
    float edge = abs(input.Corner.y);
    float profile = saturate(along * 10.0) * pow(saturate(1.0 - along), 0.7);
    float soft = max(fwidth(edge) * 1.5, 1e-3);
    float body = saturate((profile - edge) / soft);
    float3 color = lerp(Tint.rgb, 1.0, saturate(1.0 - edge * 2.0) * 0.5);

    return Glow(color, body * input.Data.x);
}

float Visibility(float3 world)
{
    if (SeesDepth < 0.5)
        return 1.0;

    float4 clip = mul(float4(world, 1.0), WorldViewProjection);

    if (clip.w <= 0.0)
        return 0.0;

    float3 ndc = clip.xyz / clip.w;

    if (any(abs(ndc.xy) > 1.0))
        return 1.0;

    uint wide;
    uint tall;
    SceneDepth.GetDimensions(wide, tall);

    float2 size = float2(wide, tall);
    float2 pixel = float2(ndc.x * 0.5 + 0.5, 0.5 - ndc.y * 0.5) * size;
    float seen = 0.0;

    [unroll]
    for (int i = 0; i < 5; i++)
    {
        int2 at = int2(clamp(pixel + DepthTaps[i], 0.0, size - 1.0));
        seen += ndc.z <= SceneDepth.Load(int3(at, 0)) ? 1.0 : 0.0;
    }

    return seen / 5.0;
}

PS_IN VSFlash(VS_IN input)
{
    float s, c;
    sincos(Twist, s, c);

    float3 along = c * Right.xyz + s * Up.xyz;
    float3 across = -s * Right.xyz + c * Up.xyz;
    float3 world = Center.xyz + (along * input.Corner.x * Stretch + across * input.Corner.y) * Reach;

    return Emit(world, input.Corner, float4(Visibility(Center.xyz), 0.0, 0.0, 0.0));
}

float4 PSFlash(PS_IN input) : SV_TARGET
{
    float2 corner = input.Corner;
    float square = dot(corner, corner);
    float core = exp(-square * 20.0);
    float halo = exp(-square * 4.0) * 0.5;
    float star = exp(-abs(corner.y) * 60.0) * saturate(1.0 - abs(corner.x)) + exp(-abs(corner.x) * 60.0) * saturate(1.0 - abs(corner.y));
    float edge = saturate((1.0 - sqrt(square)) * 4.0);
    float3 color = lerp(Tint.rgb, 1.0, saturate(core * 1.5));

    return Glow(color, (core + halo + star * 0.7) * edge * input.Data.x);
}

float3 SparkAt(float3 axis, float speed, float age)
{
    float rest = 1.0 - age;
    float travel = 1.0 - pow(rest, 2.5);

    return Center.xyz + axis * Reach * speed * travel - float3(0.0, 1.0, 0.0) * Extra * age * age;
}

PS_IN VSSparks(VS_IN input)
{
    float life = 0.45 + 0.55 * input.Seed.y;
    float age = saturate(Progress / life);
    float speed = 0.35 + 0.65 * input.Seed.x;
    float3 axis = ConeDirection(input.Dir.yz);
    float3 head = SparkAt(axis, speed, age);
    float3 tail = SparkAt(axis, speed, max(age - SparkTrail, 0.0));
    float3 trail = head - tail;
    float size = length(trail);
    float3 forward = size > 1e-6 ? trail / size : axis;
    float width = Width * (0.6 + 0.4 * input.Seed.z) * (1.0 - age * age);
    float along = input.Corner.x * 0.5 + 0.5;
    float3 at = lerp(tail - forward * width * 0.5, head + forward * width * 0.5, along);
    float3 world = at + Side(forward, at) * input.Corner.y * width * 0.5;

    return Emit(world, float2(along, input.Corner.y), float4(1.0 - age, age < 1.0 ? 1.0 - age * age : 0.0, 0.0, 0.0));
}

float4 PSSparks(PS_IN input) : SV_TARGET
{
    float along = input.Corner.x;
    float edge = abs(input.Corner.y);
    float body = saturate(1.0 - edge * edge) * saturate(along * 3.0) * saturate((1.0 - along) * 8.0);
    float heat = input.Data.x;
    float3 color = lerp(Tint.rgb, 1.0, heat * heat * 0.6);

    return Glow(color, body * (0.5 + heat) * input.Data.y);
}
