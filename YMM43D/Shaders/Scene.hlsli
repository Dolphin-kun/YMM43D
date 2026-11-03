#ifndef YMM43D_SCENE_HLSLI
#define YMM43D_SCENE_HLSLI

#include "Light.hlsli"

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
    float4 Shading;
    float4 Ground;
};
#define Opacity     Options.x
#define Unlit       Options.y
#define FogStart    Options.z
#define FogEnd      Options.w
#define AlphaCutoff Surface.x
#define LightCount  Surface.y
#define Gloss       Surface.z
#define GlossPower  Surface.w
#define ToonSteps   Shading.x
#define ToonShift   Shading.y
#define ToonFeather Shading.z

#endif
