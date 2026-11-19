#include "Backlight.hlsli"

PS_INPUT VSMain(VS_INPUT input)
{
    PS_INPUT output;

    output.Screen = input.Position.xy * 2.0f;
    output.Position = float4(output.Screen, 0.5f, 1.0f);

    return output;
}
