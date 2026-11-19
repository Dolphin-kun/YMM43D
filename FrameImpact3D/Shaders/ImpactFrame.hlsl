cbuffer FrameConstants : register(b1)
{
    float4 Look;
    float4 Spread;
    float4 Focus;
    float4 Tint;
    float4 Range;
    float4 Aim;
};
#define Kind      Look.x
#define Threshold Look.y
#define Edge      Look.z
#define BlurSize  Spread.x
#define Shift     Spread.y
#define GlowSize  Spread.z
#define Size      Focus.zw
#define Radius    Range.x
#define Softness  Range.y
#define Heading   Aim.xy
#define Streams   Aim.z

Texture2D Canvas : register(t13);
Texture2D<float> CanvasDepth : register(t14);

static const float KindBurst = 1.0;
static const float KindInverted = 2.0;
static const float KindWhiteOut = 3.0;
static const float3 Luma = float3(0.299, 0.587, 0.114);

static const float2 Disk[12] =
{
    float2(-0.326, -0.406), float2(-0.840, -0.074), float2(-0.696, 0.457), float2(-0.203, 0.621),
    float2(0.962, -0.195), float2(0.473, -0.480), float2(0.519, 0.767), float2(0.185, -0.893),
    float2(0.507, 0.064), float2(0.896, 0.412), float2(-0.322, -0.933), float2(-0.792, -0.598),
};

struct PS_OUT
{
    float4 Color : SV_TARGET;
    float Depth : SV_DEPTH;
};

struct VS_IN
{
    float3 Dir : POSITION;
    float4 Seed : TEXCOORD0;
    float2 Corner : TEXCOORD1;
};

float4 VSMain(VS_IN input) : SV_POSITION
{
    return float4(input.Corner, 0.0, 1.0);
}

float4 Fetch(float2 pixel)
{
    int2 at = int2(clamp(pixel, 0.0, Size - 1.0));
    return Canvas.Load(int3(at, 0));
}

float4 Blurred(float2 pixel, float radius)
{
    float4 sum = Fetch(pixel);

    if (Streams > 0.5)
    {
        [unroll]
        for (int i = 1; i <= 6; i++)
        {
            float2 step = Heading * radius * 1.5 * (i / 6.0);
            sum += Fetch(pixel + step) + Fetch(pixel - step);
        }
    }
    else
    {
        [unroll]
        for (int i = 0; i < 12; i++)
            sum += Fetch(pixel + Disk[i] * radius);
    }

    return sum / 13.0;
}

float DepthAt(float2 pixel)
{
    int2 at = int2(clamp(pixel, 0.0, Size - 1.0));
    return CanvasDepth.Load(int3(at, 0));
}

float NearestDepth(float2 pixel, float radius)
{
    float nearest = DepthAt(pixel);

    [unroll]
    for (int i = 0; i < 12; i++)
        nearest = min(nearest, DepthAt(pixel + Disk[i] * radius));

    return nearest;
}

float3 Straight(float4 color)
{
    return color.a > 1e-4 ? color.rgb / color.a : 0.0;
}

float Influence(float2 pixel)
{
    if (Radius <= 0.0)
        return 1.0;

    float distance = length(pixel - Focus.xy);

    return 1.0 - smoothstep(Radius - Softness * 0.5, Radius + Softness * 0.5, distance);
}

float4 BlurFrame(float2 pixel)
{
    float2 away = pixel - Focus.xy;
    float distance = length(away);
    float2 direction = Streams > 0.5 ? Heading : (distance > 1e-3 ? away / distance : 0.0);

    float4 red = Blurred(pixel + direction * Shift, BlurSize);
    float4 green = Blurred(pixel, BlurSize);
    float4 blue = Blurred(pixel - direction * Shift, BlurSize);

    float alpha = max(max(red.a, green.a), blue.a);
    float3 color = float3(red.r, green.g, blue.b);

    float4 wide = Blurred(pixel, BlurSize * 2.5 + 6.0);
    float bright = saturate((dot(Straight(wide), Luma) - 0.7) / 0.3) * wide.a;
    float3 glow = Tint.rgb * bright * GlowSize * 2.0;

    float covered = max(alpha, saturate(dot(glow, Luma)));

    return float4((color + glow) / max(covered, 1e-4), covered);
}

float4 BurstFrame(float4 color)
{
    float light = dot(Straight(color), Luma);
    float shine = smoothstep(Threshold - 0.08, Threshold + 0.08, light);
    float core = smoothstep(Threshold + 0.1, Threshold + 0.35, light);

    return float4(lerp(Tint.rgb, 1.0, core) * shine, color.a);
}

float4 InvertedFrame(float4 color)
{
    float light = dot(Straight(color), Luma);
    float shade = smoothstep(Threshold - 0.08, Threshold + 0.08, light);
    float offset = (light - Threshold) / 0.12;
    float rim = exp(-offset * offset);

    return float4(lerp(1.0, 0.0, shade) + Tint.rgb * rim, color.a);
}

float Height(float2 pixel)
{
    float4 color = Fetch(pixel);
    return dot(color.rgb, Luma) * 0.7 + color.a * 0.6;
}

float Outline(float2 pixel, float e)
{
    float tl = Height(pixel + float2(-e, -e));
    float t = Height(pixel + float2(0.0, -e));
    float tr = Height(pixel + float2(e, -e));
    float l = Height(pixel + float2(-e, 0.0));
    float r = Height(pixel + float2(e, 0.0));
    float bl = Height(pixel + float2(-e, e));
    float b = Height(pixel + float2(0.0, e));
    float br = Height(pixel + float2(e, e));

    float gx = (tr + 2.0 * r + br) - (tl + 2.0 * l + bl);
    float gy = (bl + 2.0 * b + br) - (tl + 2.0 * t + tr);

    return saturate((length(float2(gx, gy)) - 0.08) * 1.6);
}

float4 NeonFrame(float2 pixel, float4 color)
{
    float e = max(Edge, 0.5);
    float core = Outline(pixel, e);
    float halo = Outline(pixel, e * 3.0) * 0.6;
    float3 glow = Tint.rgb * saturate(core + halo) + saturate(core * 1.5 - 1.2) * 0.5;

    return float4(glow, max(color.a, saturate(core + halo)));
}

float4 Styled(float2 pixel, float4 color)
{
    if (Kind < 0.5)
        return BlurFrame(pixel);

    if (Kind < KindBurst + 0.5)
        return BurstFrame(color);

    if (Kind < KindInverted + 0.5)
        return InvertedFrame(color);

    if (Kind < KindWhiteOut + 0.5)
        return float4(1.0, 1.0, 1.0, color.a);

    return NeonFrame(pixel, color);
}

PS_OUT PSMain(float4 position : SV_POSITION)
{
    float2 pixel = position.xy;
    float4 color = Fetch(pixel);
    float amount = Influence(pixel);
    float4 plain = float4(Straight(color), color.a);
    float4 mixed = amount > 0.0 ? lerp(plain, Styled(pixel, color), amount) : plain;

    if (mixed.a <= 1e-4)
        discard;

    float reach = max(max(BlurSize * 2.5 + 6.0 + Shift, Edge * 3.0), 1.0);
    float depth = color.a >= 0.5 ? DepthAt(pixel) : NearestDepth(pixel, reach);

    PS_OUT output;
    output.Color = float4(saturate(mixed.rgb), saturate(mixed.a));
    output.Depth = min(depth, 0.999999);

    return output;
}
