#ifndef YMM43D_SCENEFIELDS_HLSLI
#define YMM43D_SCENEFIELDS_HLSLI

#include "Light.hlsli"

    matrix WorldViewProjection;
    matrix World;
    matrix WorldInverse;
    float4 CameraPosition;
    float4 Ambient;
    float4 FogColor;
    float4 Options;
    float4 Surface;
    Light  Lights[4];

#endif
