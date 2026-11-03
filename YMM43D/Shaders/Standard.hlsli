#ifndef YMM43D_STANDARD_HLSLI
#define YMM43D_STANDARD_HLSLI

#include "Lighting.hlsli"
#include "Texture.hlsli"

struct VS_IN
{
    float3 Pos : POSITION;
    float4 Col : COLOR;
    float2 Tex : TEXCOORD0;
    float3 Nrm : NORMAL;
};

struct PS_IN
{
    float4 Pos : SV_POSITION;
    float4 Col : COLOR;
    float2 Tex : TEXCOORD0;
    float3 Nrm : NORMAL;
    float3 World : TEXCOORD1;
};

PS_IN MakeVertex(float3 position, float4 color, float2 texCoord, float3 normal)
{
    PS_IN output;
    output.Pos = mul(float4(position, 1.0), WorldViewProjection);
    output.Col = color;
    output.Tex = texCoord;
    output.Nrm = mul(float4(normal, 0.0), WorldInverse).xyz;
    output.World = mul(float4(position, 1.0), World).xyz;
    return output;
}

#ifndef YMM43D_CUSTOM_VERTEX_SHADER
PS_IN VSMain(VS_IN input)
{
    return MakeVertex(input.Pos, input.Col, input.Tex, input.Nrm);
}
#endif

float4 Shade(float4 color, PS_IN input)
{
    clip(color.a - AlphaCutoff);

    color.rgb = ApplyLight(color.rgb, input.Nrm, input.World);
    color.rgb = ApplyFog(color.rgb, input.World);
    color.a *= Opacity;
    return color;
}

#endif
