#include "../../YMM43D/Shaders/Scene.hlsli"

cbuffer BeamConstants : register(b1)
{
    float4 BeamColor;

    float4 BeamShape;
};

struct VS_IN
{
    float3 Pos : POSITION;
    float  Weight : TEXCOORD0;
};

struct PS_IN
{
    float4 Pos : SV_POSITION;
    float3 Local : TEXCOORD0;
    float  Weight : TEXCOORD1;
};

PS_IN VSMain(VS_IN input)
{
    PS_IN output;

    output.Pos = mul(float4(input.Pos, 1.0), WorldViewProjection);
    output.Local = input.Pos;
    output.Weight = input.Weight;

    return output;
}

float4 PSMain(PS_IN input) : SV_TARGET
{
    float axial = saturate(input.Local.z);
    float along = pow(saturate(1.0 - axial), BeamShape.x);

    float intensity = saturate(input.Weight * along * BeamColor.a * Opacity);

    return float4(BeamColor.rgb * intensity, intensity);
}
