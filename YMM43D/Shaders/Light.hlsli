#ifndef YMM43D_LIGHT_HLSLI
#define YMM43D_LIGHT_HLSLI

// 光源ひとつぶん。数に上限を設けたくないので定数バッファには置かず、
// t1 の StructuredBuffer から読む。何個入っているかは b0 の LightCount。
struct Light
{
    // xyz: 平行光なら光が来る向き、点光源とスポットなら置いた場所
    // w  : 0 平行光 / 1 点光源 / 2 スポット
    float4 Vector;

    // rgb: 色と明るさ / a: 届く距離
    float4 Color;

    // xyz: スポットが照らす向き / w: 外側の角の cos
    float4 Cone;

    // x: 内側の角の cos / yzw: 予備
    float4 Edge;
};

StructuredBuffer<Light> Lights : register(t1);

#endif
