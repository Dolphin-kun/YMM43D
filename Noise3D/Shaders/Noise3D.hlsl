#include "../../YMM43D/Shaders/Scene.hlsli"

cbuffer NoiseConstants : register(b1)
{
    float4 NoiseColor;
    float4 NoiseBox;
    float4 NoiseCamera;
    float4 NoiseOffset;
    float4 NoiseFeature;
    float4 NoiseTone;
    float4 NoiseFractal;
    float4 NoiseWarp;
};

#define TYPE_VALUE    0
#define TYPE_RANDOM   1
#define TYPE_BLOCK    2
#define TYPE_PERLIN   3
#define TYPE_CELLULAR 4
#define TYPE_VORONOI  5
#define TYPE_SIMPLEX  6
#define TYPE_MARBLE   7

#define MODE_NORMAL     0
#define MODE_TURBULENCE 1
#define MODE_RIDGED     2

struct VS_IN
{
    float3 Pos : POSITION;
};

struct PS_IN
{
    float4 Pos : SV_POSITION;
    float3 Local : TEXCOORD0;
};

PS_IN VSMain(VS_IN input)
{
    PS_IN output;

    output.Pos = mul(float4(input.Pos, 1.0), WorldViewProjection);
    output.Local = input.Pos;

    return output;
}

uint Hash(uint x)
{
    x ^= x >> 16;
    x *= 0x7feb352dU;
    x ^= x >> 15;
    x *= 0x846ca68bU;
    x ^= x >> 16;
    return x;
}

uint HashCell(int3 cell, uint seed)
{
    return Hash((uint)cell.x + Hash((uint)cell.y + Hash((uint)cell.z + Hash(seed))));
}

float Random01(int3 cell, uint seed)
{
    return (HashCell(cell, seed) >> 8) / 16777215.0;
}

float3 RandomVector01(int3 cell, uint seed)
{
    uint h = HashCell(cell, seed);
    return float3(Hash(h) >> 8, Hash(h + 1U) >> 8, Hash(h + 2U) >> 8) / 16777215.0;
}

float3 Gradient(int3 cell, uint seed)
{
    return normalize(RandomVector01(cell, seed) * 2.0 - 1.0 + 1e-4);
}

float3 Fade(float3 t)
{
    return t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
}

float ValueNoise(float3 p, uint seed)
{
    int3 i = (int3)floor(p);
    float3 u = Fade(frac(p));

    float c000 = Random01(i, seed);
    float c100 = Random01(i + int3(1, 0, 0), seed);
    float c010 = Random01(i + int3(0, 1, 0), seed);
    float c110 = Random01(i + int3(1, 1, 0), seed);
    float c001 = Random01(i + int3(0, 0, 1), seed);
    float c101 = Random01(i + int3(1, 0, 1), seed);
    float c011 = Random01(i + int3(0, 1, 1), seed);
    float c111 = Random01(i + int3(1, 1, 1), seed);

    return lerp(
        lerp(lerp(c000, c100, u.x), lerp(c010, c110, u.x), u.y),
        lerp(lerp(c001, c101, u.x), lerp(c011, c111, u.x), u.y),
        u.z);
}

float PerlinNoise(float3 p, uint seed)
{
    int3 i = (int3)floor(p);
    float3 f = frac(p);
    float3 u = Fade(f);

    float n000 = dot(Gradient(i, seed), f);
    float n100 = dot(Gradient(i + int3(1, 0, 0), seed), f - float3(1, 0, 0));
    float n010 = dot(Gradient(i + int3(0, 1, 0), seed), f - float3(0, 1, 0));
    float n110 = dot(Gradient(i + int3(1, 1, 0), seed), f - float3(1, 1, 0));
    float n001 = dot(Gradient(i + int3(0, 0, 1), seed), f - float3(0, 0, 1));
    float n101 = dot(Gradient(i + int3(1, 0, 1), seed), f - float3(1, 0, 1));
    float n011 = dot(Gradient(i + int3(0, 1, 1), seed), f - float3(0, 1, 1));
    float n111 = dot(Gradient(i + int3(1, 1, 1), seed), f - float3(1, 1, 1));

    float n = lerp(
        lerp(lerp(n000, n100, u.x), lerp(n010, n110, u.x), u.y),
        lerp(lerp(n001, n101, u.x), lerp(n011, n111, u.x), u.y),
        u.z);

    return clamp(n * 1.5, -1.0, 1.0);
}

float SimplexCorner(float3 offset, float3 cell, uint seed)
{
    float t = 0.6 - dot(offset, offset);

    if (t <= 0.0)
        return 0.0;

    t *= t;
    return t * t * dot(Gradient((int3)cell, seed), offset);
}

float SimplexNoise(float3 v, uint seed)
{
    const float F3 = 1.0 / 3.0;
    const float G3 = 1.0 / 6.0;

    float3 i = floor(v + dot(v, F3));
    float3 x0 = v - i + dot(i, G3);

    float3 g = step(x0.yzx, x0.xyz);
    float3 l = 1.0 - g;
    float3 i1 = min(g, l.zxy);
    float3 i2 = max(g, l.zxy);

    float3 x1 = x0 - i1 + G3;
    float3 x2 = x0 - i2 + 2.0 * G3;
    float3 x3 = x0 - 1.0 + 3.0 * G3;

    float n = SimplexCorner(x0, i, seed)
            + SimplexCorner(x1, i + i1, seed)
            + SimplexCorner(x2, i + i2, seed)
            + SimplexCorner(x3, i + 1.0, seed);

    return clamp(32.0 * n, -1.0, 1.0);
}

