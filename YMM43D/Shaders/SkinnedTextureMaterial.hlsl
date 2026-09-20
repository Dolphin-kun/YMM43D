#define YMM43D_CUSTOM_VERTEX_SHADER
#include "Standard.hlsli"

Texture2D    tex  : register(t0);
SamplerState samp : register(s0);

// 骨ごとの行列。行を4つ並べた形で持つ（CPU 側の並びに合わせるため）。
struct BoneMatrix
{
    float4 Row0;
    float4 Row1;
    float4 Row2;
    float4 Row3;
};

StructuredBuffer<BoneMatrix> Bones : register(t3);

struct VS_IN_SKINNED
{
    float3 Pos : POSITION;
    float4 Col : COLOR;
    float2 Tex : TEXCOORD0;
    float3 Nrm : NORMAL;
    uint4  Idx : BLENDINDICES;
    float4 Wgt : BLENDWEIGHT;
};

float4 ApplyBone(float4 value, BoneMatrix bone)
{
    return value.x * bone.Row0 + value.y * bone.Row1 + value.z * bone.Row2 + value.w * bone.Row3;
}

float4 Blend(float4 value, uint4 indices, float4 weights)
{
    return weights.x * ApplyBone(value, Bones[indices.x])
         + weights.y * ApplyBone(value, Bones[indices.y])
         + weights.z * ApplyBone(value, Bones[indices.z])
         + weights.w * ApplyBone(value, Bones[indices.w]);
}

PS_IN VSMain(VS_IN_SKINNED input)
{
    float total = input.Wgt.x + input.Wgt.y + input.Wgt.z + input.Wgt.w;

    // 骨に結びついていない頂点は、そのままの場所に置く。
    if (total <= 0.0001)
        return MakeVertex(input.Pos, input.Col, input.Tex, input.Nrm);

    float3 position = Blend(float4(input.Pos, 1.0), input.Idx, input.Wgt).xyz;
    float3 normal = Blend(float4(input.Nrm, 0.0), input.Idx, input.Wgt).xyz;

    return MakeVertex(position, input.Col, input.Tex, normal);
}

float4 PSMain(PS_IN input) : SV_TARGET
{
    return Shade(input.Col * Unpremultiply(tex.Sample(samp, input.Tex)), input);
}
