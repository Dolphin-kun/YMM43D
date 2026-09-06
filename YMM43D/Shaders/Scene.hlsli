#ifndef YMM43D_SCENE_HLSLI
#define YMM43D_SCENE_HLSLI

#include "Light.hlsli"

// 3D の場そのもの。b0 はいつもこれが入っている。
// プラグインが足したい値は、この後ろに割り込ませず b1 に置く。
cbuffer SceneConstants : register(b0)
{
    matrix WorldViewProjection;
    matrix World;
    matrix WorldInverse;
    float4 CameraPosition;
    float4 Ambient;
    float4 FogColor;
    float4 Options;
    float4 Surface;
    Light  Lights[4];
};
#define Opacity     Options.x
#define Unlit       Options.y
#define FogStart    Options.z
#define FogEnd      Options.w
#define AlphaCutoff Surface.x

#endif