void NearestCell(float3 p, uint seed, out float distance, out int3 nearest)
{
    int3 base = (int3)floor(p);
    distance = 8.0;
    nearest = base;

    for (int z = -1; z <= 1; z++)
    for (int y = -1; y <= 1; y++)
    for (int x = -1; x <= 1; x++)
    {
        int3 cell = base + int3(x, y, z);
        float3 feature = (float3)cell + RandomVector01(cell, seed);
        float d = length(feature - p);

        if (d < distance)
        {
            distance = d;
            nearest = cell;
        }
    }
}

float SignedBase(int type, float3 p, uint seed)
{
    float distance;
    int3 nearest;

    switch (type)
    {
        case TYPE_PERLIN:
            return PerlinNoise(p, seed);

        case TYPE_SIMPLEX:
            return SimplexNoise(p, seed);

        case TYPE_CELLULAR:
            NearestCell(p, seed, distance, nearest);
            return saturate(distance) * 2.0 - 1.0;

        case TYPE_VORONOI:
            NearestCell(p, seed, distance, nearest);
            return Random01(nearest, seed + 101U) * 2.0 - 1.0;

        default:
            return ValueNoise(p, seed) * 2.0 - 1.0;
    }
}

float FractalNoise(int type, float3 p, uint seed)
{
    int octaves = (int)NoiseFractal.x;
    float lacunarity = NoiseFractal.y;
    float gain = NoiseFractal.z;
    int mode = (int)NoiseFractal.w;

    float sum = 0.0;
    float norm = 0.0;
    float amplitude = 1.0;
    float frequency = 1.0;

    [loop]
    for (int octave = 0; octave < octaves; octave++)
    {
        float s = SignedBase(type, p * frequency, seed + (uint)octave * 7919U);

        if (mode == MODE_TURBULENCE)
            s = abs(s);
        else if (mode == MODE_RIDGED)
            s = 1.0 - abs(s);

        sum += s * amplitude;
        norm += amplitude;
        amplitude *= gain;
        frequency *= lacunarity;
    }

    sum /= max(norm, 1e-5);

    return mode == MODE_NORMAL ? sum * 0.5 + 0.5 : saturate(sum);
}

float SampleNoise(float3 pixels)
{
    uint seed = (uint)NoiseFeature.w;
    int type = (int)NoiseTone.w;

    float3 q = pixels - NoiseOffset.xyz;

    float c = cos(NoiseOffset.w);
    float s = sin(NoiseOffset.w);
    q.xy = float2(c * q.x + s * q.y, -s * q.x + c * q.y);

    q /= NoiseFeature.xyz;

    if (NoiseWarp.x > 0.0)
    {
        float3 w = q * NoiseWarp.y;
        float3 shift = float3(
            ValueNoise(w + float3(17.1, 3.7, 9.2), seed + 11U),
            ValueNoise(w + float3(5.3, 31.7, 1.9), seed + 23U),
            ValueNoise(w + float3(2.9, 7.4, 47.3), seed + 37U)) * 2.0 - 1.0;

        q += shift * NoiseWarp.x / NoiseFeature.xyz;
    }

    float n;

    switch (type)
    {
        case TYPE_RANDOM:
            n = Random01((int3)floor(q * 16.0), seed);
            break;

        case TYPE_BLOCK:
            n = Random01((int3)floor(q), seed);
            break;

        case TYPE_MARBLE:
            n = 0.5 + 0.5 * sin((q.x + 2.0 * FractalNoise(TYPE_VALUE, q, seed)) * 3.14159265);
            break;

        default:
            n = FractalNoise(type, q, seed);
            break;
    }

    float threshold = NoiseTone.y;
    n = saturate((n - threshold) / max(1.0 - threshold, 1e-3));

    float levels = NoiseTone.z;

    if (levels >= 1.0)
        n = saturate(floor(n * levels) / max(levels - 1.0, 1.0));

    return n * NoiseTone.x;
}

float EdgeFactor(float3 local)
{
    float3 inside = 1.0 - abs(local) * 2.0;
    float nearest = min(inside.x, min(inside.y, inside.z));

    if (NoiseBox.w <= 0.0)
        return nearest >= 0.0 ? 1.0 : 0.0;

    return smoothstep(0.0, NoiseBox.w, nearest);
}

float4 PSMain(PS_IN input) : SV_TARGET
{
    float3 local = input.Local;

    float edge = EdgeFactor(local);
    clip(edge - 1e-4);

    int axis = (int)NoiseWarp.z;
    float slices = NoiseCamera.w;

    float3 toPixel = local - NoiseCamera.xyz;
    float along = max(abs(toPixel[axis]), 1e-5);
    float spacing = NoiseBox[axis] / slices;
    float stepLength = min(length(toPixel * NoiseBox.xyz) / (along * slices), spacing * 8.0);

    float density = SampleNoise(float3(local.x, -local.y, local.z) * NoiseBox.xyz);

    float alpha = (1.0 - exp(-NoiseColor.a * density * edge * stepLength)) * Opacity;
    clip(alpha - 1.0 / 1024.0);

    return float4(NoiseColor.rgb, alpha);
}
