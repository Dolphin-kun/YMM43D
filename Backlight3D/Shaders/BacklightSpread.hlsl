#include "Backlight.hlsli"

Texture2D    Gathered : register(t4);
SamplerState Smooth   : register(s0);

float4 main(PS_INPUT input) : SV_Target
{
    float2 uv = input.Screen * float2(0.5, -0.5) + 0.5;
    float4 found = Gathered.SampleLevel(Smooth, uv, 0);

    if (found.a <= 0.0)
        discard;

    return found;
}
