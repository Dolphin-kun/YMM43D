#ifndef YMM43D_EXTRUSION_HLSLI
#define YMM43D_EXTRUSION_HLSLI

#include "../../YMM43D/Shaders/Light.hlsli"

cbuffer ExtrusionConstants : register(b0)
{
#include "../../YMM43D/Shaders/SceneFields.hlsli"
    float4 SideColor;
    float3 CameraLocalPos;
    int    ExtrusionType;
};

#include "../../YMM43D/Shaders/SceneNames.hlsli"

#include "../../YMM43D/Shaders/Lighting.hlsli"

#include "../../YMM43D/Shaders/Texture.hlsli"

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
    float4 Color    : COLOR;
    float2 TexCoord : TEXCOORD;
    float3 LocalPos : LOCPOS;
};

#endif
