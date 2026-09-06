#ifndef YMM43D_POINTCLOUD_HLSLI
#define YMM43D_POINTCLOUD_HLSLI

#include "../../YMM43D/Shaders/Light.hlsli"

cbuffer PointCloudConstants : register(b0)
{
#include "../../YMM43D/Shaders/SceneFields.hlsli"
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

#include "../../YMM43D/Shaders/SceneNames.hlsli"

#include "../../YMM43D/Shaders/Lighting.hlsli"

#include "../../YMM43D/Shaders/Texture.hlsli"

static const float Pi = 3.14159265;

// 丸い粒と線はカメラを向いた板でしかないので、球（線なら円柱）の表面と
// みなして法線を作る。縁ほど横を向くので、丸みがついて見える。
// 四角い粒には使わない。板のままなのに丸い陰影が乗ってしまう。
//
// 画素ごとに求めること。頂点で求めると四隅がどれも真横を向き、
// 補間された真ん中の法線が打ち消し合って 0 に潰れる。
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

    // 形の中心で 0、縁で ±1。Coverage() が縁を滑らかにするのに使う。
    float2 Edge : EDGE;

    float3 Nrm   : NORMAL;
    float3 World : TEXCOORD1;

    // 法線をどこから取るか。0 … 面（Nrm をそのまま）、
    // 1 … 丸い粒と線（画素ごとに球・円柱として作り直す）、
    // 2 … 四角い粒（カメラを向いた板のまま）。
    nointerpolation float Shading : SHADING;

    // x … 格子の点ごと（面の不透明度）、y … 線ごと（引くかどうか）。
    nointerpolation float2 Random : RANDOM;
};

#endif
