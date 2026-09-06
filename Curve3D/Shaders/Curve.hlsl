#include "../../YMM43D/Shaders/Deform.hlsli"

cbuffer CurveConstants : register(b1)
{
    float BendRadians;
    float TwistRadians;
    float AxisRadians;
    float Anchor;
};

float2 Turn(float2 v, float angle)
{
    float s = sin(angle);
    float c = cos(angle);

    return float2(v.x * c - v.y * s, v.x * s + v.y * c);
}

float3 Deform(float3 local, float3 piece)
{
    // 軸の角度ぶん回してから、いつも横向きに曲げる。終わったら戻す。
    float3 p = float3(Turn(local.xy, -AxisRadians), local.z);

    // 基準位置が動かない点になるよう、いったん原点へ寄せる。
    p.x -= Anchor;

    if (abs(BendRadians) > 1e-4)
    {
        // 幅 1 の板を、BendRadians だけ回り込む円弧に置き換える。
        float radius = 1.0 / BendRadians;
        float angle = p.x * BendRadians;

        p = float3(radius * sin(angle), p.y, p.z + radius * (1.0 - cos(angle)));
    }

    if (abs(TwistRadians) > 1e-4)
    {
        // 縦の位置に応じて、軸まわりにひねる。
        float2 turned = Turn(float2(p.x, p.z), p.y * TwistRadians);
        p = float3(turned.x, p.y, turned.y);
    }

    p.x += Anchor;

    return float3(Turn(p.xy, AxisRadians), p.z);
}

float DeformFade(float3 local, float3 piece)
{
    return 1.0;
}
