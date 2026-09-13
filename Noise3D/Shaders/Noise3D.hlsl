#include "../../YMM43D/Shaders/Scene.hlsli"

Texture3D<float> NoiseVolume : register(t0);
SamplerState NoiseSampler : register(s0);

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

#define VOLUME_CELLS 16.0

#define TYPE_RANDOM   1
#define TYPE_BLOCK    2
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

float Random01(int3 cell, uint seed)
{
    return (Hash((uint)cell.x + Hash((uint)cell.y + Hash((uint)cell.z + Hash(seed)))) >> 8) / 16777215.0;
}

float3 OctaveShift(int octave)
{
    return frac(float3(0.1311, 0.3737, 0.7193) * (octave + 1) * 7.31);
}

float SignedVolume(float3 uvw, float3 dx, float3 dy)
{
    return NoiseVolume.SampleGrad(NoiseSampler, uvw, dx, dy) * 2.0 - 1.0;
}

float FractalNoise(float3 q, float3 qdx, float3 qdy, float cutoff)
{
    int octaves = (int)NoiseFractal.x;
    float lacunarity = NoiseFractal.y;
    float gain = NoiseFractal.z;
    int mode = (int)NoiseFractal.w;

    float3 uvw = q / VOLUME_CELLS;
    float3 dx = qdx / VOLUME_CELLS;
    float3 dy = qdy / VOLUME_CELLS;

    float norm = 0.0;
    float weight = 1.0;

    for (int i = 0; i < octaves; i++)
    {
        norm += weight;
        weight *= gain;
    }

    norm = max(norm, 1e-5);

    float sum = 0.0;
    float remaining = norm;
    float amplitude = 1.0;
    float frequency = 1.0;

    [loop]
    for (int octave = 0; octave < octaves; octave++)
    {
        float s = SignedVolume(uvw * frequency + OctaveShift(octave), dx * frequency, dy * frequency);

        if (mode == MODE_TURBULENCE)
            s = abs(s);
        else if (mode == MODE_RIDGED)
            s = 1.0 - abs(s);

        sum += s * amplitude;
        remaining -= amplitude;
        amplitude *= gain;
        frequency *= lacunarity;

        float reachable = (sum + remaining) / norm;

        if ((mode == MODE_NORMAL ? reachable * 0.5 + 0.5 : reachable) <= cutoff)
            return 0.0;
    }

    sum /= norm;

    return mode == MODE_NORMAL ? sum * 0.5 + 0.5 : saturate(sum);
}

float3 ToNoiseSpace(float3 v)
{
    float c = cos(NoiseOffset.w);
    float s = sin(NoiseOffset.w);

    return float3(c * v.x + s * v.y, -s * v.x + c * v.y, v.z) / NoiseFeature.xyz;
}

float SampleNoise(float3 pixels, float3 pixelsDx, float3 pixelsDy)
{
    uint seed = (uint)NoiseFeature.w;
    int type = (int)NoiseTone.w;

    float3 q = ToNoiseSpace(pixels - NoiseOffset.xyz);
    float3 qdx = ToNoiseSpace(pixelsDx);
    float3 qdy = ToNoiseSpace(pixelsDy);

    if (NoiseWarp.x > 0.0)
    {
        float3 w = q * NoiseWarp.y / VOLUME_CELLS;
        float3 wdx = qdx * NoiseWarp.y / VOLUME_CELLS;
        float3 wdy = qdy * NoiseWarp.y / VOLUME_CELLS;

        float3 shift = float3(
            SignedVolume(w + float3(0.17, 0.53, 0.29), wdx, wdy),
            SignedVolume(w.yzx + float3(0.61, 0.07, 0.83), wdx.yzx, wdy.yzx),
            SignedVolume(w.zxy + float3(0.37, 0.91, 0.47), wdx.zxy, wdy.zxy));

        q += shift * NoiseWarp.x / NoiseFeature.xyz;
    }

    float threshold = NoiseTone.y;
    float n;

    if (type == TYPE_RANDOM)
        n = Random01((int3)floor(q * 16.0), seed);
    else if (type == TYPE_BLOCK)
        n = Random01((int3)floor(q), seed);
    else if (type == TYPE_MARBLE)
        n = 0.5 + 0.5 * sin((q.x + 2.0 * FractalNoise(q, qdx, qdy, -1.0)) * 3.14159265);
    else
        n = FractalNoise(q, qdx, qdy, threshold);

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

    float3 flip = float3(1.0, -1.0, 1.0) * NoiseBox.xyz;
    float3 basePixels = local * flip;
    float3 pixelsDx = ddx(basePixels);
    float3 pixelsDy = ddy(basePixels);

    float edge = EdgeFactor(local);
    clip(edge - 1e-4);

    int axis = (int)NoiseWarp.z;
    float slices = NoiseCamera.w;

    float3 toPixel = local - NoiseCamera.xyz;
    float along = max(abs(toPixel[axis]), 1e-5);
    float spacing = NoiseBox[axis] / slices;
    float stepLength = min(length(toPixel * NoiseBox.xyz) / (along * slices), spacing * 8.0);

    uint2 pixel = (uint2)input.Pos.xy;
    float jitter = (Hash(pixel.x + Hash(pixel.y)) >> 8) / 16777215.0 - 0.5;
    float3 sampled = local + toPixel * (jitter / (along * slices));

    float density = SampleNoise(sampled * flip, pixelsDx, pixelsDy);

    float alpha = (1.0 - exp(-NoiseColor.a * density * edge * stepLength)) * Opacity;
    clip(alpha - 1.0 / 1024.0);

    return float4(NoiseColor.rgb, alpha);
}
