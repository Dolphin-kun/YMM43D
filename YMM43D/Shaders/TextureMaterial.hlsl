#include "Standard.hlsli"

Texture2D    tex  : register(t0);
SamplerState samp : register(s0);

float4 PSMain(PS_IN input) : SV_TARGET
{
    return Shade(input.Col * Unpremultiply(tex.Sample(samp, input.Tex)), input);
}
