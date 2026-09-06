#include "Standard.hlsli"

float4 PSMain(PS_IN input) : SV_TARGET
{
    return Shade(input.Col, input);
}
