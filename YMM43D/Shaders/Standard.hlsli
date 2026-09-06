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

PS_IN VSMain(VS_IN input)
{
    PS_IN output;
    output.Pos = mul(float4(input.Pos, 1.0), WorldViewProjection);
    output.Col = input.Col;
    output.Tex = input.Tex;
    output.Nrm = mul(float4(input.Nrm, 0.0), WorldInverse).xyz;
    output.World = mul(float4(input.Pos, 1.0), World).xyz;
    return output;
}

float4 Shade(float4 color, PS_IN input)
{
    // 透けている画素を捨てる。捨てないと板の四角いままに深度が書かれ、
    // 文字のまわりの何も無いところが後ろの物を隠してしまう。
    // AlphaCutoff は深度だけを書くときに上げる（画素の色は同じでも、
    // 隠すかどうかは輪郭で決めたいため）。既定の 0 では誰も捨てない。
    clip(color.a - AlphaCutoff);

    color.rgb = ApplyLight(color.rgb, input.Nrm, input.World);
    color.rgb = ApplyFog(color.rgb, input.World);
    color.a *= Opacity;
    return color;
}

#endif
