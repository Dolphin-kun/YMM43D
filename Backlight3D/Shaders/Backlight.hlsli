#ifndef YMM43D_BACKLIGHT_HLSLI
#define YMM43D_BACKLIGHT_HLSLI

#include "../../YMM43D/Shaders/Lighting.hlsli"

cbuffer BacklightConstants : register(b1)
{
    matrix BoxInverse;
    matrix Unproject;
    float4 Tint;
    float4 Air;
    float4 Walk;
};

#define Thickness Tint.a
#define EdgeBlur  Air.x
#define Forward   Air.y
#define Bloom     Air.z
#define Fade      Air.w
#define Steps     Walk.x
#define Reach     Walk.y

struct VS_INPUT
{
    float3 Position : POSITION;
    float4 Color    : COLOR;
    float2 TexCoord : TEXCOORD;
    float3 Normal   : NORMAL;
};

struct PS_INPUT
{
    float4 Position : SV_POSITION;
    float2 Screen   : SCREEN;
};

#endif
