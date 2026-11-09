#include "Model.hlsli"

struct BoneMatrix
{
    float4 Row0;
    float4 Row1;
    float4 Row2;
    float4 Row3;
};

StructuredBuffer<BoneMatrix> Bones : register(t3);

StructuredBuffer<float4> Morphs : register(t4);

struct MODEL_IN_SKINNED
{
    float3 Pos : POSITION;
    float4 Col : COLOR;
    float2 Tex : TEXCOORD0;
    float3 Nrm : NORMAL;
    float4 Tan : TANGENT;
    uint4  Idx : BLENDINDICES;
    float4 Wgt : BLENDWEIGHT;
    uint   Id  : SV_VertexID;
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

MODEL_PS VSMain(MODEL_IN_SKINNED input)
{
    float3 shaped = input.Pos + Morphs[input.Id].xyz;

    float total = input.Wgt.x + input.Wgt.y + input.Wgt.z + input.Wgt.w;

    if (total <= 0.0001)
        return MakeModelVertex(shaped, input.Col, input.Tex, input.Nrm, input.Tan);

    float3 position = Blend(float4(shaped, 1.0), input.Idx, input.Wgt).xyz;
    float3 normal = Blend(float4(input.Nrm, 0.0), input.Idx, input.Wgt).xyz;
    float3 tangent = Blend(float4(input.Tan.xyz, 0.0), input.Idx, input.Wgt).xyz;

    return MakeModelVertex(
        position, input.Col, input.Tex, normal, float4(tangent, input.Tan.w));
}

float4 PSMain(MODEL_PS input) : SV_TARGET
{
    return ShadeModel(input);
}
