#include "Light.hlsli"

cbuffer ExtrusionConstants : register(b0)
{
#include "SceneFields.hlsli"
    float4 SideColor;
    float3 CameraLocalPos;
    int    ExtrusionType;
};

#include "SceneNames.hlsli"

#include "Lighting.hlsli"

#include "Texture.hlsli"

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
