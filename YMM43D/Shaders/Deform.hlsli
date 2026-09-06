#ifndef YMM43D_DEFORM_HLSLI
#define YMM43D_DEFORM_HLSLI

#include "Lighting.hlsli"
#include "Texture.hlsli"

Texture2D    txDiffuse : register(t0);
SamplerState samLinear : register(s0);

struct VS_IN
{
    // 平らな板の上の位置。x と y は -0.5〜0.5、z は 0。
    float3 Position : POSITION;
    float2 TexCoord : TEXCOORD;

    // 破片の中心。つながった板では Position と同じ値が入る。
    float3 Piece    : PIECE;
};

struct PS_IN
{
    float4 Position : SV_POSITION;
    float2 TexCoord : TEXCOORD;
    float3 Nrm      : NORMAL;
    float3 World    : TEXCOORD1;
    float  Fade     : TEXCOORD2;
};

// ここから下の2つは、エフェクトごとに用意する。
//
// Deform は、平らな板の1点が動いた先を返す。
// DeformFade は、その点をどれだけ残すかを返す（1 でそのまま、0 で消える）。
float3 Deform(float3 local, float3 piece);
float  DeformFade(float3 local, float3 piece);

// 法線は、少しずらした2点を同じように動かして、その差から出す。
// こうすると、どんな変形でも同じ手順で陰影がつき、
// エフェクト側は「点がどこへ行くか」だけ考えればよくなる。
static const float NormalStep = 1.0 / 512.0;

PS_IN VSMain(VS_IN input)
{
    float3 here   = Deform(input.Position, input.Piece);
    float3 alongX = Deform(input.Position + float3(NormalStep, 0.0, 0.0), input.Piece);
    float3 alongY = Deform(input.Position + float3(0.0, NormalStep, 0.0), input.Piece);

    PS_IN output;
    output.Position = mul(float4(here, 1.0), WorldViewProjection);
    output.TexCoord = input.TexCoord;
    output.Nrm      = mul(float4(cross(alongY - here, alongX - here), 0.0), WorldInverse).xyz;
    output.World    = mul(float4(here, 1.0), World).xyz;
    output.Fade     = DeformFade(input.Position, input.Piece);
    return output;
}

float4 PSMain(PS_IN input) : SV_TARGET
{
    float4 color = Unpremultiply(txDiffuse.Sample(samLinear, input.TexCoord));

    color.a *= input.Fade;

    clip(color.a - AlphaCutoff);

    color.rgb = ApplyLight(color.rgb, input.Nrm, input.World);
    color.rgb = ApplyFog(color.rgb, input.World);
    color.a *= Opacity;
    return color;
}

// 破片ごとのばらつき。同じ破片なら毎回同じ値になるので、
// 再生し直しても飛び方が変わらない。
float3 PieceNoise(float3 piece, float seed)
{
    float3 p = piece * 127.1 + seed;

    return frac(sin(float3(
        dot(p, float3(12.9898, 78.233, 37.719)),
        dot(p, float3(93.9898, 67.345, 24.113)),
        dot(p, float3(45.1641, 19.873, 83.155)))) * 43758.5453);
}

float3 RotateAround(float3 position, float3 axis, float angle)
{
    float s = sin(angle);
    float c = cos(angle);

    return position * c + cross(axis, position) * s + axis * dot(axis, position) * (1.0 - c);
}

#endif
