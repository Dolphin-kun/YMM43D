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

    // x: 内側の角の cos    / y: 影の板の番号（-1 で影を落とさない）
    // z: 影の濃さ          / w: 影の板 1 画素ぶんの大きさ
    float4 Edge;

    // ワールド座標を、その光から見た影の板の座標へ移す
    matrix Shadow;
};

StructuredBuffer<Light> Lights : register(t1);

Texture2DArray ShadowMaps : register(t2);
SamplerComparisonState ShadowSampler : register(s1);

// 面と影の板がぴったり重なると、自分自身に縞が出る。
// 引くときだけ法線の向きへ少し浮かせて避ける。ワールド単位。
static const float ShadowLift = 0.02;

float ShadowAt(Light light, float3 world, float3 normal, float lambert)
{
    int slice = (int)light.Edge.y;

    if (slice < 0)
        return 1.0;

    // 光と面が浅い角ほど縞が出やすいので、そのぶん多く浮かせる。
    float3 lifted = world + normal * (ShadowLift * (2.0 - lambert));

    float4 placed = mul(float4(lifted, 1.0), light.Shadow);

    if (placed.w <= 0.0)
        return 1.0;

    placed.xyz /= placed.w;

    if (placed.z <= 0.0 || placed.z >= 1.0)
        return 1.0;

    float2 uv = placed.xy * float2(0.5, -0.5) + 0.5;

    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
        return 1.0;

    float texel = light.Edge.w;
    float lit = 0.0;

    [unroll]
    for (int y = -1; y <= 1; y++)
    {
        [unroll]
        for (int x = -1; x <= 1; x++)
        {
            float2 at = uv + float2(x, y) * texel;

            lit += ShadowMaps.SampleCmpLevelZero(ShadowSampler, float3(at, slice), placed.z);
        }
    }

    return lerp(1.0, lit / 9.0, saturate(light.Edge.z));
}

#endif
