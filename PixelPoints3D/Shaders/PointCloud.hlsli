#ifndef YMM43D_POINTCLOUD_HLSLI
#define YMM43D_POINTCLOUD_HLSLI

#include "../../YMM43D/Shaders/Lighting.hlsli"
#include "../../YMM43D/Shaders/Texture.hlsli"

cbuffer PointCloudConstants : register(b1)
{
    float4 Color;
    float3 GridCount;
    float  Threshold;
    float3 Extent;
    float  Seed;
    float3 Scatter;
    float  PointHalfSize;
    float3 ViewRight;
    float  LineHalfWidth;
    float3 ViewUp;
    float  UseSourceColor;
    float3 ViewForward;
    float  ExtraOpacity;
    float3 DeformAxis;
    float  DeformKind;
    float  DeformAmount;
    float  DeformPeriod;
    float  DeformPhase;
    float  LineRandomness;
    float  OpacityRandomness;
    float  PointIsRound;
    float2 Padding;
};

static const float Pi = 3.14159265;

float3 Billboard(float2 edge)
{
    float2 offset = clamp(edge, -1.0, 1.0);
    float depth = sqrt(saturate(1.0 - dot(offset, offset)));

    return normalize(ViewRight * offset.x + ViewUp * offset.y - ViewForward * depth);
}

struct VS_INPUT
{
    float3 Cell   : CELL;
    float2 Corner : CORNER;
    float3 Other  : OTHER;
};

struct PS_INPUT
{
    float4 Position : SV_POSITION;
    float2 TexCoord : TEXCOORD;

    float2 Edge : EDGE;

    float3 Nrm   : NORMAL;
    float3 World : TEXCOORD1;

    nointerpolation float Shading : SHADING;

    nointerpolation float2 Random : RANDOM;
};

#endif
