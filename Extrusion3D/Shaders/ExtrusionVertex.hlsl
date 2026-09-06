#include "Extrusion.hlsli"

PS_INPUT VSMain(VS_INPUT input)
{
    PS_INPUT output;
    output.Position = mul(float4(input.Position, 1.0f), WorldViewProjection);
    output.Color    = input.Color;
    output.TexCoord = input.TexCoord;
    output.LocalPos = input.Position;
    return output;
}
