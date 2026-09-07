#include "../../YMM43D/Shaders/Scene.hlsli"

cbuffer BeamConstants : register(b1)
{
    // rgb: 光の色 / a: 濃さ
    float4 BeamColor;

    // x: 先へ行くほど弱まる度合いの指数
    float4 BeamShape;
};

struct VS_IN
{
    float3 Pos : POSITION;
    float  Weight : TEXCOORD0;
};

struct PS_IN
{
    float4 Pos : SV_POSITION;
    float3 Local : TEXCOORD0;
    float  Weight : TEXCOORD1;
};

PS_IN VSMain(VS_IN input)
{
    PS_IN output;

    output.Pos = mul(float4(input.Pos, 1.0), WorldViewProjection);
    output.Local = input.Pos;
    output.Weight = input.Weight;

    return output;
}

// 円錐の殻を内側から外側まで重ねて描き、足し合わせて筋にする。
// 1枚ぶんの濃さは頂点が持っているので、ここでは長さ方向の弱まりだけを掛ける。
float4 PSMain(PS_IN input) : SV_TARGET
{
    float axial = saturate(input.Local.z);
    float along = pow(saturate(1.0 - axial), BeamShape.x);

    float intensity = saturate(input.Weight * along * BeamColor.a * Opacity);

    // 乗算済みアルファで返す。呼び出し側が足し込む合成にしている。
    return float4(BeamColor.rgb * intensity, intensity);
}
